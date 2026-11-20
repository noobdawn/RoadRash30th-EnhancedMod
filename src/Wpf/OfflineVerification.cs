using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RoadRash.Workbench
{
    // Explicit offline entry points. Never call Apply, Start, or save a user's INI.
    internal static class OfflineVerification
    {
        private static int count;
        private static readonly List<string> lines = new List<string>();
        private static void Check(bool condition, string description) { if (!condition) throw new Exception(description); count++; lines.Add("PASS " + description); }
        internal static int Run(string mode, string output)
        {
            Directory.CreateDirectory(output);
            try
            {
                var vm = new WorkbenchViewModel(output, false); var s = vm.Settings;
                Check(s.Valid, "factory configuration is valid");
                CheckRankSettings(output);
                CheckOpponentSettings(output);
                CheckRaceRules(output);
                Check(s.Fields.Count() == SettingsStore.Defaults().Count - 2, "every factory key has one editable field");
                s.Put("playerWalkSpeed", 32.5m); s.Put("sameTrafficSpeed", 50000m); s.Put("independentAudio", false); s.ApplyMode("run");
                Check(s.B("longRun") && s.B("hideFallenVehicles") && s.B("aiRunVariation") && s.I("remountPolicy") == 3, "marathon preset configures running rules");
                Check(s.D("playerWalkSpeed") == 32.5m && s.D("sameTrafficSpeed") == 50000m && !s.B("independentAudio"), "preset preserves speed and Fix preferences");
                var before = s.Snapshot(); vm.Reset();
                Check(SettingsStore.Defaults().All(p => s.Snapshot()[p.Key] == p.Value), "reset matches every factory value");
                Check(vm.GameDirectory == output && vm.Dirty && vm.CanUndo, "reset preserves directory and remains an undoable draft");
                vm.Undo(); Check(before.All(p => s.Snapshot()[p.Key] == p.Value), "undo restores complete configuration");
                string testIni = Path.Combine(output, "roundtrip.ini"); s.Save(testIni);
                var restored = new SettingsStore(); restored.Load(testIni, output);
                Check(before.All(p => restored.Snapshot()[p.Key] == p.Value), "INI round trip preserves all settings");
                File.WriteAllText(testIni, "settingsVersion=7\nlongRun=1\nindependentAudio=0\nroadWidth=2\ncarShell=1\n");
                restored = new SettingsStore(); restored.Load(testIni, output);
                Check(restored.B("fallAnimationFix") && !restored.B("independentAudio") && restored.B("hideFallenVehicles") && restored.B("aiRunVariation") && restored.B("rockSolid"), "legacy migration preserves explicit Fix choice and supplies missing defaults");
                Check(restored.I("roadWidth") == 2, "legacy road width remains a selected index");
                Check(restored.B("sameTraffic") && restored.B("oppositeTraffic") && !restored.B("forceTrackLength") && !restored.B("nearbyImpactAudio"), "old configurations preserve original traffic, race length and sounds");
                Check(restored.D("trackLengthKm") == 8.28m, "old configuration without length uses kilometer default");
                foreach (int blocks in new[] { 100, 1000, 1800, 1801, 5000 })
                {
                    File.WriteAllText(testIni, "settingsVersion=10\nforceTrackLength=1\ntrackLength=" + blocks + "\nracers=500\nindependentAudio=0\n");
                    restored = new SettingsStore(); restored.Load(testIni, output);
                    Check(restored.B("forceTrackLength") && restored.D("trackLengthKm") == TrackLengthUnits.ToKilometers(blocks) && restored.I("racers") == 500 && !restored.B("independentAudio") && restored.LoadWarnings.Count == 0, "legacy blocks migrate without changing race or other preferences " + blocks);
                    restored.Save(testIni); var migrated = new SettingsStore(); migrated.Load(testIni, output);
                    Check(TrackLengthUnits.ToBlocks(migrated.D("trackLengthKm")) == blocks && !migrated.Snapshot().ContainsKey("trackLength") && migrated.Snapshot()["settingsVersion"] == "11", "migrated kilometer save reload preserves native finish " + blocks);
                }
                File.WriteAllText(testIni, "settingsVersion=10\ntrackLength=1000\ntrackLengthKm=10.5\n");
                restored = new SettingsStore(); restored.Load(testIni, output);
                Check(restored.D("trackLengthKm") == 10.5m, "explicit kilometer value wins over stale legacy key");
                foreach (string invalid in new[] { "99", "5001", "1800.5", "bad" })
                {
                    File.WriteAllText(testIni, "trackLength=" + invalid + "\n");
                    restored = new SettingsStore(); restored.Load(testIni, output);
                    Check(restored.D("trackLengthKm") == 8.28m && restored.LoadWarnings.Count == 1, "invalid legacy length resets with a warning " + invalid);
                }
                foreach (int mask in new[] { 0, 1, 2, 3 })
                {
                    restored.Put("sameTraffic", (mask & 1) != 0); restored.Put("oppositeTraffic", (mask & 2) != 0);
                    restored.Put("forceTrackLength", true); restored.Put("trackLengthKm", 23m); restored.Put("nearbyImpactAudio", true);
                    foreach (string preset in new[] { "normal", "car", "truck", "run" })
                    {
                        restored.ApplyMode(preset);
                        Check(restored.B("sameTraffic") == ((mask & 1) != 0) && restored.B("oppositeTraffic") == ((mask & 2) != 0) && restored.B("nearbyImpactAudio") && restored.D("trackLengthKm") == 23m && restored.B("forceTrackLength"), "preset preserves independent race features " + preset + " directions=" + mask);
                    }
                    restored.Save(testIni); var reread = new SettingsStore(); reread.Load(testIni, output);
                    Check(restored.Snapshot().All(p => reread.Snapshot()[p.Key] == p.Value), "new feature choices survive reload directions=" + mask);
                }
                restored.Put("forceTrackLength", false); Check(!restored["trackLengthKm"].Enabled, "length editor follows override switch");
                restored.Put("forceTrackLength", true); Check(restored["trackLengthKm"].Enabled, "length editor enables independently of map forcing");
                foreach (string value in new[] { "0.4599", "23.0001", "0", "8.28001", "bad" }) { restored.Put("trackLengthKm", value); Check(!restored.Valid, "unsupported kilometer length is rejected " + value); }
                foreach (decimal value in new[] { 0.46m, 4.6m, 8.2846m, 10.5m, 23m }) { restored.Put("trackLengthKm", value); Check(restored.Valid, "fractional kilometer length is accepted " + value); }
                restored.Put("trackLengthKm", 8.28m);
                restored.Put("roadWidth", 2);
                var request = GameService.Options(restored);
                Check(request.Elements("option").Single(e => (string)e.Attribute("name") == "RoadWidth").Value == "2", "patch bridge converts road width index to multiplier");
                Check(request.Elements("option").Single(e => (string)e.Attribute("name") == "PlayerBikeId").Value == "4", "patch bridge maps bike choice to native ID");
                Check(request.Elements("option").Count() == 18, "all enhancement options are mapped");
                GameService.Options(new SettingsStore()).Save(Path.Combine(output, "factory-options.xml"));
                foreach (string invalid in new[] { "", "oops", "-1", "3.5", "10001" }) { s.Put("racers", invalid); Check(!vm.ApplyCommand.CanExecute(null) && !vm.LaunchCommand.CanExecute(null), "invalid racer value blocks apply: " + invalid); }
                s.Put("racers", 500); s.Put("sameTrafficSpeed", 999999999999m); Check(s.Valid, "traffic multiplier has no gameplay upper limit");
                s.Put("playerWalkSpeed", 128m); Check(s.Valid, "running speed is not capped at eight");
                s.Put("aiWalkSpeed", "1.23"); Check(!s.Valid, "unsupported native running precision is reported"); s.Put("aiWalkSpeed", 4);
                s.Put("longRunScope", 1); Check(!s["aiRunVariation"].Enabled && !s["aiRunWeave"].Enabled, "player-only running disables AI settings");
                s.Put("longRunScope", 2); Check(s["aiRunWeave"].Enabled, "AI controls re-enable for all-rider running");
                s.ApplyMode("truck"); Check(s.B("carShell") && s.B("invincible") && s.B("ramKnockdown") && !s.B("longRun"), "truck preset removes marathon and configures collision");
                s.ApplyMode("normal"); Check(!s.B("carShell") && !s.B("ramKnockdown") && !s.B("longRun") && s.B("infiniteDurability"), "normal preset clears mode-specific flags");
                Check(vm.ModePreset == "normal", "preset selection follows detailed settings");
                s.Put("ramKnockdown", true); Check(vm.ModePreset == "custom", "manual mode edit shows custom preset");
                if (mode == "--render-ui") Render(vm, output);
                lines.Add("TOTAL " + count + " PASS"); File.WriteAllLines(Path.Combine(output, "results.txt"), lines); return 0;
            }
            catch (Exception ex) { lines.Add("FAIL " + ex); File.WriteAllLines(Path.Combine(output, "results.txt"), lines); return 1; }
        }
        private static void CheckRankSettings(string output)
        {
            var settings = new SettingsStore(null);
            string path = Path.Combine(output, "rank-options.ini");
            File.WriteAllText(path, "racers=10000\n");
            settings.Load(path, output);
            Check(settings.I("rankMode") == 0, "legacy 10000-rider profiles keep stock ranking");
            for (int mode = 1; mode <= 3; ++mode) {
                settings.Put("rankMode", mode);
                settings.ApplyMode("normal");
                settings.Save(path);
                var restored = new SettingsStore(null);
                restored.Load(path, output);
                Check(restored.Valid && restored.I("rankMode") == mode, "ranking selection survives preset, save and reload " + mode);
            }
        }
        private static void CheckRaceRules(string output)
        {
            var s = new SettingsStore(null);
            Check(!s.B("hardcore") && !s.B("policePlayer") && s.D("knockdownCreditSeconds")==2 && s.D("streakSeconds")==18 && s.I("knockdownsPerVoice")==1,"factory knockdown and rules defaults");
            s.Put("streakSeconds",3.125m); s.Put("knockdownCreditSeconds",0.75m); s.Put("knockdownsPerVoice",3);
            s.ApplyMode("police"); s["hardcore"].BoolValue=false;
            Check(s.B("hardcore") && !s["hardcore"].Enabled && GameService.OpponentIdentity(s)==0 && s["policeOpponents"].Enabled,"police locks hardcore while preserving the ordinary roster");
            string path=Path.Combine(output,"race-rules.ini"); s.Save(path); s=new SettingsStore(null); s.Load(path,output);
            Check(s.B("policePlayer") && s.B("hardcore") && s.D("streakSeconds")==3.125m && s.D("knockdownCreditSeconds")==0.75m && s.I("knockdownsPerVoice")==3,"custom rules and police survive save and reload");
            s.ApplyMode("normal"); Check(!s.B("hardcore") && s["hardcore"].Enabled,"leaving police restores independent hardcore preference");
            s.Put("hardcore",true); s.ApplyMode("police"); s.ApplyMode("car"); Check(s.B("hardcore") && s["hardcore"].Enabled,"standalone hardcore also survives mode switching");
            s.Put("knockdownVoice",false); Check(!s["knockdownsPerVoice"].Enabled && s["streakSeconds"].Enabled,"voice cadence follows its own switch");
            s.Put("knockdownHud",false); Check(!s["knockdownCreditSeconds"].Enabled && !s["streakSeconds"].Enabled && s.B("hardcore"),"race rules operate independently of the meter");
            foreach(string key in new[]{"streakSeconds","knockdownCreditSeconds","knockdownsPerVoice"}){
                string saved=s.S(key); s.Put(key,0); Check(!s.Valid,"reject zero "+key);
                s.Put(key,0.1234m); Check(!s.Valid,"reject unsupported precision "+key); s.Put(key,saved);
            }
            s.Restore(new Dictionary<string,string>{{"policePlayer","1"},{"hardcore","0"},{"longRun","1"},{"carShell","1"}});
            Check(s.B("hardcore") && !s.B("longRun") && !s.B("carShell"),"conflicting saved police settings resolve to motorcycle and forced hardcore");
        }
        private static void CheckOpponentSettings(string output)
        {
            var stock = new SettingsStore(null);
            Check(GameService.OpponentIdentity(stock) == 0 && !stock.B("opponentsTargetPlayer"), "old profiles default to stock AI");
            stock.Put("forceOpponent", true);
            for (int i = 0; i < 8; i++)
            {
                stock.Put("opponentRider", i);
                Check(GameService.OpponentIdentity(stock) == i + 1, "rider choice maps to game identity " + i);
            }
            stock.Put("policeOpponents", true); stock.Put("opponentsTargetPlayer", true);
            Check(GameService.OpponentIdentity(stock) == 15 && !stock["opponentRider"].Enabled, "police overrides the remembered rider");
            stock.ApplyMode("run");
            string ini = Path.Combine(output, "opponents.ini");
            stock.Save(ini);
            Check(SettingsStore.Parse(File.ReadAllLines(ini))["policeOpponents"] == "0", "running saves an inactive police identity");
            File.AppendAllText(ini, "policeOpponents=1" + Environment.NewLine);
            var reread = new SettingsStore(null); reread.Load(ini, output);
            Check(!reread.B("policeOpponents") && !reread["policeOpponents"].Enabled && reread.B("opponentsTargetPlayer"), "old police and marathon configuration is normalized without clearing targeting");
            reread.Put("policeOpponents", true);
            Check(!reread.B("policeOpponents"), "marathon rejects a programmatic police identity");
            Check(GameService.OpponentIdentity(reread) == 8 && reread["opponentRider"].Enabled, "previous rider returns after disabling police");
            reread.ApplyMode("normal");
            Check(reread["policeOpponents"].Enabled && !reread.B("policeOpponents"), "leaving marathon unlocks police without reactivating it");
            reread.Put("policeOpponents", true);
            reread.Put("longRun", true);
            Check(GameService.OpponentIdentity(reread) == 8 && !reread.B("policeOpponents"), "advanced long-run switch clears police without changing the selected rider");
            string locale = Path.Combine(output, "language-test.json");
            File.WriteAllText(locale, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string,string> {
                ["setting.forceTrackLength.label"]="Override finish", ["setting.opponentsTargetPlayer.description"]="Player only",
                ["setting.opponentRider.option.0"]="Mike", ["setting.roadWidth.option.2"]="Double"}));
            var translated = new SettingsStore(locale);
            Check(translated["forceTrackLength"].Label == "Override finish" && translated["opponentsTargetPlayer"].Description == "Player only" &&
                translated["opponentRider"].Choices[0] == "Mike" && translated["sameTraffic"].Label == stock["sameTraffic"].Label, "partial language file overrides labels, descriptions and choices");
            translated.Put("roadWidth", 2);
            var request = GameService.Options(translated);
            Check(request.Elements("option").Single(e => (string)e.Attribute("name") == "RoadWidth").Value == "2", "localized option text preserves native value");
            File.WriteAllText(locale, "{");
            Check(new SettingsStore(locale)["opponentRider"].Choices.Length == 8, "invalid language file falls back to built-in labels");
        }

        private static void Render(WorkbenchViewModel vm, string output)
        {
            vm.Settings.Restore(SettingsStore.Defaults()); vm.Settings.ApplyMode("normal"); vm.Settings.Put("racers", 500);
            var window = new MainWindow(vm, true);
            // Detached rendering does not create, focus, or show a desktop window.
            var surface = (FrameworkElement)window.Content; window.Content = null; surface.DataContext = vm;
            surface.Resources.MergedDictionaries.Add(window.Resources);
            surface.Measure(new Size(1280, 800)); surface.Arrange(new Rect(0, 0, 1280, 800)); surface.UpdateLayout(); Pump();
            var enemySwitch = window.PublicEnemySwitch;
            var settings = vm.Settings;
            var police = Descendants(surface).OfType<Button>().Single(b=>Equals(b.CommandParameter,"police"));
            police.Command.Execute(police.CommandParameter); Pump();
            var hard = Descendants(surface).OfType<CheckBox>().Single(b=>b.DataContext is Setting f && f.Key=="hardcore");
            Check(vm.ModePreset=="police" && hard.IsChecked==true && !hard.IsEnabled,"race preset button locks the checked hardcore switch");
            surface.UpdateLayout(); SaveBitmap(surface,1280,800,Path.Combine(output,"police-preset.png"));
            settings.ApplyMode("normal"); Pump();
            Check(hard.IsEnabled && hard.IsChecked==false,"switch returns to the saved standalone value");
            vm.Page=vm.Pages[5]; Pump(); surface.UpdateLayout();
            foreach(string key in new[]{"knockdownCreditSeconds","streakSeconds","knockdownsPerVoice"}){
                var input=Descendants(surface).OfType<TextBox>().Single(t=>t.DataContext is Setting f && f.Key==key);
                string previousValue=settings.S(key); input.Text="3"; Pump();
                Check(input.IsEnabled && settings.I(key)==3,"rendered knockdown editor changes "+key); settings.Put(key,previousValue);
            }
            vm.Page=vm.Pages[0]; Pump(); surface.UpdateLayout();
            var previous = settings.Snapshot();
            enemySwitch.SetCurrentValue(CheckBox.IsCheckedProperty, true); Pump();
            Check(GameService.OpponentIdentity(settings) == 15 && settings.B("opponentsTargetPlayer") &&
                vm.PresetNote.Contains(settings.Strings.Get("ui.presets.publicEnemy")), "public enemy switch applies both runtime rules and updates the preset note");
            var enabled = settings.Snapshot();
            Check(previous.Where(p => p.Key != "policeOpponents" && p.Key != "opponentsTargetPlayer").All(p => enabled[p.Key] == p.Value),
                "public enemy preserves the current race and movement settings");
            string presetDirectory = Path.Combine(output, "public-enemy");
            Directory.CreateDirectory(presetDirectory);
            settings.Save(Path.Combine(presetDirectory, "launcher_settings.ini"));
            Check(new WorkbenchViewModel(presetDirectory).PublicEnemyPreset, "saved rules restore the active public enemy preset");
            settings.Put("policeOpponents", false); Pump();
            Check(enemySwitch.IsChecked == false && settings.B("opponentsTargetPlayer"), "editing just one rule updates the switch and preserves the other rule");
            enemySwitch.SetCurrentValue(CheckBox.IsCheckedProperty, true); Pump();
            enemySwitch.SetCurrentValue(CheckBox.IsCheckedProperty, false); Pump();
            Check(!settings.B("policeOpponents") && !settings.B("opponentsTargetPlayer"), "turning off public enemy clears both opponent rules");
            settings.ApplyMode("run"); Pump();
            Check(!enemySwitch.IsEnabled && enemySwitch.IsChecked == false, "marathon disables the public enemy switch");
            vm.PublicEnemyPreset = true;
            Check(!vm.PublicEnemyPreset && !settings.B("opponentsTargetPlayer"), "blocked preset leaves targeting unchanged");
            settings.ApplyMode("normal"); Pump();
            Check(enemySwitch.IsEnabled, "riding unlocks the public enemy switch");
            var countInput = Descendants(surface).OfType<TextBox>().Single(t => t.DataContext is Setting f && f.Key == "racers");
            countInput.Text = "321"; Pump();
            Check(vm.SummaryCount == "321" && vm.Settings.S("racers") == "321", "rendered numeric input updates shared model and live summary");
            countInput.Text = "invalid"; Pump(); Check(!vm.ApplyCommand.CanExecute(null), "invalid rendered input disables apply command");
            vm.ResetCommand.Execute(null); Pump(); Check(countInput.Text == "15", "reset command refreshes the rendered editor");
            var lengthInput = Descendants(surface).OfType<TextBox>().Single(t => t.DataContext is Setting f && f.Key == "trackLengthKm");
            vm.Settings.Put("forceTrackLength", true); Pump(); lengthInput.Text = "4.6"; Pump();
            Check(lengthInput.IsEnabled && vm.Settings.D("trackLengthKm") == 4.6m && TrackLengthUnits.ToBlocks(vm.Settings.D("trackLengthKm")) == 1000, "rendered kilometer input retains decimal and converts measured distance");
            vm.Settings.Put("forceTrackLength", false); Pump(); Check(!lengthInput.IsEnabled, "rendered kilometer editor disables when override is off");
            vm.Settings.Put("trackLengthKm", 8.28m);
            vm.Settings.ApplyMode("run"); vm.Settings.Put("racers", 500);
            vm.Page = vm.Pages[4]; Pump(); surface.UpdateLayout();
            var widthInput = Descendants(surface).OfType<ComboBox>().Single(t => t.DataContext is Setting f && f.Key == "roadWidth");
            widthInput.SelectedIndex = 3; Pump(); Check(vm.Settings.S("roadWidth") == "3", "rendered dropdown updates persisted choice index");
            vm.Settings.Put("roadWidth", 1);
            vm.Settings.ApplyMode("normal"); vm.Page = vm.Pages[3]; Pump(); surface.UpdateLayout();
            var roster = Descendants(surface).OfType<ComboBox>().Single(t => t.DataContext is Setting f && f.Key == "opponentRider");
            vm.Settings.Put("forceOpponent", true); Pump(); roster.SelectedIndex = 7; Pump();
            Check(roster.IsEnabled && GameService.OpponentIdentity(vm.Settings) == 8, "combat page selects Axel through the real dropdown");
            vm.Settings.Put("policeOpponents", true); Pump();
            Check(!roster.IsEnabled && GameService.OpponentIdentity(vm.Settings) == 15, "combat page displays police precedence");
            Check(Descendants(surface).OfType<Button>().Any(b => Equals(b.Content, vm.Settings.Strings.Get("ui.apply"))), "detached window includes localized action captions");
            vm.Settings.Put("forceOpponent", false); vm.Settings.Put("policeOpponents", false);
            foreach (int width in new[] { 1280, 1064 })
            {
                int height = width == 1280 ? 800 : 660;
                foreach (SettingsPage page in vm.Pages)
                {
                    if (page == vm.Pages[0])
                    {
                        vm.Settings.ApplyMode("normal");
                        vm.PublicEnemyPreset = true;
                    }
                    if (page == vm.Pages[1]) vm.Settings.ApplyMode("run");
                    vm.Page = page; window.PageScroll.ScrollToTop(); Pump();
                    surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height)); surface.UpdateLayout(); Pump();
                    Check(surface.ActualWidth == width, "layout " + page.Number + " at " + width);
                    SaveBitmap(surface, width, height, Path.Combine(output, "page-" + page.Number + "-" + width + ".png"));
                    foreach (Expander expander in Descendants(surface).OfType<Expander>()) expander.IsExpanded = true;
                    window.PageScroll.ScrollToEnd(); Pump(); surface.UpdateLayout();
                    SaveBitmap(surface, width, height, Path.Combine(output, "page-" + page.Number + "-" + width + "-bottom.png"));
                }
            }
            window.Close();
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
        private static void Pump() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        private static void SaveBitmap(FrameworkElement surface, int width, int height, string path)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
        }
    }
}
