using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

// Explicit opt-in live smoke tests. Never apply disk patches or save settings.
// Each process belongs to this test and is stopped in finally.
internal static class RuntimeCompatibilityTests
{
    static readonly MethodInfo GetBase = typeof(RuntimePatch).GetMethod("GetBase", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo Apply = typeof(RuntimePatch).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo Read = typeof(RuntimePatch).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static);
    static long Base(Process p) { return ((IntPtr)GetBase.Invoke(null, new object[] { p })).ToInt64(); }
    static byte[] Bytes(Process p, long at, int size) { return (byte[])Read.Invoke(null, new object[] { p.Handle, at, size }); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo {
        public int cb; public string reserved, desktop, title; public int x, y, width, height, charsX, charsY, fill, flags;
        public short show, reservedCount; public IntPtr reservedBytes, input, output, error;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr process, thread; public int pid, tid; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, string command, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref StartupInfo si, out ProcessInfo pi);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static void SuspendedProbe(string directory) {
        StartupInfo si = new StartupInfo(); si.cb = Marshal.SizeOf<StartupInfo>(); si.flags=1; si.show=0;
        ProcessInfo pi;
        if (!CreateProcess(Path.Combine(directory,"RoadRash95.exe"), null, IntPtr.Zero, IntPtr.Zero, false, 4, IntPtr.Zero, directory, ref si, out pi))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {
            using (Process p=Process.GetProcessById(pi.pid)) {
                try {
                    try { Console.WriteLine("Suspended legacy MainModule="+p.MainModule.ModuleName+" 0x"+p.MainModule.BaseAddress.ToInt64().ToString("X")); }
                    catch (System.ComponentModel.Win32Exception e) { Console.WriteLine("Suspended legacy lookup failed: "+e.NativeErrorCode); }
                    long before=Base(p); if(before!=0)throw new Exception("Resolver accepted a module before WOW64 loader initialization");
                    ResumeThread(pi.thread);
                    long after=0; for(int i=0;i<100&&after==0;i++){Thread.Sleep(50);after=Base(p);}
                    if(after<=0||after>uint.MaxValue)throw new Exception("Resolver did not recover on same Process instance");
                    Console.WriteLine("PASS suspended startup: rejected native module, recovered PE32 image=0x"+after.ToString("X"));
                } finally { if(!p.HasExited){p.Kill();p.WaitForExit(5000);} }
            }
        } finally { CloseHandle(pi.thread);CloseHandle(pi.process); }
    }
    static int Main(string[] args)
    {
        try {
            if (args.Length != 2 || (args[0] != "--probe" && args[0] != "--install")) throw new ArgumentException("--probe|--install GAME_DIRECTORY");
            if (Process.GetProcessesByName("RoadRash95").Length != 0) throw new Exception("Exit the game before live verification.");
            string directory = Path.GetFullPath(args[1]);
            if(args[0]=="--probe") SuspendedProbe(directory);
            string[] scenarios = args[0] == "--probe" ? new[] { "probe-1", "probe-2", "probe-3" } :
                new[] { "fixes", "long-run", "truck-combat", "car-14", "collision-overlay", "debug", "repeat-fixes" };
            foreach (string scenario in scenarios) {
                using (Process p = Process.Start(new ProcessStartInfo(Path.Combine(directory, "RoadRash95.exe")) {
                    WorkingDirectory = directory, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })) {
                    try {
                        if (args[0] == "--probe") {
                            long early = Base(p);
                            Console.WriteLine(scenario + " early GetBase=0x" + early.ToString("X"));
                            Thread.Sleep(1000);
                            Console.WriteLine(scenario + " same Process GetBase=0x" + Base(p).ToString("X"));
                            p.Refresh();
                            Console.WriteLine(scenario + " refreshed MainModule=" + p.MainModule.ModuleName + " base=0x" + p.MainModule.BaseAddress.ToInt64().ToString("X"));
                            continue;
                        }
                        var values = new Dictionary<string, object> { ["process"]=p, ["longFactor"]=1, ["lateralAllFactor"]=1,
                            ["lateralPlayerFactor"]=1, ["aoeTargets"]=3, ["courseIndex"]=-1,
                            ["independentAudio"]=true, ["fallAnimationFix"]=true };
                        var logs = new List<string>();
                        values["log"] = (Action<string>)(s => { logs.Add(s); Console.WriteLine(scenario + ": " + s); });
                        if (scenario == "long-run") {
                            foreach (string name in new[] { "longRun", "hideFallenVehicles", "aiRunVariation", "infiniteDurability", "onFootProtection", "onFootEveryone", "crazyDrivers" }) values[name] = true;
                            values["playerWalkSpeed"] = 4m; values["aiWalkSpeed"] = 4m; values["remountPolicy"] = 3;
                            values["sameTrafficSpeed"] = 4m; values["oppositeTrafficSpeed"] = 33m;
                        }
                        if (scenario == "truck-combat") {
                            foreach (string name in new[] { "carShell", "ramKnockdown", "invincible", "rockSolid", "exaggeratedLaunch", "launchEveryone", "oneHit", "noDirection", "aoeEnabled", "aoeAll", "downedEnabled", "downedAll", "forceMap", "infiniteDurability" }) values[name]=true;
                            values["longFactor"]=4; values["lateralAllFactor"]=2; values["lateralPlayerFactor"]=3;
                            values["levelIndex"]=4; values["courseIndex"]=4;
                        }
                        if (scenario == "car-14") { values["carShell"]=true; values["carStyle"]=14; }
                        if (scenario == "collision-overlay") { values["carCollision"]=true; values["overlayCar"]=true; }
                        if (scenario == "debug") values["carDebugCopies"]=true;
                        object[] inputs = Apply.GetParameters().Select(a => values.ContainsKey(a.Name) ? values[a.Name] : a.HasDefaultValue ? a.DefaultValue : Activator.CreateInstance(a.ParameterType)).ToArray();
                        Apply.Invoke(null, inputs);
                        if (logs.Any(s => s.Contains("失败") || s.Contains("未安装"))) throw new Exception("Installer reported failure");
                        long image = Base(p);
                        if (image <= 0 || image > uint.MaxValue || Bytes(p, image, 2)[0] != 0x4D) throw new Exception("Invalid x86 game image");
                        if (Bytes(p, image + RuntimePatch.FallAnimationSite + 1, 1)[0] != 0x1C || Bytes(p, image + 0x45AD5, 1)[0] != 0xE9 || Bytes(p, image + 0x4546D, 1)[0] != 0x83) throw new Exception("Fix bytes absent");
                        if (scenario == "long-run" && Bytes(p, image + RuntimePatch.TrafficDriveSite, 1)[0] != 0xE9) throw new Exception("Traffic hook absent");
                        Thread.Sleep(1500);
                        if (p.HasExited) throw new Exception("Game exited during startup: " + p.ExitCode);
                        Console.WriteLine("PASS live installation " + scenario + " image=0x" + image.ToString("X"));
                    } finally { if (!p.HasExited) { p.Kill(); p.WaitForExit(5000); } }
                }
            }
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
