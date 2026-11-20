using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RoadRash.Workbench
{
    internal static class GameService
    {
        internal static readonly Dictionary<string, string> OptionKeys = new Dictionary<string, string>
        {
            ["Chinese"]="chinese", ["HD"]="hd", ["SkipCutscenes"]="skipCutscenes", ["NoStun"]="noStun", ["RearEndProtection"]="rearEnd",
            ["Combat"]="combat", ["OpponentAggression"]="aggression", ["Racers"]="racers", ["FrameRate"]="frameRate",
            ["PoliceMultiplier"]="police", ["TrafficMultiplier"]="traffic", ["ForcePlayerName"]="forceName", ["PlayerName"]="playerName",
            ["ForcePlayerBike"]="forceBike", ["ForcePlayerWeapon"]="forceWeapon", ["PlayerWeaponId"]="playerWeapon"
        };
        internal static XElement Options(SettingsStore settings)
        {
            if (!settings.Valid) throw new InvalidOperationException(settings.FirstError);
            var request = new XElement("options");
            foreach (var pair in OptionKeys)
            {
                Setting f = settings[pair.Value];
                string value = f.Kind == "bool" ? f.BoolValue.ToString() : f.Raw.Trim();
                if (pair.Key == "PlayerName" && value.Length == 0) value = "玩家";
                request.Add(new XElement("option", new XAttribute("name", pair.Key), value));
            }
            request.Add(new XElement("option", new XAttribute("name", "RoadWidth"), Schema.RoadWidths[settings.I("roadWidth")]));
            request.Add(new XElement("option", new XAttribute("name", "PlayerBikeId"), Schema.BikeIds[settings.I("playerBike")]));
            return request;
        }
        internal static void PopulateApplied(SettingsStore settings, XElement response)
        {
            settings.Update(() =>
            {
                foreach (XElement item in response.Elements("option"))
                {
                    string name = (string)item.Attribute("name"), value = item.Value;
                    if (OptionKeys.TryGetValue(name, out string key))
                    {
                        if (settings[key].Kind == "bool") value = bool.Parse(value) ? "1" : "0";
                        if (key == "racers" && value == "0") value = "15";
                        if (key == "frameRate" && value == "0") value = "60";
                        settings.Put(key, value);
                    }
                    else if (name == "PlayerBikeId") settings.Put("playerBike", Math.Max(0, Array.IndexOf(Schema.BikeIds, int.Parse(value, CultureInfo.InvariantCulture))));
                    else if (name == "RoadWidth") settings.Put("roadWidth", Math.Max(0, Array.IndexOf(Schema.RoadWidths, value)));
                }
            });
        }
        internal static async Task<XElement> Bridge(string operation, string gameDir, XElement options, Action<string> log)
        {
            string exe = Path.Combine(AppContext.BaseDirectory, "Tools", "RoadRash.PatchBridge.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("缺少补丁兼容程序，请完整解压修改器包。", exe);
            if (!File.Exists(Path.Combine(gameDir, "RoadRash95.exe"))) throw new FileNotFoundException("所选目录中没有 RoadRash95.exe，请重新选择游戏目录。");
            string temp = Path.Combine(Path.GetTempPath(), "RoadRashLauncher", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string request = Path.Combine(temp, "request.xml"), reply = Path.Combine(temp, "reply.xml");
                (options ?? new XElement("options")).Save(request);
                var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = gameDir };
                foreach (string arg in new[] { operation, gameDir, request, reply }) start.ArgumentList.Add(arg);
                using Process process = Process.Start(start);
                await process.WaitForExitAsync();
                if (!File.Exists(reply)) throw new InvalidOperationException("补丁兼容程序未返回结果（退出码 " + process.ExitCode + "）。");
                XElement result = XElement.Load(reply);
                foreach (XElement line in result.Elements("log")) log(line.Value);
                if (process.ExitCode != 0 || (bool?)result.Attribute("ok") != true) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Elements("error").Select(e => e.Value)));
                return result;
            }
            finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
        }
        internal static async Task Apply(SettingsStore settings, string gameDir, Action<string> log)
        {
            if (Launcher.GameRunning()) throw new InvalidOperationException("请先退出游戏，再应用设置。");
            // Build the immutable request before crossing the async process boundary.
            XElement request = Options(settings);
            await Bridge("apply", gameDir, request, log);
            settings.Save(Path.Combine(gameDir, "launcher_settings.ini"));
        }
        internal static async Task Start(SettingsStore s, string gameDir, Action<string> log)
        {
            if (Launcher.GameRunning()) throw new InvalidOperationException("游戏已经在运行。");
            if (!s.Valid) throw new InvalidOperationException(s.FirstError);
            bool debug = s.B("carDebugCopies"), overlay = s.B("overlayCar") && !debug;
            string overlayPath = overlay ? Path.GetFullPath(s.S("overlayImage"), gameDir) : "";
            if (overlay && !File.Exists(overlayPath)) throw new FileNotFoundException("找不到叠加图片，请修改路径或关闭图片叠加。", overlayPath);
            bool shell = s.B("carShell") && !overlay && !debug, collision = s.B("carCollision") && !shell;
            Launcher.GameDir = gameDir;
            Process process = Process.Start(new ProcessStartInfo(Path.Combine(gameDir, "RoadRash95.exe")) { WorkingDirectory = gameDir, UseShellExecute = false });
            var patchLog = new StringBuilder();
            bool patchFailed = false;
            void Report(string text) { patchLog.AppendLine(text); if (text.Contains("失败")) patchFailed = true; log(text); }
            try
            {
                await Task.Run(() => RuntimePatch.Apply(process, s.B("oneHit"), s.I("longRange"), s.I("lateralAll"), s.I("lateralPlayer"),
                    s.B("noDirection"), s.B("aoe"), s.I("aoeScope") == 1, s.I("aoeTargets"), s.B("downed"), s.I("downScope") == 1,
                    s.B("ramKnockdown"), s.B("invincible"), shell, collision, overlay, debug, s.B("forceMap"), s.I("mapLevel"), s.I("mapCourse") - 1,
                    Report, s.I("carStyle"), s.B("carStability"), s.B("autoCarCollision"), s.B("independentAudio"), s.B("rockSolid"),
                    s.B("exaggeratedLaunch"), s.I("launchScope") == 1, s.D("launchMultiplier"), s.B("infiniteDurability"),
                    s.D("playerWalkSpeed"), s.D("aiWalkSpeed"), s.B("onFootProtection"), s.I("onFootScope") == 1,
                    s.B("longRun"), s.I("longRunScope"), s.I("remountPolicy"), s.I("chaseDistance"), s.B("hideFallenVehicles"),
                    s.B("aiRunVariation"), s.I("aiRunRhythm"), s.I("aiRunSprint"), s.I("aiRunChase"), s.I("aiRunWeave"),
                    s.D("sameTrafficSpeed"), s.D("oppositeTrafficSpeed"), s.B("crazyDrivers"), s.B("fallAnimationFix"),
                    s.B("sameTraffic"), s.B("oppositeTraffic"), s.B("nearbyImpactAudio"), s.B("forceTrackLength"), TrackLengthUnits.ToBlocks(s.D("trackLengthKm")),
                    OpponentIdentity(s), s.B("opponentsTargetPlayer"), s.B("knockdownHud"), s.B("knockdownVoice"),
                    (int)(1000 * s.D("knockdownCreditSeconds")), (int)(1000 * s.D("streakSeconds")), s.I("knockdownsPerVoice"),
                    s.B("hardcore"), s.B("policePlayer"), s.I("rankMode")));
                if (patchFailed) throw new InvalidOperationException("部分运行时补丁未能完成，请查看具体日志。");
                Report("运行时补丁已写入。");
                if (overlay) SpawnHelper(gameDir, "--overlay", process.Id.ToString(CultureInfo.InvariantCulture), overlayPath);
                var watch = new List<string> { "--watch", process.Id.ToString(CultureInfo.InvariantCulture) };
                if (s.B("independentAudio")) watch.Add("--audio-diagnostics");
                if (s.I("rankMode") != 0) watch.Add("--rank-stats");
                SpawnHelper(gameDir, watch.ToArray());
            }
            catch (Exception ex) { Report("游戏已启动，但补丁未全部完成：" + ex.Message); throw new InvalidOperationException("游戏已启动，但补丁未全部完成。请退出游戏后查看日志再重试。\n" + ex.Message, ex); }
            finally
            {
                try { File.WriteAllText(Path.Combine(gameDir, "runtime_patch.log"), patchLog.ToString(), Encoding.UTF8); }
                catch (IOException ex) { log("无法保存运行日志：" + ex.Message); }
                catch (UnauthorizedAccessException ex) { log("无法保存运行日志：" + ex.Message); }
                process.Dispose();
            }
        }
        internal static int OpponentIdentity(SettingsStore settings) =>
            settings.B("policeOpponents") ? 15 : settings.B("forceOpponent") ? settings.I("opponentRider") + 1 : 0;

        private static void SpawnHelper(string dir, params string[] args)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath) { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using Process p = Process.Start(start);
        }
    }
}
