"""Build a local PCM bank from the classic announcer in an installed Dota 2 VPK."""
import argparse
import pathlib
import struct
import subprocess
import zlib


CLIPS = ("1stblood", "kill_double", "kill_triple", "kill_ultra", "kill_rampage",
         "kill_spree", "kill_dominate", "kill_mega", "kill_unstop",
         "kill_wicked", "kill_monster", "kill_godlike", "kill_holy", "ownage")


def recordings(directory):
    with directory.open("rb") as index:
        signature, version, size = struct.unpack("<III", index.read(12))
        if (signature, version) != (0x55AA1234, 2):
            raise ValueError("Expected a Valve VPK v2 directory")
        index.read(16)
        tree = index.read(size)
    cursor = 0

    def token():
        nonlocal cursor
        end = tree.index(0, cursor)
        value = tree[cursor:end]
        cursor = end + 1
        return value

    found = {}
    while ext := token():
        while folder := token():
            wanted = ext == b"vsnd_c" and folder == b"sounds/vo/announcer_killing_spree"
            while name := token():
                crc, preload, part, offset, length, end = struct.unpack_from("<IHHIIH", tree, cursor)
                if end != 0xffff:
                    raise ValueError("Invalid VPK directory entry")
                cursor += 18
                if wanted:
                    archive = directory if part == 0x7fff else directory.with_name(f"pak01_{part:03d}.vpk")
                    with archive.open("rb") as stream:
                        stream.seek(offset + (28 + size if part == 0x7fff else 0))
                        resource = tree[cursor:cursor + preload] + stream.read(length)
                    if zlib.crc32(resource) != crc:
                        raise ValueError(f"Bad checksum for {name!r}")
                    data_start, header_version = struct.unpack_from("<IH", resource)
                    if header_version != 12 or not 16 <= data_start < len(resource):
                        raise ValueError(f"Unsupported sound resource {name!r}")
                    found[name] = resource[data_start:]
                cursor += preload
            if wanted:
                return found
    raise ValueError("Classic English announcer not found in this Dota installation")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=pathlib.Path, help="Dota game/dota/pak01_dir.vpk")
    parser.add_argument("output", type=pathlib.Path, help="Road Rash Tools/KnockdownVoice.bank")
    args = parser.parse_args()
    files = recordings(args.directory)
    header = bytearray(b"KDV1")
    data = bytearray()
    for clip in CLIPS:
        key = f"announcer_{clip}_01".encode("ascii")
        pcm = subprocess.run(["ffmpeg", "-nostdin", "-v", "error", "-i", "pipe:0",
                              "-ac", "1", "-ar", "22050", "-f", "s16le", "pipe:1"],
                             input=files[key], stdout=subprocess.PIPE, check=True).stdout
        if not 4410 <= len(pcm) <= 441000 or len(pcm) % 2:
            raise ValueError(f"Unexpected PCM length for {clip}")
        print(f"{clip:18} {len(pcm) / 44100:.2f}s")
        header.extend(struct.pack("<II", 4 + 8 * len(CLIPS) + len(data), len(pcm)))
        data.extend(pcm)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(".bank.tmp")
    temporary.write_bytes(header + data)
    temporary.replace(args.output)


if __name__ == "__main__":
    main()
