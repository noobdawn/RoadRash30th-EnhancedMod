using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class RuntimePatch
{
    internal const int RankDispatch = 0x49F8, RankRoutine = 0x16940, RankGridInit = 0x15487;
    internal const int RankTraceTicks = 300, RankHeaderSize = 32, RankRowSize = 32;
    private const int RankMagic = 0x314B4E52, RankDataOffset = 0x1000, RankNativeOffset = 0x80;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

    private static byte[] RankEntry(long entry)
    {
        var c = new CodeBuffer(entry);
        B(c, 0xBA); D(c, entry + RankDataOffset);
        JmpAbs(c, entry + RankNativeOffset + RankBatchNative.EntryOffset);
        return c.Finish();
    }
    private static byte[] RankInitOriginal(long image)
    {
        var c = new CodeBuffer(0);
        B(c, 0xA3); D(c, image + RvaRacerCount);
        return c.Finish();
    }
    private static long InstalledRankState(IntPtr process, long image)
    {
        long site = image + RankDispatch;
        byte[] jump = Read(process, site, 5);
        if (jump[0] != 0xE9) return 0;
        long entry = unchecked((uint)(site + 5 + BitConverter.ToInt32(jump, 1)));
        if (entry == image + RankRoutine || entry < 0x10000 || entry + RankDataOffset > uint.MaxValue) return 0;
        byte[] expected = RankEntry(entry), actual = Read(process, entry, expected.Length);
        for (int i = 0; i < actual.Length; ++i) if (actual[i] != expected[i]) return 0;
        long state = entry + RankDataOffset;
        return BitConverter.ToInt32(Read(process, state + 16, 4), 0) == RankMagic ? state : 0;
    }
    internal static void ApplyRankBatch(IntPtr process, long image, int mode, Action<string> log)
    {
        if (mode == 0) return;
        if (mode < 1 || mode > 3) throw new ArgumentOutOfRangeException("mode");
        int window = mode == 1 ? 0 : mode == 2 ? 3 : 6;
        long installed = InstalledRankState(process, image);
        if (installed != 0) {
            if (BitConverter.ToInt32(Read(process, installed + 8, 4), 0) != window)
                throw new InvalidOperationException("请重新启动比赛程序以切换名次模式。");
            return;
        }
        long gate = image + RankDispatch, init = image + RankGridInit;
        byte[] originalGate = Jmp(gate, image + RankRoutine, 5), originalInit = RankInitOriginal(image);
        RequireMotionSignature(process, gate, originalGate);
        RequireMotionSignature(process, init, originalInit);
        RequireMotionSignature(process, image + 0x16950, new byte[]{0x56,0x8A,0x40,0x36,0xBE});
        RequireMotionSignature(process, image + 0x169B9, new byte[]{0x89,0xB9,0xFC,1,0,0,0xBA,0x3C,0,0,0});
        foreach (int caller in new int[]{0x15A61,0x16048,0x162A3}) {
            var call = new CodeBuffer(image + caller); CallAbs(call, gate);
            RequireMotionSignature(process, image + caller, call.Finish());
        }
        // The enhancer relocates this table on large grids. Use the operand the
        // original function actually reads, including loader relocations.
        uint roster = BitConverter.ToUInt32(Read(process, image + 0x16955, 4), 0);
        int pe = BitConverter.ToInt32(Read(process, image + 0x3C, 4), 0);
        uint imageSize = BitConverter.ToUInt32(Read(process, image + pe + 80, 4), 0);
        if (roster < image || roster + 4L > image + imageSize)
            throw new InvalidOperationException("当前游戏的参赛表位置不符合此版本。");
        IntPtr block = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)0x81000, MemCommitReserve, 0x04);
        if (block == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        long entry = unchecked((uint)block.ToInt64()), state = entry + RankDataOffset;
        bool publishing = false;
        try {
            var header = new CodeBuffer(0);
            D(header, image); D(header, roster); D(header, window); D(header, 1); D(header, RankMagic);
            Write(process, state, header.Finish());
            Write(process, entry, RankEntry(entry));
            var reset = new CodeBuffer(entry + 0x20);
            B(reset, 0x9C, 0xFF, 0x05); D(reset, state + 12); B(reset, 0x9D);
            reset.Data.AddRange(originalInit);
            JmpAbs(reset, init + originalInit.Length);
            Write(process, entry + 0x20, reset.Finish());
            Write(process, entry + RankNativeOffset, RankBatchNative.Code);
            uint old;
            if (!VirtualProtectEx(process, block, (IntPtr)RankDataOffset, 0x20, out old))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            publishing = true;
            Write(process, init, Jmp(init, entry + 0x20, 5));
            Write(process, gate, Jmp(gate, entry, 5));
        } catch {
            if (publishing) { Write(process, gate, originalGate); Write(process, init, originalInit); }
            VirtualFreeEx(process, block, UIntPtr.Zero, 0x8000);
            throw;
        }
        log("起跑名次统计已启用。AI 合并窗口为 " + window + " 个模拟步（0 为原版对照）。");
    }

    internal sealed class RankTraceFile
    {
        private long image, state;
        private int savedRace, savedTicks;
        internal void Poll(Process game, string directory)
        {
            if (image == 0) image = GetBase(game).ToInt64();
            if (image != 0) PollImage(game, directory, image);
        }
        internal void PollImage(Process game, string directory, long module)
        {
            IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, game.Id);
            if (handle == IntPtr.Zero) return;
            try {
                if (state == 0) state = InstalledRankState(handle, module);
                if (state == 0) return;
                byte[] header = Read(handle, state, RankHeaderSize);
                int race = BitConverter.ToInt32(header, 12), first = BitConverter.ToInt32(header, 24);
                if (race == 0 || race != BitConverter.ToInt32(header, 20)) return;
                // Capture only closed simulation ticks. A long frame or a pause
                // can leave the current row open across several watcher polls.
                int clock = BitConverter.ToInt32(Read(handle, module + 0x2036E8, 4), 0);
                int ticks = (int)Math.Min(RankTraceTicks, (long)clock - first);
                if (ticks <= 0 || (savedRace == race && ticks <= savedTicks)) return;
                byte[] rows = Read(handle, state + RankHeaderSize, ticks * RankRowSize);
                byte[] after = Read(handle, state + 12, 16);
                if (BitConverter.ToInt32(after, 0) != race || BitConverter.ToInt32(after, 8) != race ||
                    BitConverter.ToInt32(after, 12) != first) return;
                string name = "rank-start-" + game.Id + "-" + game.StartTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + race + ".csv";
                string path = Path.Combine(directory, name), temp = path + ".tmp";
                File.WriteAllText(temp, FormatRankCsv(rows, first, BitConverter.ToInt32(header, 8)), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                savedRace = race; savedTicks = ticks;
            } finally { CloseHandle(handle); }
        }
    }
    internal static string FormatRankCsv(byte[] rows, int firstTick, int window)
    {
        var text = new StringBuilder("tick,window_ticks,player,ai_timed,ai_reload,stock_scans,index_builds,index_queries,rank_ms\r\n");
        for (int off = 0; off < rows.Length; off += RankRowSize) {
            text.Append(firstTick + off / RankRowSize).Append(',').Append(window);
            for (int field = 0; field < 6; ++field) text.Append(',').Append(BitConverter.ToUInt32(rows, off + 4 * field));
            double ms = BitConverter.ToUInt64(rows, off + 24) * 1000.0 / Stopwatch.Frequency;
            text.Append(',').AppendLine(ms.ToString("F4", CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }
}
