using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Launcher
{
    internal const string ToolFile = "RoadRash30thEnhancement-v1.1.0.exe";
    internal static string GameDir;
    internal static string ToolPath;
    internal static Assembly ToolAsm;
    internal static Type OptionsType;
    internal static Type ManagerType;

    [STAThread]
    private static void Main(string[] args)
    {
        GameDir = DetectGameDir();
        ToolPath = FindTool();
        if (args.Length > 0)
        {
            RunCli(args);
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new LauncherForm());
    }

    internal static void RunCli(string[] args)
    {
        string mode = args[0].ToLowerInvariant();
        string logPath = Path.Combine(GameDir, "launcher_cli.log");
        StringBuilder log = new StringBuilder();
        Action<string> report = delegate(string s)
        {
            log.AppendLine(s);
        };
        try
        {
            if (mode == "--audio-diag" && args.Length > 1)
            {
                File.WriteAllText(Path.Combine(GameDir, "audio_runtime.log"), RuntimePatch.ReadAudioState(Process.GetProcessById(int.Parse(args[1]))), Encoding.UTF8);
                return;
            }
            if (mode == "--audio-status")
            {
                int count = GameAudioSession.Inspect(0, Path.Combine(GameDir, "RoadRash95.exe"), report);
                report("匹配游戏音频会话数：" + count);
                File.WriteAllText(Path.Combine(GameDir, "audio_session.log"), log.ToString(), Encoding.UTF8);
                return;
            }
            if (mode == "--status")
            {
                LoadTool();
                bool recovery;
                object options = Inspect(out recovery);
                string text = "recovery=" + recovery + Environment.NewLine + Describe(options);
                File.WriteAllText(Path.Combine(GameDir, "launcher_status.txt"), text, Encoding.UTF8);
                return;
            }
            if (mode == "--dump-code")
            {
                string dir = Path.Combine(Path.GetTempPath(), "RoadRashRuntimeDump");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "aoe.bin"), RuntimePatch.BuildAoeCode(0x400000, 0x400000 + 0x235000 + 40004));
                File.WriteAllBytes(Path.Combine(dir, "downed.bin"), RuntimePatch.BuildDownedCode(0x400000));
                File.WriteAllBytes(Path.Combine(dir, "data.bin"), RuntimePatch.BuildAoeData(true, true, true, 2));
                File.WriteAllBytes(Path.Combine(dir, "level.bin"), RuntimePatch.BuildLevelCode(0x400000, 1, -1));
                File.WriteAllBytes(Path.Combine(dir, "level_course.bin"), RuntimePatch.BuildLevelCode(0x400000, 3, 2));
                File.WriteAllBytes(Path.Combine(dir, "level_entry.bin"), RuntimePatch.BuildLevelEntryCode(0x400000, 2));
                File.WriteAllBytes(Path.Combine(dir, "ram.bin"), RuntimePatch.BuildRamCode(0x400000));
                File.WriteAllBytes(Path.Combine(dir, "ram_native.bin"), RuntimePatch.BuildRamCode(0x400000, true));
                File.WriteAllBytes(Path.Combine(dir, "midi_volume.bin"), RuntimePatch.BuildMidiScaleCode(0x400000, 0x5100000, true));
                File.WriteAllBytes(Path.Combine(dir, "invincible.bin"), RuntimePatch.BuildInvincibleCode(0x400000, 0x406500));
                File.WriteAllBytes(Path.Combine(dir, "invincible_bike.bin"), RuntimePatch.BuildInvincibleBikeCode(0x400000, 0x406580));
                File.WriteAllBytes(Path.Combine(dir, "car_shell.bin"), RuntimePatch.BuildCarShellCode(0x400000, 0x5000000, 0x5001000, 0x5002000, 0x5003000, false));
                File.WriteAllBytes(Path.Combine(dir, "car_shell_debug.bin"), RuntimePatch.BuildCarShellCode(0x400000, 0x5000000, 0x5001000, 0x5002000, 0x5003000, true));
                File.WriteAllBytes(Path.Combine(dir, "bike_copies.bin"), RuntimePatch.BuildBikeCopyWrapper(0x400000, 0x5000600, 0x5001000));
                File.WriteAllBytes(Path.Combine(dir, "bike_copy_hook.bin"), RuntimePatch.BuildBikeCopyHookCode(0x400000, 0x5000E00, 0x5001000));
                File.WriteAllBytes(Path.Combine(dir, "bike_probe.bin"), RuntimePatch.BuildBikeProbeCode(0x400000, 0x5000A00, 0x5001000));
                File.WriteAllBytes(Path.Combine(dir, "rider_copy_hook.bin"), RuntimePatch.BuildRiderCopyHookCode(0x400000, 0x5001200, 0x5001000));
                File.WriteAllBytes(Path.Combine(dir, "player_car.bin"), RuntimePatch.BuildPlayerCarCode(0x400000, 0x5001200, 0x5001000, 0x5002000, 0x5003000, 0, 0x5001800));
                File.WriteAllBytes(Path.Combine(dir, "car_contact.bin"), RuntimePatch.BuildCarContactCode(0x400000, 0x5000000, 0x5001000));
                File.WriteAllBytes(Path.Combine(dir, "car_bounds.bin"), RuntimePatch.BuildCarBoundsCode(0x400000, 0x5001800, 0x5001000, 0x5003000));
                File.WriteAllBytes(Path.Combine(dir, "car_hitbox.bin"), RuntimePatch.BuildCarHitboxCode(0x400000, 0x5000800, 0x5001000));
                File.WriteAllBytes(Path.Combine(dir, "car_wrapper.bin"), RuntimePatch.BuildCarDrawWrapper(0x400000, 0x5002000, 0x5000600, 0x5001088));
                File.WriteAllBytes(Path.Combine(dir, "screen_capture.bin"), RuntimePatch.BuildScreenCaptureCode(0x400000, 0x5000C00, 0x5001000));
                File.WriteAllBytes(Path.Combine(dir, "hitbox.bin"), RuntimePatch.BuildHitboxCode(0x400000, 0x5000800));
                File.WriteAllText(Path.Combine(dir, "paths.txt"),
                    Path.Combine(dir, "aoe.bin") + Environment.NewLine +
                    Path.Combine(dir, "downed.bin") + Environment.NewLine +
                    Path.Combine(dir, "data.bin") + Environment.NewLine +
                    Path.Combine(dir, "level.bin") + Environment.NewLine +
                    Path.Combine(dir, "level_course.bin") + Environment.NewLine +
                    Path.Combine(dir, "level_entry.bin") + Environment.NewLine +
                    Path.Combine(dir, "ram.bin") + Environment.NewLine +
                    Path.Combine(dir, "invincible.bin") + Environment.NewLine +
                    Path.Combine(dir, "invincible_bike.bin") + Environment.NewLine +
                    Path.Combine(dir, "car_shell.bin") + Environment.NewLine +
                    Path.Combine(dir, "car_shell_debug.bin") + Environment.NewLine +
                    Path.Combine(dir, "bike_copies.bin") + Environment.NewLine +
                    Path.Combine(dir, "bike_copy_hook.bin") + Environment.NewLine +
                    Path.Combine(dir, "bike_probe.bin") + Environment.NewLine +
                    Path.Combine(dir, "rider_copy_hook.bin") + Environment.NewLine +
                    Path.Combine(dir, "player_car.bin") + Environment.NewLine +
                    Path.Combine(dir, "car_wrapper.bin") + Environment.NewLine +
                    Path.Combine(dir, "screen_capture.bin") + Environment.NewLine +
                    Path.Combine(dir, "hitbox.bin"), Encoding.UTF8);
                return;
            }
            if (mode == "--ui-check")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (LauncherForm form = new LauncherForm())
                {
                    StringBuilder tree = new StringBuilder();
                    DumpControls(form, tree, 0);
                    File.WriteAllText(Path.Combine(GameDir, "launcher_ui.txt"), tree.ToString(), Encoding.UTF8);
                }
                return;
            }
            if (mode == "--watch" && args.Length >= 2)
            {
                Watch(int.Parse(args[1], CultureInfo.InvariantCulture), Array.IndexOf(args, "--audio-diagnostics") >= 0,
                    Array.IndexOf(args, "--rank-stats") >= 0);
                return;
            }
            if (mode == "--diag" && args.Length >= 2)
            {
                Process target = Process.GetProcessById(int.Parse(args[1], CultureInfo.InvariantCulture));
                int[] diag = RuntimePatch.ReadDiag(target);
                int[] car = RuntimePatch.ReadCarState(target);
                File.WriteAllText(Path.Combine(GameDir, "runtime_diag.txt"),
                    FormatDiag(diag) + Environment.NewLine +
                    (car == null ? "car: state unavailable" : FormatCarState(car)), Encoding.UTF8);
                return;
            }
            if (mode == "--overlay" && args.Length >= 3)
            {
                RunOverlay(int.Parse(args[1], CultureInfo.InvariantCulture), args[2]);
                return;
            }
            if (mode == "--overlay-test" && args.Length >= 2)
            {
                RunOverlayTest(args[1]);
                return;
            }
            if (mode == "--runtime-test" && args.Length >= 2)
            {
                Process target = Process.GetProcessById(int.Parse(args[1], CultureInfo.InvariantCulture));
                RuntimePatch.Apply(target, false, 1, 1, 1, false, false, false, 2,
                    false, false, true, false, false, false, false, false, false, 0, -1, report);
                File.WriteAllText(Path.Combine(GameDir, "runtime_test.log"), log.ToString(), Encoding.UTF8);
                return;
            }
            if (mode == "--runtime-test-all" && args.Length >= 2)
            {
                Process target = Process.GetProcessById(int.Parse(args[1], CultureInfo.InvariantCulture));
                RuntimePatch.Apply(target, false, 1, 1, 1, false, false, false, 2,
                    false, false, true, true, true, true, false, false, false, 0, -1, report);
                File.WriteAllText(Path.Combine(GameDir, "runtime_test_all.log"), log.ToString(), Encoding.UTF8);
                return;
            }
            if (mode == "--runtime-test-shell" && args.Length >= 2)
            {
                Process target = Process.GetProcessById(int.Parse(args[1], CultureInfo.InvariantCulture));
                RuntimePatch.Apply(target, false, 1, 1, 1, false, false, false, 2,
                    false, false, false, false, true, false, false, false, false, 0, -1, report);
                File.WriteAllText(Path.Combine(GameDir, "runtime_test_shell.log"), log.ToString(), Encoding.UTF8);
                return;
            }
            LoadTool();
            object opts;
            if (mode == "--restore")
            {
                opts = NewOptions();
            }
            else if (mode == "--apply")
            {
                opts = DefaultProfile();
            }
            else
            {
                throw new ArgumentException("Unknown argument: " + mode);
            }
            ApplyOptions(opts, report, true);
            File.WriteAllText(logPath, log.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Exception inner = Unwrap(ex);
            File.WriteAllText(logPath, log.ToString() + Environment.NewLine + inner.ToString(), Encoding.UTF8);
            Environment.ExitCode = 1;
        }
    }

    private static void RunOverlay(int pid, string imagePath)
    {
        if (!Path.IsPathRooted(imagePath))
            imagePath = Path.Combine(GameDir, imagePath);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        CarOverlayForm form = new CarOverlayForm(pid, imagePath);
        Application.Run(new ApplicationContext(form));
    }

    private static void RunOverlayTest(string imagePath)
    {
        if (!Path.IsPathRooted(imagePath))
            imagePath = Path.Combine(GameDir, imagePath);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        CarOverlayForm form = new CarOverlayForm(-1, imagePath, true, new Rectangle(240, 180, 512, 320), 6);
        Application.Run(new ApplicationContext(form));
    }

    internal static string FormatDiag(int[] d)
    {
        if (d == null) return "无法读取诊断数据（游戏可能已退出或补丁未写入）。";
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("hook=" + d[0] + " playerPairs=" + d[1] + " downOpponent=" + d[2] + " downPlayer=" + d[3]);
        sb.AppendLine("lastDiff=" + d[4] + " lastVictim=0x" + d[5].ToString("X8") + " player=0x" + d[6].ToString("X8") + " opponent=0x" + d[7].ToString("X8"));
        sb.AppendLine("lastReason=" + d[8] + " (1=大运模式撞倒对手)");
        if (d.Length >= 15)
        {
            sb.AppendLine("armor: calls=" + d[9] + " blocked=" + d[10] + " victim=0x" + d[11].ToString("X8"));
            sb.AppendLine("bikeArmor: calls=" + d[12] + " blocked=" + d[13] + " victim=0x" + d[14].ToString("X8"));
        }
        return sb.ToString();
    }

    internal static string FormatCarState(int[] d)
    {
        if (d == null) return "car: 未安装或无法读取";
        string s = "car: sprite=0x" + d[0].ToString("X8") + " type=" + d[6] + " active=" + d[7] +
            " count=" + d[21] + " result=" + d[22] +
            " sprite8=0x" + d[23].ToString("X8") + " traffic=" + d[24];
        if (d.Length >= 39)
        {
            s += " orig=0x" + d[1].ToString("X8") +
                " spriteC=0x" + d[25].ToString("X8") + " sprite3C=" + d[26] + " sprite40=" + d[27] +
                " bike98=0x" + d[28].ToString("X8") + " x=" + d[29] + " y=" + d[30] + " z=" + d[31] +
                " scene=0x" + d[32].ToString("X8") + " draw=0x" + d[33].ToString("X8") +
                " wrapperCalls=" + d[34] + " b44=" + d[35] + " b48=" + d[36] +
                " b50=" + d[37] + " b4c=" + d[38];
            if (d.Length >= 55)
                s += " debugRuns=" + d[53] + " playerCopyDraws=" + d[54];
            if (d.Length >= 64)
                s += " drawEntries=" + d[45] + " playerCallbacks=" + d[44] +
                    " callback=0x" + d[51].ToString("X8") +
                    " bikeChecks=" + d[56] + " playerChecks=" + d[57] + " nullSprite=" + d[58] +
                    " lastActor=0x" + d[59].ToString("X8") + " expectedPlayer=0x" + d[60].ToString("X8") +
                    " playerSprite=0x" + d[61].ToString("X8") + " viewBike=0x" + d[62].ToString("X8") +
                    " playerModel=0x" + d[63].ToString("X8");
            if (d.Length >= 76)
                s += " riderRuns=" + d[64] + " playerRiderDraws=" + d[65] +
                    " lastRider=0x" + d[66].ToString("X8") + " expectedRider=0x" + d[67].ToString("X8") +
                    " riderSprite=0x" + d[68].ToString("X8") + " riderModel=0x" + d[69].ToString("X8") +
                    " riderContext=0x" + d[70].ToString("X8") + " riderY=" + d[71] + " riderX=" + d[72] +
                    " riderBike=0x" + d[73].ToString("X8") + " riderCopyCalls=" + d[74] + " riderNullSprite=" + d[75];
            if (d.Length >= 78) s += " riderScale=" + d[76] + " riderBadScale=" + d[77];
            if (d.Length >= 93)
                s += " carHookCalls=" + d[80] + " carPlayerCalls=" + d[81] + " carDraws=" + d[82] +
                    " carFallbacks=" + d[83] + " carActor=0x" + d[84].ToString("X8") +
                    " carSprite=0x" + d[85].ToString("X8") + " carFrameBuilds=" + d[86] +
                    " carType=" + d[87] + " carTypeCount=" + d[88] + " carFlags=0x" + d[89].ToString("X") +
                    " carMeta=0x" + d[90].ToString("X8") + " carWrapFlags=0x" + d[91].ToString("X") +
                    " carReason=" + d[92];
            if (d.Length >= 96)
                s += " carYaw=" + d[93] + " sourceLean=" + d[94] + " carModel=0x" + d[95].ToString("X8");
            if (d.Length >= 104)
                s += " contactCalls=" + d[96] + " playerContacts=" + d[97] + " contactHeightFixes=" + d[98] +
                    " contactVelocityFixes=" + d[99] + " contactHeightBefore=" + d[100] + " contactHeightAfter=" + d[101] +
                    " contactVelocityBefore=" + d[102] + " contactVelocityAfter=" + d[103];
            if (d.Length >= 112)
                s += " carBoundsReady=" + d[104] + " carHalfWidth=" + d[105] + " carHalfLength=" + d[106] +
                    " carHalfHeight=" + d[107] + " carBoundsChecks=" + d[108] + " carBoundsReason=" + d[109];
            if (d.Length >= 116)
                s += " requestedCarType=" + d[112] + " carStyleFallback=" + d[113] + " carDirectionFlags=0x" + d[114].ToString("X");
        }
        return s;
    }

    private static void Watch(int pid, bool audioDiagnostics = false, bool rankStats = false)
    {
        string path = Path.Combine(GameDir, "runtime_stats.log");
        StringBuilder log = new StringBuilder();
        log.AppendLine("运行时撞击/汽车外观统计（每秒检查一次）");
        int lastHook = -1, lastOpp = -1, lastPlayer = -1, lastCallback = 0;
        string lastCarLine = null;
        int audioAttempts = 0;
        bool audioPending = audioDiagnostics;
        var rankTrace = rankStats ? new RuntimePatch.RankTraceFile() : null;
        try
        {
            while (true)
            {
                Process p;
                try { p = Process.GetProcessById(pid); }
                catch { break; }
                if (p.HasExited) break;
                if (rankTrace != null) {
                    try { rankTrace.Poll(p, GameDir); }
                    catch (Win32Exception) { }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                if (audioPending && audioAttempts++ < 60)
                {
                    try
                    {
                        StringBuilder audioLog = new StringBuilder();
                        int matches = GameAudioSession.Inspect(pid, Path.Combine(GameDir, "RoadRash95.exe"),
                            delegate(string message) { audioLog.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + message); });
                        if (matches > 0)
                        {
                            audioPending = false;
                            audioLog.AppendLine(RuntimePatch.ReadAudioState(p));
                            File.WriteAllText(Path.Combine(GameDir, "audio_session.log"), audioLog.ToString(), Encoding.UTF8);
                            log.Append(audioLog); File.WriteAllText(path, log.ToString(), Encoding.UTF8);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (audioAttempts == 1) File.WriteAllText(Path.Combine(GameDir, "audio_session.log"), "音频会话检查失败：" + ex.Message, Encoding.UTF8);
                    }
                }
                int callback = RuntimePatch.ReadPlayerCallback(p);
                if (callback != 0 && callback != lastCallback)
                {
                    lastCallback = callback;
                    string dump = Path.Combine(GameDir, "callback_code.bin");
                    if (RuntimePatch.DumpCallbackCode(p, callback, dump))
                        log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " callback=0x" + callback.ToString("X8") + " code dumped");
                }
                int[] d = RuntimePatch.ReadDiag(p);
                if (d != null && (d[0] != lastHook || d[2] != lastOpp || d[3] != lastPlayer))
                {
                    log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + FormatDiag(d).Replace(Environment.NewLine, " | ").TrimEnd(' ', '|'));
                    File.WriteAllText(path, log.ToString(), Encoding.UTF8);
                    lastHook = d[0];
                    lastOpp = d[2];
                    lastPlayer = d[3];
                }
                int[] car = RuntimePatch.ReadCarState(p);
                string carLine = car == null ? null : FormatCarState(car);
                if (carLine != null && carLine != lastCarLine)
                {
                    log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + carLine);
                    File.WriteAllText(path, log.ToString(), Encoding.UTF8);
                    lastCarLine = carLine;
                }
                Thread.Sleep(1000);
            }
        }
        catch
        {
        }
        log.AppendLine("游戏已退出。");
        try { File.WriteAllText(path, log.ToString(), Encoding.UTF8); } catch { }
    }

    internal static object NewOptions()
    {
        return Activator.CreateInstance(OptionsType);
    }

    internal static object DefaultProfile()
    {
        object o = NewOptions();
        Set(o, "Chinese", true);
        Set(o, "HD", true);
        Set(o, "Racers", 100);
        Set(o, "OpponentAggression", 3);
        Set(o, "PoliceMultiplier", 2);
        Set(o, "TrafficMultiplier", 2);
        Set(o, "FrameRate", 60);
        Set(o, "RoadWidth", 1m);
        return o;
    }

    internal static void LoadTool()
    {
        if (ToolAsm != null) return;
        if (!File.Exists(ToolPath))
            throw new FileNotFoundException("缺少增强补丁：" + ToolPath);
        ToolAsm = Assembly.LoadFrom(ToolPath);
        OptionsType = ToolAsm.GetType("Options", true);
        ManagerType = ToolAsm.GetType("PatchManager", true);
    }

    internal static object Inspect(out bool recovery)
    {
        object manager = Activator.CreateInstance(ManagerType, new object[] { GameDir });
        object[] args = new object[] { false };
        object options = ManagerType.GetMethod("Inspect").Invoke(manager, args);
        recovery = (bool)args[0];
        return options;
    }

    internal static void ApplyOptions(object options, Action<string> report, bool offline)
    {
        object manager = Activator.CreateInstance(ManagerType, new object[] { GameDir });
        MethodInfo apply = ManagerType.GetMethod("Apply", new Type[] { OptionsType, typeof(Action<string>), typeof(bool), typeof(bool) });
        apply.Invoke(manager, new object[] { options, report, offline, false });
    }

    internal static void Set(object o, string name, object value)
    {
        OptionsType.GetProperty(name).SetValue(o, value, null);
    }

    internal static T Get<T>(object o, string name)
    {
        return (T)OptionsType.GetProperty(name).GetValue(o, null);
    }

    internal static string Describe(object o)
    {
        if (o == null) return "无";
        StringBuilder sb = new StringBuilder();
        sb.Append("中文=").Append(Get<bool>(o, "Chinese"));
        sb.Append(" 高清=").Append(Get<bool>(o, "HD"));
        sb.Append(" 人数=").Append(Get<int>(o, "Racers") == 0 ? 15 : Get<int>(o, "Racers"));
        sb.Append(" 攻击倾向=").Append(Get<int>(o, "OpponentAggression"));
        sb.Append(" 警车倍率=").Append(Get<int>(o, "PoliceMultiplier"));
        sb.Append(" 汽车倍率=").Append(Get<int>(o, "TrafficMultiplier"));
        sb.Append(" 帧率=").Append(Get<int>(o, "FrameRate") == 0 ? 60 : Get<int>(o, "FrameRate"));
        sb.Append(" 路宽=").Append(Get<decimal>(o, "RoadWidth"));
        return sb.ToString();
    }

    internal static Exception Unwrap(Exception ex)
    {
        TargetInvocationException tie = ex as TargetInvocationException;
        if (tie != null && tie.InnerException != null) return Unwrap(tie.InnerException);
        return ex;
    }

    internal static string DetectGameDir()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(dir, "RoadRash95.exe"))) return dir;
        string[] keys = new string[]
        {
            @"SOFTWARE\WOW6432Node\Electronic Arts\RoadRash 95",
            @"SOFTWARE\Electronic Arts\RoadRash 95"
        };
        foreach (string key in keys)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key))
                {
                    if (k != null)
                    {
                        string p = k.GetValue("path") as string;
                        if (!string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, "RoadRash95.exe"))) return p;
                    }
                }
            }
            catch { }
        }
        return dir;
    }

    internal static string FindTool()
    {
        string a = Path.Combine(GameDir, "Tools", ToolFile);
        if (File.Exists(a)) return a;
        string b = Path.Combine(GameDir, ToolFile);
        if (File.Exists(b)) return b;
        return a;
    }

    internal static bool TryResetStateIfVanilla()
    {
        string work = Path.Combine(GameDir, ".RoadRash30thPatch");
        string state = Path.Combine(work, "state.json");
        string pending = Path.Combine(work, "pending.json");
        if (!File.Exists(state) || File.Exists(pending)) return false;
        if (HashFile(Path.Combine(GameDir, "RoadRash95.exe")) != "9e40e25737149ab095061b7154797a35495faea97644fafc5a1e616f1400c968") return false;
        if (HashFile(Path.Combine(GameDir, "TEXT", "ENU.DLL")) != "2309d5b30560103c7a8b3b97e626877fb89fd85164f82186082dc1c665466e70") return false;
        if (HashFile(Path.Combine(GameDir, "DATA", "GLOBAL.RSC")) != "3f579b842129bffbe8da1b5f430fb8054d76c1d5a292ca32d7f66223ffef66ee") return false;
        string backup = state + ".steamverify.bak";
        try
        {
            string hd = Path.Combine(GameDir, "RoadRashHD.dll");
            string ini = Path.Combine(GameDir, "RoadRashHD.ini");
            string hdBackup = Path.Combine(work, "steamverify_RoadRashHD.dll.bak");
            string iniBackup = Path.Combine(work, "steamverify_RoadRashHD.ini.bak");
            if (File.Exists(hdBackup)) File.Delete(hdBackup);
            if (File.Exists(iniBackup)) File.Delete(iniBackup);
            if (File.Exists(hd)) File.Move(hd, hdBackup);
            if (File.Exists(ini)) File.Move(ini, iniBackup);
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(state, backup);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string HashFile(string path)
    {
        if (!File.Exists(path)) return "";
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(stream);
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    internal static bool GameRunning()
    {
        return Process.GetProcessesByName("RoadRash95").Length > 0 ||
               Process.GetProcessesByName("RoadRash95_mod").Length > 0;
    }

    private static void DumpControls(Control control, StringBuilder output, int depth)
    {
        output.Append(' ', depth * 2)
            .Append(control.GetType().Name)
            .Append(": ")
            .Append(control.Text);
        CheckBox check = control as CheckBox;
        if (check != null) output.Append(check.Checked ? " [x]" : " [ ]");
        output.AppendLine();
        foreach (Control child in control.Controls)
            DumpControls(child, output, depth + 1);
    }
}

internal sealed partial class LauncherForm : Form
{
    private readonly TextBox gamePath = new TextBox();
    private readonly CheckBox chinese = new CheckBox();
    private readonly CheckBox hd = new CheckBox();
    private readonly CheckBox skipCutscenes = new CheckBox();
    private readonly CheckBox noStun = new CheckBox();
    private readonly CheckBox rearEnd = new CheckBox();
    private readonly CheckBox oneHit = new CheckBox();
    private readonly CheckBox noDirection = new CheckBox();
    private readonly CheckBox aoe = new CheckBox();
    private readonly PresetComboBox aoeScope = new PresetComboBox();
    private readonly NumericUpDown aoeTargets = new NumericUpDown();
    private readonly CheckBox downed = new CheckBox();
    private readonly PresetComboBox downScope = new PresetComboBox();
    private readonly CheckBox ramKnockdown = new CheckBox();
    private readonly CheckBox invincible = new CheckBox();
    private readonly CheckBox carShell = new CheckBox();
    private readonly PresetComboBox carStyle = new PresetComboBox();
    private readonly CheckBox carCollision = new CheckBox();
    private readonly CheckBox carDebugCopies = new CheckBox();
    private readonly CheckBox overlayCar = new CheckBox();
    private readonly TextBox overlayImage = new TextBox();
    private readonly Button overlayBrowse = new Button();
    private readonly NumericUpDown longRange = new NumericUpDown();
    private readonly NumericUpDown lateralAll = new NumericUpDown();
    private readonly NumericUpDown lateralPlayer = new NumericUpDown();
    private bool uiSettingsLoaded;
    private readonly CheckBox forceName = new CheckBox();
    private readonly TextBox playerName = new TextBox();
    private readonly CheckBox forceBike = new CheckBox();
    private readonly PresetComboBox playerBike = new PresetComboBox();
    private readonly CheckBox forceWeapon = new CheckBox();
    private readonly PresetComboBox playerWeapon = new PresetComboBox();
    private readonly CheckBox forceMap = new CheckBox();
    private readonly PresetComboBox mapLevel = new PresetComboBox();
    private readonly PresetComboBox mapCourse = new PresetComboBox();
    private readonly PresetComboBox combat = new PresetComboBox();
    private readonly NumericUpDown aggression = new NumericUpDown();
    private readonly NumericUpDown racers = new NumericUpDown();
    private readonly NumericUpDown frameRate = new NumericUpDown();
    private readonly PresetComboBox roadWidth = new PresetComboBox();
    private readonly NumericUpDown police = new NumericUpDown();
    private readonly NumericUpDown traffic = new NumericUpDown();
    private readonly TextBox log = new TextBox();
    private readonly Label status = new Label();
    private readonly Button applyLaunch = new Button();
    private readonly Button applyOnly = new Button();
    private readonly Button startGame = new Button();
    private readonly Button restore = new Button();
    private readonly Button choose = new Button();
    private readonly Button detect = new Button();
    private readonly Button readme = new Button();
    private bool busy;

    private sealed class BikeChoice
    {
        public readonly int Id;
        private readonly string label;

        public BikeChoice(int id, string label)
        {
            Id = id;
            this.label = label;
        }

        public override string ToString()
        {
            return label;
        }
    }

    private static readonly BikeChoice[] BikeChoices = new BikeChoice[]
    {
        new BikeChoice(4, "破旧摩托-125cc 猎犬 小峰"),
        new BikeChoice(1, "破旧摩托-400cc 海盗 飞燕"),
        new BikeChoice(2, "破旧摩托-250cc 神风 ZYX 250"),
        new BikeChoice(0, "破旧摩托-450cc 万岁 运动450"),
        new BikeChoice(3, "破旧摩托-500cc 杀手鼠"),
        new BikeChoice(14, "运动摩托-600cc 利刃 侵略者"),
        new BikeChoice(13, "运动摩托-250cc 猎犬 巨人"),
        new BikeChoice(12, "运动摩托-750cc 神风 ZYX 750"),
        new BikeChoice(10, "运动摩托-750cc 恶魔 吸血鬼"),
        new BikeChoice(11, "运动摩托-1000cc DMG M10"),
        new BikeChoice(6, "超级摩托-600cc 海盗 渡鸦N"),
        new BikeChoice(8, "超级摩托-750cc 神风 ZYX 750N"),
        new BikeChoice(5, "超级摩托-750/1100cc 万岁 超级运动"),
        new BikeChoice(9, "超级摩托-900cc 利刃 刺客N"),
        new BikeChoice(7, "超级摩托-1000cc 恶魔 毒蛇N")
    };

    private static readonly string[] CourseDisplayNames = new string[]
    {
        "City", "Sierra", "Penin", "Napa", "PCH"
    };

    private static readonly string[] CourseFileNames = new string[]
    {
        "City", "Hiway", "Medly", "Napa", "Canyn"
    };

    public LauncherForm()
    {
        Text = "暴力摩托 30 周年纪念版 · 增强修改器";
        Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5f);
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 840);
        MinimumSize = new Size(900, 760);
        MaximizeBox = true;
        try
        {
            string icon = Path.Combine(Launcher.GameDir, "RASHICON.DLL");
            if (File.Exists(icon)) this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(icon);
        }
        catch { }

        BuildModernInterface();

        applyLaunch.Click += delegate { ApplyCurrent(true); };
        applyOnly.Click += delegate { ApplyCurrent(false); };
        startGame.Click += delegate { StartGame(); };
        restore.Click += delegate { RestoreVanilla(); };
        choose.Click += delegate { ChooseGame(); };
        detect.Click += delegate { DetectGame(); };
        readme.Click += delegate { OpenReadme(); };
        Shown += delegate { DetectGame(); };
        FormClosing += delegate { SaveUiSettings(); };
    }

    private Control BuildPathRow()
    {
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Fill;
        t.AutoSize = true;
        t.ColumnCount = 4;
        t.RowCount = 1;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gamePath.Dock = DockStyle.Fill;
        gamePath.ReadOnly = true;
        choose.Text = "选择…";
        choose.AutoSize = true;
        detect.Text = "自动检测";
        detect.AutoSize = true;
        t.Controls.Add(MakeLabel("游戏目录", 70), 0, 0);
        t.Controls.Add(gamePath, 1, 0);
        t.Controls.Add(choose, 2, 0);
        t.Controls.Add(detect, 3, 0);
        return t;
    }

    private TabPage BuildDisplayTab()
    {
        TabPage page = Tab("画面设置");
        TableLayoutPanel layout = PageLayout(2);
        layout.Controls.Add(BuildDisplayGroup(), 0, 0);
        layout.Controls.Add(BuildFrameGroup(), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildDisplayGroup()
    {
        GroupBox g = Group("显示与汉化");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 3;
        t.RowCount = 1;
        for (int i = 0; i < 3; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        chinese.Text = "简体中文汉化";
        chinese.AutoSize = true;
        chinese.Checked = true;
        hd.Text = "原生高清与宽屏";
        hd.AutoSize = true;
        hd.Checked = true;
        skipCutscenes.Text = "跳过过场动画";
        skipCutscenes.AutoSize = true;
        t.Controls.Add(chinese, 0, 0);
        t.Controls.Add(hd, 1, 0);
        t.Controls.Add(skipCutscenes, 2, 0);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildFrameGroup()
    {
        GroupBox g = Group("帧率");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 3;
        t.RowCount = 1;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        frameRate.Minimum = 60;
        frameRate.Maximum = 300;
        frameRate.Value = 60;
        frameRate.Increment = 10;
        frameRate.Width = 65;
        t.Controls.Add(MakeLabel("帧率上限", 65), 0, 0);
        t.Controls.Add(frameRate, 1, 0);
        Label hint = MakeLabel("60–300；原版为 60，只影响单人赛", 320);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 2, 0);
        g.Controls.Add(t);
        return g;
    }

    private TabPage BuildAiTab()
    {
        TabPage page = Tab("AI 设置");
        TableLayoutPanel layout = PageLayout(3);
        layout.Controls.Add(BuildRunAiGroup(), 0, 0);
        layout.Controls.Add(BuildAiCombatGroup(), 0, 1);
        layout.Controls.Add(BuildAllRangeGroup(), 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildAiCombatGroup()
    {
        GroupBox g = Group("AI 战斗");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 4;
        t.RowCount = 2;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        combat.DropDownStyle = ComboBoxStyle.DropDownList;
        combat.Dock = DockStyle.Fill;
        combat.MinimumSize = new Size(240, 0);
        combat.Items.Add("原版战斗动作");
        combat.Items.Add("另类模式 1 · 加强肘击和勾拳");
        combat.Items.Add("另类模式 2 · 额外调整脚踢");
        combat.SelectedIndex = 0;
        aggression.Minimum = 0;
        aggression.Maximum = 10;
        aggression.Value = 3;
        aggression.Width = 55;

        t.Controls.Add(MakeLabel("战斗规则", 70), 0, 0);
        t.Controls.Add(combat, 1, 0);
        t.SetColumnSpan(combat, 3);
        Label attackLabel = MakeLabel("对手攻击倾向", 95);
        t.Controls.Add(attackLabel, 0, 1);
        t.Controls.Add(aggression, 1, 1);
        Label hint = MakeLabel("0 只还手 / 1 原版 / 10 极度激进", 260);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 2, 1);
        t.SetColumnSpan(hint, 2);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildAllRangeGroup()
    {
        GroupBox g = Group("全员攻击范围");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 5;
        t.RowCount = 2;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        longRange.Minimum = 1;
        longRange.Maximum = 32;
        longRange.Value = 1;
        longRange.Width = 55;
        lateralAll.Minimum = 1;
        lateralAll.Maximum = 32;
        lateralAll.Value = 1;
        lateralAll.Width = 55;

        t.Controls.Add(MakeLabel("纵向（所有车手）", 130), 0, 0);
        t.Controls.Add(longRange, 1, 0);
        t.Controls.Add(MakeLabel("横向（所有车手）", 130), 2, 0);
        t.Controls.Add(lateralAll, 3, 0);
        Label hint = MakeLabel("1 = 原版，最大 32；对玩家和 AI 都生效", 360);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 4, 0);
        noDirection.Text = "取消攻击方向限制（所有车手）";
        noDirection.AutoSize = true;
        t.Controls.Add(noDirection, 0, 1);
        t.SetColumnSpan(noDirection, 5);
        g.Controls.Add(t);
        return g;
    }

    private TabPage BuildMapTab()
    {
        TabPage page = Tab("赛道与规模");
        TableLayoutPanel layout = PageLayout(6);
        layout.Controls.Add(BuildLongRunGroup(), 0, 0);
        layout.Controls.Add(BuildStartMapGroup(), 0, 1);
        layout.Controls.Add(BuildRaceSizeGroup(), 0, 2);
        layout.Controls.Add(BuildRoadGroup(), 0, 3);
        layout.Controls.Add(BuildTrafficDrivingGroup(), 0, 4);
        layout.Controls.Add(BuildTrackLengthGroup(), 0, 5);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildStartMapGroup()
    {
        GroupBox g = Group("开局地图");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 4;
        t.RowCount = 3;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        forceMap.Text = "指定开局地图";
        forceMap.AutoSize = true;
        forceMap.CheckedChanged += delegate
        {
            mapLevel.Enabled = forceMap.Checked;
            mapCourse.Enabled = forceMap.Checked;
        };

        mapLevel.DropDownStyle = ComboBoxStyle.DropDownList;
        mapLevel.Width = 150;
        for (int i = 0; i < CourseDisplayNames.Length; i++)
            mapLevel.Items.Add((i + 1) + " 级");
        mapLevel.SelectedIndex = 0;
        mapLevel.Enabled = false;

        mapCourse.DropDownStyle = ComboBoxStyle.DropDownList;
        mapCourse.Width = 150;
        mapCourse.Items.Add("跟随菜单");
        for (int i = 0; i < CourseDisplayNames.Length; i++)
            mapCourse.Items.Add(CourseDisplayNames[i]);
        mapCourse.SelectedIndex = 0;
        mapCourse.Enabled = false;

        t.Controls.Add(forceMap, 0, 0);
        t.SetColumnSpan(forceMap, 4);
        t.Controls.Add(MakeLabel("等级", 40), 0, 1);
        t.Controls.Add(mapLevel, 1, 1);
        t.Controls.Add(MakeLabel("地图", 40), 2, 1);
        t.Controls.Add(mapCourse, 3, 1);
        Label hint = MakeLabel("等级决定对手强度与进度；地图决定赛道。未指定时跟随游戏菜单。", 600);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 0, 2);
        t.SetColumnSpan(hint, 4);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildRaceSizeGroup()
    {
        GroupBox g = Group("比赛规模");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 3;
        t.RowCount = 1;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.Controls.Add(MakeLabel("参赛人数（含玩家）", 105), 0, 0);
        racers.Minimum = 4;
        racers.Maximum = 10000;
        racers.Value = 100;
        racers.Increment = 10;
        racers.Width = 85;
        t.Controls.Add(racers, 1, 0);
        Label racerHint = MakeLabel("4–10000，10000 为实验性", 260);
        racerHint.AutoSize = false;
        racerHint.Dock = DockStyle.Fill;
        racerHint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(racerHint, 2, 0);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildRoadGroup()
    {
        GroupBox g = Group("道路与交通");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 7;
        t.RowCount = 2;
        for (int i = 0; i < 6; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.Controls.Add(MakeLabel("马路宽度", 65), 0, 0);
        roadWidth.DropDownStyle = ComboBoxStyle.DropDownList;
        roadWidth.Width = 60;
        roadWidth.Items.Add("0.5");
        for (int i = 1; i <= 20; i++) roadWidth.Items.Add(i.ToString());
        roadWidth.SelectedIndex = 1;
        t.Controls.Add(roadWidth, 1, 0);
        t.Controls.Add(MakeLabel("警车倍率", 65), 2, 0);
        police.Minimum = 0;
        police.Maximum = 40;
        police.Value = 2;
        police.Width = 55;
        t.Controls.Add(police, 3, 0);
        t.Controls.Add(MakeLabel("汽车倍率", 65), 4, 0);
        traffic.Minimum = 0;
        traffic.Maximum = 40;
        traffic.Value = 2;
        traffic.Width = 55;
        t.Controls.Add(traffic, 5, 0);
        Label hint = MakeLabel("倍率为 0 表示关闭该类车辆", 260);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 0, 1);
        t.SetColumnSpan(hint, 7);
        g.Controls.Add(t);
        return g;
    }

    private TabPage BuildPlayerTab()
    {
        TabPage page = Tab("玩家与战斗");
        TableLayoutPanel layout = PageLayout(6);
        layout.Controls.Add(BuildIdentityGroup(), 0, 0);
        layout.Controls.Add(BuildCharacterGroup(), 0, 1);
        layout.Controls.Add(BuildPlayerCombatGroup(), 0, 2);
        layout.Controls.Add(BuildAoeGroup(), 0, 3);
        layout.Controls.Add(BuildTargetFilterGroup(), 0, 4);
        layout.Controls.Add(BuildDefenseGroup(), 0, 5);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildIdentityGroup()
    {
        GroupBox g = Group("身份");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 3;
        t.RowCount = 1;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        forceName.Text = "指定姓名";
        forceName.AutoSize = true;
        playerName.Dock = DockStyle.Fill;
        playerName.MaxLength = 19;
        playerName.Text = "玩家";
        playerName.Enabled = false;
        forceName.CheckedChanged += delegate { playerName.Enabled = forceName.Checked; };
        t.Controls.Add(forceName, 0, 0);
        t.Controls.Add(playerName, 1, 0);
        t.Controls.Add(MakeLabel("留空则使用“玩家”", 150), 2, 0);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildLoadoutGroup()
    {
        GroupBox g = Group("开局装备");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 3;
        t.RowCount = 3;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        forceBike.Text = "强制玩家车辆";
        forceBike.AutoSize = true;
        playerBike.DropDownStyle = ComboBoxStyle.DropDownList;
        playerBike.Width = 310;
        playerBike.DropDownWidth = 420;
        playerBike.Items.AddRange(BikeChoices);
        playerBike.SelectedItem = BikeChoices[BikeChoices.Length - 1];
        playerBike.Enabled = false;
        forceBike.CheckedChanged += delegate { playerBike.Enabled = forceBike.Checked; };

        forceWeapon.Text = "强制玩家武器";
        forceWeapon.AutoSize = true;
        playerWeapon.DropDownStyle = ComboBoxStyle.DropDownList;
        playerWeapon.Width = 120;
        playerWeapon.Items.Add("空手");
        playerWeapon.Items.Add("棍子");
        playerWeapon.Items.Add("铁链");
        playerWeapon.SelectedIndex = 1;
        playerWeapon.Enabled = false;
        forceWeapon.CheckedChanged += delegate { playerWeapon.Enabled = forceWeapon.Checked; };

        t.Controls.Add(forceBike, 0, 0);
        t.Controls.Add(playerBike, 1, 0);
        Label bikeHint = MakeLabel("车辆性能；应用后从下一局开始生效", 300);
        bikeHint.AutoSize = false;
        bikeHint.Dock = DockStyle.Fill;
        bikeHint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(bikeHint, 2, 0);

        t.Controls.Add(forceWeapon, 0, 1);
        t.Controls.Add(playerWeapon, 1, 1);
        Label weaponHint = MakeLabel("空手 / 棍子 / 铁链", 300);
        weaponHint.AutoSize = false;
        weaponHint.Dock = DockStyle.Fill;
        weaponHint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(weaponHint, 2, 1);

        Label hint = MakeLabel("未勾选时，车辆性能和武器由游戏存档决定。汽车外观款式在上方选择。", 560);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 0, 2);
        t.SetColumnSpan(hint, 3);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildPlayerCombatGroup()
    {
        GroupBox g = Group("玩家战斗");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 4;
        t.RowCount = 2;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        oneHit.Text = "一击必杀（仅玩家）";
        oneHit.AutoSize = true;
        lateralPlayer.Minimum = 1;
        lateralPlayer.Maximum = 32;
        lateralPlayer.Value = 1;
        lateralPlayer.Width = 55;

        t.Controls.Add(oneHit, 0, 0);
        t.SetColumnSpan(oneHit, 2);
        t.Controls.Add(MakeLabel("横向额外倍率（仅玩家）", 150), 2, 0);
        t.Controls.Add(lateralPlayer, 3, 0);
        Label hint = MakeLabel("玩家横向 = 全员横向 × 此倍率；1 = 原版，最大 32", 420);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 0, 1);
        t.SetColumnSpan(hint, 4);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildCarShellGroup()
    {
        GroupBox g = Group("汽车外观与碰撞");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 1;
        t.RowCount = 7;
        overlayCar.Text = "透明图片叠加（旧方案）";
        overlayCar.AutoSize = true;
        overlayCar.Checked = false;
        carShell.Text = "使用游戏内汽车外观";
        carShell.AutoSize = true;
        carShell.Checked = false;
        carCollision.Text = "单独放大摩托碰撞体（未开启汽车外观时使用）";
        carCollision.AutoSize = true;
        carCollision.Checked = false;
        carDebugCopies.Text = "调试：把玩家摩托 sprite 横向复制 5 份";
        carDebugCopies.AutoSize = true;
        carDebugCopies.Checked = false;

        FlowLayoutPanel imageRow = new FlowLayoutPanel();
        imageRow.AutoSize = true;
        imageRow.Dock = DockStyle.Fill;
        imageRow.WrapContents = false;
        imageRow.Controls.Add(MakeLabel("汽车图片", 70));
        overlayImage.Width = 420;
        try
        {
            overlayImage.Text = Path.Combine(Launcher.GameDir, "Tools", "player_car.png");
        }
        catch
        {
            overlayImage.Text = "Tools\\player_car.png";
        }
        overlayBrowse.Text = "选择…";
        overlayBrowse.AutoSize = true;
        overlayBrowse.Click += delegate
        {
            using (System.Windows.Forms.OpenFileDialog d = new System.Windows.Forms.OpenFileDialog())
            {
                d.Filter = "图片|*.png;*.bmp;*.jpg;*.jpeg|所有文件|*.*";
                d.Title = "选择汽车 PNG 图片";
                if (File.Exists(overlayImage.Text))
                    d.FileName = overlayImage.Text;
                if (d.ShowDialog(this) == DialogResult.OK)
                    overlayImage.Text = d.FileName;
            }
        };
        imageRow.Controls.Add(overlayImage);
        imageRow.Controls.Add(overlayBrowse);

        t.Controls.Add(overlayCar, 0, 0);
        t.Controls.Add(imageRow, 0, 1);
        t.Controls.Add(carShell, 0, 2);
        FlowLayoutPanel styleRow = new FlowLayoutPanel();
        styleRow.AutoSize = true;
        styleRow.Controls.Add(MakeLabel("游戏内车型", 90));
        carStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        carStyle.Width = 210;
        for (int i = 0; i < 15; i++) carStyle.Items.Add("款式 " + (i + 1).ToString("00") + "（赛道素材）");
        carStyle.SelectedIndex = 0;
        styleRow.Controls.Add(carStyle);
        
        t.Controls.Add(styleRow, 0, 3);
        t.Controls.Add(carCollision, 0, 4);
        FlowLayoutPanel physics = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown };
        autoCarCollision.Text = "碰撞体随车型大小匹配"; autoCarCollision.AutoSize = true; autoCarCollision.Checked = true;
        carStability.Text = "碰撞稳定（抑制碰撞带来的弹跳）"; carStability.AutoSize = true; carStability.Checked = true;
        physics.Controls.Add(autoCarCollision); physics.Controls.Add(carStability);
        t.Controls.Add(physics, 0, 5);
        Label hint = MakeLabel("款式来自当前赛道；缺少同向素材时自动选用可用的车尾款式。汽车保持直立，转向跟随驾驶方向。碰撞稳定只抑制碰撞弹跳；加速、制动仍沿用摩托车。", 900);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 0, 6);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildRamGroup()
    {
        GroupBox g = Group("撞击");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 1;
        t.RowCount = 3;
        ramKnockdown.Text = "碰撞其他车手使其倒地（大运模式）";
        ramKnockdown.AutoSize = true;
        ramKnockdown.Checked = true;
        invincible.Text = "霸体模式（玩家永不倒地）";
        invincible.AutoSize = true;
        invincible.Checked = false;
        t.Controls.Add(ramKnockdown, 0, 0);
        t.Controls.Add(invincible, 0, 1);
        Label hint = MakeLabel("大运模式：玩家碰到其他车手时对方直接倒地，不再设置速度差。霸体模式：玩家撞建筑、车辆或其他车手都不会倒地。", 760);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 0, 2);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildAoeGroup()
    {
        GroupBox g = Group("群体攻击");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 4;
        t.RowCount = 3;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        aoe.Text = "启用群体攻击";
        aoe.AutoSize = true;
        aoeScope.DropDownStyle = ComboBoxStyle.DropDownList;
        aoeScope.Width = 110;
        aoeScope.Items.Add("仅玩家");
        aoeScope.Items.Add("所有车手");
        aoeScope.SelectedIndex = 0;
        aoeTargets.Minimum = 2;
        aoeTargets.Maximum = 32;
        aoeTargets.Value = 3;
        aoeTargets.Width = 55;
        aoe.CheckedChanged += delegate
        {
            aoeScope.Enabled = aoe.Checked;
            aoeTargets.Enabled = aoe.Checked;
        };
        aoeScope.Enabled = false;
        aoeTargets.Enabled = false;

        t.Controls.Add(aoe, 0, 0);
        t.SetColumnSpan(aoe, 4);
        t.Controls.Add(MakeLabel("作用对象", 60), 0, 1);
        t.Controls.Add(aoeScope, 1, 1);
        t.Controls.Add(MakeLabel("最多命中（含主目标）", 140), 2, 1);
        t.Controls.Add(aoeTargets, 3, 1);
        Label hint = MakeLabel("只打当前攻击范围内的其他车手；3 = 主目标 + 最多 2 个额外目标；所有车手会明显增加 CPU 负担", 560);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 0, 2);
        t.SetColumnSpan(hint, 4);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildTargetFilterGroup()
    {
        GroupBox g = Group("目标筛选");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 3;
        t.RowCount = 2;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        downed.Text = "不把倒地车手作为目标";
        downed.AutoSize = true;
        downScope.DropDownStyle = ComboBoxStyle.DropDownList;
        downScope.Width = 110;
        downScope.Items.Add("仅玩家");
        downScope.Items.Add("所有车手");
        downScope.SelectedIndex = 0;
        downed.CheckedChanged += delegate { downScope.Enabled = downed.Checked; };
        downScope.Enabled = false;

        t.Controls.Add(downed, 0, 0);
        t.SetColumnSpan(downed, 3);
        t.Controls.Add(MakeLabel("作用对象", 60), 0, 1);
        t.Controls.Add(downScope, 1, 1);
        Label hint = MakeLabel("选中后，倒地/失速车手不再被选为攻击目标；若主目标已倒地，本次攻击会落空", 480);
        hint.AutoSize = false;
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        t.Controls.Add(hint, 2, 1);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildDefenseGroup()
    {
        GroupBox g = Group("防御");
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.ColumnCount = 2;
        t.RowCount = 1;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        noStun.Text = "取消被击中硬直";
        noStun.AutoSize = true;
        rearEnd.Text = "追尾保护（仅玩家）";
        rearEnd.AutoSize = true;
        t.Controls.Add(noStun, 0, 0);
        t.Controls.Add(rearEnd, 1, 0);
        g.Controls.Add(t);
        return g;
    }

    private Control BuildButtons()
    {
        FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        applyLaunch.Text = "应用并启动  →";
        applyLaunch.AutoSize = true; applyLaunch.Font = new Font(Font, FontStyle.Bold);
        applyOnly.Text = "仅应用设置"; applyOnly.AutoSize = true;
        row.Controls.Add(applyLaunch); row.Controls.Add(applyOnly);
        return row;
    }

    private static TabPage Tab(string text)
    {
        TabPage page = new TabPage(text);
        page.AutoScroll = true;
        page.BackColor = Canvas;
        page.Padding = new Padding(4);
        return page;
    }

    private static TableLayoutPanel PageLayout(int rows)
    {
        TableLayoutPanel t = new TableLayoutPanel();
        t.Dock = DockStyle.Top;
        t.AutoSize = true;
        t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        t.ColumnCount = 1;
        t.RowCount = rows;
        t.Padding = new Padding(10);
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < rows; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return t;
    }

    private static GroupBox Group(string text)
    {
        GroupBox g = new SettingsCard();
        g.BackColor = Color.White;
        g.Margin = new Padding(4, 4, 4, 8);
        g.Text = text;
        g.AutoSize = true;
        g.Dock = DockStyle.Fill;
        g.Padding = new Padding(14, 18, 14, 8);
        return g;
    }

    private static Label MakeLabel(string text, int width)
    {
        Label l = new Label();
        l.Text = text;
        l.AutoSize = false;
        using (Font measure = new Font("Microsoft YaHei UI", 9.5f))
            l.Width = width < 250 ? Math.Max(width, TextRenderer.MeasureText(text, measure).Width + 4) : width;
        l.Height = width >= 480 ? 54 : 28;
        if (width >= 250) l.ForeColor = Muted;
        l.TextAlign = ContentAlignment.MiddleLeft;
        return l;
    }

    private void DetectGame()
    {
        gamePath.Text = Launcher.DetectGameDir();
        RefreshState();
        if (!uiSettingsLoaded)
        {
            LoadUiSettings();
            uiSettingsLoaded = true;
        }
        SyncQuickSettings();
        changeStatus.Text = "已载入设置 · 应用并启动后生效";
    }

    private void ChooseGame()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "选择包含 RoadRash95.exe 的游戏目录";
            d.SelectedPath = gamePath.Text;
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                if (!File.Exists(Path.Combine(d.SelectedPath, "RoadRash95.exe")))
                {
                    MessageBox.Show(this, "该目录没有 RoadRash95.exe。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                gamePath.Text = d.SelectedPath;
                Launcher.GameDir = d.SelectedPath;
                Launcher.ToolPath = Launcher.FindTool();
                RefreshState();
                LoadUiSettings();
                uiSettingsLoaded = true;
                SyncQuickSettings();
            }
        }
    }

    private string UiSettingsPath
    {
        get
        {
            string dir = string.IsNullOrEmpty(gamePath.Text) ? Launcher.GameDir : gamePath.Text;
            return Path.Combine(dir, "launcher_settings.ini");
        }
    }

    private static void AddSetting(StringBuilder sb, string key, bool value)
    {
        sb.AppendLine(key + "=" + (value ? "1" : "0"));
    }

    private static void AddSetting(StringBuilder sb, string key, int value)
    {
        sb.AppendLine(key + "=" + value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool ReadBool(Dictionary<string, string> d, string key, bool fallback)
    {
        string s;
        if (!d.TryGetValue(key, out s)) return fallback;
        return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt(Dictionary<string, string> d, string key, int fallback)
    {
        string s;
        int v;
        if (!d.TryGetValue(key, out s) || !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            return fallback;
        return v;
    }

    private static void SetNumeric(NumericUpDown n, int value)
    {
        int min = (int)n.Minimum;
        int max = (int)n.Maximum;
        if (value < min) value = min;
        if (value > max) value = max;
        n.Value = value;
    }

    private static void SetCombo(ComboBox c, int index)
    {
        if (index >= 0 && index < c.Items.Count) c.SelectedIndex = index;
    }

    private void SaveUiSettings()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            AddSetting(sb, "settingsVersion", 11);
            AddSetting(sb,"sameTraffic",sameTraffic.Checked);AddSetting(sb,"oppositeTraffic",oppositeTraffic.Checked);
            AddSetting(sb,"nearbyImpactAudio",nearbyImpactAudio.Checked);AddSetting(sb,"forceTrackLength",forceTrackLength.Checked);sb.AppendLine("trackLengthKm="+TrackLengthUnits.Format(trackLength.Value));
            AddSetting(sb, "chinese", chinese.Checked);
            AddSetting(sb, "hd", hd.Checked);
            AddSetting(sb, "skipCutscenes", skipCutscenes.Checked);
            AddSetting(sb, "noStun", noStun.Checked);
            AddSetting(sb, "rearEnd", rearEnd.Checked);
            AddSetting(sb, "forceName", forceName.Checked);
            AddSetting(sb, "forceBike", forceBike.Checked);
            AddSetting(sb, "forceWeapon", forceWeapon.Checked);
            AddSetting(sb, "aggression", (int)aggression.Value);
            AddSetting(sb, "racers", (int)racers.Value);
            AddSetting(sb, "frameRate", (int)frameRate.Value);
            AddSetting(sb, "police", (int)police.Value);
            AddSetting(sb, "traffic", (int)traffic.Value);
            sb.AppendLine("sameTrafficSpeed="+sameTrafficSpeed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("oppositeTrafficSpeed="+oppositeTrafficSpeed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddSetting(sb, "crazyDrivers", crazyDrivers.Checked);
            AddSetting(sb, "combat", combat.SelectedIndex);
            AddSetting(sb, "playerBike", playerBike.SelectedIndex);
            AddSetting(sb, "playerWeapon", playerWeapon.SelectedIndex);
            AddSetting(sb, "roadWidth", roadWidth.SelectedIndex);
            sb.AppendLine("playerName=" + playerName.Text.Replace("\r", "").Replace("\n", ""));
            AddSetting(sb, "oneHit", oneHit.Checked);
            AddSetting(sb, "noDirection", noDirection.Checked);
            AddSetting(sb, "longRange", (int)longRange.Value);
            AddSetting(sb, "lateralAll", (int)lateralAll.Value);
            AddSetting(sb, "lateralPlayer", (int)lateralPlayer.Value);
            AddSetting(sb, "aoe", aoe.Checked);
            AddSetting(sb, "aoeScope", aoeScope.SelectedIndex);
            AddSetting(sb, "aoeTargets", (int)aoeTargets.Value);
            AddSetting(sb, "downed", downed.Checked);
            AddSetting(sb, "downScope", downScope.SelectedIndex);
            AddSetting(sb, "ramKnockdown", ramKnockdown.Checked);
            AddSetting(sb, "invincible", invincible.Checked);
            AddSetting(sb, "infiniteDurability", infiniteDurability.Checked);
            sb.AppendLine("playerWalkSpeed=" + playerWalkSpeed.Value.ToString("0.0", CultureInfo.InvariantCulture));
            sb.AppendLine("aiWalkSpeed=" + aiWalkSpeed.Value.ToString("0.0", CultureInfo.InvariantCulture));
            AddSetting(sb, "onFootProtection", onFootProtection.Checked);
            AddSetting(sb, "onFootScope", onFootScope.SelectedIndex);
            AddSetting(sb, "hideFallenVehicles", hideFallenVehicles.Checked);
            AddSetting(sb, "longRun", longRun.Checked);
            AddSetting(sb, "longRunScope", longRunScope.SelectedIndex);
            AddSetting(sb, "remountPolicy", remountPolicy.SelectedIndex);
            AddSetting(sb, "chaseDistance", (int)chaseDistance.Value);
            AddSetting(sb,"aiRunVariation",aiRunVariation.Checked);
            AddSetting(sb,"aiRunRhythm",aiRunRhythm.SelectedIndex);
            AddSetting(sb,"aiRunSprint",aiRunSprint.SelectedIndex);
            AddSetting(sb,"aiRunChase",aiRunChase.SelectedIndex);
            AddSetting(sb,"aiRunWeave",aiRunWeave.SelectedIndex);
            AddSetting(sb, "rockSolid", rockSolid.Checked);
            AddSetting(sb, "exaggeratedLaunch", exaggeratedLaunch.Checked);
            AddSetting(sb, "launchScope", launchScope.SelectedIndex);
            sb.AppendLine("launchMultiplier=" + launchMultiplier.Value.ToString("0.0", CultureInfo.InvariantCulture));
            AddSetting(sb, "overlayCar", overlayCar.Checked);
            sb.AppendLine("overlayImage=" + overlayImage.Text);
            AddSetting(sb, "overlayMigration", true);
            AddSetting(sb, "carShell", carShell.Checked);
            AddSetting(sb, "carStyle", carStyle.SelectedIndex);
            AddSetting(sb, "carStability", carStability.Checked);
            AddSetting(sb, "autoCarCollision", autoCarCollision.Checked);
            AddSetting(sb, "independentAudio", independentAudio.Checked);
            AddSetting(sb, "fallAnimationFix", fallAnimationFix.Checked);
            AddSetting(sb, "carCollision", carCollision.Checked);
            AddSetting(sb, "carDebugCopies", carDebugCopies.Checked);
            AddSetting(sb, "forceMap", forceMap.Checked);
            AddSetting(sb, "mapLevel", mapLevel.SelectedIndex);
            AddSetting(sb, "mapCourse", mapCourse.SelectedIndex);
            File.WriteAllText(UiSettingsPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void LoadUiSettings()
    {
        try
        {
            string path = UiSettingsPath;
            if (!File.Exists(path)) return;
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            chinese.Checked = ReadBool(d, "chinese", chinese.Checked);
            hd.Checked = ReadBool(d, "hd", hd.Checked);
            skipCutscenes.Checked = ReadBool(d, "skipCutscenes", skipCutscenes.Checked);
            noStun.Checked = ReadBool(d, "noStun", noStun.Checked);
            rearEnd.Checked = ReadBool(d, "rearEnd", rearEnd.Checked);
            forceName.Checked = ReadBool(d, "forceName", forceName.Checked);
            forceBike.Checked = ReadBool(d, "forceBike", forceBike.Checked);
            forceWeapon.Checked = ReadBool(d, "forceWeapon", forceWeapon.Checked);
            SetNumeric(aggression, ReadInt(d, "aggression", (int)aggression.Value));
            SetNumeric(racers, ReadInt(d, "racers", (int)racers.Value));
            SetNumeric(frameRate, ReadInt(d, "frameRate", (int)frameRate.Value));
            SetNumeric(police, ReadInt(d, "police", (int)police.Value));
            SetNumeric(traffic, ReadInt(d, "traffic", (int)traffic.Value));
            sameTrafficSpeed.Value=ReadTrafficSpeed(d,"sameTrafficSpeed");
            oppositeTrafficSpeed.Value=ReadTrafficSpeed(d,"oppositeTrafficSpeed");
            crazyDrivers.Checked=ReadBool(d,"crazyDrivers",false);
            sameTraffic.Checked=ReadBool(d,"sameTraffic",true);oppositeTraffic.Checked=ReadBool(d,"oppositeTraffic",true);
            nearbyImpactAudio.Checked=ReadBool(d,"nearbyImpactAudio",false);forceTrackLength.Checked=ReadBool(d,"forceTrackLength",false);trackLength.Value=TrackLengthUnits.ReadKilometers(d);
            SetCombo(combat, ReadInt(d, "combat", combat.SelectedIndex));
            SetCombo(playerBike, ReadInt(d, "playerBike", playerBike.SelectedIndex));
            SetCombo(playerWeapon, ReadInt(d, "playerWeapon", playerWeapon.SelectedIndex));
            SetCombo(roadWidth, ReadInt(d, "roadWidth", roadWidth.SelectedIndex));
            string savedName; if (d.TryGetValue("playerName", out savedName)) playerName.Text = savedName;
            oneHit.Checked = ReadBool(d, "oneHit", oneHit.Checked);
            noDirection.Checked = ReadBool(d, "noDirection", noDirection.Checked);
            SetNumeric(longRange, ReadInt(d, "longRange", (int)longRange.Value));
            SetNumeric(lateralAll, ReadInt(d, "lateralAll", (int)lateralAll.Value));
            SetNumeric(lateralPlayer, ReadInt(d, "lateralPlayer", (int)lateralPlayer.Value));
            aoe.Checked = ReadBool(d, "aoe", aoe.Checked);
            SetCombo(aoeScope, ReadInt(d, "aoeScope", aoeScope.SelectedIndex));
            SetNumeric(aoeTargets, ReadInt(d, "aoeTargets", (int)aoeTargets.Value));
            downed.Checked = ReadBool(d, "downed", downed.Checked);
            SetCombo(downScope, ReadInt(d, "downScope", downScope.SelectedIndex));
            ramKnockdown.Checked = ReadBool(d, "ramKnockdown", ramKnockdown.Checked);
            invincible.Checked = ReadBool(d, "invincible", invincible.Checked);
            infiniteDurability.Checked = ReadBool(d, "infiniteDurability", true);
            playerWalkSpeed.Value = ReadWalkSpeed(d, "playerWalkSpeed");
            aiWalkSpeed.Value = ReadWalkSpeed(d, "aiWalkSpeed");
            onFootProtection.Checked = ReadBool(d, "onFootProtection", false);
            SetCombo(onFootScope, ReadInt(d, "onFootScope", 0) == 1 ? 1 : 0);
            longRun.Checked = ReadBool(d, "longRun", false);
            hideFallenVehicles.Checked = ReadBool(d, "hideFallenVehicles", longRun.Checked);
            aiRunVariation.Checked=ReadBool(d,"aiRunVariation",longRun.Checked);
            aiRunRhythm.SelectedIndex=ReadRunAiLevel(d,"aiRunRhythm");
            aiRunSprint.SelectedIndex=ReadRunAiLevel(d,"aiRunSprint");
            aiRunChase.SelectedIndex=ReadRunAiLevel(d,"aiRunChase");
            aiRunWeave.SelectedIndex=ReadRunAiLevel(d,"aiRunWeave");
            int runScope = ReadInt(d, "longRunScope", 2), recovery = ReadInt(d, "remountPolicy", 0);
            longRunScope.SelectedIndex = runScope >= 0 && runScope <= 2 ? runScope : 2;
            remountPolicy.SelectedIndex = recovery >= 0 && recovery <= 3 ? recovery : 0;
            SetNumeric(chaseDistance, ReadInt(d, "chaseDistance", 96));
            overlayCar.Checked = ReadBool(d, "overlayCar", overlayCar.Checked);
            string overlayPath;
            if (d.TryGetValue("overlayImage", out overlayPath) && !string.IsNullOrEmpty(overlayPath))
                overlayImage.Text = overlayPath;
            carShell.Checked = ReadBool(d, "carShell", carShell.Checked);
            rockSolid.Checked = ReadBool(d, "rockSolid", carShell.Checked);
            exaggeratedLaunch.Checked = ReadBool(d, "exaggeratedLaunch", carShell.Checked && invincible.Checked && ramKnockdown.Checked);
            SetCombo(launchScope, ReadInt(d, "launchScope", 0));
            string launchText; decimal launchValue;
            if (!d.TryGetValue("launchMultiplier", out launchText) || !decimal.TryParse(launchText, NumberStyles.Number, CultureInfo.InvariantCulture, out launchValue)) launchValue = 1.5m;
            launchMultiplier.Value = Math.Max(1m, Math.Min(8m, decimal.Round(launchValue, 1)));
            carStability.Checked = ReadBool(d, "carStability", true);
            autoCarCollision.Checked = ReadBool(d, "autoCarCollision", true);
            independentAudio.Checked = ReadBool(d, "independentAudio", true);
            fallAnimationFix.Checked = ReadBool(d, "fallAnimationFix", true);
            SetCombo(carStyle, Math.Max(0, Math.Min(14, ReadInt(d, "carStyle", 0))));
            carCollision.Checked = ReadBool(d, "carCollision", carCollision.Checked);
            carDebugCopies.Checked = ReadBool(d, "carDebugCopies", carDebugCopies.Checked);
            if (!ReadBool(d, "overlayMigration", false) && !overlayCar.Checked && carShell.Checked)
            {
                string full;
                try { full = Path.IsPathRooted(overlayImage.Text) ? overlayImage.Text : Path.Combine(Launcher.GameDir, overlayImage.Text); }
                catch { full = ""; }
                if (!string.IsNullOrEmpty(full) && File.Exists(full))
                    overlayCar.Checked = true;
            }
            forceMap.Checked = ReadBool(d, "forceMap", forceMap.Checked);
            SetCombo(mapLevel, ReadInt(d, "mapLevel", mapLevel.SelectedIndex));
            SetCombo(mapCourse, ReadInt(d, "mapCourse", mapCourse.SelectedIndex));
        }
        catch
        {
        }
    }

    private void RefreshState()
    {
        RefreshState(false);
    }

    private void RefreshState(bool retried)
    {
        try
        {
            Launcher.GameDir = gamePath.Text;
            Launcher.ToolPath = Launcher.FindTool();
            Launcher.ToolAsm = null;
            Launcher.LoadTool();
            bool recovery;
            object opts = Launcher.Inspect(out recovery);
            Populate(opts);
            if (recovery)
            {
                status.Text = "状态：检测到未完成操作，请点击“应用”恢复。";
            }
            else
            {
                status.Text = "游戏已就绪 · 已读取应用配置";
                Append(Launcher.Describe(opts));
            }
        }
        catch (Exception ex)
        {
            if (!retried && Launcher.TryResetStateIfVanilla())
            {
                Launcher.ToolAsm = null;
                Append("检测到 Steam 已还原为官方原版，补丁状态已重置。");
                RefreshState(true);
                return;
            }
            status.Text = "状态：未应用，或文件不是官方 build 26105967";
            Append("读取状态失败：" + Launcher.Unwrap(ex).Message);
        }
    }

    private void Populate(object o)
    {
        if (o == null) return;
        chinese.Checked = Launcher.Get<bool>(o, "Chinese");
        hd.Checked = Launcher.Get<bool>(o, "HD");
        skipCutscenes.Checked = Launcher.Get<bool>(o, "SkipCutscenes");
        noStun.Checked = Launcher.Get<bool>(o, "NoStun");
        rearEnd.Checked = Launcher.Get<bool>(o, "RearEndProtection");
        int c = Launcher.Get<int>(o, "Combat");
        combat.SelectedIndex = c >= 0 && c < combat.Items.Count ? c : 0;
        aggression.Value = Clamp(Launcher.Get<int>(o, "OpponentAggression"), 0, 10);
        int r = Launcher.Get<int>(o, "Racers");
        racers.Value = Clamp(r == 0 ? 15 : r, 4, 10000);
        int f = Launcher.Get<int>(o, "FrameRate");
        frameRate.Value = Clamp(f == 0 ? 60 : f, 60, 300);
        decimal w = Launcher.Get<decimal>(o, "RoadWidth");
        roadWidth.Text = w.ToString(CultureInfo.InvariantCulture);
        police.Value = Clamp(Launcher.Get<int>(o, "PoliceMultiplier"), 0, 40);
        traffic.Value = Clamp(Launcher.Get<int>(o, "TrafficMultiplier"), 0, 40);
        forceName.Checked = Launcher.Get<bool>(o, "ForcePlayerName");
        string name = Launcher.Get<string>(o, "PlayerName");
        if (name != null) playerName.Text = name;
        playerName.Enabled = forceName.Checked;
        forceBike.Checked = Launcher.Get<bool>(o, "ForcePlayerBike");
        SelectBike(Launcher.Get<int>(o, "PlayerBikeId"));
        playerBike.Enabled = forceBike.Checked;
        forceWeapon.Checked = Launcher.Get<bool>(o, "ForcePlayerWeapon");
        int weaponId = Launcher.Get<int>(o, "PlayerWeaponId");
        playerWeapon.SelectedIndex = weaponId >= 0 && weaponId < playerWeapon.Items.Count ? weaponId : 1;
        playerWeapon.Enabled = forceWeapon.Checked;
    }

    private void SelectBike(int id)
    {
        for (int i = 0; i < BikeChoices.Length; i++)
        {
            if (BikeChoices[i].Id == id)
            {
                playerBike.SelectedItem = BikeChoices[i];
                return;
            }
        }
        playerBike.SelectedItem = BikeChoices[BikeChoices.Length - 1];
    }

    private object BuildOptions()
    {
        object o = Launcher.NewOptions();
        Launcher.Set(o, "Chinese", chinese.Checked);
        Launcher.Set(o, "HD", hd.Checked);
        Launcher.Set(o, "SkipCutscenes", skipCutscenes.Checked);
        Launcher.Set(o, "NoStun", noStun.Checked);
        Launcher.Set(o, "RearEndProtection", rearEnd.Checked);
        Launcher.Set(o, "Combat", combat.SelectedIndex);
        Launcher.Set(o, "OpponentAggression", (int)aggression.Value);
        Launcher.Set(o, "Racers", (int)racers.Value);
        Launcher.Set(o, "FrameRate", (int)frameRate.Value);
        Launcher.Set(o, "RoadWidth", decimal.Parse(roadWidth.Text, CultureInfo.InvariantCulture));
        Launcher.Set(o, "PoliceMultiplier", (int)police.Value);
        Launcher.Set(o, "TrafficMultiplier", (int)traffic.Value);
        Launcher.Set(o, "ForcePlayerName", forceName.Checked);
        Launcher.Set(o, "PlayerName", string.IsNullOrEmpty(playerName.Text.Trim()) ? "玩家" : playerName.Text.Trim());
        BikeChoice bike = playerBike.SelectedItem as BikeChoice;
        Launcher.Set(o, "ForcePlayerBike", forceBike.Checked);
        Launcher.Set(o, "PlayerBikeId", bike != null ? bike.Id : 7);
        Launcher.Set(o, "ForcePlayerWeapon", forceWeapon.Checked);
        Launcher.Set(o, "PlayerWeaponId", playerWeapon.SelectedIndex < 0 ? 1 : playerWeapon.SelectedIndex);
        return o;
    }

    private void ApplyCurrent(bool launch)
    {
        if (busy) return;
        if (Launcher.GameRunning())
        {
            MessageBox.Show(this, "请先退出游戏，再应用补丁。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Launcher.GameDir = gamePath.Text;
            Launcher.ToolPath = Launcher.FindTool();
            Launcher.ToolAsm = null;
            Launcher.LoadTool();
            object opts = BuildOptions();
            SetBusy(true);
            Append("正在应用增强补丁…");
            Launcher.ApplyOptions(opts, Append, true);
            Append("应用完成。");
            RefreshState();
            SaveUiSettings();
            changeStatus.Text = "设置已应用 · 可以启动游戏";
            SetBusy(false);
            if (launch) StartGame();
        }
        catch (Exception ex)
        {
            SetBusy(false);
            Exception inner = Launcher.Unwrap(ex);
            if (Launcher.TryResetStateIfVanilla())
            {
                Launcher.ToolAsm = null;
                Append("检测到 Steam 已还原为官方原版，补丁状态已重置，请再次点击应用。");
                RefreshState();
                return;
            }
            Append("失败：" + inner.Message);
            MessageBox.Show(this, inner.Message, "应用失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestoreVanilla()
    {
        if (busy) return;
        if (Launcher.GameRunning())
        {
            MessageBox.Show(this, "请先退出游戏，再还原。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, "将还原到官方英文原版。继续吗？", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        try
        {
            Launcher.GameDir = gamePath.Text;
            Launcher.ToolPath = Launcher.FindTool();
            Launcher.ToolAsm = null;
            Launcher.LoadTool();
            object opts = Launcher.NewOptions();
            SetBusy(true);
            Append("正在还原原版…");
            Launcher.ApplyOptions(opts, Append, true);
            Append("还原完成。");
            RefreshState();
            SetBusy(false);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            Exception inner = Launcher.Unwrap(ex);
            Append("还原失败：" + inner.Message);
            MessageBox.Show(this, inner.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartGame()
    {
        if (Launcher.GameRunning())
        {
            MessageBox.Show(this, "游戏已经在运行。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string exe = Path.Combine(gamePath.Text, "RoadRash95.exe");
        if (!File.Exists(exe))
        {
            MessageBox.Show(this, "找不到 RoadRash95.exe。", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = exe;
        psi.WorkingDirectory = gamePath.Text;
        psi.UseShellExecute = false;
        Process process = Process.Start(psi);
        int longFactor = (int)longRange.Value;
        int lateralAllFactor = (int)lateralAll.Value;
        int lateralPlayerFactor = (int)lateralPlayer.Value;
        bool aoeOn = aoe.Checked;
        bool aoeAll = aoeScope.SelectedIndex == 1;
        int aoeMax = (int)aoeTargets.Value;
        bool downOn = downed.Checked;
        bool downAll = downScope.SelectedIndex == 1;
        bool ramOn = ramKnockdown.Checked;
        bool invincibleOn = invincible.Checked;
        bool overlayOn = overlayCar.Checked;
        string overlayPath = overlayImage.Text;
        if (overlayOn)
        {
            string full = Path.IsPathRooted(overlayPath) ? overlayPath : Path.Combine(gamePath.Text, overlayPath);
            if (!File.Exists(full))
            {
                Append("自绘汽车图片不存在，已跳过叠加层：" + full);
                overlayOn = false;
            }
            else
            {
                overlayPath = full;
            }
        }
        bool debugCopiesOn = carDebugCopies.Checked;
        if (debugCopiesOn) overlayOn = false;
        bool carShellOn = carShell.Checked && !overlayOn && !debugCopiesOn;
        int selectedCarStyle = Math.Max(0, carStyle.SelectedIndex);
        bool carCollisionOn = carCollision.Checked && !carShellOn;
        if (ramOn && carCollisionOn && !carShellOn)
        {
            Append("提示：汽车级碰撞体已开启，会放大玩家碰撞范围；如果只想测试撞击，请取消勾选“汽车级碰撞体”。");
        }
        bool startMap = forceMap.Checked;
        int startLevel = mapLevel.SelectedIndex >= 0 ? mapLevel.SelectedIndex : 0;
        int startCourse = mapCourse.SelectedIndex > 0 ? mapCourse.SelectedIndex - 1 : -1;
        StringBuilder patchLog = new StringBuilder();
        patchLog.AppendLine("本次启用：大运模式=" + (ramOn ? "开" : "关") +
            "，霸体模式=" + (invincibleOn ? "开" : "关") +
            "，自绘汽车=" + (overlayOn ? "开" : "关") +
            "，汽车外观壳=" + (carShellOn ? "开" : "关") +
            "，调试摩托复制=" + (debugCopiesOn ? "开" : "关") +
            "，碰撞体=" + (carShellOn && autoCarCollision.Checked ? "随车型自动匹配" : (carCollisionOn && !carShellOn ? "独立放大" : "原版")) +
            "，碰撞稳定=" + (carShellOn && carStability.Checked ? "开" : "关"));
        Action<string> report = delegate(string s)
        {
            Append(s);
            patchLog.AppendLine(s);
        };
        try
        {
            RuntimePatch.Apply(process, oneHit.Checked, longFactor, lateralAllFactor, lateralPlayerFactor,
                noDirection.Checked, aoeOn, aoeAll, aoeMax, downOn, downAll, ramOn, invincibleOn,
                carShellOn, carCollisionOn, overlayOn, debugCopiesOn,
                startMap, startLevel, startCourse, report, selectedCarStyle, carStability.Checked, autoCarCollision.Checked, independentAudio.Checked, rockSolid.Checked, exaggeratedLaunch.Checked, launchScope.SelectedIndex == 1, launchMultiplier.Value,
                infiniteDurability.Checked, playerWalkSpeed.Value, aiWalkSpeed.Value, onFootProtection.Checked, onFootScope.SelectedIndex == 1,
                longRun.Checked, longRunScope.SelectedIndex, remountPolicy.SelectedIndex, (int)chaseDistance.Value, hideFallenVehicles.Checked,
                aiRunVariation.Checked,aiRunRhythm.SelectedIndex,aiRunSprint.SelectedIndex,aiRunChase.SelectedIndex,aiRunWeave.SelectedIndex,
                sameTrafficSpeed.Value,oppositeTrafficSpeed.Value,crazyDrivers.Checked,fallAnimationFix.Checked,
                sameTraffic.Checked,oppositeTraffic.Checked,nearbyImpactAudio.Checked,forceTrackLength.Checked,TrackLengthUnits.ToBlocks(trackLength.Value));
            Append("运行时补丁已写入。");
            patchLog.AppendLine("运行时补丁已写入。");
        }
        catch (Exception ex)
        {
            patchLog.AppendLine("运行时补丁写入失败：" + Launcher.Unwrap(ex).Message);
            MessageBox.Show(this, "运行时补丁写入失败：\n" + Launcher.Unwrap(ex).Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            try
            {
                File.WriteAllText(Path.Combine(gamePath.Text, "runtime_patch.log"), patchLog.ToString(), Encoding.UTF8);
            }
            catch { }
        }
        if (overlayOn)
        {
            try
            {
                ProcessStartInfo ov = new ProcessStartInfo();
                ov.FileName = Application.ExecutablePath;
                ov.Arguments = "--overlay " + process.Id.ToString(CultureInfo.InvariantCulture) + " \"" + overlayPath + "\"";
                ov.WorkingDirectory = gamePath.Text;
                ov.UseShellExecute = false;
                ov.CreateNoWindow = true;
                Process.Start(ov);
            }
            catch { }
        }
        try
        {
            ProcessStartInfo watch = new ProcessStartInfo();
            watch.FileName = Application.ExecutablePath;
            watch.Arguments = "--watch " + process.Id.ToString(CultureInfo.InvariantCulture) + (independentAudio.Checked ? " --audio-diagnostics" : "");
            watch.WorkingDirectory = gamePath.Text;
            watch.UseShellExecute = false;
            watch.CreateNoWindow = true;
            Process.Start(watch);
        }
        catch { }
        Close();
    }

    private void OpenReadme()
    {
        string a = Path.Combine(Launcher.GameDir, "Tools", "MOD_README_CN.txt");
        string b = Path.Combine(Launcher.GameDir, "MOD_README_CN.txt");
        string c = Path.Combine(Launcher.GameDir, "Tools");
        string target = File.Exists(a) ? a : (File.Exists(b) ? b : c);
        try { Process.Start(target); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        Cursor = value ? Cursors.WaitCursor : Cursors.Default;
        applyLaunch.Enabled = !value;
        applyOnly.Enabled = !value;
        startGame.Enabled = !value;
        restore.Enabled = !value;
        choose.Enabled = !value;
        detect.Enabled = !value;
    }

    private void Append(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Append), message);
            return;
        }
        log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
    }

    private static decimal Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}

internal sealed class CarOverlayForm : Form
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 8;
    private const int ULW_ALPHA = 2;

    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    private readonly int pid;
    private readonly string imagePath;
    private readonly string logPath;
    private readonly System.Windows.Forms.Timer timer;
    private readonly bool selfTest;
    private readonly Rectangle selfTestRect;
    private readonly int selfTestUntil;
    private Bitmap source;
    private Bitmap scaled;
    private bool shown;
    private int lastLogTick;
    private string lastLogState = "";
    private int lastCaptureTick = -1;
    private int lastCallback;
    private int staleTicks;

    public CarOverlayForm(int pid, string imagePath)
        : this(pid, imagePath, false, Rectangle.Empty, 0)
    {
    }

    public CarOverlayForm(int pid, string imagePath, bool selfTest, Rectangle selfTestRect, int selfTestSeconds)
    {
        this.pid = pid;
        this.imagePath = imagePath;
        this.selfTest = selfTest;
        this.selfTestRect = selfTestRect;
        this.selfTestUntil = Environment.TickCount + selfTestSeconds * 1000;
        logPath = Path.Combine(Launcher.GameDir, "overlay.log");
        try { File.WriteAllText(logPath, ""); } catch { }
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(1, 1);
        Text = "RoadRash Car Overlay";
        Log("start pid=" + pid + " image=" + imagePath);
        try
        {
            using (Bitmap raw = new Bitmap(imagePath))
            {
                Bitmap copy = new Bitmap(raw.Width, raw.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(copy))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
                }
                source = AutoCrop(copy);
                Log("image loaded raw=" + raw.Width + "x" + raw.Height +
                    " cropped=" + source.Width + "x" + source.Height);
            }
        }
        catch (Exception ex)
        {
            source = null;
            Log("image load failed: " + ex.Message);
        }
        IntPtr unused = Handle;
        timer = new System.Windows.Forms.Timer();
        timer.Interval = 16;
        timer.Tick += delegate { Tick(); };
        timer.Start();
    }

    private void Log(string s)
    {
        try
        {
            File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff ") + s + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    private void LogState(string s)
    {
        int now = Environment.TickCount;
        if (s != lastLogState || now - lastLogTick >= 1000)
        {
            lastLogState = s;
            lastLogTick = now;
            Log(s);
        }
    }

    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    private static Bitmap AutoCrop(Bitmap src)
    {
        int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                if (src.GetPixel(x, y).A > 8)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        }
        if (maxX < minX || maxY < minY) return src;
        Rectangle r = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        Bitmap crop = src.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        src.Dispose();
        return crop;
    }

    private IntPtr FindGameWindow()
    {
        try
        {
            Process p = Process.GetProcessById(pid);
            IntPtr main = p.MainWindowHandle;
            if (main != IntPtr.Zero && Native.IsWindowVisible(main))
                return main;
        }
        catch { return IntPtr.Zero; }
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows(delegate(IntPtr hwnd, IntPtr lparam)
        {
            uint wp;
            Native.GetWindowThreadProcessId(hwnd, out wp);
            if (wp == (uint)pid && Native.IsWindowVisible(hwnd))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private void Tick()
    {
        if (IsDisposed) return;
        if (source == null)
        {
            HideOverlay("image missing");
            return;
        }
        if (selfTest)
        {
            ShowOverlay(selfTestRect.Left, selfTestRect.Top, selfTestRect.Width, selfTestRect.Height, "selftest");
            if (Environment.TickCount >= selfTestUntil)
            {
                Log("selftest done");
                Close();
            }
            return;
        }
        Process p;
        try { p = Process.GetProcessById(pid); }
        catch { Log("game process gone"); Close(); return; }
        if (p.HasExited)
        {
            Log("game exited");
            Close();
            return;
        }
        IntPtr win = FindGameWindow();
        if (win == IntPtr.Zero)
        {
            HideOverlay("no game window");
            return;
        }
        IntPtr foreground = Native.GetForegroundWindow();
        uint fgPid;
        Native.GetWindowThreadProcessId(foreground, out fgPid);
        if (fgPid != (uint)pid)
        {
            HideOverlay("not foreground fg=0x" + foreground.ToInt64().ToString("X") + " fgPid=" + fgPid);
            return;
        }
        int[] pos = RuntimePatch.ReadPlayerScreen(p);
        if (pos == null)
        {
            HideOverlay("no player screen coords (" + RuntimePatch.PlayerScreenError + ")");
            return;
        }
        if (pos[10] != 0 && pos[10] != lastCallback)
        {
            lastCallback = pos[10];
            string dump = Path.Combine(Launcher.GameDir, "callback_code.bin");
            if (RuntimePatch.DumpCallbackCode(p, pos[10], dump))
                Log("callback code dumped: 0x" + pos[10].ToString("X8") + " -> " + dump);
        }
        if (lastCaptureTick == pos[3])
        {
            staleTicks++;
            if (staleTicks > 45)
            {
                HideOverlay("player coords stale");
                return;
            }
        }
        else
        {
            lastCaptureTick = pos[3];
            staleTicks = 0;
        }
        Native.RECT rc;
        if (!Native.GetClientRect(win, out rc))
        {
            HideOverlay("GetClientRect failed");
            return;
        }
        int cw = rc.Right - rc.Left;
        int ch = rc.Bottom - rc.Top;
        if (cw < 64 || ch < 64)
        {
            HideOverlay("client too small " + cw + "x" + ch);
            return;
        }
        double logicalW = pos[2];
        double scale = Math.Min(cw / logicalW, ch / 240.0);
        if (scale < 0.5)
        {
            HideOverlay("scale too small " + scale.ToString("0.###"));
            return;
        }
        double vw = logicalW * scale;
        double vh = 240.0 * scale;
        int offX = (int)Math.Round((cw - vw) / 2.0);
        int offY = (int)Math.Round((ch - vh) / 2.0);
        Native.POINT origin = new Native.POINT(0, 0);
        Native.ClientToScreen(win, ref origin);
        int logicalCarW = 64;
        int logicalCarH = Math.Max(1, source.Height * logicalCarW / source.Width);
        int dispW = Math.Max(1, (int)Math.Round(logicalCarW * scale));
        int dispH = Math.Max(1, (int)Math.Round(logicalCarH * scale));
        int screenX = origin.X + offX + (int)Math.Round(pos[0] * scale);
        int screenY = origin.Y + offY + (int)Math.Round(pos[1] * scale);
        int left = screenX - dispW / 2;
        int top = screenY - dispH;
        ShowOverlay(left, top, dispW, dispH,
            "win=0x" + win.ToInt64().ToString("X") + " fgPid=" + fgPid +
            " player=" + pos[0] + "," + pos[1] + " logicalW=" + pos[2] +
            " tick=" + pos[3] + " drawEntry=" + pos[4] +
            " ctx=0x" + pos[5].ToString("X8") + " callback=0x" + pos[10].ToString("X8") +
            " b44=" + pos[6] + " b50=" + pos[7] + " b4c=" + pos[8] + " b48=" + pos[9] +
            " client=" + cw + "x" + ch + " scale=" + scale.ToString("0.###") +
            " rect=" + left + "," + top + "," + dispW + "x" + dispH);
    }

    private void ShowOverlay(int left, int top, int w, int h, string state)
    {
        if (scaled == null || scaled.Width != w || scaled.Height != h)
        {
            if (scaled != null) scaled.Dispose();
            scaled = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source, new Rectangle(0, 0, w, h));
            }
        }
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = scaled.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Native.SelectObject(memDc, hBmp);
        Native.SIZE size = new Native.SIZE(w, h);
        Native.POINT src = new Native.POINT(0, 0);
        Native.POINT dst = new Native.POINT(left, top);
        Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
        blend.BlendOp = 0;
        blend.BlendFlags = 0;
        blend.SourceConstantAlpha = 255;
        blend.AlphaFormat = 1;
        bool ok = Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        int err = Marshal.GetLastWin32Error();
        Native.SelectObject(memDc, old);
        Native.DeleteObject(hBmp);
        Native.DeleteDC(memDc);
        Native.ReleaseDC(IntPtr.Zero, screenDc);
        Native.SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        if (!shown)
        {
            Native.ShowWindow(Handle, SW_SHOWNOACTIVATE);
            shown = true;
        }
        LogState("show ulw=" + (ok ? "ok" : "fail err=" + err) + " " + state);
    }

    private void HideOverlay(string reason)
    {
        if (shown)
        {
            Native.ShowWindow(Handle, SW_HIDE);
            shown = false;
            Log("hide: " + reason);
        }
        else
        {
            LogState("hidden: " + reason);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        if (scaled != null) scaled.Dispose();
        if (source != null) source.Dispose();
        base.OnFormClosing(e);
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, int flags);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SIZE
        {
            public int CX;
            public int CY;
            public SIZE(int cx, int cy) { CX = cx; CY = cy; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }
    }
}

internal static partial class RuntimePatch
{
    private const int RvaOneHitPatch = 0x18FA7;
    private const int RvaOneHitCave = 0x19220;
    private const int RvaOneHitReturn = 0x18FAF;
    private const int RvaPlayer = 0x1A69EC;
    private const int RvaWeaponTable = 0x190930;

    private const int RvaDirectionPatch = 0x1938B;

    private const int RvaLongPatch = 0x1939E;
    private const int RvaLongCave = 0x19260;
    private const int RvaLongBack = 0x193A3;
    private const int RvaLongFail = 0x193EB;

    private const int RvaLateralPatch = 0x193CA;
    private const int RvaLateralCave = 0x192A0;
    private const int RvaLateralBack = 0x193CF;

    private const int RvaAoePatch = 0x1903E;
    private const int RvaAoeCave = 0x21A30;
    private const int RvaAoeReturn = 0x19044;
    private const int RvaAoeData = 0x11280;
    private const int RvaRacerCount = 0x18F1D8;
    private const int RvaFightReach = 0x19370;
    private const int RvaDamageFunction = 0x3D96;

    private const int RvaDownedPatch = 0x18D99;
    private const int RvaDownedCave = 0x126EA;
    private const int RvaDownedBack = 0x18D9F;
    private const int RvaDownedMiss = 0x19208;

    private const int RvaLevelPatch = 0x376B9;
    private const int RvaLevelCave = 0x11110;
    private const int RvaLevelBack = 0x376BE;
    private const int RvaLevelCall = 0x352B;
    private const int RvaLevelA1 = 0x2041A1;
    private const int RvaLevelA2 = 0x2041A2;
    private const int RvaLevelA3 = 0x2041A3;
    private const int RvaRaceLevel = 0x1AC1CD;
    private const int RvaLevelEntryPatch = 0x2BE33;
    private const int RvaLevelEntryCave = 0x11190;
    private const int RvaLevelEntryBack = 0x2BE38;
    private const int RvaLevelEntryCall = 0x3EE5;

    private const int RvaRamHook = 0x4968E;
    private const int RvaRamCave = 0x6200;
    private const int RvaRamBack = 0x496C2;
    private const int RvaRamForward = 0x2345D3;
    private const int RvaCrashFunction = 0x13B60;
    private const int RvaInvincibleHook = 0x13B60;
    private const int RvaInvincibleBack = 0x13B66;
    private const int RvaInvincibleCave = 0x6500;
    private const int RvaInvincibleBikeHook = 0x14320;
    private const int RvaInvincibleBikeBack = 0x14325;
    private const int RvaInvincibleBikeCave = 0x6580;
    private const int RvaCrashSound = 0x28BD0;
    private const int RvaCrashSoundObject = 0x2162B0;

    private const int RvaCarShellHook = 0x66512;
    private const int RvaCarShellBack = 0x66517;
    private const int RvaHitboxHook = 0x49590;
    private const int RvaHitboxBack = 0x49596;
    private const int RvaActorChain = 0x1C55D0;
    private const int RvaDrawArray = 0x32449C;
    private const int RvaDrawCount = 0x1C55D8;
    private const int RvaDrawSprite = 0x46EC;
    private const int RvaBikeDraw = 0x5C000;
    private const int RvaBikeDrawCall = 0x5C11C;
    private const int RvaDrawCallback = 0x4AD58;
    private const int RvaBikeCopyHook = 0x5C0AE;
    private const int BikeCopyCaveOffset = 0xE00;
    private const int RvaBikeProbeHook = 0x5C045;
    private const int BikeProbeCaveOffset = 0xA00;
    private const int RvaRiderCopyHook = 0x5CE14;
    private const int RiderCopyCaveOffset = 0x1200;
    private const int RvaPlayerCarCall = 0x5CE0F;
    private const int RvaCarAssets = 0x1CD308;
    private const int RvaCarModels = 0x1A2474;
    private const int RvaCarAnimationInit = 0x5D010;
    private const int RvaPrepareSprite = 0x2F72;
    private const int CarStateSize = 0x1D0;
    private const int RvaCarDirectionFlags = 0x204B20;
    private const int RvaCarContactCall = 0x496A1;
    private const int RvaCollisionResponse = 0x48B10;
    private const int RvaCarContactBack = 0x496C2;
    private const int RvaScaleTable = 0x197C90;
    private const int RvaCameraX = 0x212828;
    private const int RvaCameraY = 0x21282C;
    private const int RvaScreenWidth = 0x191C0C;
    private const int ScreenCaptureCaveOffset = 0xC00;
    private const int RvaRiderDraw = 0x5CCC0;
    private const int RvaCarDraw = 0x5CFA0;
    private const int RvaDrawList = 0x5312;
    private const int RvaCarThunk = 0x5358;
    private const int RvaCarTypeCount = 0x1CD300;
    private const int RvaDiag = 0x6100;

    private static readonly string[] CourseDisplayNames = new string[]
    {
        "City", "Sierra", "Penin", "Napa", "PCH"
    };

    private static readonly string[] CourseFileNames = new string[]
    {
        "City", "Hiway", "Medly", "Napa", "Canyn"
    };

    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint ListModules32Bit = 0x01;
    private const uint MemCommitReserve = 0x3000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, IntPtr size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, IntPtr size);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcessModulesEx(IntPtr process, [Out] IntPtr[] modules, int size, out int needed, uint filter);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileNameEx(IntPtr process, IntPtr module, StringBuilder name, int size);

    internal static void Apply(Process process, bool oneHit, int longFactor, int lateralAllFactor, int lateralPlayerFactor,
        bool noDirection, bool aoeEnabled, bool aoeAll, int aoeTargets, bool downedEnabled, bool downedAll, bool ramKnockdown,
        bool invincible, bool carShell, bool carCollision, bool overlayCar, bool carDebugCopies,
        bool forceMap, int levelIndex, int courseIndex, Action<string> log, int carStyle = 0, bool carStability = true, bool autoCarCollision = true, bool independentAudio = false, bool rockSolid = false, bool exaggeratedLaunch = false, bool launchEveryone = false, decimal launchMultiplier = 1.5m,
        bool infiniteDurability = false, decimal playerWalkSpeed = 1m, decimal aiWalkSpeed = 1m, bool onFootProtection = false, bool onFootEveryone = false,
        bool longRun = false, int longRunScope = 2, int remountPolicy = 0, int chaseDistance = 96, bool hideFallenVehicles = false,
        bool aiRunVariation=false,int aiRunRhythm=2,int aiRunSprint=2,int aiRunChase=2,int aiRunWeave=2,
        decimal sameTrafficSpeed=1m,decimal oppositeTrafficSpeed=1m,bool crazyDrivers=false,bool fallAnimationFix=false,
        bool sameTraffic=true,bool oppositeTraffic=true,bool nearbyImpactAudio=false,bool forceTrackLength=false,int trackLength=1800,
        int opponentIdentity=0,bool opponentsTargetPlayer=false,bool knockdownHud=false,bool knockdownVoice=true,
        int creditMs=2000,int streakMs=18000,int knockdownsPerVoice=1,bool hardcore=false,bool policePlayer=false,int rankMode=0)
    {
        IntPtr baseAddress = IntPtr.Zero;
        for (int i = 0; i < 100; i++)
        {
            if (process.HasExited) throw new InvalidOperationException("游戏已退出，无法写入运行时补丁。");
            baseAddress = GetBase(process);
            if (baseAddress != IntPtr.Zero) break;
            Thread.Sleep(100);
        }
        if (baseAddress == IntPtr.Zero) throw new InvalidOperationException("无法获取游戏模块基址。");

        long base64 = baseAddress.ToInt64();
        log("已确认游戏模块：RoadRash95.exe，PE32 / x86，基址 0x" + base64.ToString("X") + "。");
        IntPtr h = OpenProcess(ProcessVmOperation | ProcessVmWrite | ProcessVmRead | ProcessQueryInformation, false, process.Id);
        if (h == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            ApplyRiderRoadGuard(h, base64, log);
            ApplyRankBatch(h, base64, rankMode, log);
            long codeBase = 0;
            long stateBase = 0;
            long spriteBase = 0;
            if (carShell || carCollision || overlayCar || carDebugCopies)
            {
                try
                {
                    IntPtr block = VirtualAllocEx(h, new IntPtr(0x5000000), (UIntPtr)0x4000, MemCommitReserve, PageExecuteReadWrite);
                    if (block == IntPtr.Zero)
                        block = VirtualAllocEx(h, IntPtr.Zero, (UIntPtr)0x4000, MemCommitReserve, PageExecuteReadWrite);
                    if (block == IntPtr.Zero)
                        throw new InvalidOperationException("无法在游戏进程内分配汽车补丁代码区。");
                    codeBase = block.ToInt64();
                    stateBase = codeBase + 0x1000;
                    spriteBase = codeBase + 0x2000;
                    long d1 = (codeBase + RiderCopyCaveOffset) - (base64 + RvaPlayerCarCall + 5);
                    long d2 = (codeBase + 0x800) - (base64 + RvaHitboxHook + 5);
                    long d3 = (codeBase + ScreenCaptureCaveOffset) - (base64 + RvaDrawCallback + 5);
                    long d4 = (codeBase + BikeCopyCaveOffset) - (base64 + RvaBikeCopyHook + 5);
                    long d5 = (codeBase + BikeProbeCaveOffset) - (base64 + RvaBikeProbeHook + 5);
                    long d6 = (codeBase + RiderCopyCaveOffset) - (base64 + RvaRiderCopyHook + 5);
                    if ((carShell && (d1 > int.MaxValue || d1 < int.MinValue)) ||
                        (carDebugCopies && (d4 > int.MaxValue || d4 < int.MinValue || d5 > int.MaxValue || d5 < int.MinValue ||
                            d6 > int.MaxValue || d6 < int.MinValue)) ||
                        (carCollision && (d2 > int.MaxValue || d2 < int.MinValue)) ||
                        ((overlayCar || carDebugCopies) && (d3 > int.MaxValue || d3 < int.MinValue)))
                        throw new InvalidOperationException("汽车补丁代码区离游戏模块太远，无法写入跳转。");
                    Write(h, stateBase, new byte[CarStateSize]);
                }
                catch (Exception ex)
                {
                    codeBase = 0;
                    stateBase = 0;
                    log("汽车补丁代码区分配失败：" + ex.Message);
                }
            }

            if (aoeEnabled || downedEnabled)
            {
                int maxExtra = aoeEnabled ? Math.Max(0, aoeTargets - 1) : 0;
                WriteAoeData(h, base64, downedEnabled, downedAll, aoeEnabled && aoeAll, maxExtra);
            }
            long aiCode=0,aiConfig=0;
            ApplyOpponents(h, base64, opponentIdentity, opponentsTargetPlayer, log, policePlayer);
            ApplyTrafficDirections(h,base64,sameTraffic,oppositeTraffic,log);
            ApplyTrackLength(h,base64,forceTrackLength,trackLength,log);
            ApplyNearbyAudio(h,base64,nearbyImpactAudio,ramKnockdown,log);
            ApplyTrafficDriving(h,base64,sameTrafficSpeed,oppositeTrafficSpeed,crazyDrivers,log);
            if(longRun&&longRunScope!=1&&aiRunVariation)
                aiCode=PrepareRunAi(h,base64,aiWalkSpeed,aiRunRhythm,aiRunSprint,aiRunChase,aiRunWeave,log,out aiConfig,chaseDistance);
            ApplyRace(h, base64, longRun, longRunScope, remountPolicy, chaseDistance, log, playerWalkSpeed, aiWalkSpeed,aiCode,aiConfig);
            ApplyCharacter(h, base64, infiniteDurability, playerWalkSpeed, aiWalkSpeed, onFootProtection, onFootEveryone, log,aiCode,aiConfig);
            if (hideFallenVehicles) ApplyHideFallenVehicles(h, base64, log);
            if (rockSolid || exaggeratedLaunch)
                ApplyMotion(h, base64, rockSolid, exaggeratedLaunch, launchEveryone, launchMultiplier, log);
            ApplyFixes(h, base64, independentAudio, fallAnimationFix, log);
            if (oneHit) ApplyOneHit(h, base64, log);
            if (noDirection) ApplyNoDirection(h, base64, log);
            if (longFactor > 1) ApplyLongitudinal(h, base64, longFactor, log);
            if (lateralAllFactor > 1 || lateralPlayerFactor > 1)
                ApplyLateral(h, base64, lateralAllFactor, lateralPlayerFactor, log);
            if (downedEnabled) ApplyDowned(h, base64, downedAll, log);
            if (aoeEnabled) ApplyAoe(h, base64, aoeAll, aoeTargets, log, opponentsTargetPlayer);
            long knockdownEntry = knockdownHud || hardcore || policePlayer ? InstallKnockdown(h, base64, knockdownHud,
                knockdownVoice, aoeEnabled, creditMs, streakMs, knockdownsPerVoice, hardcore, policePlayer, log) : 0;
            if (ramKnockdown)
            {
                try
                {
                    ApplyRam(h, base64, log, knockdownEntry);
                }
                catch (Exception ex)
                {
                    log("大运模式写入失败：" + ex.Message);
                }
            }
            if (invincible)
            {
                try
                {
                    ApplyInvincible(h, base64, log);
                }
                catch (Exception ex)
                {
                    log("霸体模式写入失败：" + ex.Message);
                }
            }
            if (carShell && !carDebugCopies && codeBase != 0)
            {
                try
                {
                    ApplyPlayerCar(h, base64, codeBase, stateBase, spriteBase, log, carStyle, hideFallenVehicles);
                    if (carStability) ApplyCarContact(h, base64, codeBase, stateBase, log);
                }
                catch (Exception ex)
                {
                    log("汽车外观壳写入失败：" + ex.Message);
                }
            }
            if (carDebugCopies && codeBase != 0)
            {
                try
                {
                    ApplyBikeCopyHook(h, base64, codeBase, stateBase, log);
                    ApplyBikeProbe(h, base64, codeBase, stateBase, log);
                    ApplyRiderCopyHook(h, base64, codeBase, stateBase, log);
                }
                catch (Exception ex)
                {
                    log("调试摩托复制写入失败：" + ex.Message);
                }
            }
            if (((carCollision && !carShell) || (autoCarCollision && carShell && !carDebugCopies)) && codeBase != 0)
            {
                try
                {
                    ApplyHitbox(h, base64, codeBase + 0x800, log, carShell && !carDebugCopies ? stateBase : 0);
                }
                catch (Exception ex)
                {
                    log("汽车级碰撞体写入失败：" + ex.Message);
                }
            }
            if ((overlayCar || carDebugCopies) && codeBase != 0)
            {
                try
                {
                    ApplyScreenCapture(h, base64, codeBase, stateBase, log);
                }
                catch (Exception ex)
                {
                    log("玩家绘制坐标捕获写入失败：" + ex.Message);
                }
            }
            if (forceMap)
            {
                ApplyLevel(h, base64, levelIndex, courseIndex, log);
                if (courseIndex >= 0)
                    ApplyLevelEntry(h, base64, courseIndex, log);
            }
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private static void ApplyOneHit(IntPtr h, long base64, Action<string> log)
    {
        long patch = base64 + RvaOneHitPatch;
        long cave = base64 + RvaOneHitCave;
        if (IsPatched(h, patch))
        {
            log("运行时一击必杀已经存在。");
            return;
        }
        byte[] head = Read(h, patch, 8);
        if (head[0] != 0x0F || head[1] != 0xAF || head[2] != 0x04 || head[3] != 0xB5)
            throw new InvalidOperationException("伤害指令与预期不一致，可能被其他补丁修改。");
        CheckCave(h, cave, 28);

        uint player = (uint)(base64 + RvaPlayer);
        uint table = (uint)(base64 + RvaWeaponTable);
        List<byte> code = new List<byte>();
        code.Add(0x3B); code.Add(0x3D); Add(code, player);
        code.Add(0x75); code.Add(0x07);
        code.Add(0xB8); Add(code, 0x7FFF);
        code.Add(0xEB); code.Add(0x08);
        code.Add(0x0F); code.Add(0xAF); code.Add(0x04); code.Add(0xB5); Add(code, table);
        int oneHitReturn = (int)(base64 + RvaOneHitReturn - (cave + code.Count + 5));
        code.Add(0xE9); Add(code, oneHitReturn);
        Write(h, cave, code.ToArray());
        Write(h, patch, Jmp(patch, cave, 8));
        log("一击必杀运行时补丁已写入（仅玩家）。");
    }

    private static void ApplyNoDirection(IntPtr h, long base64, Action<string> log)
    {
        long site = base64 + RvaDirectionPatch;
        byte[] head = Read(h, site, 2);
        if (head[0] == 0x90 && head[1] == 0x90)
        {
            log("攻击方向限制已经取消。");
            return;
        }
        if (head[0] != 0x75 || head[1] != 0x5E)
            throw new InvalidOperationException("攻击方向指令与预期不一致。");
        Write(h, site, new byte[] { 0x90, 0x90 });
        log("攻击方向限制已取消（所有车手）。");
    }

    private static void ApplyLongitudinal(IntPtr h, long base64, int factor, Action<string> log)
    {
        long patch = base64 + RvaLongPatch;
        long cave = base64 + RvaLongCave;
        if (IsPatched(h, patch))
        {
            log("纵向攻击范围补丁已经存在。");
            return;
        }
        byte[] head = Read(h, patch, 5);
        if (head[0] != 0x83 || head[1] != 0xF9 || head[2] != 0x40 || head[3] != 0x7D || head[4] != 0x48)
            throw new InvalidOperationException("纵向距离指令与预期不一致。");
        CheckCave(h, cave, 18);

        List<byte> code = new List<byte>();
        code.Add(0x81); code.Add(0xF9); Add(code, 0x40 * factor);
        code.Add(0x7D); code.Add(0x05);
        int longBack = (int)(base64 + RvaLongBack - (cave + code.Count + 5));
        code.Add(0xE9); Add(code, longBack);
        int longFail = (int)(base64 + RvaLongFail - (cave + code.Count + 5));
        code.Add(0xE9); Add(code, longFail);
        Write(h, cave, code.ToArray());
        Write(h, patch, Jmp(patch, cave, 5));
        log("纵向攻击范围 x" + factor + "（所有车手）已写入。");
    }

    private static void ApplyLateral(IntPtr h, long base64, int allFactor, int playerFactor, Action<string> log)
    {
        long patch = base64 + RvaLateralPatch;
        long cave = base64 + RvaLateralCave;
        if (IsPatched(h, patch))
        {
            log("横向攻击范围补丁已经存在。");
            return;
        }
        byte[] head = Read(h, patch, 5);
        if (head[0] != 0xC1 || head[1] != 0xE1 || head[2] != 0x0C || head[3] != 0x3B || head[4] != 0xC1)
            throw new InvalidOperationException("横向距离指令与预期不一致。");
        CheckCave(h, cave, 32);

        uint player = (uint)(base64 + RvaPlayer);
        List<byte> code = new List<byte>();
        if (allFactor > 1) AddImul(code, allFactor);
        if (playerFactor > 1)
        {
            code.Add(0x3B); code.Add(0x1D); Add(code, player);
            int jne = code.Count;
            code.Add(0x75); code.Add(0x00);
            AddImul(code, playerFactor);
            code[jne + 1] = (byte)(code.Count - (jne + 2));
        }
        code.Add(0xC1); code.Add(0xE1); code.Add(0x0C);
        code.Add(0x3B); code.Add(0xC1);
        int lateralBack = (int)(base64 + RvaLateralBack - (cave + code.Count + 5));
        code.Add(0xE9); Add(code, lateralBack);
        Write(h, cave, code.ToArray());
        Write(h, patch, Jmp(patch, cave, 5));
        log("横向攻击范围已写入（全员 x" + allFactor + "，玩家额外 x" + playerFactor + "）。");
    }

    private static void ApplyDowned(IntPtr h, long base64, bool all, Action<string> log)
    {
        long patch = base64 + RvaDownedPatch;
        long cave = base64 + RvaDownedCave;
        if (IsPatched(h, patch))
        {
            log("倒地目标过滤已经存在。");
            return;
        }
        byte[] head = Read(h, patch, 6);
        if (head[0] != 0x8B || head[1] != 0x93 || head[2] != 0xFC || head[3] != 0x02 || head[4] != 0x00 || head[5] != 0x00)
            throw new InvalidOperationException("倒地判定入口与预期不一致。");
        byte[] code = BuildDownedCode(base64);
        CheckCave(h, cave, code.Length);
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, 6));
        log("倒地目标过滤已启用（" + (all ? "所有车手" : "仅玩家") + "）。");
    }

    private static void ApplyAoe(IntPtr h, long base64, bool all, int targets, Action<string> log, bool opponentsTargetPlayer)
    {
        long patch = base64 + RvaAoePatch;
        long cave = base64 + RvaAoeCave;
        if (IsPatched(h, patch))
        {
            log("群体攻击已经存在。");
            return;
        }
        byte[] head = Read(h, patch, 6);
        if (head[0] != 0x6A || head[1] != 0x02 || head[2] != 0x8B || head[3] != 0xD7 || head[4] != 0x8B || head[5] != 0xCB)
            throw new InvalidOperationException("群体攻击入口与预期不一致。");
        long riderArray = GetRrdataBase(h, base64);
        if (riderArray == 0)
            throw new InvalidOperationException("未找到增强器的 .rrdata 车手数组，请先应用增强补丁。");
        riderArray += 40004;
        byte[] code = BuildAoeCode(base64, riderArray, opponentsTargetPlayer);
        CheckCave(h, cave, code.Length);
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, 6));
        log("群体攻击已启用（" + (all ? "所有车手" : "仅玩家") + "，最多 " + targets + " 人，含主目标）。");
    }

    private static void ApplyLevel(IntPtr h, long base64, int levelIndex, int courseIndex, Action<string> log)
    {
        if (levelIndex < 0) levelIndex = 0;
        if (levelIndex > 4) levelIndex = 4;
        if (courseIndex < -1) courseIndex = -1;
        if (courseIndex > 4) courseIndex = 4;

        long patch = base64 + RvaLevelPatch;
        long cave = base64 + RvaLevelCave;
        if (IsPatched(h, patch))
        {
            log("开局地图补丁已经存在。");
            return;
        }
        byte[] head = Read(h, patch, 5);
        if (head[0] != 0xE8 || head[1] != 0x6D || head[2] != 0xBE || head[3] != 0xFC || head[4] != 0xFF)
            throw new InvalidOperationException("开局地图入口与预期不一致。");

        byte[] code = BuildLevelCode(base64, levelIndex, courseIndex);
        CheckCave(h, cave, code.Length);
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, 5));
        log("开局地图已指定：等级 " + (levelIndex + 1) +
            (courseIndex >= 0 ? "，地图 " + CourseDisplayNames[courseIndex] : "，地图跟随菜单") + "。");
    }

    internal static byte[] BuildLevelCode(long base64, int levelIndex, int courseIndex)
    {
        if (levelIndex < 0) levelIndex = 0;
        if (levelIndex > 4) levelIndex = 4;
        if (courseIndex < -1) courseIndex = -1;
        if (courseIndex > 4) courseIndex = 4;
        long cave = base64 + RvaLevelCave;
        List<byte> code = new List<byte>();
        if (courseIndex >= 0)
            EmitByteAbs(code, base64 + RvaLevelA1, (byte)(courseIndex + 1));
        code.Add(0xA0);
        Add(code, (int)(base64 + RvaLevelA2));
        EmitByteAbs(code, base64 + RvaLevelA2, (byte)levelIndex);
        code.Add(0x3C);
        code.Add((byte)levelIndex);
        int jePos = code.Count;
        code.Add(0x74); code.Add(0x00);
        EmitByteAbs(code, base64 + RvaLevelA3, 0);
        code[jePos + 1] = (byte)(code.Count - (jePos + 2));
        if (courseIndex >= 0)
        {
            code.Add(0xB1);
            code.Add((byte)(courseIndex + 1));
        }
        code.Add(0x8A); code.Add(0xC1);
        code.Add(0xFE); code.Add(0xC8);
        code.Add(0xA2);
        Add(code, (int)(base64 + RvaRaceLevel));
        int callRel = (int)(base64 + RvaLevelCall - (cave + code.Count + 5));
        code.Add(0xE8); Add(code, callRel);
        int backRel = (int)(base64 + RvaLevelBack - (cave + code.Count + 5));
        code.Add(0xE9); Add(code, backRel);
        return code.ToArray();
    }

    private static void ApplyLevelEntry(IntPtr h, long base64, int courseIndex, Action<string> log)
    {
        if (courseIndex < 0) return;
        if (courseIndex > 4) courseIndex = 4;
        long patch = base64 + RvaLevelEntryPatch;
        long cave = base64 + RvaLevelEntryCave;
        if (IsPatched(h, patch))
        {
            log("地图入口已经存在。");
            return;
        }
        byte[] head = Read(h, patch, 5);
        if (head[0] != 0xE8 || head[1] != 0xAD || head[2] != 0x80 || head[3] != 0xFD || head[4] != 0xFF)
            throw new InvalidOperationException("地图入口与预期不一致。");

        byte[] code = BuildLevelEntryCode(base64, courseIndex);
        CheckCave(h, cave, code.Length);
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, 5));
        log("地图入口已写入（" + CourseDisplayNames[courseIndex] + "）。");
    }

    internal static byte[] BuildLevelEntryCode(long base64, int courseIndex)
    {
        if (courseIndex < 0) courseIndex = 0;
        if (courseIndex > 4) courseIndex = 4;
        long cave = base64 + RvaLevelEntryCave;
        List<byte> code = new List<byte>();
        EmitByteAbs(code, base64 + RvaLevelA1, (byte)(courseIndex + 1));
        EmitByteAbs(code, base64 + RvaRaceLevel, (byte)courseIndex);
        code.Add(0xBB);
        Add(code, courseIndex + 1);
        code.Add(0x89); code.Add(0xD9);
        int callRel = (int)(base64 + RvaLevelEntryCall - (cave + code.Count + 5));
        code.Add(0xE8); Add(code, callRel);
        int backRel = (int)(base64 + RvaLevelEntryBack - (cave + code.Count + 5));
        code.Add(0xE9); Add(code, backRel);
        return code.ToArray();
    }

    // 在原版 cmp [esi+A8],0 之前记录；该分支可能直接跳过复制 hook。
    // 最后重放 cmp，让原版 je 使用它自己的标志，不改变分支结果。
    internal static byte[] BuildBikeProbeCode(long base64, long caveBase, long stateBase)
    {
        CodeBuffer c = new CodeBuffer(caveBase);
        B(c, 0x9C, 0x60); // pushfd; pushad
        B(c, 0xFF, 0x05); D(c, stateBase + 0xE0);
        B(c, 0x89, 0x35); D(c, stateBase + 0xEC); // lastActor = esi
        MovEaxMem(c, base64 + RvaPlayer);
        B(c, 0xA3); D(c, stateBase + 0xF0);
        B(c, 0x3B, 0xF0); // cmp esi,eax
        Jcc(c, 0x85, "done");
        B(c, 0xFF, 0x05); D(c, stateBase + 0xE4);
        B(c, 0x8B, 0x86); D(c, 0xA8);
        B(c, 0xA3); D(c, stateBase + 0xF4);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x85, "have_sprite");
        B(c, 0xFF, 0x05); D(c, stateBase + 0xE8);
        c.Label("have_sprite");
        MovEaxMem(c, base64 + RvaPlayer + 4); // 当前视角 bike
        B(c, 0xA3); D(c, stateBase + 0xF8);
        B(c, 0x8B, 0x86); D(c, 0x98);
        B(c, 0xA3); D(c, stateBase + 0xFC);
        c.Label("done");
        B(c, 0x61, 0x9D); // popad; popfd
        B(c, 0x83, 0xBE, 0xA8, 0x00, 0x00, 0x00, 0x00);
        JmpAbs(c, base64 + RvaBikeProbeHook + 7);
        return c.Finish();
    }

    private static void ApplyBikeProbe(IntPtr h, long base64, long codeBase, long stateBase, Action<string> log)
    {
        long patch = base64 + RvaBikeProbeHook;
        byte[] expected = { 0x83, 0xBE, 0xA8, 0x00, 0x00, 0x00, 0x00 };
        byte[] head = Read(h, patch, expected.Length);
        if (head[0] == 0xE9) throw new InvalidOperationException("BikeDraw sprite 检查入口已有其他跳转。");
        for (int i = 0; i < expected.Length; i++)
            if (head[i] != expected[i]) throw new InvalidOperationException("BikeDraw sprite 检查入口与预期不一致。");
        long cave = codeBase + BikeProbeCaveOffset;
        byte[] code = BuildBikeProbeCode(base64, cave, stateBase);
        if (code.Length > ScreenCaptureCaveOffset - BikeProbeCaveOffset)
            throw new InvalidOperationException("BikeDraw 诊断代码超出代码洞。");
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, expected.Length));
        log("BikeDraw 分支诊断已写入（记录玩家进入次数、空 sprite 次数和 actor 指针）。");
    }

    // 替换 RiderDraw 原有实体调用的包装与大小参数，其余位置/深度/弹跳参数原样保留。
    // 所选车型必须支持同向；每帧读取赛道资源，在私有 actor 上按玩家朝向选帧。
    internal static byte[] BuildPlayerCarCode(long base64, long caveBase, long stateBase, long spriteBase, long actorBase, int carStyle = 0, long boundsCode = 0, bool hideFallenVehicles = false)
    {
        if (carStyle < 0 || carStyle > 14) throw new ArgumentOutOfRangeException("carStyle");
        CodeBuffer c = new CodeBuffer(caveBase);
        B(c, 0x9C, 0x60); // pushfd; pushad；原栈参数位于 esp+36
        B(c, 0xFF, 0x05); D(c, stateBase + 0x140);
        MovEaxMem(c, base64 + RvaPlayer);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "original");
        B(c, 0x3B, 0xB0); D(c, 0x2FC); // esi == player->rider
        Jcc(c, 0x85, "original");
        if (hideFallenVehicles) {
            // The car shell replaces the rider sprite. While on foot, retain the
            // original person instead; the vehicle callback is filtered separately.
            B(c,0x83,0xB8); D(c,0x2F4); B(c,1); Jcc(c,0x82,"car_visible");
            B(c,0x83,0xB8); D(c,0x2F4); B(c,3); Jcc(c,0x86,"original");
            c.Label("car_visible");
        }
        B(c, 0xFF, 0x05); D(c, stateBase + 0x144);
        B(c, 0x89, 0xC3); // ebx=玩家 bike；与 rider 共用位置，但方向取自玩家车辆
        B(c, 0x31, 0xC0);
        B(c, 0xA3); D(c, stateBase + 0x154);
        B(c, 0xA3); D(c, stateBase + 0x15C);
        B(c, 0xA3); D(c, stateBase + 0x164);
        B(c, 0xA3); D(c, stateBase + 0x168);
        B(c, 0xA3); D(c, stateBase + 0x16C);
        B(c, 0xA3); D(c, stateBase + 0x170);
        B(c, 0xA3); D(c, stateBase + 0x17C);
        B(c, 0xA3); D(c, stateBase + 0x1A0); // 尺寸未就绪时恢复原摩托碰撞体
        B(c, 0xA3); D(c, stateBase + 0x1C4);
        B(c, 0xA3); D(c, stateBase + 0x1C8);
        B(c, 0xC7, 0x05); D(c, stateBase + 0x1C0); D(c, carStyle);
        B(c, 0xC7, 0x05); D(c, stateBase + 0x150); D(c, actorBase);
        B(c, 0x8B, 0x43, 0x68, 0xA3); D(c, stateBase + 0x174);
        B(c, 0x8B, 0x43, 0x6C, 0xA3); D(c, stateBase + 0x178);
        B(c, 0x0F, 0xB6, 0x05); D(c, base64 + RvaCarTypeCount);
        B(c, 0xA3); D(c, stateBase + 0x160);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "no_assets");
        B(c, 0xBD); D(c, carStyle); // ebp=本帧实际车型，辅助函数保留
        CmpEax(c, carStyle);
        Jcc(c, 0x8F, "check_direction");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x1C4); D(c, 1);
        Jmp(c, "find_rear");
        c.Label("check_direction");
        B(c, 0x8B, 0x04, 0xAD); D(c, base64 + RvaCarDirectionFlags);
        B(c, 0xA9); D(c, 0x1C0000); Jcc(c, 0x84, "style_ready");
        B(c, 0xA9); D(c, 0x100000); Jcc(c, 0x85, "style_ready");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x1C4); D(c, 2);
        c.Label("find_rear");
        B(c, 0x31, 0xED);
        c.Label("rear_candidate");
        B(c, 0x3B, 0x2D); D(c, stateBase + 0x160); Jcc(c, 0x83, "no_assets");
        B(c, 0x83, 0xFD, 15); Jcc(c, 0x83, "no_assets");
        B(c, 0x8B, 0x04, 0xAD); D(c, base64 + RvaCarDirectionFlags);
        B(c, 0xA9); D(c, 0x1C0000); Jcc(c, 0x84, "rear_resources");
        B(c, 0xA9); D(c, 0x100000); Jcc(c, 0x84, "next_rear");
        c.Label("rear_resources");
        for (int i = 0; i < 2; i++)
        { B(c, 0x83, 0x3C, 0xED); D(c, base64 + RvaCarAssets + i * 4); B(c, 0); Jcc(c, 0x84, "next_rear"); }
        B(c, 0x8B, 0x14, 0xAD); D(c, base64 + RvaCarModels);
        B(c, 0x85, 0xD2); Jcc(c, 0x84, "next_rear");
        B(c, 0x83, 0x3A, 0); Jcc(c, 0x85, "style_ready");
        c.Label("next_rear"); B(c, 0x45); Jmp(c, "rear_candidate");
        c.Label("style_ready");
        B(c, 0xA3); D(c, stateBase + 0x1C8);
        B(c, 0x89, 0x2D); D(c, stateBase + 0x15C);
        // 赛道卸载会清空资源对。每帧验证，避免沿用已经释放的上一赛道资源。
        B(c, 0x83, 0x3C, 0xED); D(c, base64 + RvaCarAssets); B(c, 0x00);
        Jcc(c, 0x84, "no_assets");
        B(c, 0x83, 0x3C, 0xED); D(c, base64 + RvaCarAssets + 4); B(c, 0x00);
        Jcc(c, 0x84, "no_assets");
        B(c, 0x8B, 0x04, 0xAD); D(c, base64 + RvaCarModels);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "no_model");
        B(c, 0x83, 0x38, 0x00);
        Jcc(c, 0x84, "no_model");
        B(c, 0xFC, 0x31, 0xC0); // cld; xor eax,eax
        B(c, 0xBF); D(c, actorBase);
        B(c, 0xB9); D(c, 0xE0 / 4);
        B(c, 0xF3, 0xAB); // 清空私有 actor；不写入任何游戏 actor
        B(c, 0x8D, 0x73, 0x44);
        B(c, 0xBF); D(c, actorBase + 0x44);
        B(c, 0xB9); D(c, 0x2C / 4);
        B(c, 0xF3, 0xA5); // 拷贝位置、深度、弹跳和 yaw(+68)，随后清除侧倾(+6C)
        B(c, 0xC7, 0x05); D(c, actorBase + 0x6C); D(c, 0); // 汽车保持直立，不继承摩托转弯侧倾
        B(c, 0xC7, 0x05); D(c, actorBase + 0xC4); D(c, 16);
        B(c, 0xB9); D(c, actorBase + 0x8C);
        B(c, 0x89, 0xEA); // dl=本帧实际车型
        CallAbs(c, base64 + RvaCarAnimationInit);
        MovEaxMem(c, actorBase + 0x98);
        B(c, 0xA3); D(c, stateBase + 0x17C);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "no_model");
        if (boundsCode != 0)
        {
            CallAbs(c, boundsCode);
            // 尺寸查询使用标准视角，之后重新初始化实际车型并恢复玩家空间参数。
            B(c, 0xB9); D(c, actorBase + 0x8C);
            B(c, 0x89, 0xEA); CallAbs(c, base64 + RvaCarAnimationInit);
            B(c, 0x8D, 0x73, 0x44, 0xBF); D(c, actorBase + 0x44);
            B(c, 0xB9); D(c, 11); B(c, 0xFC, 0xF3, 0xA5);
            B(c, 0xC7, 0x05); D(c, actorBase + 0x6C); D(c, 0);
        }
        B(c, 0xB9); D(c, actorBase);
        B(c, 0x8B, 0x54, 0x24, 0x18); // 原 ECX=context；辅助调用可破坏易失寄存器
        B(c, 0xFF, 0x05); D(c, stateBase + 0x158);
        CallAbs(c, base64 + RvaPrepareSprite);
        MovEaxMem(c, actorBase + 0xA8);
        B(c, 0xA3); D(c, stateBase + 0x154);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "no_frame");
        B(c, 0x83, 0x78, 0x08, 0x00);
        Jcc(c, 0x84, "no_metadata");
        B(c, 0x8B, 0x48, 0x08, 0x89, 0x0D); D(c, stateBase + 0x168);
        B(c, 0x8B, 0x0D); D(c, actorBase + 0xB8);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x16C);
        B(c, 0xFC); // cld；退出时恢复调用前 DF
        B(c, 0x89, 0xC6); // esi=源 sprite
        B(c, 0xBF); D(c, spriteBase);
        B(c, 0xB9); D(c, 0x20);
        B(c, 0xF3, 0xA5); // 复制 sprite 对象 0x80 字节
        B(c, 0xBE); D(c, actorBase + 0xA8);
        B(c, 0xBF); D(c, spriteBase + 0x80);
        B(c, 0xB9); D(c, 6);
        B(c, 0xF3, 0xA5); // 完整复制 0x18 字节 sprite 包装
        B(c, 0xC7, 0x05); D(c, spriteBase + 0x80); D(c, spriteBase);
        B(c, 0xC7, 0x44, 0x24, 0x14); D(c, spriteBase + 0x80); // popad 将恢复新的 edx
        B(c, 0xC7, 0x44, 0x24, 0x24); D(c, 0x500); // 原栈 arg1=交通车绘制大小
        B(c, 0xC7, 0x05); D(c, stateBase + 0x164); D(c, 0x500);
        B(c, 0xFF, 0x05); D(c, stateBase + 0x148);
        Jmp(c, "original");
        c.Label("no_assets");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x170); D(c, 1);
        Jmp(c, "fallback");
        c.Label("no_model");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x170); D(c, 2);
        Jmp(c, "fallback");
        c.Label("no_frame");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x170); D(c, 3);
        Jmp(c, "fallback");
        c.Label("no_metadata");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x170); D(c, 4);
        c.Label("fallback");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x1A0); D(c, 0);
        B(c, 0xFF, 0x05); D(c, stateBase + 0x14C);
        c.Label("original");
        B(c, 0x61, 0x9D);
        CallAbs(c, base64 + RvaDrawSprite); // 正好一次，callee 清理原有五个栈参数
        JmpAbs(c, base64 + RvaPlayerCarCall + 5);
        return c.Finish();
    }

    private static void ApplyPlayerCar(IntPtr h, long base64, long codeBase, long stateBase, long spriteBase, Action<string> log, int carStyle, bool hideFallenVehicles = false)
    {
        long patch = base64 + RvaPlayerCarCall;
        byte[] head = Read(h, patch, 5);
        if (head[0] != 0xE8 || unchecked((uint)(patch + 5 + BitConverter.ToInt32(head, 1))) != (uint)(base64 + RvaDrawSprite))
            throw new InvalidOperationException("RiderDraw 实体 sprite 调用与预期不一致。");
        byte[] init = Read(h, base64 + RvaCarAnimationInit, 7);
        byte[] expectedInit = { 0x56, 0x0F, 0xBE, 0xF2, 0x57, 0x8B, 0xF9 };
        for (int i = 0; i < expectedInit.Length; i++)
            if (init[i] != expectedInit[i]) throw new InvalidOperationException("汽车动画初始化入口与预期不一致。");
        byte[] prepare = Read(h, base64 + RvaPrepareSprite, 5);
        if (prepare[0] != 0xE9 || unchecked((uint)(base64 + RvaPrepareSprite + 5 + BitConverter.ToInt32(prepare, 1))) != (uint)(base64 + 0x4E4F0))
            throw new InvalidOperationException("汽车方向选帧入口与预期不一致。");
        long cave = codeBase + RiderCopyCaveOffset;
        byte[] bounds = BuildCarBoundsCode(base64, codeBase + 0x1800, stateBase, codeBase + 0x3000);
        byte[] code = BuildPlayerCarCode(base64, cave, stateBase, spriteBase, codeBase + 0x3000, carStyle, codeBase + 0x1800, hideFallenVehicles);
        if (code.Length > 0x1800 - RiderCopyCaveOffset || bounds.Length > 0x800)
            throw new InvalidOperationException("汽车替换代码超出代码洞。");
        Write(h, codeBase + 0x1800, bounds);
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, 5));
        log("游戏内汽车替换已写入（所选款式 " + (carStyle + 1).ToString("00") + "；仅同向素材，编号不存在/仅对向时回退可用同向款式；碰撞体随实际车型匹配）。");
    }

    // 包住一次碰撞响应及双方回调，只撤销正常骑行玩家的碰撞垂直增量。
    internal static byte[] BuildCarBoundsCode(long base64, long cave, long state, long actor)
    {
        CodeBuffer c = new CodeBuffer(cave);
        long context = actor + 0x100;
        B(c, 0x60);
        B(c, 0xFF, 0x05); D(c, state + 0x1B0);
        B(c, 0xC7, 0x05); D(c, state + 0x1A0); D(c, 0);
        B(c, 0xC7, 0x05); D(c, state + 0x1B4); D(c, 1);
        foreach (long at in new long[] { actor + 0x44, actor + 0x68, actor + 0x6C, context + 0x10, context + 0x1C })
        { B(c, 0xC7, 0x05); D(c, at); D(c, 0); }
        B(c, 0xC7, 0x05); D(c, context + 0x18); D(c, 10000);
        MovEaxMem(c, base64 + RvaCameraX); B(c, 0xC1, 0xE0, 0x08, 0xA3); D(c, actor + 0x50);
        // 先查询所选款式正视、侧视，再查询款式0侧视进行原版纵深标定。
        for (int view = 0; view < 3; view++)
        {
            if (view == 1) { B(c, 0xC7, 0x05); D(c, actor + 0x68); D(c, 0x400000); }
            if (view == 2)
            {
                B(c, 0x83, 0x3D); D(c, base64 + RvaCarAssets); B(c, 0); Jcc(c, 0x84, "bounds_done");
                B(c, 0x83, 0x3D); D(c, base64 + RvaCarAssets + 4); B(c, 0); Jcc(c, 0x84, "bounds_done");
                MovEaxMem(c, base64 + RvaCarModels); B(c, 0x85, 0xC0); Jcc(c, 0x84, "bounds_done");
                B(c, 0x83, 0x38, 0); Jcc(c, 0x84, "bounds_done");
                B(c, 0xB9); D(c, actor + 0x8C); B(c, 0x31, 0xD2);
                CallAbs(c, base64 + RvaCarAnimationInit);
            }
            B(c, 0xB9); D(c, actor); B(c, 0xBA); D(c, context);
            CallAbs(c, base64 + RvaPrepareSprite);
            B(c, 0xE8); c.AddFixup("dimensions");
            B(c, 0x85, 0xC0); Jcc(c, 0x84, "bounds_done");
            if (view == 0)
            {
                B(c, 0xC1, 0xE0, 0x07, 0xA3); D(c, state + 0x1A4); // 半宽，世界单位转8.8
                B(c, 0xC1, 0xE1, 0x07, 0x89, 0x0D); D(c, state + 0x1AC); // 半高
            }
            else if (view == 1) { B(c, 0xA3); D(c, state + 0x1A8); }
            else
            {
                B(c, 0x89, 0xC1); MovEaxMem(c, state + 0x1A8);
                B(c, 0xC1, 0xE0, 0x06, 0x31, 0xD2, 0xF7, 0xF1); // 64 * 所选侧视长度 / 款式0侧视长度
                B(c, 0x85, 0xC0); Jcc(c, 0x84, "bounds_done");
                CmpEax(c, 0x400); Jcc(c, 0x87, "bounds_done");
                B(c, 0xA3); D(c, state + 0x1A8);
            }
        }
        B(c, 0x8B, 0x04, 0xED); D(c, base64 + RvaCarAssets); B(c, 0xA3); D(c, state + 0x1B8);
        B(c, 0x8B, 0x04, 0xED); D(c, base64 + RvaCarAssets + 4); B(c, 0xA3); D(c, state + 0x1BC);
        B(c, 0xC7, 0x05); D(c, state + 0x1A0); D(c, 1);
        B(c, 0xC7, 0x05); D(c, state + 0x1B4); D(c, 0);
        c.Label("bounds_done"); B(c, 0x61, 0xC3);
        c.Label("dimensions");
        MovEaxMem(c, actor + 0xA8); B(c, 0x85, 0xC0); Jcc(c, 0x84, "bad_dimensions");
        B(c, 0x8B, 0x50, 0x08, 0x85, 0xD2); Jcc(c, 0x84, "bad_dimensions");
        B(c, 0x81, 0x3A); D(c, 0xBC614E); Jcc(c, 0x85, "encoded_dimensions");
        B(c, 0xF6, 0x42, 0x0C, 1); Jcc(c, 0x85, "encoded_dimensions");
        B(c, 0x0F, 0xBF, 0x4A, 0x0A, 0x0F, 0xBF, 0x5A, 0x06, 0x29, 0xD9);
        B(c, 0x0F, 0xBF, 0x42, 0x08, 0x0F, 0xBF, 0x5A, 0x04, 0x29, 0xD8);
        Jmp(c, "dimensions_valid");
        c.Label("encoded_dimensions");
        B(c, 0x89, 0xC2);
        foreach (int offset in new int[] { 0x40, 0x3C })
        {
            B(c, 0x8B, 0x4A, (byte)offset, 0x85, 0xC9); Jcc(c, 0x89, "positive_" + offset);
            B(c, 0x83, 0xE1, 0x1F, 0xB8); D(c, 1); B(c, 0xD3, 0xE0);
            Jmp(c, "decoded_" + offset);
            c.Label("positive_" + offset); B(c, 0x89, 0xC8, 0x25); D(c, 0x1FF);
            c.Label("decoded_" + offset);
            if (offset == 0x40) B(c, 0x89, 0xC3); else B(c, 0x89, 0xD9);
        }
        c.Label("dimensions_valid");
        B(c, 0x85, 0xC0); Jcc(c, 0x8E, "bad_dimensions");
        CmpEax(c, 2048); Jcc(c, 0x8F, "bad_dimensions");
        B(c, 0x85, 0xC9); Jcc(c, 0x8E, "bad_dimensions");
        B(c, 0x81, 0xF9); D(c, 2048); Jcc(c, 0x8F, "bad_dimensions");
        // 与原绘制大小 0x500/256=5 及包装缩放位保持一致。
        B(c, 0xBB); D(c, 5);
        B(c, 0xF7, 0x05); D(c, actor + 0xB8); D(c, 0x10000); Jcc(c, 0x84, "not_double");
        B(c, 0xD1, 0xE3); c.Label("not_double");
        B(c, 0xF7, 0x05); D(c, actor + 0xB8); D(c, 0x20000); Jcc(c, 0x84, "not_quad");
        B(c, 0xC1, 0xE3, 2); c.Label("not_quad");
        B(c, 0x0F, 0xAF, 0xC3, 0x0F, 0xAF, 0xCB, 0xC3);
        c.Label("bad_dimensions"); B(c, 0x31, 0xC0, 0xC3);
        return c.Finish();
    }

    // 快照在当前栈帧，保留已有腾空/坡道运动、水平碰撞结果以及其他 actor 的响应。
    internal static byte[] BuildCarContactCode(long base64, long codeBase, long stateBase)
    {
        CodeBuffer c = new CodeBuffer(codeBase);
        B(c, 0x8D, 0x64, 0x24, 0xE0, 0x9C, 0x60); // 32 字节快照；pushfd; pushad
        B(c, 0xFF, 0x05); D(c, stateBase + 0x180);
        B(c, 0xC7, 0x44, 0x24, 0x24); D(c, 0);
        B(c, 0xC7, 0x44, 0x24, 0x34); D(c, 0);
        MovEaxMem(c, base64 + RvaPlayer);
        B(c, 0x85, 0xC0); Jcc(c, 0x84, "respond");
        B(c, 0x83, 0xB8); D(c, 0x2F4); B(c, 0); Jcc(c, 0x85, "respond");
        B(c, 0x83, 0xB8); D(c, 0x2F8); B(c, 0); Jcc(c, 0x85, "respond");
        B(c, 0x8B, 0xB8); D(c, 0x2FC); // edi=玩家 rider
        B(c, 0x39, 0xC1); Jcc(c, 0x84, "snapshot");
        B(c, 0x39, 0xC2); Jcc(c, 0x84, "snapshot");
        B(c, 0x85, 0xFF); Jcc(c, 0x84, "respond");
        B(c, 0x39, 0xF9); Jcc(c, 0x84, "snapshot");
        B(c, 0x39, 0xFA); Jcc(c, 0x85, "respond");
        c.Label("snapshot");
        B(c, 0xFF, 0x05); D(c, stateBase + 0x184);
        // 两份记录：actor 指针、路面相对高度 +20、垂直速度 +F4、待应用垂直冲量 +18C。
        for (int actor = 0; actor < 2; actor++)
        {
            int slot = 36 + actor * 16;
            B(c, 0x89, 0x44, 0x24, (byte)slot);
            int[] fields = { 0x20, 0xF4, 0x18C };
            for (int field = 0; field < fields.Length; field++)
            {
                B(c, 0x8B, 0x98); D(c, fields[field]);
                B(c, 0x89, 0x5C, 0x24, (byte)(slot + 4 + field * 4));
            }
            if (actor == 0)
            {
                B(c, 0x89, 0xF8, 0x85, 0xC0);
                Jcc(c, 0x84, "respond");
            }
        }
        c.Label("respond");
        B(c, 0x61, 0x9D);
        CallAbs(c, base64 + RvaCollisionResponse);
        // 原 496A6..496C1，回调顺序和 ECX 参数保持不变。
        B(c, 0x8B, 0x83); D(c, 0x1A4);
        B(c, 0x85, 0xC0); Jcc(c, 0x84, "second_callback");
        B(c, 0x89, 0xD9, 0xFF, 0xD0);
        c.Label("second_callback");
        B(c, 0x8B, 0x86); D(c, 0x1A4);
        B(c, 0x85, 0xC0); Jcc(c, 0x84, "restore");
        B(c, 0x89, 0xF1, 0xFF, 0xD0);
        c.Label("restore");
        B(c, 0x9C, 0x60, 0x8B, 0x44, 0x24, 0x24, 0x85, 0xC0);
        Jcc(c, 0x84, "done");
        B(c, 0x3B, 0x05); D(c, base64 + RvaPlayer); Jcc(c, 0x85, "done");
        // 碰撞导致真正摔车时，不把摔车状态强行改回骑行。
        B(c, 0x83, 0xB8); D(c, 0x2F4); B(c, 0); Jcc(c, 0x85, "done");
        B(c, 0x83, 0xB8); D(c, 0x2F8); B(c, 0); Jcc(c, 0x85, "done");
        B(c, 0x8B, 0x58, 0x20, 0x89, 0x1D); D(c, stateBase + 0x194);
        B(c, 0x8B, 0x98); D(c, 0xF4); B(c, 0x89, 0x1D); D(c, stateBase + 0x19C);
        B(c, 0x8B, 0x5C, 0x24, 0x28, 0x89, 0x1D); D(c, stateBase + 0x190);
        B(c, 0x8B, 0x5C, 0x24, 0x2C, 0x89, 0x1D); D(c, stateBase + 0x198);
        for (int actor = 0; actor < 2; actor++)
        {
            int slot = 36 + actor * 16;
            B(c, 0x8B, 0x44, 0x24, (byte)slot, 0x85, 0xC0);
            Jcc(c, 0x84, "done");
            if (actor == 1)
            {
                B(c, 0x8B, 0x5C, 0x24, 0x24, 0x3B, 0x83); D(c, 0x2FC);
                Jcc(c, 0x85, "done");
            }
            int[] fields = { 0x20, 0xF4, 0x18C };
            for (int field = 0; field < fields.Length; field++)
            {
                string same = "same_" + actor + "_" + field;
                B(c, 0x8B, 0x5C, 0x24, (byte)(slot + 4 + field * 4));
                B(c, 0x39, 0x98); D(c, fields[field]);
                Jcc(c, 0x84, same);
                B(c, 0xFF, 0x05); D(c, stateBase + (field == 0 ? 0x188 : 0x18C));
                B(c, 0x89, 0x98); D(c, fields[field]);
                c.Label(same);
            }
        }
        c.Label("done");
        B(c, 0x61, 0x9D, 0x8D, 0x64, 0x24, 0x20); // lea 不改变原回调返回的标志
        JmpAbs(c, base64 + RvaCarContactBack);
        return c.Finish();
    }

    private static void ApplyCarContact(IntPtr h, long base64, long codeBase, long stateBase, Action<string> log)
    {
        try
        {
            long patch = base64 + RvaCarContactCall;
            byte[] call = Read(h, patch, 5);
            if (call[0] != 0xE8 || unchecked((uint)(patch + 5 + BitConverter.ToInt32(call, 1))) != (uint)(base64 + RvaCollisionResponse))
                throw new InvalidOperationException("碰撞响应调用与预期不一致。");
            byte[] expected = { 0x8B, 0x83, 0xA4, 0x01, 0, 0, 0x85, 0xC0, 0x74, 0x04, 0x8B, 0xCB, 0xFF, 0xD0,
                0x8B, 0x86, 0xA4, 0x01, 0, 0, 0x85, 0xC0, 0x74, 0x04, 0x8B, 0xCE, 0xFF, 0xD0 };
            byte[] callbacks = Read(h, patch + 5, expected.Length);
            for (int i = 0; i < expected.Length; i++)
                if (callbacks[i] != expected[i]) throw new InvalidOperationException("碰撞回调序列与预期不一致。");
            byte[] code = BuildCarContactCode(base64, codeBase, stateBase);
            if (code.Length > 0x600) throw new InvalidOperationException("汽车碰撞代码超出代码区。");
            Write(h, codeBase, code);
            Write(h, patch, Jmp(patch, codeBase, 5));
            log("汽车碰撞稳定已写入（仅撤销玩家碰撞新增的垂直位移/速度；保留水平响应与原有道路运动）。");
        }
        catch (Exception ex) { log("汽车碰撞稳定写入失败：" + ex.Message); }
    }

    // 正常骑行时 bike 本体可能没有模型；有效车手/车辆图像位于 rider 的 sprite。
    // 在 RiderDraw 第二次调用后复制当时仍有效的包装，不缓存动画资源指针。
    internal static byte[] BuildRiderCopyHookCode(long base64, long caveBase, long stateBase)
    {
        CodeBuffer c = new CodeBuffer(caveBase);
        B(c, 0x8B, 0x96); D(c, 0x1C4); // 原指令 mov edx,[esi+0x1C4]
        B(c, 0x9C, 0x60); // pushfd; pushad
        B(c, 0xFF, 0x05); D(c, stateBase + 0x100);
        B(c, 0x89, 0x35); D(c, stateBase + 0x108);
        MovEaxMem(c, base64 + RvaPlayer);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "done");
        B(c, 0x8B, 0x80); D(c, 0x2FC); // player->rider
        B(c, 0xA3); D(c, stateBase + 0x10C);
        B(c, 0x3B, 0xF0); // cmp esi,eax
        Jcc(c, 0x85, "done");
        B(c, 0xFF, 0x05); D(c, stateBase + 0x104);
        B(c, 0x89, 0x15); D(c, stateBase + 0x124);
        B(c, 0x89, 0x3D); D(c, stateBase + 0x118);
        B(c, 0x8B, 0x86); D(c, 0x98);
        B(c, 0xA3); D(c, stateBase + 0x114);
        B(c, 0x8B, 0x86); D(c, 0xA8);
        B(c, 0xA3); D(c, stateBase + 0x110);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x85, "copy");
        B(c, 0xFF, 0x05); D(c, stateBase + 0x12C);
        Jmp(c, "done");
        c.Label("copy");
        // 复刻 sprite 函数的比例查表，将逻辑像素间距换算成世界单位。
        B(c, 0x8B, 0x47, 0x18, 0x0F, 0xB6, 0x56, 0x44, 0x03, 0xC2, 0xD1, 0xF8);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x88, "bad_scale");
        CmpEax(c, 0x1C00);
        Jcc(c, 0x8C, "scale_index_ok");
        MovEax(c, 0x1BFF);
        c.Label("scale_index_ok");
        B(c, 0x8B, 0x04, 0x85); D(c, base64 + RvaScaleTable);
        B(c, 0xA3); D(c, stateBase + 0x130);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x8E, "bad_scale");
        EmitRiderCopyAt(c, base64, stateBase, -96);
        EmitRiderCopyAt(c, base64, stateBase, -48);
        EmitRiderCopyAt(c, base64, stateBase, 48);
        EmitRiderCopyAt(c, base64, stateBase, 96);
        Jmp(c, "done");
        c.Label("bad_scale");
        B(c, 0xFF, 0x05); D(c, stateBase + 0x134);
        c.Label("done");
        B(c, 0x61, 0x9D);
        JmpAbs(c, base64 + RvaRiderCopyHook + 6);
        return c.Finish();
    }

    private static void EmitRiderCopyAt(CodeBuffer c, long base64, long stateBase, int pixelOffsetX)
    {
        B(c, 0x6A, 0x01); // mode=1，第二段为实体图像
        B(c, 0xFF, 0x76, 0x44);
        B(c, 0x8B, 0x46, 0x48, 0xC1, 0xF8, 0x08, 0x03, 0x46, 0x4C);
        B(c, 0xA3); D(c, stateBase + 0x11C);
        B(c, 0x50);
        B(c, 0x8B, 0x46, 0x50, 0x99, 0x81, 0xE2); D(c, 0xFF);
        B(c, 0x03, 0xC2, 0xC1, 0xF8, 0x08);
        B(c, 0xA3); D(c, stateBase + 0x120);
        B(c, 0x89, 0xC3); // ebx=原始世界 X（结束时由 popad 恢复）
        MovEax(c, pixelOffsetX * 65536);
        B(c, 0x99, 0xF7, 0x3D); D(c, stateBase + 0x130); // cdq; idiv scale
        B(c, 0x03, 0xC3);
        B(c, 0x50, 0x68); D(c, 0x200);
        B(c, 0x8D, 0x96); D(c, 0xA8); // edx=&rider->sprite
        B(c, 0x89, 0xF9); // ecx=edi（RiderDraw 的上下文）
        CallAbs(c, base64 + RvaDrawSprite);
        B(c, 0xFF, 0x05); D(c, stateBase + 0x128);
    }

    private static void ApplyRiderCopyHook(IntPtr h, long base64, long codeBase, long stateBase, Action<string> log)
    {
        long patch = base64 + RvaRiderCopyHook;
        byte[] expected = { 0x8B, 0x96, 0xC4, 0x01, 0x00, 0x00 };
        byte[] head = Read(h, patch, expected.Length);
        for (int i = 0; i < expected.Length; i++)
            if (head[i] != expected[i]) throw new InvalidOperationException("RiderDraw 第二段 sprite 调用后的入口与预期不一致。");
        long cave = codeBase + RiderCopyCaveOffset;
        byte[] code = BuildRiderCopyHookCode(base64, cave, stateBase);
        if (code.Length > 0x2000 - RiderCopyCaveOffset)
            throw new InvalidOperationException("骑手复制代码超出代码洞。");
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, expected.Length));
        log("玩家骑手 sprite 复制已写入（RiderDraw 第二段实体图像，额外绘制 4 份）。");
    }

    internal static byte[] BuildBikeCopyHookCode(long base64, long caveBase, long stateBase)
    {
        long playerVar = base64 + RvaPlayer;
        long backAddr = base64 + RvaBikeCopyHook + 5;
        CodeBuffer c = new CodeBuffer(caveBase);
        // 补回被覆盖的 5 字节：mov eax,[ebp-8]; mov edx,ebx
        B(c, 0x8B, 0x45, 0xF8);
        B(c, 0x8B, 0xD3);
        B(c, 0xFF, 0x05); D(c, stateBase + 0xD4); // 调试计数
        B(c, 0x60); // pushad
        MovEaxMem(c, playerVar);
        B(c, 0x3B, 0xF0); // cmp esi, eax
        Jcc(c, 0x85, "done");
        B(c, 0xFF, 0x05); D(c, stateBase + 0xD8); // 玩家命中计数
        EmitBikeCopyAt(c, base64, -10000);
        EmitBikeCopyAt(c, base64, -5000);
        EmitBikeCopyAt(c, base64, 5000);
        EmitBikeCopyAt(c, base64, 10000);
        c.Label("done");
        B(c, 0x61); // popad
        JmpAbs(c, backAddr);
        return c.Finish();
    }

    private static void EmitBikeCopyAt(CodeBuffer c, long base64, int offsetX)
    {
        B(c, 0x6A, 0x00); // push 0
        B(c, 0xFF, 0x76, 0x44); // push [esi+0x44]
        B(c, 0x8B, 0x46, 0x4C); // mov eax,[esi+0x4c]
        B(c, 0x50); // push eax
        B(c, 0x8B, 0x46, 0x50); // mov eax,[esi+0x50]
        B(c, 0x99); // cdq
        B(c, 0x81, 0xE2); D(c, 0xFF); // and edx,0xff
        B(c, 0x03, 0xC2); // add eax,edx
        B(c, 0xC1, 0xF8, 0x08); // sar eax,8
        B(c, 0x05); D(c, offsetX); // add eax,offsetX
        B(c, 0x50); // push eax
        B(c, 0x68); D(c, 0x200); // push 0x200
        B(c, 0x89, 0xFA); // mov edx,edi (edi = &bike+0xA8)
        B(c, 0x89, 0xD9); // mov ecx,ebx (上下文)
        CallAbs(c, base64 + RvaDrawSprite);
    }

    private static void ApplyBikeCopyHook(IntPtr h, long base64, long codeBase, long stateBase, Action<string> log)
    {
        long patch = base64 + RvaBikeCopyHook;
        byte[] head = Read(h, patch, 5);
        if (head[0] == 0xE9)
        {
            log("调试摩托复制 hook 已经存在。");
            return;
        }
        if (head[0] != 0x8B || head[1] != 0x45 || head[2] != 0xF8 ||
            head[3] != 0x8B || head[4] != 0xD3)
            throw new InvalidOperationException("BikeDraw 第一段 sprite 调用后的入口与预期不一致。");
        long cave = codeBase + BikeCopyCaveOffset;
        byte[] code = BuildBikeCopyHookCode(base64, cave, stateBase);
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, 5));
        log("调试摩托复制已写入（BikeDraw 内部、第一段 sprite 调用之后）。");
    }

    private static void ApplyCarShell(IntPtr h, long base64, long codeBase, long stateBase, long spriteBase, long shellBase, bool debugCopies, Action<string> log)
    {
        long patch = base64 + RvaCarShellHook;
        byte[] head = Read(h, patch, 5);
        if (head[0] == 0xE9)
        {
            long target = patch + 5 + BitConverter.ToInt32(head, 1);
            if (target == 0)
                throw new InvalidOperationException("汽车外观入口跳转目标无效。");
        }
        else if (head[0] != 0xE8 || head[1] != 0xFB || head[2] != 0xED || head[3] != 0xF9 || head[4] != 0xFF)
        {
            throw new InvalidOperationException("汽车外观入口与预期不一致。");
        }

        byte[] code = BuildCarShellCode(base64, codeBase, stateBase, spriteBase, shellBase, debugCopies);
        Write(h, codeBase, code);
        if (debugCopies)
        {
            Write(h, codeBase + 0x600, BuildBikeCopyWrapper(base64, codeBase + 0x600, stateBase));
            log("调试摩托复制已写入（玩家摩托 sprite 横向额外绘制 4 份）。");
        }
        else
        {
            Write(h, codeBase + 0x600, BuildCarDrawWrapper(base64, spriteBase, codeBase + 0x600, stateBase + 0x88));
            log("汽车外观壳已写入（在玩家摩托上叠加汽车 sprite；交通车只用于复制 sprite）。");
        }
        Write(h, patch, Jmp(patch, codeBase, 5));
    }

    internal static byte[] BuildBikeCopyWrapper(long base64, long wrapperBase, long stateBase)
    {
        CodeBuffer c = new CodeBuffer(wrapperBase);
        B(c, 0xFF, 0x05); D(c, stateBase + 0x88); // wrapper 调用计数
        B(c, 0x51); // push ecx
        B(c, 0x52); // push edx
        B(c, 0xA1); D(c, stateBase + 0x04); // mov eax, [state+4] 原绘制回调
        B(c, 0x85, 0xC0); // test eax, eax
        Jcc(c, 0x84, "skip_original");
        B(c, 0xFF, 0xD0); // call eax
        c.Label("skip_original");
        B(c, 0x5A); // pop edx
        B(c, 0x59); // pop ecx
        B(c, 0x60); // pushad
        B(c, 0x89, 0xD5); // mov ebp, edx  context
        B(c, 0x89, 0xCF); // mov edi, ecx  bike
        // 世界 X 的缩放约为 0.01 像素/单位，必须用很大的偏移才能拉开。
        // 直接展开 4 个偏移；加上原绘制共 5 辆摩托。
        EmitBikeCopy(c, base64, -10000);
        EmitBikeCopy(c, base64, -5000);
        EmitBikeCopy(c, base64, 5000);
        EmitBikeCopy(c, base64, 10000);
        B(c, 0x61); // popad
        B(c, 0xC3); // ret
        return c.Finish();
    }

    private static void EmitBikeCopy(CodeBuffer c, long base64, int offsetX)
    {
        B(c, 0x6A, 0x00); // push 0 (arg5)
        B(c, 0xFF, 0x77, 0x44); // push [edi+0x44] (arg4)
        B(c, 0x8B, 0x47, 0x4C); // mov eax,[edi+0x4c]
        B(c, 0x50); // push eax (arg3)
        B(c, 0x8B, 0x47, 0x50); // mov eax,[edi+0x50]
        B(c, 0xC1, 0xF8, 0x08); // sar eax,8
        B(c, 0x05); D(c, offsetX); // add eax, offsetX
        B(c, 0x50); // push eax (arg2)
        B(c, 0x68); D(c, 0x200); // push 0x200 (arg1)
        B(c, 0x8D, 0x97, 0xA8, 0x00, 0x00, 0x00); // lea edx,[edi+0xA8]
        B(c, 0x8B, 0xCD); // mov ecx,ebp (context)
        CallAbs(c, base64 + RvaDrawSprite);
    }

    internal static byte[] BuildCarShellCode(long base64, long codeBase, long stateBase, long spriteBase, long shellBase, bool debugCopies)
    {
        CodeBuffer c = new CodeBuffer(codeBase);
        B(c, 0x60, 0x9C);
        B(c, 0x8B, 0x3D); D(c, base64 + RvaPlayer);
        B(c, 0x85, 0xFF);
        Jcc(c, 0x84, "after");
        B(c, 0x83, 0xBF, 0xC4, 0x00, 0x00, 0x00, 0x04);
        Jcc(c, 0x85, "after");

        if (debugCopies)
        {
            B(c, 0xFF, 0x05); D(c, stateBase + 0xD4); // 调试分支运行计数
            B(c, 0x8B, 0x87, 0xDC, 0x00, 0x00, 0x00); // mov eax,[edi+0xDC]
            B(c, 0x3D); D(c, codeBase + 0x600); // cmp eax, 复制 wrapper
            Jcc(c, 0x84, "debug_installed");
            B(c, 0xA3); D(c, stateBase + 0x04); // mov [state+4],eax 保存原回调
            B(c, 0xC7, 0x87, 0xDC, 0x00, 0x00, 0x00); D(c, codeBase + 0x600); // mov [edi+0xDC],wrapper
            c.Label("debug_installed");
            Jmp(c, "after");
        }

        // 地图变化时丢弃旧的汽车 sprite。
        B(c, 0x0F, 0xB6, 0x05); D(c, base64 + RvaRaceLevel);
        B(c, 0x39, 0x05); D(c, stateBase + 0x14);
        Jcc(c, 0x84, "map_ok");
        B(c, 0xC7, 0x05); D(c, stateBase); D(c, 0);
        B(c, 0xC7, 0x05); D(c, stateBase + 0x1C); D(c, 0);
        B(c, 0xA3); D(c, stateBase + 0x14);
        c.Label("map_ok");

        // 换 bike 时重新保存原始绘制字段。
        B(c, 0x39, 0x3D); D(c, stateBase + 0x20);
        Jcc(c, 0x84, "bike_same");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x2C); D(c, 0);
        B(c, 0x89, 0x3D); D(c, stateBase + 0x20);
        c.Label("bike_same");

        // 没有汽车 sprite 时，复制一辆交通车的 sprite 对象。
        B(c, 0x83, 0x3D); D(c, stateBase); B(c, 0x00);
        Jcc(c, 0x85, "have_sprite");
        B(c, 0xA1); D(c, base64 + RvaActorChain);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "restore");
        B(c, 0x8B, 0x00);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "restore");
        B(c, 0x8B, 0x00);
        c.Label("search_loop");
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "restore");
        B(c, 0x83, 0xB8, 0xC4, 0x00, 0x00, 0x00, 0x10);
        Jcc(c, 0x85, "search_next");
        B(c, 0x0F, 0xB6, 0x0D); D(c, base64 + RvaCarTypeCount);
        B(c, 0x85, 0xC9);
        Jcc(c, 0x84, "search_next");
        B(c, 0x0F, 0xB6, 0x90, 0xFC, 0x01, 0x00, 0x00);
        B(c, 0x39, 0xCA);
        Jcc(c, 0x83, "search_next");
        B(c, 0x8B, 0x90, 0xA8, 0x00, 0x00, 0x00);
        B(c, 0x85, 0xD2);
        Jcc(c, 0x84, "search_next");
        B(c, 0x8B, 0x4A, 0x08);
        B(c, 0x85, 0xC9);
        Jcc(c, 0x84, "search_next");
        B(c, 0x81, 0x39); D(c, 0x00BC614E);
        Jcc(c, 0x85, "search_next");
        B(c, 0xF6, 0x80, 0xC0, 0x00, 0x00, 0x00, 0x08);
        Jcc(c, 0x85, "search_next");
        B(c, 0x8B, 0x88, 0x08, 0x02, 0x00, 0x00);
        B(c, 0x85, 0xC9);
        Jcc(c, 0x84, "search_next");
        B(c, 0x8B, 0x88, 0xDC, 0x00, 0x00, 0x00);
        B(c, 0x81, 0xF9); D(c, base64 + RvaCarDraw);
        Jcc(c, 0x84, "car_ok");
        B(c, 0x81, 0xF9); D(c, base64 + RvaCarThunk);
        Jcc(c, 0x85, "search_next");
        c.Label("car_ok");

        // 复制 sprite 对象 0x80 字节到私有缓冲区。
        B(c, 0x50);
        B(c, 0x57);
        B(c, 0xFC);
        B(c, 0x8B, 0xB0, 0xA8, 0x00, 0x00, 0x00);
        B(c, 0xBF); D(c, spriteBase);
        B(c, 0xB9); D(c, 0x20);
        B(c, 0xF3, 0xA5); // rep movsd，旧代码误用 stosd 会用 actor 地址填充缓冲区
        B(c, 0x5F);
        B(c, 0x58);
        B(c, 0xA1); D(c, spriteBase + 0x3C);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x85, "spr_3c_ok");
        B(c, 0xB8); D(c, 0x1E);
        B(c, 0xA3); D(c, spriteBase + 0x3C);
        c.Label("spr_3c_ok");
        B(c, 0xA1); D(c, spriteBase + 0x40);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x85, "spr_40_ok");
        B(c, 0xB8); D(c, 0x15);
        B(c, 0xA3); D(c, spriteBase + 0x40);
        c.Label("spr_40_ok");
        B(c, 0xC7, 0x05); D(c, stateBase); D(c, spriteBase);
        B(c, 0xC7, 0x05); D(c, stateBase + 0x58); D(c, 3);
        B(c, 0xC7, 0x05); D(c, stateBase + 0x60); D(c, 1);
        B(c, 0xA3); D(c, stateBase + 0x24);
        B(c, 0x8B, 0x88, 0x08, 0x02, 0x00, 0x00);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x28);
        B(c, 0x0F, 0xB6, 0x88, 0xFC, 0x01, 0x00, 0x00);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x18);
        B(c, 0x0F, 0xB6, 0x0D); D(c, base64 + RvaCarTypeCount);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x54);
        B(c, 0x8B, 0x0D); D(c, spriteBase + 8);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x5C);
        B(c, 0x8B, 0x90, 0xB0, 0x00, 0x00, 0x00);
        B(c, 0x85, 0xD2);
        Jcc(c, 0x85, "car_b0_ok");
        B(c, 0xBA); D(c, 0x1E);
        c.Label("car_b0_ok");
        B(c, 0x89, 0x15); D(c, stateBase + 0x40);
        B(c, 0x8B, 0x90, 0xB4, 0x00, 0x00, 0x00);
        B(c, 0x85, 0xD2);
        Jcc(c, 0x85, "car_b4_ok");
        B(c, 0xBA); D(c, 0x15);
        c.Label("car_b4_ok");
        B(c, 0x89, 0x15); D(c, stateBase + 0x44);
        B(c, 0x8B, 0x90, 0xB8, 0x00, 0x00, 0x00);
        B(c, 0x89, 0x15); D(c, stateBase + 0x48);
        B(c, 0x8B, 0x90, 0xBC, 0x00, 0x00, 0x00);
        B(c, 0x89, 0x15); D(c, stateBase + 0x4C);
        B(c, 0x8B, 0x0D); D(c, spriteBase + 0x0C);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x64);
        B(c, 0x8B, 0x0D); D(c, spriteBase + 0x3C);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x68);
        B(c, 0x8B, 0x0D); D(c, spriteBase + 0x40);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x6C);
        B(c, 0x8B, 0x8F, 0x98, 0x00, 0x00, 0x00);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x70);
        B(c, 0x8B, 0x8F, 0x44, 0x00, 0x00, 0x00);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x74);
        B(c, 0x8B, 0x8F, 0x48, 0x00, 0x00, 0x00);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x78);
        B(c, 0x8B, 0x8F, 0x50, 0x00, 0x00, 0x00);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x7C);
        B(c, 0x8B, 0x8F, 0x54, 0x00, 0x00, 0x00);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x80);
        B(c, 0x8B, 0x8F, 0xDC, 0x00, 0x00, 0x00);
        B(c, 0x89, 0x0D); D(c, stateBase + 0x84);
        B(c, 0xC7, 0x05); D(c, spriteBase + 0x80); D(c, spriteBase);
        B(c, 0xC7, 0x05); D(c, spriteBase + 0x84); D(c, 0);
        B(c, 0xA1); D(c, stateBase + 0x40);
        B(c, 0xA3); D(c, spriteBase + 0x88);
        B(c, 0xA1); D(c, stateBase + 0x44);
        B(c, 0xA3); D(c, spriteBase + 0x8C);
        B(c, 0xA1); D(c, stateBase + 0x48);
        B(c, 0xA3); D(c, spriteBase + 0x90);
        B(c, 0xA1); D(c, stateBase + 0x4C);
        B(c, 0xA3); D(c, spriteBase + 0x94);
        Jmp(c, "have_sprite");

        c.Label("search_next");
        B(c, 0x8B, 0x00);
        Jmp(c, "search_loop");

        c.Label("have_sprite");
        B(c, 0x83, 0x3D); D(c, stateBase); B(c, 0x00);
        Jcc(c, 0x84, "restore");

        // 第一次应用时保存原始绘制字段。
        B(c, 0x83, 0x3D); D(c, stateBase + 0x2C); B(c, 0x00);
        Jcc(c, 0x85, "saved");
        B(c, 0x8B, 0x87, 0xDC, 0x00, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x04);
        B(c, 0x8B, 0x87, 0xA8, 0x00, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x08);
        B(c, 0x8B, 0x87, 0xB0, 0x00, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x30);
        B(c, 0x8B, 0x87, 0xB4, 0x00, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x34);
        B(c, 0x8B, 0x87, 0xB8, 0x00, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x38);
        B(c, 0x8B, 0x87, 0xBC, 0x00, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x3C);
        B(c, 0x8B, 0x87, 0xFC, 0x02, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x10);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "no_rider_save");
        B(c, 0x8B, 0x80, 0xDC, 0x00, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x0C);
        c.Label("no_rider_save");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x2C); D(c, 1);
        c.Label("saved");

        // 摔倒时让游戏自己的摔车绘制接管。
        B(c, 0x83, 0xBF, 0xF4, 0x02, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "restore");
        B(c, 0x83, 0xBF, 0xF8, 0x02, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "restore");

        B(c, 0xC7, 0x87, 0xDC, 0x00, 0x00, 0x00); D(c, codeBase + 0x600);
        B(c, 0x8B, 0x87, 0xDC, 0x00, 0x00, 0x00);
        B(c, 0xA3); D(c, stateBase + 0x84);
        c.Label("applied");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x1C); D(c, 1);
        Jmp(c, "after");

        c.Label("restore");
        B(c, 0x83, 0x3D); D(c, stateBase + 0x2C); B(c, 0x00);
        Jcc(c, 0x84, "after");
        B(c, 0xA1); D(c, stateBase + 0x08);
        B(c, 0x89, 0x87, 0xA8, 0x00, 0x00, 0x00);
        B(c, 0xA1); D(c, stateBase + 0x30);
        B(c, 0x89, 0x87, 0xB0, 0x00, 0x00, 0x00);
        B(c, 0xA1); D(c, stateBase + 0x34);
        B(c, 0x89, 0x87, 0xB4, 0x00, 0x00, 0x00);
        B(c, 0xA1); D(c, stateBase + 0x38);
        B(c, 0x89, 0x87, 0xB8, 0x00, 0x00, 0x00);
        B(c, 0xA1); D(c, stateBase + 0x3C);
        B(c, 0x89, 0x87, 0xBC, 0x00, 0x00, 0x00);
        B(c, 0x8B, 0x87, 0xDC, 0x00, 0x00, 0x00);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "restore_bike_draw");
        B(c, 0x3D); D(c, codeBase + 0x600);
        Jcc(c, 0x85, "restore_rider");
        c.Label("restore_bike_draw");
        B(c, 0xA1); D(c, stateBase + 0x04);
        B(c, 0x89, 0x87, 0xDC, 0x00, 0x00, 0x00);
        c.Label("restore_rider");
        B(c, 0x8B, 0x0D); D(c, stateBase + 0x10);
        B(c, 0x85, 0xC9);
        Jcc(c, 0x84, "restored");
        B(c, 0x83, 0xB9, 0xDC, 0x00, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "restored");
        B(c, 0xA1); D(c, stateBase + 0x0C);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "restored");
        B(c, 0x89, 0x81, 0xDC, 0x00, 0x00, 0x00);
        c.Label("restored");
        B(c, 0xC7, 0x05); D(c, stateBase + 0x1C); D(c, 0);

        c.Label("after");
        B(c, 0x9D, 0x61);
        CallAbs(c, base64 + RvaDrawList);
        JmpAbs(c, base64 + RvaCarShellBack);
        return c.Finish();
    }

    internal static byte[] BuildCarDrawWrapper(long base64, long spriteBase, long wrapperBase, long counterAddr)
    {
        CodeBuffer c = new CodeBuffer(wrapperBase);
        B(c, 0xFF, 0x05); D(c, counterAddr);
        B(c, 0x51);
        B(c, 0x52);
        // 调用玩家原来的绘制回调（不假设它一定是 0x45C000）。
        B(c, 0xA1); D(c, counterAddr - 0x84);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "skip_original_draw");
        B(c, 0xFF, 0xD0);
        c.Label("skip_original_draw");
        B(c, 0x5A);
        B(c, 0x59);
        // 记录摩托车绘制用的世界坐标；车体绘制与游戏 0x45C11C 的调用完全一致。
        B(c, 0x8B, 0x41, 0x44);
        B(c, 0xA3); D(c, counterAddr + 4);
        B(c, 0x8B, 0x41, 0x48);
        B(c, 0xA3); D(c, counterAddr + 8);
        B(c, 0x8B, 0x41, 0x4C);
        B(c, 0xA3); D(c, counterAddr + 0x10);
        B(c, 0x8B, 0x41, 0x50);
        B(c, 0xA3); D(c, counterAddr + 0xC);
        B(c, 0x60);
        B(c, 0x89, 0xD3);
        B(c, 0x6A, 0x00);
        B(c, 0xFF, 0x71, 0x44);
        B(c, 0x8B, 0x41, 0x4C);
        B(c, 0x50);
        B(c, 0x8B, 0x41, 0x50);
        B(c, 0x99);
        B(c, 0x81, 0xE2, 0xFF, 0x00, 0x00, 0x00);
        B(c, 0x03, 0xC2);
        B(c, 0xC1, 0xF8, 0x08);
        B(c, 0x50);
        B(c, 0x68); D(c, 0x200);
        B(c, 0xBA); D(c, spriteBase + 0x80);
        B(c, 0x8B, 0xCB);
        CallAbs(c, base64 + RvaDrawSprite);
        B(c, 0x61);
        B(c, 0xC3);
        return c.Finish();
    }

    internal static byte[] BuildScreenCaptureCode(long base64, long caveBase, long stateBase)
    {
        long playerVar = base64 + RvaPlayer;
        long scaleTable = base64 + RvaScaleTable;
        long cameraX = base64 + RvaCameraX;
        long cameraY = base64 + RvaCameraY;
        long screenWidth = base64 + RvaScreenWidth;
        long backAddr = base64 + RvaDrawCallback + 5;
        CodeBuffer c = new CodeBuffer(caveBase);
        B(c, 0xFF, 0x05); D(c, stateBase + 0xB4); // inc dword [state+0xB4]：绘制回调计数
        B(c, 0x60); // pushad
        B(c, 0x89, 0xC3); // mov ebx, eax：保存绘制回调指针

        MovEaxMem(c, playerVar);
        B(c, 0x3B, 0xC8); // cmp ecx, eax
        Jcc(c, 0x85, "done"); // jne done

        // 绘制循环调用回调时：ecx=actor，edx=绘制上下文，ebx=回调指针。
        B(c, 0x89, 0x1D); D(c, stateBase + 0xCC); // mov [state+0xCC], ebx：玩家回调指针
        B(c, 0x89, 0x15); D(c, stateBase + 0xB8); // mov [state+0xB8], edx：上下文指针
        B(c, 0x8B, 0x41, 0x44);
        B(c, 0xA3); D(c, stateBase + 0xBC); // [actor+0x44]
        B(c, 0x8B, 0x41, 0x50);
        B(c, 0xA3); D(c, stateBase + 0xC0); // [actor+0x50]
        B(c, 0x8B, 0x41, 0x4C);
        B(c, 0xA3); D(c, stateBase + 0xC4); // [actor+0x4c]
        B(c, 0x8B, 0x41, 0x48);
        B(c, 0xA3); D(c, stateBase + 0xC8); // [actor+0x48]
        B(c, 0x89, 0xD5); // mov ebp, edx
        // 与游戏 0x466A60 的变换保持一致，计算玩家摩托的最终屏幕坐标。
        B(c, 0x0F, 0xB6, 0x79, 0x44); // movzx edi, byte [ecx+0x44]
        B(c, 0x8B, 0x55, 0x18); // mov edx, [ebp+0x18]
        B(c, 0x03, 0xD7); // add edx, edi
        B(c, 0xD1, 0xFA); // sar edx, 1
        B(c, 0x81, 0xFA); D(c, 0x1C00); // cmp edx, 0x1c00
        Jcc(c, 0x8C, "clamp"); // jl clamp
        B(c, 0xBA); D(c, 0x1BFF); // mov edx, 0x1bff
        c.Label("clamp");
        B(c, 0x8B, 0x1C, 0x95); D(c, scaleTable); // mov ebx, [edx*4+scaleTable]

        // 相机插值 X 偏移
        B(c, 0x8B, 0x45, 0x00); // mov eax, [ebp]
        B(c, 0x8B, 0x70, 0x10); // mov esi, [eax+0x10]
        B(c, 0x2B, 0x75, 0x10); // sub esi, [ebp+0x10]
        B(c, 0x0F, 0xAF, 0xF7); // imul esi, edi
        B(c, 0xC1, 0xFE, 0x08); // sar esi, 8
        B(c, 0x03, 0x75, 0x10); // add esi, [ebp+0x10]

        // arg2 = 世界 X（8.8 定点，向下取整）
        B(c, 0x8B, 0x41, 0x50); // mov eax, [ecx+0x50]
        B(c, 0x99); // cdq
        B(c, 0x81, 0xE2); D(c, 0xFF); // and edx, 0xff
        B(c, 0x03, 0xC2); // add eax, edx
        B(c, 0xC1, 0xF8, 0x08); // sar eax, 8
        B(c, 0x03, 0xC6); // add eax, esi
        B(c, 0x2B, 0x05); D(c, cameraX); // sub eax, [cameraX]
        B(c, 0x0F, 0xAF, 0xC3); // imul eax, ebx
        B(c, 0x8B, 0x15); D(c, screenWidth); // mov edx, [screenWidth]
        B(c, 0x81, 0xE2, 0xFE, 0xFF, 0xFF, 0xFF); // and edx, 0xfffffffe
        B(c, 0xC1, 0xE2, 0x0F); // shl edx, 15
        B(c, 0x03, 0xC2); // add eax, edx
        B(c, 0xC1, 0xF8, 0x10); // sar eax, 16
        B(c, 0xA3); D(c, stateBase + 0xA0); // mov [state+0xA0], eax
        B(c, 0xA1); D(c, screenWidth); // mov eax, [screenWidth]
        B(c, 0xA3); D(c, stateBase + 0xA8); // mov [state+0xA8], eax

        // 相机插值 Y 偏移
        B(c, 0x8B, 0x45, 0x00); // mov eax, [ebp]
        B(c, 0x8B, 0x70, 0x14); // mov esi, [eax+0x14]
        B(c, 0x2B, 0x75, 0x14); // sub esi, [ebp+0x14]
        B(c, 0x0F, 0xAF, 0xF7); // imul esi, edi
        B(c, 0xC1, 0xFE, 0x08); // sar esi, 8
        B(c, 0x03, 0x75, 0x14); // add esi, [ebp+0x14]

        // arg3 = 世界 Y + 弹跳高度（第二次 sprite 调用的公式）
        B(c, 0x8B, 0x41, 0x48); // mov eax, [ecx+0x48]
        B(c, 0xC1, 0xF8, 0x08); // sar eax, 8
        B(c, 0x03, 0x41, 0x4C); // add eax, [ecx+0x4c]
        B(c, 0x03, 0xC6); // add eax, esi
        B(c, 0x8B, 0x15); D(c, cameraY); // mov edx, [cameraY]
        B(c, 0x2B, 0xD0); // sub edx, eax
        B(c, 0x0F, 0xAF, 0xD3); // imul edx, ebx
        B(c, 0x81, 0xC2); D(c, 0x5A0000); // add edx, 0x5a0000
        B(c, 0xC1, 0xFA, 0x10); // sar edx, 16
        B(c, 0x89, 0x15); D(c, stateBase + 0xA4); // mov [state+0xA4], edx
        B(c, 0xFF, 0x05); D(c, stateBase + 0xB0); // inc dword [state+0xB0]

        c.Label("done");
        B(c, 0x61); // popad
        // 补回被覆盖的 5 字节：call eax; mov edx,[ebp+8]
        B(c, 0xFF, 0xD0); // call eax
        B(c, 0x8B, 0x55, 0x08); // mov edx, [ebp+8]
        JmpAbs(c, backAddr);
        return c.Finish();
    }

    private static void ApplyScreenCapture(IntPtr h, long base64, long codeBase, long stateBase, Action<string> log)
    {
        long patch = base64 + RvaDrawCallback;
        byte[] head = Read(h, patch, 5);
        if (head[0] == 0xE9)
        {
            log("玩家屏幕坐标捕获已经存在。");
            return;
        }
        if (head[0] != 0xFF || head[1] != 0xD0 ||
            head[2] != 0x8B || head[3] != 0x55 || head[4] != 0x08)
            throw new InvalidOperationException("玩家绘制回调入口与预期不一致。");
        long cave = codeBase + ScreenCaptureCaveOffset;
        byte[] code = BuildScreenCaptureCode(base64, cave, stateBase);
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, 5));
        log("玩家屏幕坐标捕获已写入（供叠加层或调试复制诊断使用）。");
    }

    private static void ApplyHitbox(IntPtr h, long base64, long codeBase, Action<string> log, long stateBase = 0)
    {
        long patch = base64 + RvaHitboxHook;
        byte[] head = Read(h, patch, 6);
        if (head[0] == 0xE9)
        {
            uint target = unchecked((uint)(patch + 5 + BitConverter.ToInt32(head, 1)));
            if (target != (uint)codeBase)
                throw new InvalidOperationException("碰撞体入口已有其他补丁，未覆盖其跳转。");
        }
        else if (head[0] != 0x55 || head[1] != 0x8B || head[2] != 0xEC || head[3] != 0x83 || head[4] != 0xEC || head[5] != 0x0C)
        {
            throw new InvalidOperationException("碰撞体入口与预期不一致。");
        }

        byte[] code = stateBase == 0 ? BuildHitboxCode(base64, codeBase) : BuildCarHitboxCode(base64, codeBase, stateBase);
        if (code.Length > 0x200) throw new InvalidOperationException("碰撞体代码超出代码区。");
        Write(h, codeBase, code);
        Write(h, patch, Jmp(patch, codeBase, 6));
        log(stateBase == 0 ? "独立摩托碰撞体放大已写入。" : "汽车碰撞体自动匹配已写入（正视宽/高，侧视纵深比例；素材失效时恢复原尺寸）。");
    }

    internal static byte[] BuildCarHitboxCode(long base64, long codeBase, long state)
    {
        CodeBuffer c = new CodeBuffer(codeBase);
        long saved = state + 0x2400; // codeBase allocation+3400，独立保存原玩家尺寸
        B(c, 0x9C, 0x60); MovEaxMem(c, base64 + RvaPlayer);
        B(c, 0x85, 0xC0); Jcc(c, 0x84, "done");
        B(c, 0x83, 0xB8); D(c, 0x2F4); B(c, 0); Jcc(c, 0x85, "done");
        B(c, 0x83, 0xB8); D(c, 0x2F8); B(c, 0); Jcc(c, 0x85, "done");
        B(c, 0x3B, 0x05); D(c, saved); Jcc(c, 0x84, "known_player");
        B(c, 0xA3); D(c, saved);
        for (int i = 0; i < 3; i++)
        { B(c, 0x8B, 0x88); D(c, 0x174 + i * 4); B(c, 0x89, 0x0D); D(c, saved + 4 + i * 4); }
        c.Label("known_player");
        B(c, 0xBB); D(c, saved + 4);
        B(c, 0x83, 0x3D); D(c, state + 0x1A0); B(c, 1); Jcc(c, 0x85, "apply");
        B(c, 0x8B, 0x0D); D(c, state + 0x15C);
        B(c, 0x83, 0xF9, 14); Jcc(c, 0x87, "apply");
        B(c, 0x0F, 0xB6, 0x15); D(c, base64 + RvaCarTypeCount);
        B(c, 0x39, 0xD1); Jcc(c, 0x83, "apply");
        for (int i = 0; i < 2; i++)
        {
            B(c, 0x8B, 0x14, 0xCD); D(c, base64 + RvaCarAssets + i * 4);
            B(c, 0x85, 0xD2); Jcc(c, 0x84, "apply");
            B(c, 0x3B, 0x15); D(c, state + 0x1B8 + i * 4); Jcc(c, 0x85, "apply");
        }
        B(c, 0xBB); D(c, state + 0x1A4);
        c.Label("apply");
        // state 顺序 W/L/H 与 actor +174/+178/+17C 一致；纵向和横向单位不同。
        for (int i = 0; i < 3; i++)
        { B(c, 0x8B, 0x4B, (byte)(i * 4), 0x89, 0x88); D(c, 0x174 + i * 4); }
        c.Label("done"); B(c, 0x61, 0x9D, 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x0C);
        JmpAbs(c, base64 + RvaHitboxBack);
        return c.Finish();
    }

    internal static byte[] BuildHitboxCode(long base64, long codeBase)
    {
        CodeBuffer c = new CodeBuffer(codeBase);
        B(c, 0x60);
        B(c, 0xA1); D(c, base64 + RvaPlayer);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "done");
        B(c, 0x83, 0xB8, 0xF4, 0x02, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "done");
        B(c, 0x83, 0xB8, 0xF8, 0x02, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "done");
        B(c, 0xC7, 0x80, 0x74, 0x01, 0x00, 0x00); D(c, 0x4000);
        B(c, 0xC7, 0x80, 0x78, 0x01, 0x00, 0x00); D(c, 0x80);
        B(c, 0xC7, 0x80, 0x7C, 0x01, 0x00, 0x00); D(c, 0x2800);
        c.Label("done");
        B(c, 0x61);
        B(c, 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x0C);
        JmpAbs(c, base64 + RvaHitboxBack);
        return c.Finish();
    }

    private static void ApplyRam(IntPtr h, long base64, Action<string> log, long knockdownEntry)
    {
        long patch = base64 + RvaRamHook;
        long cave = base64 + RvaRamCave;
        long hElyCave = base64 + RvaRamForward;
        byte[] head = Read(h, patch, 5);
        bool nativeEntry = head[0] == 0x8B && head[1] == 0x5D && head[2] == 0xF8 && head[3] == 0x8B && head[4] == 0xD6;
        if (!nativeEntry && head[0] != 0xE9)
            throw new InvalidOperationException("撞击入口既非原版指令，也非已知跳转。");
        long target = nativeEntry ? 0 : unchecked((uint)(patch + 5 + BitConverter.ToInt32(head, 1)));
        if (!nativeEntry && target != cave && target != hElyCave)
            throw new InvalidOperationException("撞击入口已经指向未知地址 0x" + target.ToString("X") + "。");

        byte[] code = BuildRamCode(base64, nativeEntry, knockdownEntry);
        if (target != cave) CheckCave(h, cave, code.Length);
        Write(h, cave, code);
        Write(h, base64 + RvaDiag, new byte[0x40]);
        uint diagOld;
        VirtualProtectEx(h, new IntPtr(base64 + 0x6000), new IntPtr(0x1000), PageExecuteReadWrite, out diagOld);
        Write(h, patch, Jmp(patch, cave, 5));
        log("大运模式已启用（" + (nativeEntry ? "原版碰撞入口" : "增强器碰撞入口") + "；玩家撞到其他车手时对方倒地，并播放撞击音效）。");
    }

    internal static byte[] BuildRamCode(long base64, bool nativeEntry = false, long knockdownEntry = 0)
    {
        long diag = base64 + RvaDiag;
        CodeBuffer c = new CodeBuffer(base64 + RvaRamCave);
        B(c, 0x60, 0x9C);
        B(c, 0xFF, 0x05); D(c, diag + 0x00);
        B(c, 0x8B, 0x45, 0xF8);
        B(c, 0x83, 0xB8, 0xC4, 0x00, 0x00, 0x00, 0x08);
        Jcc(c, 0x85, "a_ready");
        B(c, 0x8B, 0x80, 0xC4, 0x01, 0x00, 0x00);
        c.Label("a_ready");
        B(c, 0x89, 0xF2);
        B(c, 0x83, 0xBA, 0xC4, 0x00, 0x00, 0x00, 0x08);
        Jcc(c, 0x85, "b_ready");
        B(c, 0x8B, 0x92, 0xC4, 0x01, 0x00, 0x00);
        c.Label("b_ready");
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "forward");
        B(c, 0x85, 0xD2);
        Jcc(c, 0x84, "forward");
        B(c, 0x83, 0xB8, 0xC4, 0x00, 0x00, 0x00, 0x04);
        Jcc(c, 0x85, "forward");
        B(c, 0x83, 0xBA, 0xC4, 0x00, 0x00, 0x00, 0x04);
        Jcc(c, 0x85, "forward");
        B(c, 0x8B, 0x0D); D(c, base64 + RvaPlayer);
        B(c, 0x39, 0xC8);
        Jcc(c, 0x84, "player_a");
        B(c, 0x39, 0xCA);
        Jcc(c, 0x84, "player_b");
        Jmp(c, "forward");
        c.Label("player_a");
        Jmp(c, "have");
        c.Label("player_b");
        B(c, 0x87, 0xD0);
        c.Label("have");
        B(c, 0x39, 0xD0);
        Jcc(c, 0x84, "forward");
        B(c, 0xFF, 0x05); D(c, diag + 0x04);
        B(c, 0xA3); D(c, diag + 0x18);
        B(c, 0x89, 0x15); D(c, diag + 0x1C);
        B(c, 0x83, 0xBA, 0xF4, 0x02, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "forward");
        B(c, 0x89, 0xD7);
        B(c, 0x89, 0xF9);
        CallAbs(c, base64 + RvaCrashFunction);
        B(c, 0xFF, 0x05); D(c, diag + 0x08);
        B(c, 0x89, 0x3D); D(c, diag + 0x14);
        B(c, 0xC7, 0x05); D(c, diag + 0x20); D(c, 1);
        B(c, 0x6A, 0x00);
        B(c, 0x68); D(c, 0x2710);
        B(c, 0x6A, 0x00);
        B(c, 0x8B, 0x0D); D(c, base64 + RvaCrashSoundObject);
        B(c, 0xBA); D(c, 0x4000);
        CallAbs(c, base64 + RvaCrashSound);
        B(c, 0x83, 0xBF, 0xF4, 0x02, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "skip");
        if (knockdownEntry != 0) {
            // The original crash call declined. This branch commits the ram's own fall.
            B(c, 0x9C,0x60,0x6A,0,0x57);
            FinishKnockdownCall(c, knockdownEntry, 3);
            B(c, 0x61,0x9D);
        }
        EmitVictimCrash(c, base64, "fields_done");
        if (knockdownEntry != 0) {
            B(c, 0x9C,0x60,0x6A,0,0x57); FinishKnockdownCall(c, knockdownEntry, 7); B(c, 0x61,0x9D);
        }
        c.Label("skip");
        B(c, 0x9D, 0x61);
        JmpAbs(c, base64 + RvaRamBack);
        c.Label("forward");
        B(c, 0x9D, 0x61);
        if (nativeEntry) B(c, 0x8B, 0x5D, 0xF8, 0x8B, 0xD6);
        JmpAbs(c, base64 + (nativeEntry ? RvaRamHook + 5 : RvaRamForward));
        return c.Finish();
    }

    private static void EmitVictimCrash(CodeBuffer c, long base64, string doneLabel)
    {
        B(c, 0xC7, 0x87, 0xF4, 0x02, 0x00, 0x00); D(c, 1);
        B(c, 0xC7, 0x87, 0xF8, 0x02, 0x00, 0x00); D(c, 1);
        B(c, 0xC6, 0x87, 0x44, 0x02, 0x00, 0x00, 0x01);
        B(c, 0xC7, 0x87, 0xBC, 0x00, 0x00, 0x00); D(c, 0);
        B(c, 0xC7, 0x87, 0x88, 0x02, 0x00, 0x00); D(c, 0);
        B(c, 0x8B, 0x0D); D(c, base64 + 0x18F1F4);
        B(c, 0x89, 0x8F, 0x74, 0x01, 0x00, 0x00);
        B(c, 0x8B, 0x0D); D(c, base64 + 0x18F1F8);
        B(c, 0x89, 0x8F, 0x78, 0x01, 0x00, 0x00);
        B(c, 0x8B, 0x0D); D(c, base64 + 0x18F1FC);
        B(c, 0x89, 0x8F, 0x7C, 0x01, 0x00, 0x00);
        B(c, 0x8B, 0x97, 0xFC, 0x02, 0x00, 0x00);
        B(c, 0x85, 0xD2);
        Jcc(c, 0x84, doneLabel);
        B(c, 0x80, 0x8A, 0xC0, 0x00, 0x00, 0x00, 0x03);
        B(c, 0xC7, 0x82, 0x40, 0x02, 0x00, 0x00); D(c, 0);
        c.Label(doneLabel);
    }

    private static void ApplyInvincible(IntPtr h, long base64, Action<string> log)
    {
        long patch = base64 + RvaInvincibleHook;
        long cave = base64 + RvaInvincibleCave;
        byte[] head = Read(h, patch, 6);
        if (head[0] != 0xE9 &&
            (head[0] != 0x55 || head[1] != 0x8B || head[2] != 0xEC || head[3] != 0x83 || head[4] != 0xEC || head[5] != 0x34))
            throw new InvalidOperationException("霸体入口与预期不一致。");

        byte[] code = BuildInvincibleCode(base64, cave);
        if (head[0] != 0xE9) CheckCave(h, cave, code.Length);
        Write(h, cave, code);
        Write(h, patch, Jmp(patch, cave, 6));

        long bikePatch = base64 + RvaInvincibleBikeHook;
        long bikeCave = base64 + RvaInvincibleBikeCave;
        byte[] bikeHead = Read(h, bikePatch, 5);
        if (bikeHead[0] != 0xE9 &&
            (bikeHead[0] != 0x55 || bikeHead[1] != 0x8B || bikeHead[2] != 0xEC || bikeHead[3] != 0x51 || bikeHead[4] != 0x53))
            throw new InvalidOperationException("霸体（车辆/建筑）入口与预期不一致。");
        byte[] bikeCode = BuildInvincibleBikeCode(base64, bikeCave);
        if (bikeHead[0] != 0xE9) CheckCave(h, bikeCave, bikeCode.Length);
        Write(h, bikeCave, bikeCode);
        Write(h, bikePatch, Jmp(bikePatch, bikeCave, 5));

        Write(h, base64 + RvaDiag, new byte[0x40]);
        uint diagOld;
        VirtualProtectEx(h, new IntPtr(base64 + 0x6000), new IntPtr(0x1000), PageExecuteReadWrite, out diagOld);

        log("霸体模式已启用（玩家不会倒地）。");
    }

    internal static byte[] BuildInvincibleCode(long base64, long codeBase)
    {
        long diag = base64 + RvaDiag;
        CodeBuffer c = new CodeBuffer(codeBase);
        B(c, 0xFF, 0x05); D(c, diag + 0x24);
        B(c, 0x8B, 0xC1);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "original");
        B(c, 0x83, 0xB8, 0xC4, 0x00, 0x00, 0x00, 0x08);
        Jcc(c, 0x85, "check");
        B(c, 0x8B, 0x80, 0xC4, 0x01, 0x00, 0x00);
        c.Label("check");
        B(c, 0x8B, 0x15); D(c, base64 + RvaPlayer);
        B(c, 0x85, 0xD2);
        Jcc(c, 0x84, "original");
        B(c, 0x39, 0xD0);
        Jcc(c, 0x84, "block");
        c.Label("original");
        B(c, 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x34);
        JmpAbs(c, base64 + RvaInvincibleBack);
        c.Label("block");
        B(c, 0xFF, 0x05); D(c, diag + 0x28);
        B(c, 0xA3); D(c, diag + 0x2C);
        B(c, 0xC3);
        return c.Finish();
    }

    internal static byte[] BuildInvincibleBikeCode(long base64, long codeBase)
    {
        long diag = base64 + RvaDiag;
        CodeBuffer c = new CodeBuffer(codeBase);
        B(c, 0xFF, 0x05); D(c, diag + 0x30);
        B(c, 0x51);
        B(c, 0x8B, 0xC1);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "restore");
        B(c, 0x83, 0xB8, 0xC4, 0x00, 0x00, 0x00, 0x08);
        Jcc(c, 0x85, "check");
        B(c, 0x8B, 0x80, 0xC4, 0x01, 0x00, 0x00);
        c.Label("check");
        B(c, 0x8B, 0x0D); D(c, base64 + RvaPlayer);
        B(c, 0x85, 0xC9);
        Jcc(c, 0x84, "restore");
        B(c, 0x39, 0xC8);
        Jcc(c, 0x84, "block");
        c.Label("restore");
        B(c, 0x59);
        B(c, 0x55, 0x8B, 0xEC, 0x51, 0x53);
        JmpAbs(c, base64 + RvaInvincibleBikeBack);
        c.Label("block");
        B(c, 0xFF, 0x05); D(c, diag + 0x34);
        B(c, 0xA3); D(c, diag + 0x38);
        B(c, 0x59);
        B(c, 0xC2, 0x08, 0x00);
        return c.Finish();
    }

    private static void WriteAoeData(IntPtr h, long base64, bool downedEnabled, bool downedAll, bool aoeAll, int maxExtra)
    {
        long address = base64 + RvaAoeData;
        byte[] data = BuildAoeData(downedEnabled, downedAll, aoeAll, maxExtra);
        byte[] old = Read(h, address, data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            if (old[i] != 0xCC && old[i] != data[i])
                throw new InvalidOperationException("群体攻击配置区已被占用。");
        }
        Write(h, address, data);
    }

    internal static byte[] BuildAoeData(bool downedEnabled, bool downedAll, bool aoeAll, int maxExtra)
    {
        byte[] data = new byte[8];
        data[0] = downedEnabled ? (byte)1 : (byte)0;
        data[1] = downedAll ? (byte)1 : (byte)0;
        data[2] = aoeAll ? (byte)1 : (byte)0;
        BitConverter.GetBytes(maxExtra).CopyTo(data, 4);
        return data;
    }

    internal static byte[] BuildDownedCode(long base64)
    {
        long data = base64 + RvaAoeData;
        CodeBuffer c = new CodeBuffer(base64 + RvaDownedCave);
        CmpByteAbs(c, data, 0);
        Jcc(c, 0x84, "allow");
        CmpByteAbs(c, data + 1, 0);
        Jcc(c, 0x85, "check");
        MovEaxMem(c, base64 + RvaPlayer);
        B(c, 0x39, 0xC7);
        Jcc(c, 0x85, "allow");
        c.Label("check");
        B(c, 0x83, 0xBB, 0xF4, 0x02, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "miss");
        c.Label("allow");
        B(c, 0x8B, 0x93, 0xFC, 0x02, 0x00, 0x00);
        JmpAbs(c, base64 + RvaDownedBack);
        c.Label("miss");
        JmpAbs(c, base64 + RvaDownedMiss);
        return c.Finish();
    }

    internal static byte[] BuildAoeCode(long base64, long arrayBase, bool opponentsTargetPlayer = false)
    {
        long data = base64 + RvaAoeData;
        CodeBuffer c = new CodeBuffer(base64 + RvaAoeCave);
        B(c, 0x60, 0x9C);
        B(c, 0x8B, 0x45, 0xF0);
        B(c, 0x8B, 0x55, 0xF4);
        B(c, 0x55, 0x89, 0xE5, 0x83, 0xEC, 0x40);

        MovEbpReg(c, -4, 0);
        MovEbpReg(c, -8, 2);
        MovEbpReg(c, -12, 7);
        MovEbpReg(c, -16, 3);
        for (int i = 20; i <= 52; i += 4) MovEbpDword(c, -i, 0);

        if (opponentsTargetPlayer)
        {
            SkipNetworkRace(c, base64, "usual_aoe");
            B(c, 0x83, 0xBF); D(c, 0x324); B(c, 1);
            Jcc(c, 0x84, "done"); // the player's already the primary victim
            c.Label("usual_aoe");
        }
        CmpByteAbs(c, data + 2, 0);
        Jcc(c, 0x85, "scope_ok");
        MovEaxMem(c, base64 + RvaPlayer);
        B(c, 0x39, 0xC7);
        Jcc(c, 0x85, "done");
        c.Label("scope_ok");

        MovEaxMem(c, data + 4);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x8E, "done");
        MovEbpReg(c, -40, 0);
        MovEax(c, arrayBase);
        MovEbpReg(c, -36, 0);
        MovEaxMem(c, base64 + RvaRacerCount);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x8E, "done");
        CmpEax(c, 10001);
        Jcc(c, 0x86, "count_ok");
        MovEax(c, 10001);
        c.Label("count_ok");
        MovEbpReg(c, -32, 0);

        MovRegEbp(c, 0, -16);
        B(c, 0x8B, 0x80, 0xFC, 0x02, 0x00, 0x00);
        B(c, 0x8B, 0x40, 0x28);
        MovRegEbp(c, 1, -12);
        B(c, 0x3B, 0x41, 0x28);
        B(c, 0x0F, 0x9F, 0xC0);
        B(c, 0x0F, 0xB6, 0xC0);
        MovEbpReg(c, -28, 0);

        CmpByteAbs(c, data, 0);
        Jcc(c, 0x84, "no_down_filter");
        MovEbpDword(c, -48, 1);
        CmpByteAbs(c, data + 1, 0);
        Jcc(c, 0x85, "no_down_filter");
        MovEaxMem(c, base64 + RvaPlayer);
        B(c, 0x39, 0xC7);
        Jcc(c, 0x84, "no_down_filter");
        MovEbpDword(c, -48, 0);
        c.Label("no_down_filter");

        c.Label("loop");
        MovRegEbp(c, 0, -20);
        CmpEbpReg(c, -32);
        Jcc(c, 0x8D, "done");
        MovRegEbp(c, 1, -36);
        B(c, 0x8B, 0x04, 0x81);
        B(c, 0x85, 0xC0);
        Jcc(c, 0x84, "next");
        CmpEax(c, 0x00100000);
        Jcc(c, 0x82, "next");
        CmpEax(c, 0x7FFFFFFF);
        Jcc(c, 0x87, "next");
        CmpEbpReg(c, -12);
        Jcc(c, 0x84, "next");
        CmpEbpReg(c, -16);
        Jcc(c, 0x84, "next");
        B(c, 0x8B, 0x88, 0xFC, 0x02, 0x00, 0x00);
        B(c, 0x85, 0xC9);
        Jcc(c, 0x84, "next");
        B(c, 0x81, 0xF9);
        D(c, 0x00100000);
        Jcc(c, 0x82, "next");
        B(c, 0x81, 0xF9);
        D(c, 0x7FFFFFFF);
        Jcc(c, 0x87, "next");
        CmpEbpDwordImm8(c, -48, 0);
        Jcc(c, 0x84, "no_down_candidate");
        B(c, 0x83, 0xB8, 0xF4, 0x02, 0x00, 0x00, 0x00);
        Jcc(c, 0x85, "next");
        B(c, 0x80, 0xB9, 0x80, 0x00, 0x00, 0x00, 0x09);
        Jcc(c, 0x84, "next");
        c.Label("no_down_candidate");
        MovEbpReg(c, -52, 1);
        MovEbpReg(c, -44, 0);

        MovRegEbp(c, 1, -4);
        B(c, 0x8B, 0x89, 0xD4, 0x01, 0x00, 0x00);
        B(c, 0x51);
        B(c, 0xFF, 0x75, 0xE4);
        MovRegEbp(c, 1, -12);
        MovRegEbp(c, 2, -52);
        CallAbs(c, base64 + RvaFightReach);
        B(c, 0x84, 0xC0);
        Jcc(c, 0x84, "next");

        MovRegEbp(c, 0, -4);
        B(c, 0x83, 0xB8, 0xD4, 0x01, 0x00, 0x00, 0x03);
        Jcc(c, 0x85, "no_chain");
        MovRegEbp(c, 0, -8);
        B(c, 0x99);
        B(c, 0x83, 0xE2, 0x03);
        B(c, 0x8D, 0x0C, 0x02);
        B(c, 0xC1, 0xF9, 0x02);
        B(c, 0x41);
        Jmp(c, "have_arg1");
        c.Label("no_chain");
        B(c, 0x31, 0xC9);
        c.Label("have_arg1");
        B(c, 0x31, 0xC0);
        B(c, 0x85, 0xC9);
        B(c, 0x0F, 0x95, 0xC0);
        B(c, 0x83, 0xC0, 0x04);
        B(c, 0x50);
        B(c, 0x51);
        MovRegEbp(c, 2, -8);
        MovRegEbp(c, 1, -44);
        CallAbs(c, base64 + RvaDamageFunction);
        B(c, 0xFF, 0x45, 0xE8);
        MovRegEbp(c, 0, -24);
        CmpEbpReg(c, -40);
        Jcc(c, 0x8D, "done");

        c.Label("next");
        B(c, 0xFF, 0x45, 0xEC);
        Jmp(c, "loop");

        c.Label("done");
        B(c, 0x89, 0xEC);
        B(c, 0x5D);
        B(c, 0x9D);
        B(c, 0x61);
        B(c, 0x6A, 0x02);
        B(c, 0x89, 0xFA);
        B(c, 0x89, 0xD9);
        JmpAbs(c, base64 + RvaAoeReturn);
        return c.Finish();
    }

    private static long GetRrdataBase(IntPtr h, long base64)
    {
        try
        {
            byte[] dos = Read(h, base64, 0x40);
            int pe = BitConverter.ToInt32(dos, 0x3C);
            byte[] header = Read(h, base64 + pe, 0x18);
            int count = BitConverter.ToUInt16(header, 6);
            int optionalSize = BitConverter.ToUInt16(header, 0x14);
            long table = base64 + pe + 0x18 + optionalSize;
            for (int i = 0; i < count; i++)
            {
                byte[] section = Read(h, table + i * 40, 40);
                string name = Encoding.ASCII.GetString(section, 0, 8).TrimEnd('\0');
                if (name == ".rrdata")
                    return base64 + BitConverter.ToInt32(section, 12);
            }
        }
        catch
        {
        }
        return 0;
    }

    private sealed class CodeBuffer
    {
        public readonly List<byte> Data = new List<byte>();
        public readonly long CodeBase;
        private readonly Dictionary<string, int> labels = new Dictionary<string, int>();
        private readonly List<int> fixupPositions = new List<int>();
        private readonly List<string> fixupLabels = new List<string>();

        public CodeBuffer(long codeBase)
        {
            CodeBase = codeBase;
        }

        public int Position
        {
            get { return Data.Count; }
        }

        public void Label(string name)
        {
            labels[name] = Data.Count;
        }

        public void AddFixup(string label)
        {
            fixupPositions.Add(Data.Count);
            fixupLabels.Add(label);
            Data.AddRange(new byte[4]);
        }

        public byte[] Finish()
        {
            for (int i = 0; i < fixupPositions.Count; i++)
            {
                int target;
                if (!labels.TryGetValue(fixupLabels[i], out target))
                    throw new InvalidOperationException("缺少代码标签：" + fixupLabels[i]);
                int rel = target - (fixupPositions[i] + 4);
                byte[] bytes = BitConverter.GetBytes(rel);
                for (int j = 0; j < 4; j++) Data[fixupPositions[i] + j] = bytes[j];
            }
            return Data.ToArray();
        }
    }

    private static void B(CodeBuffer c, params byte[] bytes)
    {
        c.Data.AddRange(bytes);
    }

    private static void D(CodeBuffer c, long value)
    {
        c.Data.AddRange(BitConverter.GetBytes((int)value));
    }

    private static void MovEax(CodeBuffer c, long value)
    {
        B(c, 0xB8);
        D(c, value);
    }

    private static void MovEaxMem(CodeBuffer c, long address)
    {
        B(c, 0xA1);
        D(c, address);
    }

    private static void CmpEax(CodeBuffer c, int value)
    {
        B(c, 0x3D);
        D(c, value);
    }

    private static void MovEbpDword(CodeBuffer c, int displacement, int value)
    {
        B(c, 0xC7, 0x45, (byte)(sbyte)displacement);
        D(c, value);
    }

    private static void MovEbpReg(CodeBuffer c, int displacement, byte register)
    {
        B(c, 0x89, (byte)(0x45 | (register << 3)), (byte)(sbyte)displacement);
    }

    private static void MovRegEbp(CodeBuffer c, byte register, int displacement)
    {
        B(c, 0x8B, (byte)(0x45 | (register << 3)), (byte)(sbyte)displacement);
    }

    private static void CmpEbpReg(CodeBuffer c, int displacement)
    {
        B(c, 0x3B, 0x45, (byte)(sbyte)displacement);
    }

    private static void CmpEbpDwordImm8(CodeBuffer c, int displacement, byte value)
    {
        B(c, 0x83, 0x7D, (byte)(sbyte)displacement, value);
    }

    private static void CmpByteAbs(CodeBuffer c, long address, byte value)
    {
        B(c, 0x80, 0x3D);
        D(c, address);
        B(c, value);
    }

    private static void Jmp(CodeBuffer c, string label)
    {
        B(c, 0xE9);
        c.AddFixup(label);
    }

    private static void Jcc(CodeBuffer c, byte opcode, string label)
    {
        B(c, 0x0F, opcode);
        c.AddFixup(label);
    }

    private static void CallAbs(CodeBuffer c, long target)
    {
        B(c, 0xE8);
        D(c, target - (c.CodeBase + c.Position + 4));
    }

    private static void JmpAbs(CodeBuffer c, long target)
    {
        B(c, 0xE9);
        D(c, target - (c.CodeBase + c.Position + 4));
    }

    private static void AddImul(List<byte> code, int factor)
    {
        code.Add(0x6B); code.Add(0xC9); code.Add((byte)factor);
    }

    private static void Add(List<byte> code, int value)
    {
        code.AddRange(BitConverter.GetBytes(value));
    }

    private static void EmitByteAbs(List<byte> code, long address, byte value)
    {
        code.Add(0xC6);
        code.Add(0x05);
        Add(code, (int)address);
        code.Add(value);
    }

    private static void Add(List<byte> code, uint value)
    {
        code.AddRange(BitConverter.GetBytes(value));
    }

    private static byte[] Jmp(long from, long to, int length)
    {
        byte[] patch = new byte[length];
        patch[0] = 0xE9;
        Array.Copy(BitConverter.GetBytes((int)(to - (from + 5))), 0, patch, 1, 4);
        for (int i = 5; i < length; i++) patch[i] = 0x90;
        return patch;
    }

    private static bool IsPatched(IntPtr h, long address)
    {
        return Read(h, address, 1)[0] == 0xE9;
    }

    internal static int[] ReadDiag(Process process)
    {
        IntPtr baseAddress = GetBase(process);
        if (baseAddress == IntPtr.Zero) return null;
        IntPtr h = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
        if (h == IntPtr.Zero) return null;
        try
        {
            byte[] data = Read(h, baseAddress.ToInt64() + RvaDiag, 0x40);
            int[] values = new int[16];
            for (int i = 0; i < values.Length; i++)
                values[i] = BitConverter.ToInt32(data, i * 4);
            return values;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    internal static int ReadPlayerCallback(Process process)
    {
        IntPtr baseAddress = GetBase(process);
        if (baseAddress == IntPtr.Zero) return 0;
        IntPtr h = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
        if (h == IntPtr.Zero) return 0;
        try
        {
            long b = baseAddress.ToInt64();
            int player = BitConverter.ToInt32(Read(h, b + RvaPlayer, 4), 0);
            if (player == 0) return 0;
            // 如果汽车/调试 hook 已安装，state+4 保存的是玩家原始绘制回调。
            try
            {
                byte[] hook = Read(h, b + RvaCarShellHook, 5);
                if (hook[0] == 0xE9)
                {
                    long cave = b + RvaCarShellHook + 5 + BitConverter.ToInt32(hook, 1);
                    long stateBase = cave + 0x1000;
                    int original = BitConverter.ToInt32(Read(h, stateBase + 0x04, 4), 0);
                    if (original != 0) return original;
                }
            }
            catch { }
            return BitConverter.ToInt32(Read(h, player + 0xDC, 4), 0);
        }
        catch
        {
            return 0;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    internal static bool DumpCallbackCode(Process process, int address, string path)
    {
        if (address == 0) return false;
        IntPtr h = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
        if (h == IntPtr.Zero) return false;
        try
        {
            byte[] data = Read(h, (uint)address, 0x400);
            File.WriteAllBytes(path, data);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    internal static string PlayerScreenError = "";

    internal static int[] ReadPlayerScreen(Process process)
    {
        PlayerScreenError = "";
        IntPtr baseAddress = GetBase(process);
        if (baseAddress == IntPtr.Zero) { PlayerScreenError = "no module base"; return null; }
        IntPtr h = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
        if (h == IntPtr.Zero) { PlayerScreenError = "OpenProcess failed " + Marshal.GetLastWin32Error(); return null; }
        try
        {
            long base64 = baseAddress.ToInt64();
            int player = BitConverter.ToInt32(Read(h, base64 + RvaPlayer, 4), 0);
            if (player == 0) { PlayerScreenError = "player=0"; return null; }
            int type = BitConverter.ToInt32(Read(h, player + 0xC4, 4), 0);
            if (type != 4) { PlayerScreenError = "player type=" + type; return null; }
            int crash = BitConverter.ToInt32(Read(h, player + 0x2F4, 4), 0);
            if (crash != 0) { PlayerScreenError = "player crash=" + crash; return null; }
            byte[] hook = Read(h, base64 + RvaDrawCallback, 5);
            if (hook[0] != 0xE9) { PlayerScreenError = "screen capture hook missing"; return null; }
            long cave = base64 + RvaDrawCallback + 5 + BitConverter.ToInt32(hook, 1);
            long stateBase = cave - ScreenCaptureCaveOffset + 0x1000;
            byte[] data = Read(h, stateBase + 0xA0, 0x30);
            int sx = BitConverter.ToInt32(data, 0);
            int sy = BitConverter.ToInt32(data, 4);
            int logicalWidth = BitConverter.ToInt32(data, 8);
            int tick = BitConverter.ToInt32(data, 0x10);
            int entry = BitConverter.ToInt32(data, 0x14);
            int ctx = BitConverter.ToInt32(data, 0x18);
            int b44 = BitConverter.ToInt32(data, 0x1C);
            int b50 = BitConverter.ToInt32(data, 0x20);
            int b4c = BitConverter.ToInt32(data, 0x24);
            int b48 = BitConverter.ToInt32(data, 0x28);
            int callback = BitConverter.ToInt32(data, 0x2C);
            if (logicalWidth <= 0 || logicalWidth > 4096)
            {
                PlayerScreenError = "logicalWidth=" + logicalWidth + " drawEntry=" + entry +
                    " callback=0x" + callback.ToString("X8") + " ctx=0x" + ctx.ToString("X8") +
                    " b44=" + b44 + " b50=" + b50 + " b4c=" + b4c + " b48=" + b48;
                return null;
            }
            return new int[] { sx, sy, logicalWidth, tick, entry, ctx, b44, b50, b4c, b48, callback };
        }
        catch (Exception ex)
        {
            PlayerScreenError = ex.Message;
            return null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    internal static int[] ReadCarState(Process process)
    {
        IntPtr baseAddress = GetBase(process);
        if (baseAddress == IntPtr.Zero) return null;
        IntPtr h = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
        if (h == IntPtr.Zero) return null;
        try
        {
            return ReadCarStateFromMemory(baseAddress.ToInt64(),
                delegate(long address, int length) { return Read(h, address, length); });
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // 各版本 hook 的代码洞偏移不同；调试复制不安装旧汽车壳 hook。
    // 检查本项目的代码特征，避免把 HD/其他补丁的 E9 跳转误认成状态区。
    internal static int[] ReadCarStateFromMemory(long base64, Func<long, int, byte[]> read)
    {
        int[] hooks = { RvaPlayerCarCall, RvaRiderCopyHook, RvaBikeCopyHook, RvaCarShellHook };
        int[] offsets = { RiderCopyCaveOffset, RiderCopyCaveOffset, BikeCopyCaveOffset, 0 };
        for (int candidate = 0; candidate < hooks.Length; candidate++)
        {
            try
            {
                long hook = base64 + hooks[candidate];
                byte[] head = read(hook, 5);
                if (head[0] != 0xE9) continue;
                long cave = unchecked((uint)(hook + 5 + BitConverter.ToInt32(head, 1)));
                long state = cave - offsets[candidate] + 0x1000;
                int hookRva = hooks[candidate];
                byte[] code = read(cave, hookRva == RvaRiderCopyHook ? 14 : (hookRva == RvaBikeCopyHook ? 11 : 8));
                if (hookRva == RvaPlayerCarCall)
                {
                    if (code[0] != 0x9C || code[1] != 0x60 || code[2] != 0xFF || code[3] != 0x05 ||
                        BitConverter.ToUInt32(code, 4) != (uint)(state + 0x140)) continue;
                }
                else if (hookRva == RvaRiderCopyHook)
                {
                    if (code[0] != 0x8B || code[1] != 0x96 || code[2] != 0xC4 || code[3] != 0x01 ||
                        code[4] != 0 || code[5] != 0 || code[6] != 0x9C || code[7] != 0x60 ||
                        code[8] != 0xFF || code[9] != 0x05 ||
                        BitConverter.ToUInt32(code, 10) != (uint)(state + 0x100)) continue;
                }
                else if (hookRva == RvaBikeCopyHook)
                {
                    if (code[0] != 0x8B || code[1] != 0x45 || code[2] != 0xF8 ||
                        code[3] != 0x8B || code[4] != 0xD3 || code[5] != 0xFF || code[6] != 0x05 ||
                        BitConverter.ToUInt32(code, 7) != (uint)(state + 0xD4)) continue;
                }
                else if (code[0] != 0x60 || code[1] != 0x9C || code[2] != 0x8B || code[3] != 0x3D ||
                    BitConverter.ToUInt32(code, 4) != (uint)(base64 + RvaPlayer)) continue;

                byte[] data = read(state, CarStateSize);
                int[] values = new int[CarStateSize / 4];
                for (int i = 0; i < values.Length; i++)
                    values[i] = BitConverter.ToInt32(data, i * 4);
                return values;
            }
            catch (Win32Exception) { } // 进程退出/该候选不可读时继续尝试另一入口。
        }
        return null;
    }

    private static byte[] Read(IntPtr h, long address, int length)
    {
        byte[] buffer = new byte[length];
        IntPtr read;
        if (!ReadProcessMemory(h, new IntPtr(address), buffer, (IntPtr)length, out read) || read.ToInt64() != length)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return buffer;
    }

    private static void CheckCave(IntPtr h, long address, int length)
    {
        byte[] data = Read(h, address, length);
        for (int i = 0; i < data.Length; i++)
            if (data[i] != 0xCC) throw new InvalidOperationException("代码洞已被占用。");
    }

    private static void Write(IntPtr h, long address, byte[] data)
    {
        uint old;
        if (!VirtualProtectEx(h, new IntPtr(address), (IntPtr)data.Length, PageExecuteReadWrite, out old))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            IntPtr written;
            if (!WriteProcessMemory(h, new IntPtr(address), data, (IntPtr)data.Length, out written) || written.ToInt64() != data.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            FlushInstructionCache(h, new IntPtr(address), (IntPtr)data.Length);
        }
        finally
        {
            uint ignored;
            VirtualProtectEx(h, new IntPtr(address), (IntPtr)data.Length, old, out ignored);
        }
    }

    private static IntPtr GetBase(Process process)
    {
        // A fresh WOW64 process may expose only native ntdll at first. MainModule
        // can cache that first entry for this Process instance. Enumerate afresh
        // and identify the actual PE32 EXE; zero lets Apply's retry loop wait.
        IntPtr h = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
        if (h == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            IntPtr[] modules = new IntPtr[1024];
            int needed;
            if (!EnumProcessModulesEx(h, modules, modules.Length * IntPtr.Size, out needed, ListModules32Bit) ||
                needed > modules.Length * IntPtr.Size) return IntPtr.Zero;
            for (int i = 0; i < needed / IntPtr.Size; i++)
            {
                StringBuilder name = new StringBuilder(1024);
                uint length = GetModuleFileNameEx(h, modules[i], name, name.Capacity);
                if (length == 0 || length >= name.Capacity) continue;
                long address = IntPtr.Size == 4 ? unchecked((uint)modules[i].ToInt32()) : modules[i].ToInt64();
                if (IsGameImage(name.ToString(), address, delegate(long at, int size) { return Read(h, at, size); }))
                    return modules[i];
            }
            return IntPtr.Zero;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    internal static bool IsGameImage(string path, long address, Func<long, int, byte[]> read)
    {
        if (!string.Equals(Path.GetFileName(path), "RoadRash95.exe", StringComparison.OrdinalIgnoreCase) ||
            address <= 0 || address > uint.MaxValue) return false;
        try
        {
            byte[] dos = read(address, 64);
            if (dos[0] != 0x4D || dos[1] != 0x5A) return false;
            int pe = BitConverter.ToInt32(dos, 0x3C);
            if (pe < 64 || pe > 0x100000 || address + pe + 84 > 0x100000000L) return false;
            byte[] nt = read(address + pe, 84);
            uint size = BitConverter.ToUInt32(nt, 80);
            return BitConverter.ToUInt32(nt, 0) == 0x4550 && BitConverter.ToUInt16(nt, 4) == 0x14C &&
                (BitConverter.ToUInt16(nt, 22) & 0x2002) == 2 && BitConverter.ToUInt16(nt, 24) == 0x10B &&
                size > 0x21C884 && address + size <= 0x100000000L;
        }
        catch (Win32Exception) { return false; }
    }
}
