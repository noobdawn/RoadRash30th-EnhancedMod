using System;
using System.Windows.Forms;

internal sealed partial class LauncherForm
{
    private readonly CheckBox longRun = new CheckBox();
    private readonly PresetComboBox longRunScope = new PresetComboBox();
    private readonly PresetComboBox remountPolicy = new PresetComboBox();
    private readonly NumericUpDown chaseDistance = new NumericUpDown();

    private Control BuildLongRunGroup()
    {
        GroupBox group = Group("长跑与恢复上车"); TableLayoutPanel body = PageLayout(4);
        longRun.Text = "倒计时结束后翻车，持续将车辆移到选手前方"; longRun.AutoSize = true;
        longRunScope.DropDownStyle = ComboBoxStyle.DropDownList; longRunScope.Width = 180;
        longRunScope.Items.AddRange(new string[] { "仅 AI", "仅玩家", "所有人（玩家与 AI）" }); longRunScope.SelectedIndex = 2;
        chaseDistance.Minimum = 32; chaseDistance.Maximum = 192; chaseDistance.Increment = 16; chaseDistance.Value = 96; chaseDistance.Width = 70;
        FlowLayoutPanel chase = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        chase.Controls.Add(longRunScope);
        chase.Controls.Add(new Label { Text = "车辆领先距离", AutoSize = true, Padding = new Padding(8) }); chase.Controls.Add(chaseDistance);
        remountPolicy.DropDownStyle = ComboBoxStyle.DropDownList; remountPolicy.Width = 180;
        remountPolicy.Items.AddRange(new string[] { "所有人（原版）", "仅 AI", "仅玩家", "关闭" }); remountPolicy.SelectedIndex = 0;
        FlowLayoutPanel recovery = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        recovery.Controls.Add(new Label { Text = "超时恢复上车", AutoSize = true, Padding = new Padding(8) }); recovery.Controls.Add(remountPolicy);
        body.Controls.Add(longRun, 0, 0); body.Controls.Add(chase, 0, 1); body.Controls.Add(recovery, 0, 2);
        body.Controls.Add(Paragraph("领先距离使用游戏路程单位（32–192），默认 96。AI 会持续追车；玩家保留方向控制。长跑预设关闭超时恢复上车，正常跑到车旁上车不受此开关影响。人物跑过终点后结束追车。", Muted), 0, 3);
        group.Controls.Add(body); return group;
    }
}

internal static partial class RuntimePatch
{
    internal static readonly int[] RaceSites = { 0x66307, 0x17CA7, 0x19F4E };
    internal static readonly byte[][] RaceOriginals = {
        new byte[] { 0xE8,0xFC,0xD6,0xF9,0xFF },
        new byte[] { 0x81,0xBC,0x81,0x38,1,0,0,0xB0,4,0,0 },
        new byte[] { 0x81,0xBC,0x81,0x38,1,0,0,0xB0,4,0,0 }
    };

    internal static readonly int[] RaceWorldSites = { 0x16417,0x15BE0,0x16041 };
    internal static readonly byte[][] RaceWorldOriginals = {
        new byte[] { 0xE8,0xCD,0xC2,0xFE,0xFF },
        new byte[] { 0x8B,0xBE,0x14,3,0,0 },
        new byte[] { 0xE8,0xC5,0xFE,0xFE,0xFF }
    };
    internal const int RunGuideCollisionSite=0x49610;
    internal static readonly byte[] RunGuideCollisionOriginal={0x8A,0x86,0xC0,0,0,0};
    internal const int RunGuideEndPairs = 0x496D0;
    internal static readonly byte[] RunGuideEndPairsOriginal = { 0x8B,0x55,0xF4,0x8B,0x1B };

    internal static void ApplyRace(IntPtr process, long image, bool run, int scope, int recovery, int distance, Action<string> log, decimal playerSpeed = 1m, decimal aiSpeed = 1m, long aiCode = 0, long aiConfig = 0)
    {
        ValidateRace(scope, recovery, distance);
        if (!run && recovery == 0) return;
        for (int i = run ? 0 : 1; i < 3; i++) RequireMotionSignature(process, image + RaceSites[i], RaceOriginals[i]);
        ValidateWalkSpeed(playerSpeed); ValidateWalkSpeed(aiSpeed);
        if (run) {
            RequireMotionSignature(process, image + RvaCrashFunction, new byte[] { 0x55,0x8B,0xEC,0x83,0xEC,0x34 });
            for (int i=0;i<RaceWorldSites.Length;i++) RequireMotionSignature(process,image + RaceWorldSites[i],RaceWorldOriginals[i]);
            RequireMotionSignature(process,image+RunGuideCollisionSite,RunGuideCollisionOriginal);
            RequireMotionSignature(process,image+RunGuideEndPairs,RunGuideEndPairsOriginal);
        }
        IntPtr block = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)0x4000, MemCommitReserve, PageExecuteReadWrite);
        if (block == IntPtr.Zero) throw new InvalidOperationException("无法分配长跑补丁代码区。");
        long cave = block.ToInt64();
        if (run) {
            Write(process, cave, BuildRaceStartCode(image, cave, cave + 0x1800, scope, recovery));
            CodeBuffer crash = new CodeBuffer(cave + 0x1800);
            B(crash,0x55,0x8B,0xEC,0x83,0xEC,0x34); JmpAbs(crash,image + RvaCrashFunction + 6);
            Write(process, cave + 0x1800, crash.Finish()); // bypass only the initial fall's invincibility hook
            for (int i=0;i<RaceWorldSites.Length;i++) Write(process,cave + 0x2000 + i*0x400,BuildRaceWorldCode(image,cave + 0x2000 + i*0x400,i,scope,recovery,playerSpeed,aiSpeed,aiCode,aiConfig));
            Write(process,cave+0x3400,BuildRunGuideCollisionCode(image,cave+0x3400,scope));
        }
        for (int i = 1; i < 3; i++) Write(process, cave + i * 0x800, BuildRecoveryCode(image, cave + i * 0x800, i, run, scope, recovery, distance,aiCode,aiConfig));
        for (int i = run ? 0 : 1; i < 3; i++) Write(process, image + RaceSites[i], Jmp(image + RaceSites[i], cave + i * 0x800, RaceOriginals[i].Length));
        if (run) for (int i=0;i<RaceWorldSites.Length;i++) Write(process,image + RaceWorldSites[i],Jmp(image + RaceWorldSites[i],cave + 0x2000 + i*0x400,RaceWorldOriginals[i].Length));
        if (run) {
            Write(process,image+RunGuideCollisionSite,Jmp(image+RunGuideCollisionSite,cave+0x3400,RunGuideCollisionOriginal.Length));
            log("已启用长跑引导车碰撞优化。");
        }
        log("比赛设置：长跑=" + run + "；范围=" + new string[] { "仅 AI", "仅玩家", "所有人" }[scope] + "；车辆领先=" + distance + "；超时恢复上车=" + new string[] { "所有人", "仅 AI", "仅玩家", "关闭" }[recovery]);
    }

    internal static byte[] BuildRunGuideCollisionCode(long image,long cave,int scope)
    {
        ValidateRace(scope,3,96);
        CodeBuffer c=new CodeBuffer(cave);B(c,0x9C,0x60);
        // Original pair loop: EBX=outer actor, ESI=candidate. Skip only selected
        // running bikes; rider/traffic collisions and normal remount remain native.
        B(c,0x89,0xF7,0x89,0xDE); // save candidate in EDI, inspect outer in ESI
        for(int side=0;side<2;side++) {
            string next=side==0?"candidate":"original";
            B(c,0x83,0xBE);D(c,0xC4);B(c,4);Jcc(c,0x85,next);
            B(c,0x83,0xBE);D(c,0x2F4);B(c,2);Jcc(c,0x85,next);
            RaceScope(c,image,scope,next);
            // No later candidate can collide with an outer guide. Advancing just
            // the candidate here repeatedly walks the entire remaining actor list.
            Jmp(c,side==0 ? "endPairs" : "nextCandidate");
            if(side==0){c.Label("candidate");B(c,0x89,0xFE);}
        }
        c.Label("original");B(c,0x61,0x9D);B(c,RunGuideCollisionOriginal);JmpAbs(c,image+RunGuideCollisionSite+6);
        c.Label("nextCandidate");B(c,0x61,0x9D);JmpAbs(c,image+0x496C2);
        c.Label("endPairs");B(c,0x61,0x9D);JmpAbs(c,image+RunGuideEndPairs);
        return c.Finish();
    }

    private static void ValidateRace(int scope, int recovery, int distance)
    {
        if (scope < 0 || scope > 2 || recovery < 0 || recovery > 3 || distance < 32 || distance > 192) throw new ArgumentOutOfRangeException("race settings");
    }

    // ESI=bike. Scope is deliberately based on the player pointer, never local/AI update paths.
    private static void RaceScope(CodeBuffer c, long image, int scope, string skip)
    {
        if (scope == 2) return;
        B(c,0x3B,0x35); D(c,image + RvaPlayer); Jcc(c,scope == 0 ? (byte)0x84 : (byte)0x85,skip);
    }

    internal static byte[] BuildRaceStartCode(long image, long cave, long crash, int scope, int recovery = 3)
    {
        ValidateRace(scope,recovery,96);
        CodeBuffer c = new CodeBuffer(cave);
        CallAbs(c,image + 0x3A08); // replay input polling before the first actor simulation tick
        B(c,0x9C,0x60);
        B(c,0x83,0x3D); D(c,image + 0x2036E8); B(c,1); Jcc(c,0x85,"done");
        B(c,0x8B,0x35); D(c,image + RvaActorChain); B(c,0x85,0xF6); Jcc(c,0x84,"done");
        B(c,0x8B,0x36,0x8B,0x36,0xBD); D(c,65536); // bounded walk includes expanded grids
        c.Label("loop"); B(c,0x85,0xF6); Jcc(c,0x84,"done");
        B(c,0x8B,0x1E,0x85,0xDB); Jcc(c,0x84,"done"); // keep next before native crash calls
        B(c,0x83,0xBE); D(c,0xC4); B(c,4); Jcc(c,0x85,"next");
        B(c,0x8B,0xBE); D(c,0x2FC); B(c,0x85,0xFF); Jcc(c,0x84,"next");
        B(c,0x83,0xBF); D(c,0xC4); B(c,8); Jcc(c,0x85,"next");
        B(c,0x39,0xB7); D(c,0x1C4); Jcc(c,0x85,"next");
        RaceScope(c,image,scope,"next");
        B(c,0x83,0xBE); D(c,0x2F4); B(c,0); Jcc(c,0x85,"next");
        B(c,0x80,0xBE); D(c,0x300); B(c,0); Jcc(c,0x85,"loaded");
        if(recovery==0)Jmp(c,"next");
        else if(recovery!=3) { B(c,0x3B,0x35);D(c,image+RvaPlayer);Jcc(c,recovery==1?(byte)0x85:(byte)0x84,"next"); }
        B(c,0xC7,0x86); D(c,0x2F4); D(c,2); Jmp(c,"next");
        c.Label("loaded");
        B(c,0x89,0xF1); CallAbs(c,crash);
        c.Label("next"); B(c,0x89,0xDE,0x4D); Jcc(c,0x85,"loop");
        c.Label("done"); B(c,0x61,0x9D); JmpAbs(c,image + RaceSites[0] + 5);
        return c.Finish();
    }

    internal static byte[] BuildRecoveryCode(long image, long cave, int site, bool run, int scope, int recovery, int distance, long aiCode = 0, long aiConfig = 0)
    {
        ValidateRace(scope,recovery,distance);
        if (site < 1 || site > 2) throw new ArgumentOutOfRangeException("site");
        CodeBuffer c = new CodeBuffer(cave); B(c,0x9C,0x60);
        if (site == 2) B(c,0x89,0xFE,0x89,0xDF); // normalize to ESI=bike, EDI=rider
        if (run) {
            RaceScope(c,image,scope,"recovery");
            B(c,0xF6,0x87); D(c,0xC0); B(c,0x10); Jcc(c,0x85,"recovery");
            // Work from the rider's current, streamed road context. A single move is < one
            // road segment; the native move helper resolves the next segment and junction.
            B(c,0x83,0x7F,0x2C,0); Jcc(c,0x84,"recovery");
            B(c,0x83,0xBF); D(c,0x140); B(c,0); Jcc(c,0x84,"recovery");
            foreach (int offset in new int[] { 0x1C,0x24,0x28,0x2C,0x134,0x138,0x13C,0x140 }) {
                B(c,0x8B,0x87); D(c,offset); B(c,0x89,0x86); D(c,offset);
            }
            if(aiCode!=0) {
                B(c,0x89,0xF1,0xBA);D(c,aiConfig);CallAbs(c,aiCode);
                B(c,0x01,0x56,0x28); // target offset only; rider still uses native steering
            }
            foreach (int offset in new int[] { 0x20,0xEC,0xF0,0xF4,0x108,0x10C,0x110,0x184,0x188,0x18C }) {
                B(c,0xC7,0x86); D(c,offset); D(c,0);
            }
            B(c,0x6A,0,0x89,0xF1,0xBA); D(c,distance); CallAbs(c,image + 0x4DEF0);
            // Resolve junctions using the runner's intended line, then compensate
            // for the native left-side mounting offset in the final chase target.
            B(c,0x81,0x46,0x28);D(c,0x1770);
            // Refresh previous/render positions so interpolation cannot sweep across riders.
            for (int i = 0; i < 5; i++) {
                B(c,0x8B,0x46,(byte)(0x1C + i*4),0x89,0x46,(byte)(0x30 + i*4));
            }
            B(c,0x89,0xF1); CallAbs(c,image + 0x4A0B0);
        }
        c.Label("recovery");
        if (recovery != 0) {
            if (recovery != 3) {
                B(c,0x3B,0x35); D(c,image + RvaPlayer);
                Jcc(c,recovery == 1 ? (byte)0x85 : (byte)0x84,"original");
            }
            B(c,0x61,0x9D); JmpAbs(c,image + (site == 1 ? 0x17D22 : 0x19FF5));
        }
        c.Label("original"); B(c,0x61,0x9D); B(c,RaceOriginals[site]);
        JmpAbs(c,image + RaceSites[site] + RaceOriginals[site].Length);
        byte[] result = c.Finish();
        if (result.Length > 0x800) throw new InvalidOperationException("长跑代码超出分配区。");
        return result;
    }

    // The original unload path silently remounts racers. Retain state 2 for runners
    // whose recovery is disabled, use walking speed in virtual simulation, then rebuild
    // the rider's streamed coordinates and animation on re-entry.
    internal static byte[] BuildRaceWorldCode(long image,long cave,int site,int scope,int recovery,decimal playerSpeed,decimal aiSpeed,long aiCode=0,long aiConfig=0)
    {
        ValidateRace(scope,recovery,96); ValidateWalkSpeed(playerSpeed); ValidateWalkSpeed(aiSpeed);
        if(site<0||site>=RaceWorldSites.Length)throw new ArgumentOutOfRangeException("site");
        CodeBuffer c=new CodeBuffer(cave); B(c,0x9C,0x60);
        if(site==0)B(c,0x89,0xFE); // unload uses EDI=bike, the others ESI=bike
        RaceScope(c,image,scope,"original");
        if(recovery==0) Jmp(c,"original");
        else if(recovery!=3) {
            B(c,0x3B,0x35);D(c,image+RvaPlayer);Jcc(c,recovery==1?(byte)0x85:(byte)0x84,"original");
        }
        B(c,0x83,0xBE);D(c,0x2F4);B(c,0);Jcc(c,0x84,"original");
        B(c,0x83,0xBE);D(c,0x2F4);B(c,3);Jcc(c,0x87,"original");
        if(site==0) {
            B(c,0xC7,0x86);D(c,0x2F4);D(c,2);
            B(c,0xC7,0x86);D(c,0x318);D(c,0); // remove the original simulated recovery delay
            // The skipped remount also disables the rider's collision flags
            // (native 0x12CB9). The unload tail clears both road pointers, so
            // retaining these bits would crash the collision pass at 0x495F9.
            B(c,0x8B,0x86);D(c,0x2FC);
            B(c,0x80,0xA0);D(c,0xC0);B(c,0xFC);
            B(c,0x61,0x9D);JmpAbs(c,image+0x1641C);
        } else if(site==1) {
            B(c,0x3B,0x35);D(c,image+RvaPlayer);Jcc(c,0x84,"player_speed");
            if(aiCode!=0) { B(c,0x89,0xF1,0xBA);D(c,aiConfig);CallAbs(c,aiCode); }
            else { B(c,0xB8);D(c,(int)(aiSpeed*1200)); }
            Jmp(c,"speed");c.Label("player_speed");
            B(c,0xB8);D(c,(int)(playerSpeed*1200));c.Label("speed");
            B(c,0x89,0x04,0x24); // replace saved EDI; preserve every other register and flag
            B(c,0x61,0x9D);JmpAbs(c,image+0x15C22); // skip riding rubber-band multipliers
        } else {
            B(c,0xC7,0x86);D(c,0x2F4);D(c,0);
            B(c,0x89,0xF1);CallAbs(c,image+0x5F0B);
            B(c,0xC7,0x86);D(c,0x2F4);D(c,2);
            B(c,0x8B,0xBE);D(c,0x2FC);
            // Native mounted-rider sync (0x17510) also copies the hidden bike's
            // pitch/yaw/bank. Running motion uses +0x128 independently, so the
            // copied bank can leave a straight-running rider visibly tilted.
            // Reset only the re-entering runner's pose, then refresh render fields.
            B(c,0xC7,0x47,0x58);D(c,0);
            B(c,0xC7,0x47,0x60);D(c,0);
            B(c,0x8B,0x87);D(c,0x128);B(c,0x89,0x47,0x5C);
            B(c,0x89,0xF9);CallAbs(c,image+0x4A0B0);
            B(c,0x80,0x8F);D(c,0xC0);B(c,3);
            B(c,0x8D,0x4F,0x70,0xB2,0x26);CallAbs(c,image+0x428C);
            B(c,0x8D,0x4F,0x70,0xB2,0x26);CallAbs(c,image+0x5FB0);
            B(c,0x61,0x9D);JmpAbs(c,image+0x16046);
        }
        c.Label("original");B(c,0x61,0x9D);
        if(site==0)CallAbs(c,image+0x26E9);
        else if(site==1)B(c,RaceWorldOriginals[1]);
        else CallAbs(c,image+0x5F0B);
        JmpAbs(c,image+RaceWorldSites[site]+RaceWorldOriginals[site].Length);
        return c.Finish();
    }
}
