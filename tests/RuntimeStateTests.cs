using System;
using System.Collections.Generic;
using System.ComponentModel;

internal static class RuntimeStateTests
{
    private sealed class Memory
    {
        internal readonly Dictionary<long, byte[]> Blocks = new Dictionary<long, byte[]>();
        internal byte[] Read(long address, int length)
        {
            foreach (KeyValuePair<long, byte[]> block in Blocks)
            {
                long offset = address - block.Key;
                if (offset < 0 || offset + length > block.Value.Length) continue;
                byte[] result = new byte[length];
                Array.Copy(block.Value, (int)offset, result, 0, length);
                return result;
            }
            throw new Win32Exception(299);
        }
    }

    private static void Jump(Memory memory, long hook, long target)
    {
        byte[] bytes = new byte[5];
        bytes[0] = 0xE9;
        Array.Copy(BitConverter.GetBytes(unchecked((int)(target - hook - 5))), 0, bytes, 1, 4);
        memory.Blocks[hook] = bytes;
    }

    private static void Install(Memory memory, long module, long allocation, bool debug, int runs)
    {
        long cave = allocation + (debug ? 0xE00 : 0);
        Jump(memory, module + (debug ? 0x5C0AE : 0x66512), cave);
        memory.Blocks[cave] = debug
            ? RuntimePatch.BuildBikeCopyHookCode(module, cave, allocation + 0x1000)
            : RuntimePatch.BuildCarShellCode(module, allocation, allocation + 0x1000,
                allocation + 0x2000, allocation + 0x3000, false);
        byte[] state = new byte[0x1D0];
        Array.Copy(BitConverter.GetBytes(runs), 0, state, 0xD4, 4);
        Array.Copy(BitConverter.GetBytes(7), 0, state, 0xD8, 4);
        memory.Blocks[allocation + 0x1000] = state;
    }

    private static void Check(string name, Memory memory, long module, int? expected)
    {
        int[] result = RuntimePatch.ReadCarStateFromMemory(module, memory.Read);
        if (expected.HasValue)
        {
            if (result == null || result.Length != 116 || result[53] != expected.Value || result[54] != 7)
                throw new Exception(name + ": incorrect state/counters");
            if (!Launcher.FormatCarState(result).Contains("playerCopyDraws=7"))
                throw new Exception(name + ": counters missing from log");
        }
        else if (result != null) throw new Exception(name + ": expected no state");
        Console.WriteLine("PASS " + name);
    }

    private static void Main()
    {
        CheckModuleIdentity();
        Memory memory = new Memory();
        Check("no hooks", memory, 0x400000, null);
        foreach (long module in new long[] { 0x400000, 0xBB0000, 0x10000000 })
        {
            memory = new Memory();
            Install(memory, module, 0x5000000, true, 443);
            Check("debug only, module=" + module.ToString("X"), memory, module, 443);
        }
        memory = new Memory();
        Install(memory, 0x400000, 0x90000000, true, 444);
        Check("32-bit relative jump wrapping", memory, 0x400000, 444);
        memory = new Memory();
        Install(memory, 0x400000, 0x5000000, false, 12);
        Check("legacy car shell", memory, 0x400000, 12);
        Install(memory, 0x400000, 0x6000000, true, 99);
        Check("prefer active debug hook", memory, 0x400000, 99);
        memory.Blocks.Remove(0x6001000);
        Check("unreadable debug state falls back to shell", memory, 0x400000, 12);
        memory = new Memory();
        Install(memory, 0x400000, 0x5000000, true, 99);
        memory.Blocks[0x5000E00][7] ^= 1;
        Check("reject wrong counter address", memory, 0x400000, null);
        memory = new Memory();
        Install(memory, 0x400000, 0x5000000, false, 12);
        memory.Blocks[0x5000000][4] ^= 1;
        Check("reject unrelated shell jump", memory, 0x400000, null);
        memory = new Memory();
        Install(memory, 0xBB0000, 0x5000000, true, 123);
        memory.Blocks.Remove(0xBB0000 + 0x5C0AE);
        Jump(memory, 0xBB0000 + 0x5CE14, 0x5001200);
        memory.Blocks[0x5001200] = RuntimePatch.BuildRiderCopyHookCode(0xBB0000, 0x5001200, 0x5001000);
        Check("rider copy hook only", memory, 0xBB0000, 123);
        memory.Blocks[0x5001200][10] ^= 1;
        Check("reject unrelated rider hook", memory, 0xBB0000, null);
        memory = new Memory();
        Install(memory, 0xBB0000, 0x5000000, true, 321);
        memory.Blocks.Remove(0xBB0000 + 0x5C0AE);
        Jump(memory, 0xBB0000 + 0x5CE0F, 0x5001200);
        memory.Blocks[0x5001200] = RuntimePatch.BuildPlayerCarCode(0xBB0000, 0x5001200, 0x5001000, 0x5002000, 0x5003000);
        Check("player car hook only", memory, 0xBB0000, 321);
        memory.Blocks[0x5001200][4] ^= 1;
        Check("reject unrelated player car hook", memory, 0xBB0000, null);
        Console.WriteLine("14 runtime state regression cases passed.");
    }

    private static void CheckModuleIdentity()
    {
        Memory m = new Memory(); long image = 0x590000;
        byte[] dos = new byte[64], nt = new byte[84];
        dos[0] = 0x4D; dos[1] = 0x5A; dos[60] = 0x80;
        nt[0] = 0x50; nt[1] = 0x45; nt[4] = 0x4C; nt[5] = 1;
        nt[22] = 2; nt[24] = 0x0B; nt[25] = 1;
        Array.Copy(BitConverter.GetBytes(0x240000), 0, nt, 80, 4);
        m.Blocks[image] = dos; m.Blocks[image + 0x80] = nt;
        int count = 0;
        Action<bool, string> check = delegate(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS module identity: " + name); };
        check(RuntimePatch.IsGameImage("E:\\game\\RoadRash95.exe", image, m.Read), "valid relocated game");
        check(RuntimePatch.IsGameImage("roadrash95.EXE", image, m.Read), "case insensitive name");
        check(!RuntimePatch.IsGameImage("ntdll.dll", image, m.Read), "reject first native system DLL");
        check(!RuntimePatch.IsGameImage("RoadRash95.exe", 0x7FFD74870000, m.Read), "reject log's 64-bit module address");
        check(!RuntimePatch.IsGameImage("RoadRash95.exe", 0, m.Read), "not initialized");
        check(!RuntimePatch.IsGameImage("RoadRash95.exe", image + 1, m.Read), "unreadable module retries");
        nt[4] = 0x64; nt[5] = 0x86;
        check(!RuntimePatch.IsGameImage("RoadRash95.exe", image, m.Read), "reject x64 machine"); nt[4] = 0x4C; nt[5] = 1;
        nt[25] = 2; check(!RuntimePatch.IsGameImage("RoadRash95.exe", image, m.Read), "reject PE32+"); nt[25] = 1;
        nt[23] = 0x20; check(!RuntimePatch.IsGameImage("RoadRash95.exe", image, m.Read), "reject renamed DLL"); nt[23] = 0;
        nt[82] = 0; check(!RuntimePatch.IsGameImage("RoadRash95.exe", image, m.Read), "reject undersized image"); nt[82] = 0x24;
        dos[63] = 0x7F; check(!RuntimePatch.IsGameImage("RoadRash95.exe", image, m.Read), "reject invalid PE offset"); dos[63] = 0;
        dos[0] = 0; check(!RuntimePatch.IsGameImage("RoadRash95.exe", image, m.Read), "reject invalid DOS header");
        Console.WriteLine(count + " module identity regression cases passed.");
    }
}
