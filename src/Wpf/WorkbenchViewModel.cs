using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RoadRash.Workbench
{
    public sealed class WorkbenchViewModel : ObservableObject
    {
        public SettingsStore Settings { get; } = new SettingsStore();
        public List<SettingsPage> Pages => Settings.Pages;
        private SettingsPage page;
        private string gameDirectory, status = "读取配置中…", logText = "";
        private bool busy, dirty, logVisible;
        private Dictionary<string, string> undo;
        public SettingsPage Page { get => page; set { if (SetProperty(ref page, value)) { OnPropertyChanged(nameof(IsRace)); OnPropertyChanged(nameof(IsTools)); OnPropertyChanged(nameof(IsTraffic)); OnPropertyChanged(nameof(IsDisplay)); } } }
        public bool IsRace => Page == Pages[0];
        public bool IsTools => Page == Pages[7];
        public bool IsTraffic => Page == Pages[4];
        public bool IsDisplay => Page == Pages[5];
        public string GameDirectory { get => gameDirectory; private set => SetProperty(ref gameDirectory, value); }
        public string Status { get => status; private set => SetProperty(ref status, value); }
        public string LogText { get => logText; private set => SetProperty(ref logText, value); }
        public bool Busy { get => busy; private set { if (SetProperty(ref busy, value)) { OnPropertyChanged(nameof(Editable)); RefreshCommands(); } } }
        public bool Editable => !Busy;
        public bool Dirty { get => dirty; private set => SetProperty(ref dirty, value); }
        public bool LogVisible { get => logVisible; set => SetProperty(ref logVisible, value); }
        public bool CanUndo => undo != null && !Busy;
        public string Mode => Settings.B("policePlayer") ? Settings.Strings.Get("ui.presets.police") : Settings.B("longRun") ? "长跑" : Settings.B("carShell") ? (Settings.B("ramKnockdown") && Settings.B("invincible") ? "泥头车" : "车辆") : "骑行";
        public string ModePreset
        {
            get
            {
                var s = Settings;
                if (s.B("policePlayer")) return "police";
                if (!s.B("infiniteDurability") || s.B("overlayCar") || s.B("carDebugCopies") || s.B("carCollision")) return "custom";
                bool normal = !s.B("carShell") && !s.B("invincible") && !s.B("ramKnockdown") && !s.B("rockSolid") && !s.B("exaggeratedLaunch");
                if (s.B("longRun")) return normal && s.B("hideFallenVehicles") && s.B("aiRunVariation") && s.S("remountPolicy") == "3" && s.S("longRunScope") == "2" && s.S("chaseDistance") == "96" && new[] { "aiRunRhythm", "aiRunSprint", "aiRunChase", "aiRunWeave" }.All(k => s.S(k) == "2") ? "run" : "custom";
                if (s.B("hideFallenVehicles") || s.S("remountPolicy") != "0") return "custom";
                if (normal) return "normal";
                if (s.B("carShell") && s.B("carStability") && s.B("autoCarCollision") && s.B("rockSolid"))
                {
                    if (!s.B("invincible") && !s.B("ramKnockdown") && !s.B("exaggeratedLaunch")) return "car";
                    if (s.B("invincible") && s.B("ramKnockdown") && s.B("exaggeratedLaunch") && s.S("launchScope") == "0" && s.S("launchMultiplier") == "1.5") return "truck";
                }
                return "custom";
            }
        }
        public bool PublicEnemyPreset
        {
            get => Settings.B("policeOpponents") && Settings.B("opponentsTargetPlayer");
            set
            {
                if (!Settings["policeOpponents"].Enabled) return;
                Settings.Update(() =>
                {
                    Settings.Put("policeOpponents", value);
                    Settings.Put("opponentsTargetPlayer", value);
                });
            }
        }
        public string PresetNote => (ModePreset == "custom" ? "当前玩法已自定义" : "当前玩法：" + Mode)
            + (PublicEnemyPreset ? " · " + Settings.Strings.Get("ui.presets.publicEnemy") : "");
        public string SummaryCount => Settings.S("racers");
        public string SummaryDisplay => (Settings.B("hd") ? "高清宽屏" : "经典画面") + "  ·  " + Settings.S("frameRate") + " FPS";
        public string SummaryTraffic => (Settings.B("sameTraffic") || Settings.B("oppositeTraffic") ? "汽车 ×" + Settings.S("traffic") + "（" + (Settings.B("sameTraffic") ? (Settings.B("oppositeTraffic") ? "双向" : "同向") : "对向") + "）" : "无普通交通车") + "  /  警车 ×" + Settings.S("police");
        public string SummarySpeed => "玩家 ×" + Settings.S("playerWalkSpeed") + "  /  AI ×" + Settings.S("aiWalkSpeed");
        public string SummaryFix
        {
            get
            {
                int count = new[] { "independentAudio", "fallAnimationFix", "nearbyImpactAudio" }.Count(Settings.B);
                if (Settings.I("rankMode") >= 2) count++;
                return "已启用 " + count + " 项修复";
            }
        }
        public string SummaryFeatures => string.Join("\n", new[] { "hideFallenVehicles", "aiRunVariation", "crazyDrivers", "infiniteDurability", "onFootProtection", "forceOpponent", "policeOpponents", "opponentsTargetPlayer" }
            .Where(k => Settings.B(k) && Settings[k].Enabled)
            .Select(k => "•  " + Settings[k].Label + (k == "forceOpponent" ? " · " + Choice("opponentRider") : "")));
        public string ModeNote => Settings.B("policePlayer") ? Settings.Strings.Get("ui.presets.policeDescription") : (Settings.B("longRun") ? "长跑范围：" + Choice("longRunScope") + "\n恢复上车：" + Choice("remountPolicy") : "车辆外观：" + (Settings.B("carShell") ? Choice("carStyle") : "摩托车")) + (Settings.B("hardcore") ? "\n硬核模式 · 落地 3 秒后出局" : "");
        private string Choice(string key) => Settings[key].Index >= 0 && Settings[key].Index < Settings[key].Choices.Length ? Settings[key].Choices[Settings[key].Index] : "无效选项";
        public RelayCommand ResetCommand { get; }
        public RelayCommand UndoCommand { get; }
        public RelayCommand<string> ModeCommand { get; }
        public RelayCommand<string> ScaleCommand { get; }
        public RelayCommand<string> TrafficCommand { get; }
        public RelayCommand<string> DisplayCommand { get; }
        public RelayCommand ToggleLogCommand { get; }
        public AsyncRelayCommand ApplyCommand { get; }
        public AsyncRelayCommand LaunchCommand { get; }
        public AsyncRelayCommand StartOnlyCommand { get; }
        public event Action LaunchCompleted;
        public WorkbenchViewModel(string directory, bool load = true)
        {
            GameDirectory = directory; page = Pages[0];
            if (load) Settings.Load(Path.Combine(directory, "launcher_settings.ini"), directory);
            ResetCommand = new RelayCommand(Reset, () => !Busy);
            UndoCommand = new RelayCommand(Undo, () => CanUndo);
            ModeCommand = new RelayCommand<string>(mode => Settings.ApplyMode(mode), _ => !Busy);
            ScaleCommand = new RelayCommand<string>(value => Settings.Put("racers", value), _ => !Busy);
            TrafficCommand = new RelayCommand<string>(value => Settings.Update(() => { var parts = value.Split(','); Settings.Put("traffic", parts[0]); Settings.Put("police", parts[1]); }), _ => !Busy);
            DisplayCommand = new RelayCommand<string>(value => Settings.Update(() => { Settings.Put("hd", value != "classic"); Settings.Put("frameRate", value == "smooth" ? 120 : 60); }), _ => !Busy);
            ToggleLogCommand = new RelayCommand(() => LogVisible = !LogVisible);
            ApplyCommand = new AsyncRelayCommand(() => Apply(false), CanApply);
            LaunchCommand = new AsyncRelayCommand(() => Apply(true), CanApply);
            StartOnlyCommand = new AsyncRelayCommand(StartOnly, CanApply);
            Settings.Changed += OnSettingsChanged;
            Status = "设置已读取 · 应用后生效";
            foreach (string warning in Settings.LoadWarnings) Log(warning);
        }
        private bool CanApply() => !Busy && Settings.Valid;
        private void RefreshCommands()
        {
            ApplyCommand.NotifyCanExecuteChanged(); LaunchCommand.NotifyCanExecuteChanged(); StartOnlyCommand.NotifyCanExecuteChanged(); ResetCommand.NotifyCanExecuteChanged(); UndoCommand.NotifyCanExecuteChanged();
            ModeCommand.NotifyCanExecuteChanged(); ScaleCommand.NotifyCanExecuteChanged(); TrafficCommand.NotifyCanExecuteChanged(); DisplayCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanUndo));
        }
        private void OnSettingsChanged()
        {
            Dirty = true;
            Status = Settings.Valid ? "设置已修改 · 应用后生效" : Settings.FirstError;
            foreach (string name in new[] { nameof(Mode), nameof(ModePreset), nameof(PublicEnemyPreset), nameof(PresetNote), nameof(SummaryCount), nameof(SummaryDisplay), nameof(SummaryTraffic), nameof(SummarySpeed), nameof(SummaryFix), nameof(SummaryFeatures), nameof(ModeNote) }) OnPropertyChanged(name);
            RefreshCommands();
        }
        public void Reset() { undo = Settings.Snapshot(); Settings.Restore(SettingsStore.Defaults()); Status = "已恢复默认设置 · 应用后生效，可撤销"; RefreshCommands(); }
        public void Undo() { if (undo == null) return; var snapshot = undo; undo = null; Settings.Restore(snapshot); Status = "已撤销恢复默认设置"; RefreshCommands(); }
        public void Log(string text)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.BeginInvoke(() => Log(text)); return; }
            LogText += DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine;
        }
        public async Task Inspect()
        {
            Busy = true;
            try
            {
                var reply = await GameService.Bridge("inspect", GameDirectory, null, Log);
                if (!File.Exists(Path.Combine(GameDirectory, "launcher_settings.ini")) && !Dirty) { GameService.PopulateApplied(Settings, reply); Dirty = false; }
                if ((bool?)reply.Attribute("recovery") == true) Status = "检测到未完成的补丁操作 · 点击应用恢复";
                else if (!Dirty) Status = "游戏已就绪 · 设置已读取";
                Log("游戏目录检查完成。");
            }
            catch (Exception ex) { Status = "目录或补丁需要检查 · 详见日志"; Log(ex.Message); LogVisible = true; }
            finally { Busy = false; }
        }
        public async Task ChangeDirectory(string directory)
        {
            if (Busy || string.Equals(directory, GameDirectory, StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(Path.Combine(directory, "RoadRash95.exe"))) throw new FileNotFoundException("所选目录中没有 RoadRash95.exe。");
            SaveDraft(); GameDirectory = directory; undo = null;
            Settings.Restore(SettingsStore.Defaults()); Settings.Load(Path.Combine(directory, "launcher_settings.ini"), directory); Dirty = false;
            await Inspect();
        }
        public void SaveDraft() { if (Dirty && Settings.Valid && Directory.Exists(GameDirectory)) Settings.Save(Path.Combine(GameDirectory, "launcher_settings.ini")); }
        private async Task Apply(bool launch)
        {
            if (!CanApply()) return;
            Busy = true; Status = "正在应用设置…";
            try
            {
                await GameService.Apply(Settings, GameDirectory, Log);
                Dirty = false; undo = null; Status = "设置已应用"; Log("设置已应用并保存。");
                if (launch) { Status = "正在启动游戏并写入补丁…"; await GameService.Start(Settings, GameDirectory, Log); LaunchCompleted?.Invoke(); }
            }
            catch (Exception ex) { Status = "操作未完成 · 详见日志"; Log(ex.Message); LogVisible = true; }
            finally { Busy = false; }
        }
        public async Task RestoreOriginal()
        {
            if (Busy) return;
            Busy = true;
            try { await GameService.Bridge("restore", GameDirectory, null, Log); Status = "已还原原版游戏文件 · 设置草稿保留"; Log(Status); }
            catch (Exception ex) { Status = "还原失败 · 详见日志"; Log(ex.Message); LogVisible = true; }
            finally { Busy = false; }
        }
        private async Task StartOnly()
        {
            if (!CanApply()) return;
            Busy = true; Status = "正在启动游戏…";
            try { Settings.Save(Path.Combine(GameDirectory, "launcher_settings.ini")); Dirty = false; await GameService.Start(Settings, GameDirectory, Log); LaunchCompleted?.Invoke(); }
            catch (Exception ex) { Status = "启动未完成 · 详见日志"; Log(ex.Message); LogVisible = true; }
            finally { Busy = false; }
        }
    }
}
