using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class LauncherUiTests
{
    static int passed;
    static T Field<T>(LauncherForm form, string name) { return (T)typeof(LauncherForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    static void Call(LauncherForm form, string name) { typeof(LauncherForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(form, null); }
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
    static TabPage OwnerTab(Control control) { while(control!=null&&!(control is TabPage))control=control.Parent;return control as TabPage; }
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string temp = Path.Combine(Path.GetTempPath(), "RoadRashUi-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        Launcher.GameDir = temp;
        try {
            using (LauncherForm f = new LauncherForm()) {
                Check(Field<TabControl>(f, "modes").TabCount == 3 && Field<TabControl>(f, "advancedTabs").TabCount == 6, "three views and all six detailed categories");
                TabPage fixes=Field<TabControl>(f,"modes").TabPages[2];
                Check(fixes.Text=="Fix 修复"&&OwnerTab(Field<CheckBox>(f,"independentAudio"))==fixes&&OwnerTab(Field<CheckBox>(f,"fallAnimationFix"))==fixes,"original-game corrections belong to the standalone Fix page");
                Check(Field<CheckBox>(f,"independentAudio").Checked&&Field<CheckBox>(f,"fallAnimationFix").Checked,"new installations enable both original-game fixes by default");
                int vehicleBeforeFix=Field<ComboBox>(f,"quickVehicle").SelectedIndex;
                Field<Label>(f,"changeStatus").Text="unchanged";
                Field<CheckBox>(f,"fallAnimationFix").Checked=false;Field<CheckBox>(f,"independentAudio").Checked=false;
                Check(Field<Label>(f,"changeStatus").Text.Contains("设置已修改")&&Field<ComboBox>(f,"quickVehicle").SelectedIndex==vehicleBeforeFix,"Fix edits report unsaved changes without changing the vehicle preset");
                Check(Field<CheckBox>(f,"infiniteDurability").Checked && Field<NumericUpDown>(f,"playerWalkSpeed").Value == 1 && Field<NumericUpDown>(f,"aiWalkSpeed").Value == 1 && !Field<CheckBox>(f,"onFootProtection").Checked && !Field<ComboBox>(f,"onFootScope").Enabled, "character defaults and disabled scope");
                Check(!Field<CheckBox>(f,"longRun").Checked && Field<ComboBox>(f,"remountPolicy").SelectedIndex==0 && !Field<ComboBox>(f,"longRunScope").Enabled, "legacy-safe race defaults");
                Field<ComboBox>(f,"quickVehicle").SelectedIndex=4;
                Check(!Field<CheckBox>(f,"fallAnimationFix").Checked&&!Field<CheckBox>(f,"independentAudio").Checked,"long-run preset preserves independently disabled fixes");
                Check(Field<CheckBox>(f,"hideFallenVehicles").Checked,"long run preset hides fallen vehicles");
                Check(Field<CheckBox>(f,"aiRunVariation").Checked&&Field<ComboBox>(f,"aiRunRhythm").SelectedIndex==2&&Field<ComboBox>(f,"aiRunSprint").SelectedIndex==2&&Field<ComboBox>(f,"aiRunChase").SelectedIndex==2&&Field<ComboBox>(f,"aiRunWeave").SelectedIndex==2&&Field<ComboBox>(f,"aiRunWeave").Enabled,"long run enables medium AI variation");
                Field<ComboBox>(f,"aiRunSprint").SelectedIndex=3;
                Check(Field<ComboBox>(f,"quickVehicle").SelectedIndex==0,"AI pacing override marks long run custom");
                Field<ComboBox>(f,"quickVehicle").SelectedIndex=4;
                Field<CheckBox>(f,"hideFallenVehicles").Checked=false;
                Check(Field<ComboBox>(f,"quickVehicle").SelectedIndex==0,"manual hide override makes long run custom");
                Field<ComboBox>(f,"quickVehicle").SelectedIndex=4;
                Check(Field<CheckBox>(f,"longRun").Checked && Field<ComboBox>(f,"longRunScope").SelectedIndex==2 && Field<ComboBox>(f,"remountPolicy").SelectedIndex==3 && Field<NumericUpDown>(f,"chaseDistance").Value==96 && !Field<CheckBox>(f,"carShell").Checked, "long-run preset selects everyone and disables recovery");
                Field<ComboBox>(f,"longRunScope").SelectedIndex=0;
                Check(Field<ComboBox>(f,"quickVehicle").SelectedIndex==0, "AI-only long-run becomes custom");
                Field<ComboBox>(f,"remountPolicy").SelectedIndex=2;Field<NumericUpDown>(f,"chaseDistance").Value=144;
                Call(f,"SaveUiSettings");
                using(LauncherForm raceRestored=new LauncherForm()) {
                    Call(raceRestored,"LoadUiSettings");
                    Check(Field<CheckBox>(raceRestored,"longRun").Checked && Field<ComboBox>(raceRestored,"longRunScope").SelectedIndex==0 && Field<ComboBox>(raceRestored,"remountPolicy").SelectedIndex==2 && Field<NumericUpDown>(raceRestored,"chaseDistance").Value==144, "race scope distance and recovery persist independently");
                }
                Field<ComboBox>(f,"quickVehicle").SelectedIndex=1;
                Check(!Field<CheckBox>(f,"hideFallenVehicles").Checked,"normal preset restores vehicle visibility");
                Check(!Field<CheckBox>(f,"aiRunVariation").Checked&&!Field<CheckBox>(f,"aiRunVariation").Enabled&&!Field<ComboBox>(f,"aiRunWeave").Enabled,"normal preset disables long run AI controls");
                Check(!Field<CheckBox>(f,"longRun").Checked && Field<ComboBox>(f,"remountPolicy").SelectedIndex==0 && !Field<NumericUpDown>(f,"chaseDistance").Enabled, "normal preset clears long-run and restores recovery");
                Field<ComboBox>(f,"remountPolicy").SelectedIndex=3;
                Check(!Field<CheckBox>(f,"longRun").Checked && Field<ComboBox>(f,"quickVehicle").SelectedIndex==0, "timed recovery independently configurable without long-run");
                Field<ComboBox>(f,"quickVehicle").SelectedIndex=1;
                for (int preset=1;preset<=3;preset++) {
                    Field<CheckBox>(f,"infiniteDurability").Checked = false;
                    Field<ComboBox>(f,"quickVehicle").SelectedIndex = preset;
                    Check(Field<CheckBox>(f,"infiniteDurability").Checked, "vehicle preset enables durability " + preset);
                }
                Field<CheckBox>(f,"infiniteDurability").Checked = false;
                Check(Field<ComboBox>(f,"quickVehicle").SelectedIndex == 0, "durability override marks vehicle custom");
                Field<NumericUpDown>(f,"racers").Value = 537;
                Check(Field<ComboBox>(f,"quickScale").SelectedIndex == 0, "custom race size is preserved");
                Field<ComboBox>(f,"quickScale").SelectedIndex = 4;
                Check(Field<NumericUpDown>(f,"racers").Value == 500, "large scale sets exact race count");
                Field<ComboBox>(f,"quickScale").SelectedIndex = 0;
                Check(Field<TabControl>(f,"modes").SelectedIndex == 1 && Field<TabControl>(f,"advancedTabs").SelectedIndex == 1 && Field<NumericUpDown>(f,"racers").Value == 500, "custom opens matching advanced category without changing values");
                Field<TabControl>(f,"modes").SelectedIndex = 0;
                Field<NumericUpDown>(f,"traffic").Value = 7;
                Field<ComboBox>(f,"quickVehicle").SelectedIndex = 3;
                Check(Field<CheckBox>(f,"ramKnockdown").Checked && Field<CheckBox>(f,"invincible").Checked && Field<CheckBox>(f,"carShell").Checked && Field<CheckBox>(f,"carStability").Checked && Field<CheckBox>(f,"autoCarCollision").Checked, "truck enables all five requested features");
                Check(Field<CheckBox>(f,"rockSolid").Checked && Field<CheckBox>(f,"exaggeratedLaunch").Checked && Field<ComboBox>(f,"launchScope").SelectedIndex == 0 && Field<NumericUpDown>(f,"launchMultiplier").Value == 1.5m && Field<NumericUpDown>(f,"launchMultiplier").Increment == 0.1m, "truck enables stable steering and opponent launch x1.5 with 0.1 steps");
                Field<NumericUpDown>(f,"launchMultiplier").Value = 5;
                Check(Field<ComboBox>(f,"quickVehicle").SelectedIndex == 0, "launch override marks custom");
                Field<ComboBox>(f,"quickVehicle").SelectedIndex = 2;
                Check(Field<CheckBox>(f,"rockSolid").Checked && !Field<CheckBox>(f,"exaggeratedLaunch").Checked, "car defaults to stable steering only");
                Field<ComboBox>(f,"quickVehicle").SelectedIndex = 3;
                Check(!Field<CheckBox>(f,"overlayCar").Checked && !Field<CheckBox>(f,"carDebugCopies").Checked && Field<NumericUpDown>(f,"traffic").Value == 7 && Field<NumericUpDown>(f,"racers").Value == 500, "vehicle preset clears conflicts and preserves unrelated choices");
                Field<CheckBox>(f,"carStability").Checked = false;
                Check(Field<ComboBox>(f,"quickVehicle").SelectedIndex == 0, "advanced override marks preset custom");
                Field<ComboBox>(f,"quickVehicle").SelectedIndex = 1;
                Check(!Field<CheckBox>(f,"carShell").Checked && !Field<CheckBox>(f,"ramKnockdown").Checked && !Field<CheckBox>(f,"invincible").Checked, "return to motorcycle clears truck features");
                Field<ComboBox>(f,"quickVehicle").SelectedIndex = 3;
                Field<ComboBox>(f,"carStyle").SelectedIndex = 8;
                Field<NumericUpDown>(f,"frameRate").Value = 144;
                Field<TextBox>(f,"playerName").Text = "测试车手";
                Field<CheckBox>(f,"independentAudio").Checked = false;
                Field<NumericUpDown>(f,"playerWalkSpeed").Value = 99.7m;
                Field<NumericUpDown>(f,"aiWalkSpeed").Value = 123.4m;
                Check(Field<NumericUpDown>(f,"sameTrafficSpeed").Value==1&&Field<NumericUpDown>(f,"oppositeTrafficSpeed").Value==1&&!Field<CheckBox>(f,"crazyDrivers").Checked,"traffic defaults preserve original driving");
                Field<NumericUpDown>(f,"sameTrafficSpeed").Value=123456789.25m;
                Field<NumericUpDown>(f,"oppositeTrafficSpeed").Value=0;
                Field<CheckBox>(f,"crazyDrivers").Checked=true;
                Field<ComboBox>(f,"quickTraffic").SelectedIndex=1;
                Check(Field<NumericUpDown>(f,"sameTrafficSpeed").Value==123456789.25m&&Field<CheckBox>(f,"crazyDrivers").Checked,"traffic quantity presets preserve driving choices");
                Field<NumericUpDown>(f,"traffic").Value=7;
                Field<CheckBox>(f,"onFootProtection").Checked = true;
                Field<ComboBox>(f,"onFootScope").SelectedIndex = 1;
                Call(f,"SaveUiSettings");
                using (LauncherForm restored = new LauncherForm()) {
                    Call(restored,"LoadUiSettings");
                    Check(!Field<CheckBox>(restored,"fallAnimationFix").Checked&&!Field<CheckBox>(restored,"independentAudio").Checked,"explicitly disabled fixes survive save and restart");
                    Check(Field<NumericUpDown>(restored,"sameTrafficSpeed").Value==123456789.25m&&Field<NumericUpDown>(restored,"oppositeTrafficSpeed").Value==0&&Field<CheckBox>(restored,"crazyDrivers").Checked,"traffic speed above eight, zero and crazy setting persist");
                    Check(Field<NumericUpDown>(restored,"racers").Value == 500 && Field<NumericUpDown>(restored,"traffic").Value == 7 && Field<NumericUpDown>(restored,"frameRate").Value == 144 && Field<TextBox>(restored,"playerName").Text == "测试车手" && Field<ComboBox>(restored,"carStyle").SelectedIndex == 8 && Field<ComboBox>(restored,"quickVehicle").SelectedIndex == 3 && !Field<CheckBox>(restored,"independentAudio").Checked, "draft advanced values and presets survive restart");
                    Check(Field<CheckBox>(restored,"rockSolid").Checked && Field<CheckBox>(restored,"exaggeratedLaunch").Checked && Field<NumericUpDown>(restored,"launchMultiplier").Value == 1.5m, "new motion options survive restart");
                    Check(Field<CheckBox>(restored,"infiniteDurability").Checked && Field<NumericUpDown>(restored,"playerWalkSpeed").Value == 99.7m && Field<NumericUpDown>(restored,"aiWalkSpeed").Value == 123.4m && Field<CheckBox>(restored,"onFootProtection").Checked && Field<ComboBox>(restored,"onFootScope").SelectedIndex == 1 && Field<ComboBox>(restored,"onFootScope").Enabled, "independent character options and speeds above eight survive restart");
                }
                if (args.Length > 0) {
                    Field<ComboBox>(f,"quickVehicle").SelectedIndex=4;
                    Directory.CreateDirectory(args[0]);
                    f.CreateControl();
                    // Render our own form offscreen; no desktop/window capture or input automation.
                    Render(f, Path.Combine(args[0], "quick.png"));
                    Field<TabControl>(f,"modes").SelectedIndex = 1;
                    for (int i=0; i<6; i++) { Field<TabControl>(f,"advancedTabs").SelectedIndex=i; Render(f, Path.Combine(args[0], "advanced-"+i+".png")); }
                    Field<TabControl>(f,"advancedTabs").SelectedIndex=1;
                    Control trafficCard=Field<CheckBox>(f,"crazyDrivers").Parent.Parent;
                    Field<TabControl>(f,"advancedTabs").SelectedTab.ScrollControlIntoView(trafficCard.Controls[0].Controls[3]);
                    Render(f,Path.Combine(args[0],"traffic-driving.png"));
                    using(Bitmap card=new Bitmap(trafficCard.Width,trafficCard.Height)){trafficCard.DrawToBitmap(card,new Rectangle(Point.Empty,card.Size));card.Save(Path.Combine(args[0],"traffic-driving-card.png"));}
                    f.ClientSize = new Size(944, 661);
                    Field<TabControl>(f,"modes").SelectedIndex = 0;
                    Render(f, Path.Combine(args[0], "quick-min.png"));
                    Field<CheckBox>(f,"fallAnimationFix").Checked=true;Field<CheckBox>(f,"independentAudio").Checked=true;
                    Field<TabControl>(f,"modes").SelectedIndex=2;
                    Render(f,Path.Combine(args[0],"fixes-min.png"));
                    f.ClientSize=new Size(1080,800);Render(f,Path.Combine(args[0],"fixes.png"));
                }
            }
            // Legacy INI migration defaults must preserve the verified car protections.
            File.WriteAllText(Path.Combine(temp,"launcher_settings.ini"), "carShell=1\noverlayCar=0\nramKnockdown=1\ninvincible=1\ncarStyle=3\n");
            using (LauncherForm legacy = new LauncherForm()) { Call(legacy,"LoadUiSettings"); Check(Field<CheckBox>(legacy,"carStability").Checked && Field<CheckBox>(legacy,"autoCarCollision").Checked && Field<ComboBox>(legacy,"carStyle").SelectedIndex == 3, "legacy car settings migrate without losing protections"); }
            using (LauncherForm legacy = new LauncherForm()) { Call(legacy,"LoadUiSettings"); Check(Field<CheckBox>(legacy,"infiniteDurability").Checked && Field<NumericUpDown>(legacy,"playerWalkSpeed").Value == 1 && !Field<CheckBox>(legacy,"onFootProtection").Checked, "old INI receives safe character defaults"); }
            File.WriteAllText(Path.Combine(temp,"launcher_settings.ini"),"settingsVersion=8\nindependentAudio=0\nlongRun=1\n");
            using(LauncherForm legacyFix=new LauncherForm()) {
                Call(legacyFix,"LoadUiSettings");
                Check(!Field<CheckBox>(legacyFix,"independentAudio").Checked&&Field<CheckBox>(legacyFix,"fallAnimationFix").Checked,"old settings retain audio choice and default the extracted animation fix on");
                for(int audio=0;audio<2;audio++)for(int animation=0;animation<2;animation++) {
                    Field<CheckBox>(legacyFix,"independentAudio").Checked=audio==1;Field<CheckBox>(legacyFix,"fallAnimationFix").Checked=animation==1;
                    for(int preset=1;preset<=4;preset++)Field<ComboBox>(legacyFix,"quickVehicle").SelectedIndex=preset;
                    Check(Field<CheckBox>(legacyFix,"independentAudio").Checked==(audio==1)&&Field<CheckBox>(legacyFix,"fallAnimationFix").Checked==(animation==1),"all four vehicle presets preserve fix combination audio="+audio+" animation="+animation);
                    Call(legacyFix,"SaveUiSettings");
                    using(LauncherForm savedFix=new LauncherForm()) {
                        Call(savedFix,"LoadUiSettings");
                        Check(Field<CheckBox>(savedFix,"independentAudio").Checked==(audio==1)&&Field<CheckBox>(savedFix,"fallAnimationFix").Checked==(animation==1),"each independent fix combination survives restart audio="+audio+" animation="+animation);
                    }
                }
            }
            File.WriteAllText(Path.Combine(temp,"launcher_settings.ini"), "infiniteDurability=0\nplayerWalkSpeed=99\naiWalkSpeed=invalid\nonFootProtection=1\nonFootScope=99\nlongRunScope=99\nremountPolicy=99\nchaseDistance=9999\n");
            using (LauncherForm invalid = new LauncherForm()) {
                Call(invalid,"LoadUiSettings");
                Check(!Field<CheckBox>(invalid,"infiniteDurability").Checked && Field<NumericUpDown>(invalid,"playerWalkSpeed").Value == 99 && Field<NumericUpDown>(invalid,"aiWalkSpeed").Value == 1 && Field<ComboBox>(invalid,"onFootScope").SelectedIndex == 0, "explicit off and unrestricted speed retained while invalid values fall back");
                Check(Field<ComboBox>(invalid,"longRunScope").SelectedIndex==2 && Field<ComboBox>(invalid,"remountPolicy").SelectedIndex==0 && Field<NumericUpDown>(invalid,"chaseDistance").Value==192, "invalid race settings fall back and clamp");
                Call(invalid,"SaveUiSettings");
            }
            using (LauncherForm restored = new LauncherForm()) { Call(restored,"LoadUiSettings"); Check(!Field<CheckBox>(restored,"infiniteDurability").Checked, "disabled durability stays disabled after save and restart"); }
            File.WriteAllText(Path.Combine(temp,"launcher_settings.ini"),"settingsVersion=5\nlongRun=1\n");
            using(LauncherForm legacyRun=new LauncherForm()) {
                Call(legacyRun,"LoadUiSettings");
                Check(Field<NumericUpDown>(legacyRun,"sameTrafficSpeed").Value==1&&Field<NumericUpDown>(legacyRun,"oppositeTrafficSpeed").Value==1&&!Field<CheckBox>(legacyRun,"crazyDrivers").Checked,"old config preserves original traffic speed and behavior");
                Check(Field<CheckBox>(legacyRun,"hideFallenVehicles").Checked,"existing long run defaults to hidden vehicles");
                Check(Field<CheckBox>(legacyRun,"aiRunVariation").Checked&&Field<ComboBox>(legacyRun,"aiRunChase").SelectedIndex==2,"existing long run migrates to medium AI variation");
                Field<CheckBox>(legacyRun,"aiRunVariation").Checked=false;
                Field<ComboBox>(legacyRun,"aiRunRhythm").SelectedIndex=1;Field<ComboBox>(legacyRun,"aiRunSprint").SelectedIndex=3;
                Field<ComboBox>(legacyRun,"aiRunChase").SelectedIndex=0;Field<ComboBox>(legacyRun,"aiRunWeave").SelectedIndex=1;
                Field<CheckBox>(legacyRun,"hideFallenVehicles").Checked=false;Call(legacyRun,"SaveUiSettings");
            }
            using(LauncherForm restored=new LauncherForm()) {
                Call(restored,"LoadUiSettings");
                Check(Field<CheckBox>(restored,"longRun").Checked&&!Field<CheckBox>(restored,"hideFallenVehicles").Checked,"explicit visibility choice survives long run restart");
                Check(!Field<CheckBox>(restored,"aiRunVariation").Checked&&Field<ComboBox>(restored,"aiRunRhythm").SelectedIndex==1&&Field<ComboBox>(restored,"aiRunSprint").SelectedIndex==3&&Field<ComboBox>(restored,"aiRunChase").SelectedIndex==0&&Field<ComboBox>(restored,"aiRunWeave").SelectedIndex==1,"all AI preferences and explicit off survive restart");
                Field<CheckBox>(restored,"aiRunVariation").Checked=true;Field<ComboBox>(restored,"longRunScope").SelectedIndex=1;
                Check(!Field<CheckBox>(restored,"aiRunVariation").Enabled&&!Field<ComboBox>(restored,"aiRunChase").Enabled,"player-only long run does not enable AI variation");
            }
            File.WriteAllText(Path.Combine(temp,"launcher_settings.ini"),"playerWalkSpeed=9999999999\naiWalkSpeed=-9\nsameTrafficSpeed=79228162514264337593543950335\noppositeTrafficSpeed=-9\ncrazyDrivers=invalid\n");
            using(LauncherForm bounds=new LauncherForm()) {
                Call(bounds,"LoadUiSettings");
                Check(Field<NumericUpDown>(bounds,"playerWalkSpeed").Value==RuntimePatch.MaximumWalkSpeed&&Field<NumericUpDown>(bounds,"aiWalkSpeed").Value==0.1m&&!Field<CheckBox>(bounds,"hideFallenVehicles").Checked,"native numeric storage bounds and normal visibility defaults");
                Check(Field<NumericUpDown>(bounds,"sameTrafficSpeed").Value==decimal.MaxValue&&Field<NumericUpDown>(bounds,"oppositeTrafficSpeed").Value==1&&!Field<CheckBox>(bounds,"crazyDrivers").Checked,"maximum decimal traffic input retained; negative and invalid settings use defaults");
                Call(bounds,"SaveUiSettings");
            }
            using(LauncherForm restored=new LauncherForm()){Call(restored,"LoadUiSettings");Check(Field<NumericUpDown>(restored,"sameTrafficSpeed").Value==decimal.MaxValue,"maximum decimal multiplier survives save and reload");}
            Console.WriteLine("Launcher UI tests: " + passed + " passed"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void CreateChildren(Control c) { c.CreateControl(); foreach (Control child in c.Controls) CreateChildren(child); c.PerformLayout(); }
    static void Render(LauncherForm f, string path)
    {
        IntPtr hiddenHandle = f.Handle;
        typeof(Control).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, new object[] { 2, true });
        CreateChildren(f);
        f.PerformLayout();
        using (Bitmap b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(Point.Empty, b.Size)); b.Save(path); }
    }
}
