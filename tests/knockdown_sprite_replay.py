"""Replay the installed BOB and HD graphics routines in a private x86 emulator.

Run KnockdownTests.exe first to supply the real DirectDraw index buffers.
Only file bytes are mapped. No game process, desktop or display device is used.
"""
import argparse
import hashlib
import math
from pathlib import Path
import struct

import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import (
    UC_X86_REG_EAX, UC_X86_REG_EBP, UC_X86_REG_EBX, UC_X86_REG_ECX,
    UC_X86_REG_EDI, UC_X86_REG_EIP, UC_X86_REG_ESI, UC_X86_REG_ESP,
)


class GraphicsImage:
    stack = 0x2000D000
    frame = 0x2000F000
    stop = 0x20001000

    def __init__(self, path, routines):
        with pefile.PE(str(path), fast_load=True) as pe:
            self.base = pe.OPTIONAL_HEADER.ImageBase
            size = pe.OPTIONAL_HEADER.SizeOfImage
            data = pe.get_memory_mapped_image()
        for start, end, digest in routines:
            actual = hashlib.sha256(data[start:end]).hexdigest()[:16]
            if actual != digest:
                raise ValueError(f"Unsupported {path.name} graphics routine at RVA {start:x}")
        self.cpu = Uc(UC_ARCH_X86, UC_MODE_32)
        self.cpu.mem_map(self.base, (size + 4095) & ~4095)
        self.cpu.mem_write(self.base, data)
        self.cpu.mem_map(0x20000000, 0x10000)
        self.heap = 0x21000000

    def alloc(self, size, data=None):
        p = self.heap
        length = (size + 4095) & ~4095
        self.cpu.mem_map(p, length)
        self.heap += length
        if data is not None:
            self.cpu.mem_write(p, data)
        return p

    def words(self, address, *values):
        self.cpu.mem_write(address, struct.pack(f"<{len(values)}I", *values))

    def read(self, address, count):
        return struct.unpack(f"<{count}I", self.cpu.mem_read(address, count * 4))

    def global_word(self, rva, word):
        self.words(self.base + rva, word)

    def run(self, start, end=None):
        end = self.stop if end is None else self.base + end
        self.cpu.emu_start(self.base + start, end, timeout=45_000_000)
        assert self.cpu.reg_read(UC_X86_REG_EIP) == end, f"Graphics replay timed out at RVA {start:x}"

    def stack_args(self, *args):
        self.cpu.reg_write(UC_X86_REG_ESP, self.stack)
        self.cpu.reg_write(UC_X86_REG_EBP, self.frame)
        self.words(self.stack, self.stop, *args)


def stock_graphics(game):
    g = GraphicsImage(game / "RoadRash95.exe", [
        (0x68780, 0x68893, "d1a3b6871ed6d250"), (0x69460, 0x69502, "ad43b4dcf0fce124"),
        (0x5FA6E, 0x5FABA, "ca0e4a44f0a97fc0"), (0x5D7B2, 0x5D7F7, "517bce01f78bca8c"),
    ])
    # Only the external COM methods are stand-ins. The original program supplies
    # their arguments. The companion C# fixture calls these methods on real surfaces.
    vtable = g.alloc(132)
    dest, cockpit, meter = [g.alloc(4, struct.pack("<I", vtable)) for _ in range(3)]
    calls = []

    def com_method(slot, name, nargs):
        code = bytes((0x31, 0xC0, 0xC2, 4 * nargs, 0))  # S_OK, stdcall
        p = g.alloc(len(code), code)
        g.words(vtable + slot, p)

        def record(cpu, address, size, userdata):
            args = g.read(cpu.reg_read(UC_X86_REG_ESP) + 4, nargs)
            if name == "SetColorKey":
                args = (*args[:2], g.read(args[2], 2))
            elif name == "BltFast":
                args = (*args[:4], g.read(args[4], 4), args[5])
            calls.append((name, args))

        g.cpu.hook_add(UC_HOOK_CODE, record, begin=p, end=p)

    for spec in ((0x74, "SetColorKey", 3), (0x60, "IsLost", 1),
                 (0x1C, "BltFast", 6), (0x44, "GetDC", 2)):
        com_method(*spec)
    g.global_word(0x1CD4BC, dest)
    g.global_word(0x1CD4C0, meter)
    g.global_word(0x1CD4D0, cockpit)
    g.stack_args()
    g.cpu.reg_write(UC_X86_REG_EAX, 0)
    g.run(0x5FA6E, 0x5FABA)
    assert calls == [("SetColorKey", (s, 8, (0, 0))) for s in (meter, cockpit)]

    bobs = [(game / "IMAGES" / "BIGMETER.BOB", 190), (game / "IMAGES" / "LILMETER.BOB", 95)]
    for bike in ("RAT", "SPORT", "SUPER"):
        for detail, width in (("HI", 640), ("LO", 320)):
            bobs.append((game / "IMAGES" / "BIKES" / bike / f"M{detail}DASH.BOB", width))
    for path, width in bobs:
        raw = path.read_bytes()
        height, remainder = divmod(len(raw), width)
        assert not remainder
        pitch = width + 16
        source = g.alloc(len(raw), raw)
        dest_bits = g.alloc(height * pitch, b"\xad" * (height * pitch))
        g.stack_args()
        g.words(g.frame - 0x60, pitch)
        g.words(g.frame - 0x7C, height)
        g.cpu.reg_write(UC_X86_REG_ESI, dest_bits)
        g.cpu.reg_write(UC_X86_REG_EDI, source)
        g.cpu.reg_write(UC_X86_REG_EBX, width)
        g.run(0x68840, 0x68856)  # includes the original row memcpy
        result = g.cpu.mem_read(dest_bits, height * pitch)
        assert result == b"".join(raw[offset:offset + width] + b"\xad" * 16
                                  for offset in range(0, len(raw), width))
        if width < 320:
            continue
        g.global_word(0x1AA9CC, 1)
        g.cpu.mem_write(g.base + 0x1A292A, bytes((width == 640,)))
        g.global_word(0x1CE350, height)
        for ox, oy in ((0, 0), (160, 120)) if width == 320 else ((0, 0),):
            g.global_word(0x1CD4B4, ox)
            g.global_word(0x1CD4B8, oy)
            g.global_word(0x190DC8, 0)  # no optional indicator in this call
            calls.clear()
            g.stack_args()
            g.run(0x5D7BB, 0x5D7F5)
            assert [name for name, _ in calls] == ["IsLost", "IsLost", "BltFast", "GetDC"]
            assert calls[2][1] == (dest, ox, oy + width * 3 // 4 - height, cockpit,
                                   (0, 0, width, height), 0x11)
            assert calls[3][1][0] == dest
    print("PASS original x86 BOB row copy, eight raw files and untouched row padding")
    print("PASS original x86 SetColorKey(0..0), cockpit BltFast(0x11), then GetDC on the same surface")


def palette_rgbx(indices, palette):
    indices = bytes(indices)
    result = bytearray(len(indices) * 4)
    for i in range(3):
        result[i::4] = indices.translate(palette[2 - i::4])
    return result


def stamp_indices(target, pitch, xe, ye, frame, reference, mark=False):
    # Independent oracle. The target starts with individual HD world indices.
    # Only an original logical pixel whose index changed may cover them.
    for y in range(480):
        off = y * 640
        row, old = frame[off:off + 640], reference[off:off + 640]
        if row == old:
            continue
        for x, (new, before) in enumerate(zip(row, old)):
            if new == before:
                continue
            ink = bytes((1 if mark else new,)) * (xe[x + 1] - xe[x])
            for yy in range(ye[y], ye[y + 1]):
                start = yy * pitch + xe[x]
                target[start:start + len(ink)] = ink


def hd_graphics(game, fixtures, width, height):
    g = GraphicsImage(game / "RoadRashHD.dll", [
        (0x3DF0, 0x4028, "0786e91a8a620236"), (0x8040, 0x80E8, "7f31754a219e84dc"),
        (0x5351, 0x53BA, "9b2ab4abdf897356"), (0x82C1, 0x8422, "40c03444119fb184"),
    ])
    g.global_word(0x6FDA8, 2)  # CRT SSE4 rounding implementation on this x86 CPU
    g.global_word(0x2EB18, width)
    g.global_word(0x2EB1C, height)
    g.stack_args()
    g.cpu.reg_write(UC_X86_REG_EBX, width)
    g.cpu.reg_write(UC_X86_REG_EDI, height)
    for off, val in ((0x40, width), (0x38, height), (0x28, height / 480)):
        g.cpu.mem_write(g.stack + off, struct.pack("<d", val))
    g.words(g.stack + 0x58, width)
    g.words(g.stack + 0x30, height)
    g.run(0x82C1, 0x8422)  # original viewport edge generation, including the CRT ceil
    xe = g.read(g.base + 0x2EB60, 641)
    ye = g.read(g.base + 0x2F568, 481)
    scale = min(width / 640, height / 480)
    for edges, size, length in ((xe, width, 640), (ye, height, 480)):
        expected = (math.ceil((size - length * scale) / 2 + i * scale - 0.5) for i in range(length + 1))
        assert edges == tuple(max(0, min(size, k)) for k in expected)
    print(f"PASS HD x86 viewport edges {width}x{height}, x=[{xe[0]},{xe[-1]}), y=[{ye[0]},{ye[-1]})")

    def indices(name):
        data = (fixtures / name).read_bytes()
        assert len(data) == 480 * 640
        return data

    clean = indices("base.idx")
    bob = indices("cockpit.idx")
    hud = indices("with-hud.idx")
    legacy = indices("legacy-mask.idx")
    pal = (game / "DATA" / "PALETTE.RAW").read_bytes()
    # Give each enlarged logical cell nonuniform world detail and a known sample.
    # The sample locations are fixture input. The DLL performs both snapshot writes.
    xs = [(a + b) // 2 for a, b in zip(xe, xe[1:])]
    ys = [(a + b) // 2 for a, b in zip(ye, ye[1:])]
    cycle = bytes(range(256)) * (width // 256 + 2)
    world = bytearray().join(cycle[(7*y) % 256:(7*y) % 256 + width] for y in range(height))
    for y, sample_y in enumerate(ys):
        for x, sample_x in enumerate(xs):
            world[sample_y * width + sample_x] = clean[y * 640 + x]
    world_ptr = g.alloc(len(world), bytes(world))
    g.global_word(0x2EAB0, world_ptr)
    g.global_word(0x2EAB4, world_ptr + len(world))
    g.global_word(0x2EA44, g.alloc(640 * 4, struct.pack("<640I", *xs)))
    g.global_word(0x2EAA4, g.alloc(480 * 4, struct.pack("<480I", *ys)))
    ref = g.alloc(len(clean))
    g.global_word(0x2EAD4, ref)
    pitch = 656
    frame = g.alloc(pitch * 480, b"\xec" * (pitch * 480))
    g.stack_args()
    g.words(g.stack + 0xC, frame, pitch)
    g.cpu.reg_write(UC_X86_REG_ESI, 0)
    g.cpu.reg_write(UC_X86_REG_EDI, width)
    g.cpu.reg_write(UC_X86_REG_ECX, pitch)
    g.run(0x5351, 0x53BA)
    def pitched(data):
        return b"".join(data[k:k + 640] + b"\xec" * (pitch - 640) for k in range(0, len(data), 640))

    assert g.cpu.mem_read(frame, pitch * 480) == pitched(clean)
    assert g.cpu.mem_read(ref, len(clean)) == clean
    print("PASS HD x86 pre-HUD sample written to both reference and pitched game buffer")

    logical_rgb = g.alloc(len(clean) * 4)
    output = g.alloc(len(world) * 4)
    g.global_word(0x2EA5C, logical_rgb)
    g.global_word(0x2EAC8, output)
    pal_ptr = g.alloc(len(pal), pal)
    cockpit_output = None
    for label, overlay in (("cockpit", bob), ("old transparent GDI mask", legacy), ("new x10 sprite", hud)):
        padded = pitched(overlay)
        g.cpu.mem_write(frame, padded)
        g.stack_args(frame, pitch, pal_ptr)
        g.run(0x3DF0)  # includes the original changed-run painter at 0x8040
        actual = g.cpu.mem_read(output, len(world) * 4)
        assert g.cpu.mem_read(logical_rgb, len(clean) * 4) == palette_rgbx(overlay, pal)
        expected = world.copy()
        stamp_indices(expected, width, xe, ye, overlay, clean)
        assert actual == palette_rgbx(expected, pal)
        assert g.cpu.mem_read(frame, len(padded)) == padded
        print(f"PASS HD x86 {label}, {len(world):,} final RGB pixels match independent indexed oracle")
        if cockpit_output is not None:
            allowed = bytearray(len(world))
            stamp_indices(allowed, width, xe, ye, overlay, bob, mark=True)
            changed, xmin, xmax, ymin, ymax = 0, width, -1, height, -1
            before, after = memoryview(cockpit_output).cast("I"), memoryview(actual).cast("I")
            for y in range(height):
                start = y * width
                old_row, new_row = before[start:start + width], after[start:start + width]
                if old_row == new_row:
                    continue
                ymin, ymax = min(ymin, y), y
                for x, (a, b) in enumerate(zip(old_row, new_row)):
                    if a != b:
                        assert allowed[start + x]
                        changed += 1
                        xmin, xmax = min(xmin, x), max(xmax, x)
            if overlay is hud:
                assert changed > 0
            bounds = f"bounds x={xmin}..{xmax}, y={ymin}..{ymax}" if changed else "empty bounds"
            print(f"PASS {label} changed {changed:,} RGB pixels, {bounds}; 0 outside changed indices")
        else:
            cockpit_output = actual


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path)
    parser.add_argument("fixtures", type=Path)
    args = parser.parse_args()
    stock_graphics(args.game)
    for resolution in ((1280, 720), (3840, 2160)):
        hd_graphics(args.game, args.fixtures, *resolution)
