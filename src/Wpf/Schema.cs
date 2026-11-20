using System;
using System.Linq;

namespace RoadRash.Workbench
{
    internal static class Schema
    {
        internal static readonly int[] BikeIds = { 4, 1, 2, 0, 3, 14, 13, 12, 10, 11, 6, 8, 5, 9, 7 };
        internal static readonly string[] RoadWidths = new[] { "0.5" }.Concat(Enumerable.Range(1, 20).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        internal static void Build(SettingsStore store, LauncherStrings strings)
        {
            SettingsPage page = null;
            SettingGroup group = null;
            void P(string id, string icon)
            {
                page = new SettingsPage((store.Pages.Count + 1).ToString("00"), strings.Get("page." + id + ".title"), strings.Get("page." + id + ".caption"), icon);
                store.Pages.Add(page);
            }
            void G(string id, bool advanced = false)
            {
                group = new SettingGroup(strings.Get("group." + id + ".title"), strings.Optional("group." + id + ".caption"), advanced);
                page.Groups.Add(group);
            }
            string Title(string key) => strings.Get("setting." + key + ".label");
            string Description(string key) => strings.Get("setting." + key + ".description");
            void B(string key, string dependency = null) => store.Add(group, new Setting(key, Title(key), Description(key), "bool", dependency: dependency));
            void N(string key, decimal min, decimal max = decimal.MaxValue, bool integer = false, string dependency = null) => store.Add(group, new Setting(key, Title(key), Description(key), "number", min: min, max: max, integer: integer, dependency: dependency));
            void C(string key, int count, string dependency = null)
            {
                var options = Enumerable.Range(0, count).Select(i => strings.Get("setting." + key + ".option." + i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
                store.Add(group, new Setting(key, Title(key), Description(key), "choice", options, dependency: dependency));
            }
            void T(string key, string dependency = null) => store.Add(group, new Setting(key, Title(key), Description(key), "text", dependency: dependency));

            P("race", "\uE7C1");
            store.Add(null, new Setting("policePlayer", strings.Get("ui.presets.police"), strings.Get("ui.presets.policeDescription"), "bool"));
            store.Add(null, new Setting("hardcore", Title("hardcore"), Description("hardcore"), "bool", dependency: "!policePlayer"));
            G("grid");
            N("racers", 4, 10000, true);
            G("course");
            B("forceMap");
            C("mapLevel", 5, "forceMap");
            C("mapCourse", 6, "forceMap");
            G("length");
            B("forceTrackLength");
            N("trackLengthKm", TrackLengthUnits.MinimumKm, TrackLengthUnits.MaximumKm, dependency: "forceTrackLength");

            P("run", "\uE916");
            G("runRules");
            B("longRun", "!policePlayer");
            C("longRunScope", 3, "longRun");
            B("hideFallenVehicles");
            C("remountPolicy", 4);
            G("runSpeed");
            N("playerWalkSpeed", .1m, RuntimePatch.MaximumWalkSpeed);
            N("aiWalkSpeed", .1m, RuntimePatch.MaximumWalkSpeed);
            G("runVariation");
            B("aiRunVariation", "longRun");
            C("aiRunRhythm", 4, "longRun&aiRunVariation");
            C("aiRunSprint", 4, "longRun&aiRunVariation");
            C("aiRunChase", 4, "longRun&aiRunVariation");
            C("aiRunWeave", 4, "longRun&aiRunVariation");
            G("chase", true);
            N("chaseDistance", 32, 192, true, "longRun");

            P("vehicles", "\uE804");
            G("playerVehicles");
            B("carShell", "!policePlayer");
            C("carStyle", 15, "carShell");
            B("autoCarCollision", "carShell");
            B("carStability", "carShell");
            B("infiniteDurability");
            G("collisions");
            B("ramKnockdown");
            B("invincible");
            B("rockSolid");
            B("carCollision", "!carShell");
            G("launch", true);
            B("exaggeratedLaunch");
            C("launchScope", 2, "exaggeratedLaunch");
            N("launchMultiplier", 1, 8, dependency: "exaggeratedLaunch");

            P("combat", "\uE77B");
            G("playerEquipment");
            B("forceName");
            T("playerName", "forceName");
            B("forceBike", "!policePlayer");
            C("playerBike", 15, "forceBike&!policePlayer");
            B("forceWeapon", "!policePlayer");
            C("playerWeapon", 3, "forceWeapon&!policePlayer");
            G("opponents");
            B("forceOpponent", "!policeOpponents");
            C("opponentRider", 8, "forceOpponent&!policeOpponents");
            B("policeOpponents", "!longRun");
            B("opponentsTargetPlayer");
            G("combatRules");
            C("combat", 3);
            N("aggression", 0, 10, true);
            B("noStun");
            B("rearEnd");
            B("oneHit");
            B("noDirection");
            B("onFootProtection");
            C("onFootScope", 2, "onFootProtection");
            G("attacks", true);
            N("longRange", 1, 32, true);
            N("lateralAll", 1, 32, true);
            N("lateralPlayer", 1, 32, true);
            B("aoe");
            C("aoeScope", 2, "aoe");
            N("aoeTargets", 2, 32, true, "aoe");
            B("downed");
            C("downScope", 2, "downed");

            P("traffic", "\uE81D");
            G("roads");
            C("roadWidth", 21);
            N("traffic", 0, 40, true);
            N("police", 0, 40, true);
            G("trafficDriving");
            B("sameTraffic");
            B("oppositeTraffic");
            N("sameTrafficSpeed", 0);
            N("oppositeTrafficSpeed", 0);
            B("crazyDrivers");

            P("display", "\uE7F4");
            G("knockdowns");
            B("knockdownHud");
            N("knockdownCreditSeconds", .001m, 600, dependency: "knockdownHud");
            N("streakSeconds", .001m, 600, dependency: "knockdownHud");
            B("knockdownVoice", "knockdownHud");
            N("knockdownsPerVoice", 1, 10000, true, "knockdownHud&knockdownVoice");
            G("performance");
            B("hd");
            N("frameRate", 60, 300, true);
            G("language");
            B("chinese");
            B("skipCutscenes");

            P("fixes", "\uE90F");
            G("repairs");
            C("rankMode", 4);
            B("fallAnimationFix");
            B("independentAudio");
            B("nearbyImpactAudio");

            P("tools", "\uE713");
            G("debug", true);
            B("overlayCar", "!policePlayer");
            T("overlayImage", "overlayCar");
            B("carDebugCopies", "!policePlayer");
        }
    }
}
