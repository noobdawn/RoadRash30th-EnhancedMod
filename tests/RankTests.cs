using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static partial class RaceTests
{
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int RankCall(IntPtr rider);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RankClock(out long timestamp);
    private static readonly RankClock FixtureClock = delegate(out long timestamp) { timestamp = Stopwatch.GetTimestamp(); return 1; };
    private static RankCall CallRank(long target) {
        return (RankCall)Marshal.GetDelegateForFunctionPointer(new IntPtr(target), typeof(RankCall));
    }
    private static void CheckRankModes(string path, string source) {
        if (source != null) using (SHA256 sha = SHA256.Create())
            Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-","").ToLowerInvariant() == RankBatchNative.SourceSha256,
                "ranking machine code matches the tracked C source");
        for (int mode = 0; mode <= 3; ++mode) CheckRankMode(path, mode);
    }
    private static void CheckRankMode(string path, int mode) {
        const int n = 10000, stride = 0x208, requests = 3000;
        long image = MapGame(path), table = image + 0xB0000, riders = Alloc(n * stride), roads = Alloc(0x400);
        // This fixture maps a local copy into the test process. A synthetic
        // roster allows both the 15-rider binary and the expanded build to be tested.
        Put(image + 0x16955, table);
        Put(image + 0x18F1D8, n + 1);
        Put(image + 0x2036E8, 5);
        Put(image + 0x21c3dc, Marshal.GetFunctionPointerForDelegate(FixtureClock).ToInt64());
        Put(image + 0x1A69EC, 1);
        for (int m = 0; m < 4; ++m) Marshal.WriteByte(new IntPtr(roads + 0x80 * m + 0x36), (byte)m);
        var random = new Random(417);
        for (int i = 0; i < n; ++i) {
            long r = riders + stride * i;
            Put(table + i * 4, i % 211 == 210 ? 0 : r);
            Put(r + 0x10, 80);
            Put(r + 0x1c4, i + 1);
            Put(r + 0x2c, i >= requests && i % 5 == 0 ? 0 : roads + (i % 4) * 0x80);
            Put(r + 0x1c, random.Next(-300, 301));
            Put(r + 0x1f8, random.Next(1, 501));
        }
        // Return from each mapped original CALL with its genuine return address,
        // distinguishing stream-ins from periodic requests in the counters.
        foreach (int site in new int[]{0x15a61,0x16048,0x162a3}) Write(image + site + 5, new byte[]{0xc3});
        var stock = CallRank(image + RuntimePatch.RankRoutine);
        var timed = CallRank(image + 0x15a61);
        var reload = CallRank(image + 0x16048);
        var expected = new int[requests];
        for (int i = 0; i < requests; ++i) expected[i] = stock(new IntPtr(riders + i * stride));
        IntPtr self = Process.GetCurrentProcess().Handle;
        RuntimePatch.ApplyRankBatch(self, image, mode, delegate {});
        if (mode == 0) {
            Check(Get(image + RuntimePatch.RankDispatch + 1) == RuntimePatch.RankRoutine - RuntimePatch.RankDispatch - 5,
                "default mode leaves stock ranking installed");
            return;
        }
        RuntimePatch.ApplyRankBatch(self, image, mode, delegate {});
        long entry = image + RuntimePatch.RankDispatch + 5 + Get(image + RuntimePatch.RankDispatch + 1);
        long state = (uint)Get(entry + 1), row = state + RuntimePatch.RankHeaderSize;
        bool equal = true;
        var time = Stopwatch.StartNew();
        for (int i = 0; i < requests; ++i) {
            long r = riders + i * stride;
            int rank = (i == 0 ? timed : reload)(new IntPtr(r));
            equal &= rank == expected[i] && Get(r + 0x1fc) == rank && Get(r + 0x1f4) == (i == 0 ? 84 : 140);
        }
        time.Stop();
        Check(equal, "3000 burst results and deadlines match the original binary mode=" + mode);
        Check(Get(row) == 1 && Get(row + 4) == 0 && Get(row + 8) == requests - 1 && Get(row + 12) + Get(row + 20) == requests &&
            Get(row + 16) == (mode == 1 ? 0 : 1) && Marshal.ReadInt64(new IntPtr(row + 24)) > 0, "separate player/reload/scan/build counts and timing mode=" + mode);
        Console.WriteLine("RANK BENCH 10000 racers, 3000 original CALL sites, mode={0}, elapsed_ms={1:F3}, scans={2}, builds={3}, indexed={4}",
            mode, time.Elapsed.TotalMilliseconds, Get(row + 12), Get(row + 16), Get(row + 20));
        byte[] raw = new byte[RuntimePatch.RankRowSize]; Marshal.Copy(new IntPtr(row),raw,0,raw.Length);
        Check(RuntimePatch.FormatRankCsv(raw,5,Get(state+8)).Contains("5,"+Get(state+8)+",1,0,2999,"), "CSV reports the actual closed tick and mode");
        if (mode == 1) {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"rank-trace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var writer = new RuntimePatch.RankTraceFile();
            using (Process test = Process.GetCurrentProcess()) {
                writer.PollImage(test, dir, image);
                Check(Directory.GetFiles(dir).Length == 0, "an in-progress tick is not read from the stats buffer");
                Put(image + 0x2036E8, 6);
                writer.PollImage(test, dir, image);
                string csv = Directory.GetFiles(dir,"rank-start-*.csv")[0];
                Check(File.ReadAllLines(csv).Length == 2, "first closed tick is published in the test directory");
                Put(image + 0x2036E8, 305);
                writer.PollImage(test, dir, image);
                string[] lines = File.ReadAllLines(csv);
                Check(lines.Length == 301 && lines[1].StartsWith("5,0,1,0,2999,3000,0,0,") &&
                    lines[300].StartsWith("304,0,0,0,0,0,0,0,"), "complete five-second CSV preserves prior rows and idle ticks");
            }
            return;
        }
        IntPtr q = new IntPtr(riders + stride);
        int cached = expected[1], window = mode == 2 ? 3 : 6;
        Put(q.ToInt64() + 0x1f8, 1); Put(q.ToInt64() + 0x1c, int.MaxValue);
        Put(image + 0x2036E8, 5 + window - 1);
        Check(timed(q) == cached && stock(q) != cached, "shared window includes its last tick and handles repeated requests");
        Put(riders + 0x1f8, 1); Put(riders + 0x1c, int.MaxValue);
        Check(timed(new IntPtr(riders)) == stock(new IntPtr(riders)), "player stays current inside a stale AI snapshot");
        Put(q.ToInt64() + 0x1f8, 0);
        Check(reload(q) == stock(q), "finish-distance query uses current native state");
        Put(q.ToInt64() + 0x1f8, 1);
        Marshal.WriteByte(q, 0xc0, 0x10);
        Check(reload(q) == stock(q), "finished rider flag bypasses the snapshot");
        Marshal.WriteByte(q, 0xc0, 0);
        Put(image + 0x2036E8, 5 + window);
        Check(timed(q) == stock(q), "first sparse query at expiration reads live state");
        for (int i = 0; i < 20; ++i) timed(q);
        Put(q.ToInt64() + 0x1c, int.MinValue);
        Marshal.WriteByte(new IntPtr(image + 0x218059), 1);
        Check(timed(q) == stock(q), "network race stays on the original function");
        Marshal.WriteByte(new IntPtr(image + 0x218059), 0);
        int epoch = Get(state + 12);
        Write(image + RuntimePatch.RankGridInit + 5, new byte[]{0xc3});
        Run(Alloc(0x1000), image + RuntimePatch.RankGridInit, 0);
        Put(image + 0x18F1D8, n + 1);
        Check(Get(state + 12) == epoch + 1 && timed(q) == stock(q), "new grid at identical tick invalidates the prior snapshot");
        Put(image + 0x2036E8, 1);
        Check(timed(q) == stock(q) && Get(state + 24) == 1, "rewound race clock starts a fresh trace");
        for (int i = 0; i < 16; ++i) timed(q);
        Put(image + 0x18f1d8, 128); // The native count includes one extra slot.
        int scans = Get(row + 12), builds = Get(row + 16);
        bool smallMatches = true;
        for (int i = 0; i < 20; ++i) smallMatches &= timed(q) == stock(q);
        Check(smallMatches && Get(row + 12) == scans + 20 && Get(row + 16) == builds,
            "roster shrink invalidates the index and dense requests on a small grid stay stock");
        Put(image + 0x18f1d8, 129);
        for (int i = 0; i < 16; ++i) timed(q);
        Check(Get(row + 16) == builds + 1 && timed(q) == stock(q),
            "128 actual riders qualify for a fresh index on the sixteenth AI request");
    }
}
