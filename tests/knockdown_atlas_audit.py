"""Audit the shipped artwork from its PNG resource to the real indexed sprite.

Requires Python with Pillow and Unicorn, PowerShell 7 (.NET 10), and the x86
KnockdownTests fixture. Neither launcher entry point nor the game is started.
"""
import argparse
import base64
from functools import lru_cache
from hashlib import sha256
from io import BytesIO
import os
from pathlib import Path
import struct
import subprocess
import zlib

from PIL import Image, ImageDraw, ImageFont


def managed_assembly(exe):
    raw = exe.read_bytes()
    signature = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")
    stream = BytesIO(raw)
    stream.seek(struct.unpack_from("<Q", raw, raw.index(signature) - 8)[0])

    def string():
        length = 0
        for shift in range(0, 35, 7):
            byte = stream.read(1)[0]
            length |= (byte & 127) << shift
            if byte < 128:
                return stream.read(length).decode("utf8")
        raise ValueError("Invalid bundle string")

    major, _, count = struct.unpack("<IIi", stream.read(12))
    assert major == 6
    string()
    stream.seek(40, 1)
    for _ in range(count):
        offset, size, compressed, _ = struct.unpack("<qqqB", stream.read(25))
        if string() == "RoadRash30thLauncher.dll":
            block = raw[offset:offset + (compressed or size)]
            dll = zlib.decompress(block, -15) if compressed else block
            assert len(dll) == size
            return dll
    raise ValueError(f"Missing managed launcher in {exe}")


def unpack(exe, folder, prior_palette=None):
    folder.mkdir(parents=True, exist_ok=True)
    # Invoke only the actual shipped pure decoder inside PowerShell's .NET
    # runtime. Reading the manifest stream independently preserves the original PNG.
    ps = """
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::Load([Convert]::FromBase64String([Console]::In.ReadToEnd()))
$dest=$env:RR_ATLAS_FOLDER
$p=$a.GetManifestResourceStream('KnockdownHud.png')
$f=[IO.File]::Create((Join-Path $dest 'packed.png'))
try { $p.CopyTo($f) } finally { $f.Dispose(); $p.Dispose() }
$flags=[Reflection.BindingFlags]'Static,NonPublic,Public'
$method=$a.GetType('RuntimePatch',$true).GetMethod('LoadKnockdownAtlas',$flags)
[IO.File]::WriteAllBytes((Join-Path $dest 'decoded.bgra'),$method.Invoke($null,$null))
$code=$a.GetType('KnockdownNative',$true).GetField('Code',$flags)
[IO.File]::WriteAllBytes((Join-Path $dest 'native.bin'),$code.GetValue($null))
if ($env:RR_ATLAS_PRIOR_PALETTE) {
    $p=[IO.File]::ReadAllBytes($env:RR_ATLAS_PRIOR_PALETTE)
    $m=$a.GetType('RuntimePatch',$true).GetMethod('KnockdownColorLut',$flags)
    [IO.File]::WriteAllBytes((Join-Path $dest 'color-table.bin'),$m.Invoke($null,(,$p)))
}
"""
    env = dict(os.environ, RR_ATLAS_FOLDER=str(folder.resolve()),
               RR_ATLAS_PRIOR_PALETTE=str(prior_palette.resolve()) if prior_palette else "")
    assembly = base64.b64encode(managed_assembly(exe))
    subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-Command", ps], input=assembly, env=env, check=True)
    png = (folder / "packed.png").read_bytes()
    with Image.open(BytesIO(png)) as source:
        assert source.mode == "RGBA" and source.size == (320, 112)
        art = source.copy()
    expected = art.tobytes("raw", "BGRA")
    decoded = (folder / "decoded.bgra").read_bytes()
    assert len(expected) == len(decoded)
    differing = [i for i in range(0, len(decoded), 4) if decoded[i:i+4] != expected[i:i+4]]
    assert all(decoded[i+3] == expected[i+3] == 0 for i in differing), "visible RGBA channel or row mismatch"
    glass = art.getchannel("A").crop((12, 14, 99, 43)).histogram()
    print(f"{folder.name}: {sha256(png).hexdigest()}, {len(expected)//4:,} decoded pixels, "
          f"0 visible mismatches, {len(differing)} hidden RGB differences; "
          f"glass alpha 0/partial/255 = {glass[0]}/{sum(glass[1:255])}/{glass[255]}")
    return art


def legacy_meter(renderer_folder, art_folder, art):
    """Collect the old program's actual 32-bit GDI inputs before either blit."""
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
    from unicorn.x86_const import UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EIP, UC_X86_REG_ESP

    code = (renderer_folder / "native.bin").read_bytes()
    if sha256(code).hexdigest() != "9204ec7908860ceec7fa251598d650214d55b9e051768090b6a92b952431f007":
        return None  # These two GDI call sites belong to the original installed build only.
    cpu = Uc(UC_ARCH_X86, UC_MODE_32)
    module, state, atlas, canvas, game, stack = (i << 24 for i in range(1, 7))
    for p, size in ((module, 0x2000), (state, 0x1000), (atlas, 0x24000), (canvas, 0x3A000),
                    (game, 0x220000), (stack, 0x10000)):
        cpu.mem_map(p, size)
    def words(address, *values):
        cpu.mem_write(address, struct.pack(f"<{len(values)}I", *values))

    cpu.mem_write(module, code)
    cpu.mem_write(atlas, (art_folder / "decoded.bgra").read_bytes())
    words(state, game, atlas, 0, canvas)
    words(state+0x5C, 1)
    words(game+0x21C414, module+0x1100)
    words(game+0x21C15C, module+0x1120)
    cpu.mem_write(module+0x1100, bytes.fromhex("b8e8030000c3"))  # time = 1000
    cpu.mem_write(module+0x1120, bytes.fromhex("b84c000000c23400"))  # mock successful GDI return
    cpu.mem_write(game+0x1A292A, bytes((1,)))
    words(game+0x1A2934, 640, 480)
    cpu.reg_write(UC_X86_REG_ECX, state)
    cpu.reg_write(UC_X86_REG_EDX, 5)
    cpu.reg_write(UC_X86_REG_ESP, stack+0xF000)
    words(stack+0xF000, module+0x1140, 1, 0)
    buffers = []
    start = module
    for end, rop in ((module+0x744, 0x8800C6), (module+0x7A5, 0xEE0086)):
        cpu.emu_start(start, end, timeout=3_000_000)
        assert cpu.reg_read(UC_X86_REG_EIP) == end
        args = struct.unpack("<13I", cpu.mem_read(cpu.reg_read(UC_X86_REG_ESP), 52))
        assert args[1:9] == (8, 366, 128, 76, 0, 0, 128, 76) and args[12] == rop
        info = struct.unpack("<IiiHH", cpu.mem_read(args[10], 16))
        assert info == (40, 386, -76, 1, 32)
        buffers.append(bytes(cpu.mem_read(args[9], 386*76*4)))
        start = end
    mask = Image.frombytes("RGBA", (386,76), buffers[0], "raw", "BGRA").getchannel("R")
    ink = Image.frombytes("RGBA", (386,76), buffers[1], "raw", "BGRA")
    ink.putalpha(mask.point(lambda v: 255-v))
    panel = ink.crop((0,20,128,70))
    holes = [(x,y) for y in range(14,43) for x in range(12,99) if art.getpixel((x,y))[3] == 0]
    left = [p for p in holes if panel.getpixel(p)[3] == 0]
    assert not any(art.getpixel((x,y))[3]>=128 and panel.getpixel((x,y))[3]==0
                   for y in range(14,43) for x in range(12,99))
    print(f"Legacy native raster with {art_folder.name} PNG at 000: {len(holes)} original glass holes, {len(holes)-len(left)} covered "
          f"by digit ink, {len(left)} remain open in the actual GDI source mask")
    for name,buf in zip(("mask","ink"),buffers):
        (art_folder/f"legacy-{name}.bgra").write_bytes(buf)
    panel.save(art_folder / "meter-before-gdi.png")
    return panel


def archived_label(art, paint):
    """The first native-sprite package has a 120-row surface and unscaled glyphs."""
    tick=9000//40
    noise=((tick+10*379)*2654435761)&0xffffffff
    left=38+noise%11-5
    base=55+187//9+(noise>>18)%6-2
    ink=[]
    for start,sx,w in ((0,0,155),(158,183,15),(173,168,15)):
        for col in range(w):
            x=start+col
            for y in range(26):
                a=art.getpixel((sx+col,57+y))[3]
                if not a: continue
                if x==94: color=(255,255,191)
                elif x<94: color=(255,236,154) if y<9 else (255,183,55) if y<17 else (247,117,39)
                else: color=(198,135,128) if y<10 else (145,87,92)
                ink.append((left+x+int((24-y)/5),base+y-x//9,color,a))
    for x,y,_,a in ink:
        for oy in range(-1,3):
            for ox in range(-1,3):
                paint(x+ox,y+oy,(40,21,30),a)
    for x,y,color,a in ink:
        paint(x,y,color,a)
    return left+188+8


def layout(art, chain, height=144):
    """Compose independently from the PNG crops in straight RGBA, using integer source-over."""
    width = 386
    out = bytearray(width*height*4)
    def paint(x, y, rgb, alpha):
        if not alpha:
            return
        i = 4*(y*width+x)
        for c, src in enumerate((*rgb[::-1], 255)):
            out[i+c] = (src*alpha+127)//255 + (out[i+c]*(255-alpha)+127)//255

    def crop(box, x, y):
        region = art.crop(box)
        for sy in range(region.height):
            for sx in range(region.width):
                *rgb, a = region.getpixel((sx,sy))
                paint(x+sx, y+sy, rgb, a)
    crop((0,0,128,50),0,0)
    for i, digit in enumerate("12345" if chain else "00000"):
        x = 16*int(digit)
        crop((x,86,x+16,112),14+17*i,15)
    if not chain:
        return out, 128, 50
    if height==120:
        return out, archived_label(art,paint), height
    # Reference x10 at the midpoint, sampled directly from the three PNG masks.
    tick = 9000//40
    noise = ((tick+10*379)*2654435761) & 0xFFFFFFFF
    left, base = 31+noise%11-5, 132+(noise>>18)%6-2
    points, roof = {}, {}
    faces = (0xe9dfba,0xead685,0xc98d00,0xca7028)
    for start,sx,sw,w,h in ((0,0,155,207,38),(211,183,15,25,52),(236,168,15,25,52)):
        for x in range(w):
            col = start+x
            for y in range(h):
                row = y*26//h
                if art.getpixel((sx+x*sw//w,57+row))[3]<128:
                    continue
                px,py = left+col+(h-1-y)//6, base-col//11-h+y
                if col==130: color=0xffffff
                elif col>130: color=0xad7f83 if row<10 else 0x844742
                else: color=faces[(row>=5)+(row>=11)+(row>=18)]
                points[px,py]=color
                roof[px]=min(roof.get(px,height),py)
    def solid(x,y,color):
        i=4*(y*width+x)
        out[i:i+4]=(0xff000000|color).to_bytes(4,"little")
    for x,y in points:
        for dy in range(-1,4):
            for dx in range(-1,3):
                solid(x+dx,y+dy,0x2d0b16)
    for (x,y),color in points.items():
        solid(x,y,color)
    def wave(n):
        return 16-abs(n%32-16)
    def fire(x,y,color):
        if not out[4*(y*width+x)+3]:
            solid(x,y,color)
    for x,y in sorted(roof.items()):
        col=x-left
        pulse=wave(col+2*tick+wave(col//3-tick)//2)
        reach=3+(pulse**2+3*wave(2*col-2*tick))*11//128
        for rise in range(1,reach+1):
            drift=rise//4+int((wave(tick+rise)-8)*rise/72)
            band=8*rise//reach
            fire(x+drift,y-rise,(0xd5b73b,0xd58a2b,0xfb2800,0xb61c1c)[(band>=2)+(band>=4)+(band>=7)])
        life=(tick+col*7)%32
        if col%29==12 and 4<=life<24:
            dx,dy=x+life//2+wave(tick+col)//4,y-6-life
            fire(dx,dy,0xd5b73b if life<15 else 0xfb2800)
            if life<15: fire(dx-1,dy+1,0xd58a2b)
    return out, left+261+22, height


def single_line_layout(art, chain, palette, bed, step, compact, strip=False):
    """Place the independently derived glyph masks on each release's isolated fire draw."""
    old, _, _ = layout(art, chain, 144)
    width,height=(640,136) if compact else (560,248)
    dock_x,dock_y,dock_w,dock_h=(388,4,240,84) if compact else (0,52,560,196)
    if strip:
        width,height=640,50
        dock_x,dock_y,dock_w,dock_h=386,0,144,50
    out = bytearray(width*height*4)
    meter_x = (width-128)//2 if chain else 0
    for y in range(50):
        start = (width*y+meter_x)*4
        out[start:start+512] = old[y*386*4:y*386*4+512]
    if not chain:
        return out, 128, 50
    assert chain == 10 and len(bed)==400*140
    assert set(bed) <= {0,36,37,50,52,185,187,201,229}
    grid = bytearray(bed)
    inks = [b"\0"*4] + [bytes((*rgb[::-1],255)) for rgb in palette[1:]]
    letters = {inks[i]:i for i in (45,57,60,183,184,186,188,255)}
    # At the nine-second sample, the compact label has settled in its dock.
    # The archived version still shakes. Both use the same slanted contour.
    noise=((9000//40+10*379)*2654435761)&0xffffffff
    shake_x,shake_y=noise%11-5,(noise>>18)%6-2
    baseline=103 if compact else 107+shake_y
    shift_x=(20 if compact else 97+shake_x)-(31+shake_x)
    shift_y=baseline-(132+shake_y)
    lettering = 0
    for y in range(50,144):
        for x in range(386):
            i=(386*y+x)*4
            ink=letters.get(bytes(old[i:i+4]))
            if ink is not None:
                j=(y+shift_y)*400+x+shift_x
                assert bed[j], "fire missing beneath an original stroke"
                grid[j]=ink
                lettering+=1
    assert lettering>6000
    xs=[((x//step)*2+1)*step*400//(2*dock_w) for x in range(dock_w)]
    for y in range(dock_h):
        row=(((y//step)*2+1)*step*140//(2*dock_h))*400
        start=((dock_y+y)*width+dock_x)*4
        out[start:start+dock_w*4]=b"".join(inks[grid[row+x]] for x in xs)
    draw_h=dock_y+((baseline+16)*dock_h+139)//140
    return out,width,max(50,draw_h)


def check_readback(folder, art, palette):
    @lru_cache(None)
    def nearest(r,g,b):
        def error(i):
            dr, dg, db = r-palette[i][0], g-palette[i][1], b-palette[i][2]
            return dr**2+4*dg**2+db**2+(dr-dg)**2+(db-dg)**2
        return min(range(1,256),key=error)
    assert [nearest(*p) for p in palette[1:]] == list(range(1,256))
    size=(folder/"high-0-layer.bgra").stat().st_size
    strip=size==640*50*4
    compact=strip or size==640*136*4
    single_line=compact or size==560*248*4
    stride=640 if compact else (560 if single_line else 386)
    canvas_height,remainder=divmod(size,stride*4)
    assert canvas_height in (50,120,136,144,248) and not remainder
    for chain in (0,10):
        if single_line:
            bed=(folder/"high-10-fire.idx").read_bytes() if chain else None
            if chain: assert bed==(folder/"low-10-fire.idx").read_bytes()
            expected,w,h=single_line_layout(art,chain,palette,bed,1,compact,strip)
        else:
            expected,w,h=layout(art,chain,canvas_height)
        for step, detail in ((1,"high"),(2,"low")):
            stem = folder / f"{detail}-{chain}"
            src = Path(f"{stem}-source.bob").read_bytes()
            layer = Path(f"{stem}-layer.bgra").read_bytes()
            assert expected == layer, f"PNG to composed layer differs in {stem.name}"
            fw, fh = 640//step,480//step
            oracle=single_line_layout(art,chain,palette,bed,2,compact,strip)[0] if single_line and chain and step==2 else expected
            ox, oy = (fw-(w if single_line else 128)//step)//2,8//step
            tracks = {n:bytearray([n])*fw*fh for n in (17,201)}
            sw,sh = (w+step-1)//step,h//step
            for y in range(sh):
                for x in range(sw):
                    base = 4*(step*y*stride+step*x)
                    if step==1:
                        b,g,r,a = oracle[base:base+4]
                    else:
                        b,g,r,a = ((sum(oracle[base+n+c] for n in (0,4,4*stride,4*stride+4))+2)//4 for c in range(4))
                    index = 0 if a<128 else nearest(*((v*255+a//2)//a for v in (r,g,b)))
                    assert index == src[y*stride+x], (stem.name,x,y,index,src[y*stride+x])
                    if index:
                        for frame in tracks.values(): frame[(y+oy)*fw+ox+x] = index
            for n, frame in tracks.items():
                assert Path(f"{stem}-{n}.idx").read_bytes() == frame, f"DirectDraw destination differs in {stem.name}"
            print(f"PASS {stem.name}: independently decoded PNG -> {sw}x{sh} source indices -> "
                  f"all {fw*fh:,} pixels on each of two surfaces")
    stem = folder / "high-0"
    src = Path(f"{stem}-source.bob").read_bytes()
    raw = (folder/"decoded.bgra").read_bytes()
    for label, x,y,sx,sy in (("outside",0,0,0,0),("rim",5,24,5,24),("glass",12,24,12,24),
                             ("digit 0 top segment",22,17,8,88)):
        i=4*(sy*320+sx)
        dest=[Path(f"{stem}-{n}.idx").read_bytes()[(8+y)*640+256+x] for n in (17,201)]
        print(f"  {label}: PNG ({sx},{sy}) RGBA={art.getpixel((sx,sy))}; BGRA={raw[i:i+4].hex(' ')}; "
              f"table ({x},{y}) -> index {src[y*stride+x]}, destination {dest}")
    panel = Image.frombytes("P",(stride,canvas_height),src).crop((0,0,128,50))
    panel.putpalette(bytes(v for p in palette for v in p))
    (folder / "meter-128x50.bob").write_bytes(panel.tobytes())
    panel.save(folder / "meter-128x50-palette.png")
    panel.info["transparency"] = 0
    return panel.convert("RGBA")


def comparison(path, old, new, old_draw, new_draw):
    panels = [(old.crop((0,0,128,50)), "旧启动器的 PNG 表体"), (new.crop((0,0,128,50)), "新验证版的 PNG 表体"),
              (old_draw, "旧版送给 GDI 的表体与数字"), (new_draw, "新验证版 DirectDraw 源表面")]
    board = Image.new("RGB",(864,433),"#182126")
    draw = ImageDraw.Draw(board)
    font = ImageFont.truetype("msyh.ttc",18)
    for i,(sprite,label) in enumerate(panels):
        x,y=16+(i%2)*424,8+(i//2)*190
        draw.text((x,y),label,font=font,fill="#ecf1ed")
        bg=Image.new("RGB",sprite.size)
        bg.putdata([(216,96,159) if (xx//4+yy//4)%2 else (85,52,83)
                    for yy in range(sprite.height) for xx in range(sprite.width)])
        bg.paste(sprite, mask=sprite.getchannel("A"))
        board.paste(bg.resize((384,150),Image.Resampling.NEAREST),(x,y+31))
    draw.text((16,391),"棋盘格显示透明像素。下排取自实际绘制输入，保留各版本自己的数字样式。",font=font,fill="#d2dcd6")
    board.save(path)


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ("game","launcher","fixture","output"):
        parser.add_argument(name,type=Path)
    parser.add_argument("--prior-palette", type=Path, help="Previous native-sprite launcher with a 5-bit RGB mapping")
    parser.add_argument("--baseline-launcher", type=Path, help="Saved launcher to use for the legacy meter comparison")
    args=parser.parse_args()
    old_dir,new_dir=args.output/"installed",args.output/"candidate"
    installed = args.game / "暴力摩托增强启动器.exe"
    if not installed.is_file():
        installed = args.game / "RoadRash30thLauncher.exe"
    old=unpack(args.baseline_launcher or installed,old_dir)
    new=unpack(args.launcher,new_dir)
    old_draw=legacy_meter(old_dir,old_dir,old)
    assert new.crop((12,14,99,43)).getchannel("A").getextrema()==(255,255)
    subprocess.run([str(args.fixture.resolve()),str(args.game.resolve()),"--trace-atlas",str(new_dir.resolve())],check=True)
    pal=(args.game/"DATA"/"PALETTE.RAW").read_bytes()
    palette=[tuple(pal[n:n+3]) for n in range(0,1024,4)]
    if args.prior_palette:
        prior=args.output/"previous-palette"
        unpack(args.prior_palette,prior,args.game/"DATA"/"PALETTE.RAW")
        lut=(prior/"color-table.bin").read_bytes()
        assert len(lut)==32**3
        remapped=[]
        for i,(r,g,b) in enumerate(palette[1:],1):
            dest=lut[(r>>3)*1024+(g>>3)*32+(b>>3)]
            if dest!=i: remapped.append(f"{i}->{dest}")
        print(f"Previous launcher misidentified {len(remapped)} original swatches: {', '.join(remapped)}")
    new_draw=check_readback(new_dir,new,palette)
    if old_draw is not None:
        legacy_meter(old_dir,new_dir,new)
        for target in (old_dir, new_dir):
            subprocess.run([str(args.fixture.resolve()),str(args.game.resolve()),"--legacy-atlas",str(target.resolve())],check=True)
        comparison(args.output/"atlas-comparison.png",old,new,old_draw,new_draw)
