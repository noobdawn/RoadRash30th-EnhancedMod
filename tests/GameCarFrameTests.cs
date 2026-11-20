using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

// 将本地游戏 PE 映射到独立测试进程，只执行动画初始化、角度和选帧函数。
// 不执行游戏入口、不打开游戏进程、不修改游戏文件；帧资源查询和最终绘制用 stub。
internal static class GameCarFrameTests
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")]
    private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll")]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RunCode();
    private static readonly List<IntPtr> Blocks = new List<IntPtr>();
    private static long Alloc(int size)
    {
        IntPtr p = VirtualAlloc(IntPtr.Zero, (UIntPtr)size, 0x3000, 0x40);
        if (p == IntPtr.Zero) throw new Exception("VirtualAlloc failed");
        Blocks.Add(p); return (uint)p.ToInt32();
    }
    private static void Write(long at, byte[] bytes)
    {
        Marshal.Copy(bytes, 0, new IntPtr(at), bytes.Length);
        FlushInstructionCache(new IntPtr(-1), new IntPtr(at), (UIntPtr)bytes.Length);
    }
    private static void Put(long at, long value) { Marshal.WriteInt32(new IntPtr(at), unchecked((int)value)); }
    private static int Get(long at) { return Marshal.ReadInt32(new IntPtr(at)); }
    private static void D(List<byte> code, long value) { code.AddRange(BitConverter.GetBytes(unchecked((int)value))); }
    private static void Mov(List<byte> code, byte op, long value) { code.Add(op); D(code, value); }
    private static long MapGame(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(file, 0x3C), opt = pe + 24;
        if (BitConverter.ToUInt32(file, pe) != 0x4550 || BitConverter.ToUInt16(file, opt) != 0x10B)
            throw new Exception("Expected PE32 game executable");
        int size = BitConverter.ToInt32(file, opt + 56);
        long image = Alloc(size), preferred = BitConverter.ToUInt32(file, opt + 28);
        Marshal.Copy(file, 0, new IntPtr(image), BitConverter.ToInt32(file, opt + 60));
        int section = opt + BitConverter.ToUInt16(file, pe + 20);
        for (int i = 0; i < BitConverter.ToUInt16(file, pe + 6); i++, section += 40)
        {
            int rva = BitConverter.ToInt32(file, section + 12);
            int length = BitConverter.ToInt32(file, section + 16), raw = BitConverter.ToInt32(file, section + 20);
            if (length != 0) Marshal.Copy(file, raw, new IntPtr(image + rva), length);
        }
        int reloc = BitConverter.ToInt32(file, opt + 96 + 5 * 8);
        int end = reloc + BitConverter.ToInt32(file, opt + 100 + 5 * 8);
        while (reloc < end)
        {
            int page = Get(image + reloc), block = Get(image + reloc + 4);
            if (block < 8 || reloc + block > end) throw new Exception("Invalid relocation block");
            for (int entry = 8; entry < block; entry += 2)
            {
                int fix = (ushort)Marshal.ReadInt16(new IntPtr(image + reloc + entry));
                if ((fix >> 12) == 3)
                {
                    long at = image + page + (fix & 0xFFF);
                    Put(at, unchecked((uint)Get(at)) + image - preferred);
                }
                else if ((fix >> 12) != 0) throw new Exception("Unexpected relocation type");
            }
            reloc += block;
        }
        FlushInstructionCache(new IntPtr(-1), new IntPtr(image), (UIntPtr)size);
        return image;
    }
    private static void Check(long image, long code, long data, int yaw, int roadYaw, int x, int pitch, int frame, int style = 0, bool bounds = false)
    {
        long player = data + 0x1000, rider = data + 0x1400, context = data + 0x1800;
        long state = code + 0x1000, car = code + 0x3000;
        Put(player + 0x68, yaw); Put(player + 0x6C, pitch); Put(player + 0x50, x);
        Put(context + 0x1C, roadYaw); Put(context + 0x18, 10000);
        Put(player + 0x2FC, rider); Put(image + 0x1A69EC, player);
        Put(data + 0x400, -1); Put(data + 0x404, 0);
        Write(state, new byte[0x1C0]);
        Write(code + 0x1800, RuntimePatch.BuildCarBoundsCode(image, code + 0x1800, state, car));
        Write(code + 0x1200, RuntimePatch.BuildPlayerCarCode(image, code + 0x1200, state, code + 0x2000, car, style, bounds ? code + 0x1800 : 0));
        List<byte> bridge = new List<byte>(new byte[] { 0x60, 0x9C });
        Mov(bridge, 0xB9, context); Mov(bridge, 0xBA, rider + 0xA8);
        Mov(bridge, 0xBE, rider); Mov(bridge, 0xBF, context);
        foreach (int arg in new int[] { 1, 0, 0, 0, 0x200 }) { bridge.Add(0x68); D(bridge, arg); }
        bridge.Add(0xE9); D(bridge, code + 0x1200 - (code + bridge.Count + 4));
        Write(code, bridge.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code), typeof(RunCode)))();
        if (Get(data + 0x400) != frame || Get(data + 0x404) != (bounds ? 4 : 1) || Get(state + 0x148) != 1 || Get(state + 0x14C) != 0)
            throw new Exception("Game frame mismatch: yaw=" + yaw + " expected=" + frame + " got=" + Get(data + 0x400));
        if (Get(car + 0x8C) != (int)(data + 0x800) || Get(car + 0x90) != (int)(data + 0x900) ||
            Get(car + 0x98) != (int)(data + (style == 2 ? 0x280 : 0x200)) ||
            Get(car + 0xA8) != (int)(data + (style == 2 ? 0x3800 : 0x3000) + frame * 0x80) ||
            Get(code + 0x2000 + 0x10) != frame || Get(code + 0x208C) != 45 || Get(code + 0x2094) != 0)
            throw new Exception("Game helper produced incorrect resources/wrapper/frame copy");
        if (Get(car + 0x68) != yaw || Get(car + 0x50) != x || Get(car + 0x6C) != 0)
            throw new Exception("Canonical bounds queries leaked into actual car pose");
        if (bounds && (Get(state + 0x1A0) != 1 || Get(state + 0x1A4) != (style == 2 ? 30720 : 20480) ||
            Get(state + 0x1A8) != (style == 2 ? 96 : 64) || Get(state + 0x1AC) != (style == 2 ? 15360 : 10240)))
            throw new Exception("Wrong frame-derived car dimensions");
        Console.WriteLine("PASS game frame: yaw=" + yaw + " roadYaw=" + roadYaw + " x=" + x + " pitch=" + pitch + " frame=" + frame);
    }

    private static void CheckHitbox(long image, long code, long data)
    {
        long player = data + 0x1000, state = code + 0x1000;
        Put(player + 0x174, 0x1E00); Put(player + 0x178, 0x40); Put(player + 0x17C, 0x2300);
        Write(code + 0x800, RuntimePatch.BuildCarHitboxCode(image, code + 0x800, state));
        Write(image + 0x49596, new byte[] { 0x8B, 0xE5, 0x5D, 0xC3 });
        List<byte> bridge = new List<byte>(new byte[] { 0x60, 0x9C, 0xE8 });
        D(bridge, code + 0x800 - (code + 0x500 + bridge.Count + 4));
        bridge.AddRange(new byte[] { 0x9D, 0x61, 0xC3 }); Write(code + 0x500, bridge.ToArray());
        RunCode run = (RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code + 0x500), typeof(RunCode));
        for (int scenario = 0; scenario < 6; scenario++)
        {
            Put(state + 0x1A0, scenario == 4 ? 0 : 1);
            Put(image + 0x1CD308 + 16, scenario == 1 ? 0 : data + 0x800);
            Put(image + 0x1CD30C + 16, scenario == 2 ? data + 0xA00 : data + 0x900);
            Put(player + 0x2F4, scenario == 3 ? 1 : 0);
            Put(player + 0x174, 11); Put(player + 0x178, 12); Put(player + 0x17C, 13);
            if (scenario == 0) { Put(player + 0x174, 0x1E00); Put(player + 0x178, 0x40); Put(player + 0x17C, 0x2300); }
            run();
            int[] want = scenario == 3 ? new int[] { 11, 12, 13 } :
                (scenario == 0 || scenario == 5 ? new int[] { 30720, 96, 15360 } : new int[] { 0x1E00, 0x40, 0x2300 });
            for (int i = 0; i < 3; i++) if (Get(player + 0x174 + 4 * i) != want[i])
                throw new Exception("Hitbox failed application/resource invalidation/crash guard, scenario=" + scenario);
            Console.WriteLine("PASS automatic hitbox: scenario=" + scenario);
        }
    }
    private static void Main(string[] args)
    {
        if (IntPtr.Size != 4 || args.Length != 1) throw new Exception("Run x86 test with RoadRash95.exe path");
        try
        {
            long image = MapGame(args[0]), code = Alloc(0x4000), data = Alloc(0x6000);
            for (int i = 0; i < RuntimePatch.SolidSites.Length; i++)
                for (int j = 0; j < RuntimePatch.SolidOriginals[i].Length; j++)
                    if (Marshal.ReadByte(new IntPtr(image + RuntimePatch.SolidSites[i] + j)) != RuntimePatch.SolidOriginals[i][j]) throw new Exception("Combat signature mismatch");
            foreach (int rva in RuntimePatch.LaunchSites)
                for (int j = 0; j < RuntimePatch.LaunchOriginal.Length; j++)
                    if (Marshal.ReadByte(new IntPtr(image + rva + j)) != RuntimePatch.LaunchOriginal[j]) throw new Exception("Crash signature mismatch");
            Console.WriteLine("1 motion integration case passed; all seven game signatures verified.");
            Put(image + 0x1CD300, 5); Put(image + 0x1CD308, data + 0x800); Put(image + 0x1CD30C, data + 0x900);
            Put(image + 0x1A2474, data + 0x100); Put(data + 0x100, data + 0x200);
            Put(image + 0x1CD308 + 16, data + 0x800); Put(image + 0x1CD30C + 16, data + 0x900);
            Put(image + 0x1A2474 + 8, data + 0x500); Put(data + 0x500, data + 0x280);
            // 合成八方向模型，验证真实游戏角度/选帧计算，避免依赖某张赛道的素材。
            Write(data + 0x200, new byte[] { 0, 0, 0, 0, 0, 1, 8, 0, 255, 0, 0, 0, 1, 0, 0, 0 });
            Write(data + 0x280, new byte[] { 2, 0, 0, 0, 0, 1, 8, 0, 255, 0, 0, 0, 1, 0, 0, 0 });
            Put(data + 0x290, data + 0x700);
            Put(data + 0x210, data + 0x700); Put(data + 0x600, 123); Put(data + 0x604, 45);
            for (int frame = 0; frame < 8; frame++)
            {
                Put(data + 0x2000 + frame * 32, data + 0x3000 + frame * 0x80);
                Put(data + 0x2008 + frame * 32, data + 0x600);
                Put(data + 0x3010 + frame * 0x80, frame);
                Put(data + 0x2200 + frame * 32, data + 0x3800 + frame * 0x80);
                Put(data + 0x2208 + frame * 32, data + 0x600);
                Put(data + 0x3810 + frame * 0x80, frame);
                for (int variant = 0; variant < 2; variant++)
                {
                    long meta = data + 0x4000 + variant * 0x200 + frame * 16;
                    int width = (frame == 6 ? 64 : 32) * (variant == 1 ? 3 : 2) / 2;
                    Put(meta, 0xBC614E); Put(meta + 4, 0); Put(meta + 8, width | ((variant == 1 ? 24 : 16) << 16));
                    Put(data + (variant == 0 ? 0x3008 : 0x3808) + frame * 0x80, meta);
                }
            }
            List<byte> lookup = new List<byte>(new byte[] { 0x8B, 0x44, 0x24, 0x04, 0xA3 }); D(lookup, data + 0x400);
            lookup.AddRange(new byte[] { 0xFF, 0x05 }); D(lookup, data + 0x404);
            lookup.AddRange(new byte[] { 0xC1, 0xE0, 0x05, 0xC1, 0xE2, 0x08, 0x01, 0xD0,
                0x8B, 0x54, 0x24, 0x08, 0xC7, 0x02, 0, 0, 0, 0, 0x05 });
            D(lookup, data + 0x2000); lookup.AddRange(new byte[] { 0xC2, 0x08, 0x00 });
            Write(image + 0x1A2D, lookup.ToArray());
            Write(image + 0x2C98, new byte[] { 0xC3 }); // 测试映像没有运行 CRT cookie 初始化
            Write(image + 0x46EC, new byte[] { 0xC2, 0x14, 0x00 });
            Write(image + 0x5CE14, new byte[] { 0x9D, 0x61, 0xC3 });
            Check(image, code, data, 0, 0, 0, 0, 4);
            Check(image, code, data, -0x400000, 0, 0, 0, 2);
            Check(image, code, data, 0x400000, 0, 0, 0, 6);
            Check(image, code, data, 0x800000, 0, 0, 0, 0);
            Check(image, code, data, 0, 0x400000, 0, 0, 6);
            Check(image, code, data, 0, 0, 10000 * 256, 0, 3);
            Check(image, code, data, 0, 0, -10000 * 256, 0, 5);
            Check(image, code, data, 0, 0, 0, 0x40000, 4);
            Console.WriteLine("8 original-game frame cases passed; heading, reverse, road perspective, removed motorcycle lean and repeat frames verified.");
            Check(image, code, data, 0, 0, 0, 0, 4, 0, true);
            Check(image, code, data, 0x400000, 0, 0, 0x40000, 6, 2, true);
            Check(image, code, data, 0, 0, 10000 * 256, 0, 3, 2, true);
            CheckHitbox(image, code, data);
            Console.WriteLine("9 original-game size/automatic-hitbox cases passed; selected style restored after canonical queries.");
            // 未使用矩形元数据时，原渲染器使用正值低9位或负值2的幂尺寸。
            for (int variant = 0; variant < 2; variant++)
            for (int frame = 0; frame < 8; frame++)
            {
                long sprite = data + (variant == 0 ? 0x3000 : 0x3800) + frame * 0x80;
                Put(data + 0x4000 + variant * 0x200 + frame * 16, 0);
                Put(sprite + 0x3C, variant == 0 ? (frame == 6 ? -26 : -27) : (frame == 6 ? 96 : 48));
                Put(sprite + 0x40, variant == 0 ? -28 : 24);
            }
            Check(image, code, data, 0, 0, 0, 0, 4, 0, true);
            Check(image, code, data, 0, 0, 0, 0, 4, 2, true);
            Console.WriteLine("2 encoded sprite size cases passed.");
        }
        finally { foreach (IntPtr block in Blocks) VirtualFree(block, UIntPtr.Zero, 0x8000); }
    }
}
