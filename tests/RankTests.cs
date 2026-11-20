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
    private static int clockReads;
    private static readonly RankClock FixtureClock = delegate(out long timestamp) { ++clockReads; timestamp = Stopwatch.GetTimestamp(); return 1; };
    private static RankCall CallRank(long target) {
        return (RankCall)Marshal.GetDelegateForFunctionPointer(new IntPtr(target), typeof(RankCall));
    }
    private static void CheckRankModes(string path, string source) {
        if (source != null) using (SHA256 sha = SHA256.Create())
            Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-","").ToLowerInvariant() == RankBatchNative.SourceSha256,
                "ranking machine code matches the tracked C source");
        CheckRankMode(path, false, false);
        CheckRankMode(path, true, false);
        CheckRankMode(path, false, true);
        CheckRankMode(path, true, true);
    }
    private static void CheckRankMode(string path, bool optimize, bool collectStats) {
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
        RuntimePatch.ApplyRankBatch(self, image, optimize, delegate {}, collectStats);
        if (!optimize && !collectStats) {
            Check(Get(image + RuntimePatch.RankDispatch + 1) == RuntimePatch.RankRoutine - RuntimePatch.RankDispatch - 5,
                "turning off the player switch leaves stock ranking installed");
            return;
        }
        RuntimePatch.ApplyRankBatch(self, image, optimize, delegate {}, collectStats);
        long entry = image + RuntimePatch.RankDispatch + 5 + Get(image + RuntimePatch.RankDispatch + 1);
        long state = (uint)Get(entry + 1), row = state + RuntimePatch.RankHeaderSize;
        Check(Get(state + 8) == (optimize ? 3 : 0), "production and diagnostic launches use the same fixed interval");
        bool equal = true;
        int initialClockReads = clockReads;
        var time = Stopwatch.StartNew();
        for (int i = 0; i < requests; ++i) {
            long r = riders + i * stride;
            int rank = (i == 0 ? timed : reload)(new IntPtr(r));
            equal &= rank == expected[i] && Get(r + 0x1fc) == rank && Get(r + 0x1f4) == (i == 0 ? 84 : 140);
        }
        time.Stop();
        Check(equal, "3000 burst results and deadlines match stock: optimization=" + optimize + " diagnostics=" + collectStats);
        if (collectStats) {
            Check(Get(row) == 1 && Get(row + 4) == 0 && Get(row + 8) == requests - 1 &&
                Get(row + 12) == (optimize ? 30 : requests) && Get(row + 20) == (optimize ? requests - 30 : 0) &&
                Get(row + 16) == (optimize ? 1 : 0) && Marshal.ReadInt64(new IntPtr(row + 24)) > 0, "developer diagnostics count stock scans, builds and indexed lookups");
            Console.WriteLine("RANK BENCH 10000 racers, 3000 original CALL sites, optimize={0}, elapsed_ms={1:F3}, scans={2}, builds={3}, indexed={4}",
                optimize, time.Elapsed.TotalMilliseconds, Get(row + 12), Get(row + 16), Get(row + 20));
        } else {
            Check(initialClockReads == clockReads && Get(row) == 0 && Get(row + 12) == 0 && Get(row + 20) == 0,
                "player optimization makes no timing calls and collects no rank counters");
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rank-play-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Put(image + 0x2036E8, 6);
            using (Process test = Process.GetCurrentProcess()) new RuntimePatch.RankTraceFile().PollImage(test, dir, image);
            Check(Directory.GetFiles(dir).Length == 0, "ordinary play produces no rank CSV even when polled");
            Put(image + 0x2036E8, 5);
        }
        if (!optimize) {
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
        int cached = expected[1], window = RuntimePatch.RankWindowTicks;
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
        Check(timed(q) == stock(q) && Get(state + 24) == 1, "rewound race clock invalidates the old ranking snapshot");
        for (int i = 0; i < 16; ++i) timed(q);
        Put(image + 0x18f1d8, 128); // The native count includes one extra slot.
        int scans = Get(row + 12), builds = Get(row + 16);
        bool smallMatches = true;
        for (int i = 0; i < 20; ++i) smallMatches &= timed(q) == stock(q);
        Check(smallMatches && (!collectStats || (Get(row + 12) == scans + 20 && Get(row + 16) == builds)),
            "roster shrink invalidates the index and dense requests on a small grid stay stock");
        Put(image + 0x18f1d8, 129);
        for (int i = 0; i < 16; ++i) timed(q);
        Check((!collectStats || Get(row + 16) == builds + 1) && timed(q) == stock(q),
            "128 actual riders qualify for a fresh index on the sixteenth AI request");
    }
}
