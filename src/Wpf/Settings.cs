using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RoadRash.Workbench
{
    public sealed class Setting : ObservableObject, IDataErrorInfo
    {
        private string raw;
        private bool enabled = true;
        private bool required;
        public string Key { get; }
        public string Label { get; }
        public string Description { get; }
        public string Kind { get; }
        public string[] Choices { get; }
        public decimal Min { get; }
        public decimal Max { get; }
        public bool Integer { get; }
        public string Dependency { get; }
        public string Raw
        {
            get => raw;
            set
            {
                if (!SetProperty(ref raw, value ?? "")) return;
                OnPropertyChanged(nameof(BoolValue)); OnPropertyChanged(nameof(Index)); OnPropertyChanged(nameof(Error));
            }
        }
        public bool BoolValue { get => required || raw == "1"; set { if (!required) Raw = value ? "1" : "0"; } }
        internal void Require(bool value) { if (required != value) { required = value; OnPropertyChanged(nameof(BoolValue)); } }
        public int Index { get => int.TryParse(raw, out int i) ? i : -1; set { if (value >= 0) Raw = value.ToString(CultureInfo.InvariantCulture); } }
        public bool Enabled { get => enabled; internal set => SetProperty(ref enabled, value); }
        public decimal Number => decimal.Parse(raw, CultureInfo.InvariantCulture);
        public string Error
        {
            get
            {
                if (Kind == "number")
                {
                    if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal n)) return "请输入有效数字，使用小数点。";
                    if (n < Min || n > Max) return Max == decimal.MaxValue ? "不能小于 " + Min : "请输入 " + Min + " 至 " + Max + " 之间的数值。";
                    if (Integer && n != decimal.Truncate(n)) return "请输入整数。";
                    if ((Key == "knockdownCreditSeconds" || Key == "streakSeconds") && decimal.Round(n, 3) != n) return "时间最多保留三位小数。";
                    if (Key == "trackLengthKm" && decimal.Round(n, 4) != n) return "赛道长度最多保留四位小数。";
                    if ((Key == "playerWalkSpeed" || Key == "aiWalkSpeed") && n * 10 != decimal.Truncate(n * 10)) return "跑步速度最多保留一位小数。";
                }
                if (Kind == "choice" && (Index < 0 || Index >= Choices.Length)) return "请选择有效选项。";
                if (Kind == "bool" && raw != "0" && raw != "1") return "开关值无效。";
                if (raw.IndexOfAny(new[] { '\r', '\n' }) >= 0) return "请使用单行文本。";
                if (Key == "playerName" && raw.Length > 19) return "玩家名称最多 19 个字符。";
                return "";
            }
        }
        public string this[string columnName] => columnName == nameof(Raw) ? Error : "";
        internal Setting(string key, string label, string description, string kind, string[] choices = null, decimal min = 0, decimal max = decimal.MaxValue, bool integer = false, string dependency = null)
        { Key = key; Label = label; Description = description; Kind = kind; Choices = choices; Min = min; Max = max; Integer = integer; Dependency = dependency; raw = ""; }
    }

    public sealed class SettingGroup
    {
        public string Title { get; }
        public string Caption { get; }
        public bool Advanced { get; }
        public List<Setting> Fields { get; } = new List<Setting>();
        public SettingGroup(string title, string caption = "", bool advanced = false) { Title = title; Caption = caption; Advanced = advanced; }
    }
    public sealed class SettingsPage
    {
        public string Title { get; }
        public string Caption { get; }
        public string Icon { get; }
        public string Number { get; }
        public List<SettingGroup> Groups { get; } = new List<SettingGroup>();
        public SettingsPage(string number, string title, string caption, string icon) { Number = number; Title = title; Caption = caption; Icon = icon; }
    }

    public sealed class SettingsStore
    {
        private readonly Dictionary<string, Setting> fields = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool batch;
        internal LauncherStrings Strings { get; }
        public List<SettingsPage> Pages { get; } = new List<SettingsPage>();
        public event Action Changed;
        public List<string> LoadWarnings { get; } = new List<string>();
        public Setting this[string key] => fields[key];
        public IEnumerable<Setting> Fields => fields.Values;
        public bool Valid => Fields.All(f => f.Error.Length == 0);
        public string FirstError => Fields.Where(f => f.Error.Length != 0).Select(f => f.Label + "：" + f.Error).FirstOrDefault() ?? "";
        public bool B(string key) => this[key].BoolValue;
        public int I(string key) => (int)this[key].Number;
        public decimal D(string key) => this[key].Number;
        public string S(string key) => this[key].Raw;
        public void Put(string key, object value) => this[key].Raw = value is bool b ? (b ? "1" : "0") : Convert.ToString(value, CultureInfo.InvariantCulture);
        public SettingsStore() : this(Path.Combine(AppContext.BaseDirectory, "Tools", "Locales", "zh-CN.json")) { }
        internal SettingsStore(string languageFile)
        {
            Strings = new LauncherStrings(languageFile);
            Schema.Build(this, Strings);
            foreach (Setting f in Fields) f.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(Setting.Raw) && !batch) Notify(); };
            Restore(Defaults());
        }
        internal void Add(SettingGroup group, Setting field) { fields.Add(field.Key, field); group?.Fields.Add(field); }
        public static Dictionary<string, string> Parse(IEnumerable<string> lines)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines) { int eq = line.IndexOf('='); if (eq > 0 && !line.TrimStart().StartsWith("#")) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim(); }
            return d;
        }
        public static Dictionary<string, string> Defaults()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FactoryDefaults.ini");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Parse(reader.ReadToEnd().Split('\n'));
        }
        public Dictionary<string, string> Snapshot()
        {
            var result = new Dictionary<string, string>(extra, StringComparer.OrdinalIgnoreCase);
            foreach (Setting f in Fields) result[f.Key] = f.Raw;
            result["settingsVersion"] = "11"; result["overlayMigration"] = "1";
            return result;
        }
        public void Restore(IReadOnlyDictionary<string, string> values)
        {
            batch = true;
            try { foreach (Setting f in Fields) if (values.TryGetValue(f.Key, out string value)) f.Raw = value; }
            finally { batch = false; Notify(); }
        }
        public void Update(Action action) { batch = true; try { action(); } finally { batch = false; Notify(); } }
        private void Notify()
        {
            batch = true;
            try
            {
                bool officer = B("policePlayer");
                this["hardcore"].Require(officer);
                if (officer)
                    foreach (string key in new[] { "longRun", "carShell", "overlayCar", "carDebugCopies" }) Put(key, false);
                if (B("longRun") && B("policeOpponents")) Put("policeOpponents", false);
            }
            finally { batch = false; }
            foreach (Setting f in Fields)
            {
                f.Enabled = f.Dependency == null || f.Dependency.Split('&').All(k => k.StartsWith("!") ? !B(k.Substring(1)) : B(k));
                if (f.Key.StartsWith("aiRun") && f.Key != "aiRunVariation") f.Enabled &= S("longRunScope") != "1";
                if (f.Key == "aiRunVariation") f.Enabled &= S("longRunScope") != "1";
            }
            Changed?.Invoke();
        }
        public void Load(string path, string gameDir)
        {
            if (!File.Exists(path)) return;
            var values = Parse(File.ReadAllLines(path, Encoding.UTF8));
            TrackLengthUnits.MigrateLegacySettings(values);
            bool run = values.TryGetValue("longRun", out string r) && r == "1";
            if (!values.ContainsKey("hideFallenVehicles")) values["hideFallenVehicles"] = run ? "1" : "0";
            if (!values.ContainsKey("aiRunVariation")) values["aiRunVariation"] = run ? "1" : "0";
            if (!values.ContainsKey("rockSolid") && values.TryGetValue("carShell", out string shell)) values["rockSolid"] = shell;
            if (!values.ContainsKey("exaggeratedLaunch")) values["exaggeratedLaunch"] = values.GetValueOrDefault("carShell") == "1" && values.GetValueOrDefault("invincible") == "1" && values.GetValueOrDefault("ramKnockdown") == "1" ? "1" : "0";
            if (values.GetValueOrDefault("overlayMigration") != "1" && values.GetValueOrDefault("overlayCar") != "1" && values.GetValueOrDefault("carShell") == "1")
            {
                string overlay = values.GetValueOrDefault("overlayImage", "Tools\\player_car.png");
                try { if (File.Exists(Path.GetFullPath(overlay, gameDir))) values["overlayCar"] = "1"; } catch (ArgumentException) { }
            }
            var defaults = Defaults();
            batch = true;
            try
            {
                foreach (var pair in values)
                {
                    if (!fields.TryGetValue(pair.Key, out Setting f)) { extra[pair.Key] = pair.Value; continue; }
                    f.Raw = pair.Value;
                    if (f.Error.Length > 0) { LoadWarnings.Add(f.Label + "：旧值无效，已恢复默认。"); f.Raw = defaults[pair.Key]; }
                }
            }
            finally { batch = false; Notify(); }
        }
        public void Save(string path)
        {
            if (!Valid) throw new InvalidOperationException(FirstError);
            string text = string.Join(Environment.NewLine, Snapshot().Select(p => p.Key + "=" + p.Value)) + Environment.NewLine;
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        public void ApplyMode(string mode)
        {
            Update(() =>
            {
                bool car = mode == "car" || mode == "truck", truck = mode == "truck", run = mode == "run";
                Put("policePlayer", mode == "police");
                foreach (string k in new[] { "overlayCar", "carDebugCopies", "carCollision" }) Put(k, false);
                Put("infiniteDurability", true); Put("carShell", car); Put("carStability", car); Put("autoCarCollision", car);
                Put("invincible", truck); Put("ramKnockdown", truck); Put("rockSolid", car); Put("exaggeratedLaunch", truck);
                Put("longRun", run); Put("remountPolicy", run ? 3 : 0); Put("hideFallenVehicles", run); Put("aiRunVariation", run);
                if (run) { Put("longRunScope", 2); Put("chaseDistance", 96); foreach (string k in new[] { "aiRunRhythm", "aiRunSprint", "aiRunChase", "aiRunWeave" }) Put(k, 2); }
                if (truck) { Put("launchScope", 0); Put("launchMultiplier", 1.5m); }
            });
        }
    }
}
