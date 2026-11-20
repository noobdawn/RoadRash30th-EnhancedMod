using System;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Forms;

internal sealed partial class LauncherForm
{
    private readonly CheckBox infiniteDurability = new CheckBox();
    private readonly NumericUpDown playerWalkSpeed = new NumericUpDown();
    private readonly NumericUpDown aiWalkSpeed = new NumericUpDown();
    private readonly CheckBox onFootProtection = new CheckBox();
    private readonly PresetComboBox onFootScope = new PresetComboBox();
    private readonly CheckBox hideFallenVehicles = new CheckBox();

    private Control BuildDurabilityGroup()
    {
        GroupBox group = Group("车辆耐久"); TableLayoutPanel body = PageLayout(2);
        infiniteDurability.Text = "车辆无限耐久（仅玩家）";
        infiniteDurability.AutoSize = true; infiniteDurability.Checked = true;
        body.Controls.Add(infiniteDurability, 0, 0);
        body.Controls.Add(Paragraph("车辆预设默认开启，可单独关闭。保护车辆耐久；人物受伤与翻车仍由各自选项控制。", Muted), 0, 1);
        group.Controls.Add(body); return group;
    }

    private Control BuildCharacterGroup()
    {
        GroupBox group = Group("下车后的人物移动与防护"); TableLayoutPanel body = PageLayout(5);
        FlowLayoutPanel speeds = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        NumericUpDown[] numbers = { playerWalkSpeed, aiWalkSpeed };
        string[] labels = { "玩家移动速度", "AI 移动速度" };
        for (int i = 0; i < numbers.Length; i++) {
            NumericUpDown n = numbers[i]; n.Minimum = 0.1m; n.Maximum = RuntimePatch.MaximumWalkSpeed;
            n.DecimalPlaces = 1; n.Increment = 0.1m; n.Value = 1m; n.Width = 115;
            speeds.Controls.Add(new Label { Text = labels[i], AutoSize = true, Padding = new Padding(8) });
            speeds.Controls.Add(n);
            speeds.Controls.Add(new Label { Text = "倍", AutoSize = true, Padding = new Padding(4,8,12,0) });
        }
        onFootProtection.Text = "下车后不会被撞倒"; onFootProtection.AutoSize = true;
        onFootScope.DropDownStyle = ComboBoxStyle.DropDownList;
        onFootScope.Items.AddRange(new string[] { "仅玩家", "所有人（玩家与 AI）" });
        onFootScope.SelectedIndex = 0; onFootScope.Width = 220;
        body.Controls.Add(speeds, 0, 0); body.Controls.Add(onFootProtection, 0, 1);
        body.Controls.Add(onFootScope, 0, 2);
        hideFallenVehicles.Text = "倒地后隐藏车辆"; hideFallenVehicles.AutoSize = true;
        body.Controls.Add(hideFallenVehicles, 0, 3);
        body.Controls.Add(Paragraph("跑速可直接输入，最低 0.1 倍，1 倍为原版。隐藏车辆对玩家与 AI 生效：摔车后隐藏车体与车影，重新骑上后恢复；长跑预设默认开启。防撞倒在人物起身后生效。", Muted), 0, 4);
        group.Controls.Add(body); return group;
    }

    private static decimal ReadWalkSpeed(Dictionary<string,string> settings, string key)
    {
        string text; decimal value;
        if (!settings.TryGetValue(key, out text) || !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return 1m;
        return Math.Max(0.1m, Math.Min(RuntimePatch.MaximumWalkSpeed, decimal.Round(value, 1)));
    }
}

internal static partial class RuntimePatch
{
    // Largest tenth whose native speed (factor * 1200) fits the signed 32-bit field.
    internal const decimal MaximumWalkSpeed = 1789569.7m;
    // Steam build 26105967. These sites contain no absolute addresses to relocate.
    internal static readonly int[] CharacterSites = { 0x137CC, 0x14792, 0x17D7F, 0x17ED7, 0x1A12C, 0x13F8A };
    internal static readonly byte[][] CharacterOriginals = {
        new byte[] { 0x8B,0x86,0xF0,2,0,0,0x2B,0x45,0xE0 }, // bike damage, ESI=bike
        new byte[] { 0x8B,0x86,0xF0,2,0,0,0x8D,0x96,0xF0,2,0,0,0x2B,0xC3 }, // wear, ESI=bike
        new byte[] { 0xBA,0xB4,0,0,0 }, // off-road abandoned-bike wreck
        new byte[] { 0xC7,0x87,0xEC,0,0,0,0xB0,4,0,0 }, // local/autopilot run, EDI=rider
        new byte[] { 0xC7,0x83,0xEC,0,0,0,0xB0,4,0,0 }, // AI/network run, EBX=rider
        new byte[] { 0x8B,0xB7,0xA0,1,0,0 } // rider collision callback, EDI=rider
    };

    private static void ValidateWalkSpeed(decimal factor)
    {
        if (factor < 0.1m || factor > MaximumWalkSpeed || factor * 10 != decimal.Truncate(factor * 10)) throw new ArgumentOutOfRangeException("factor");
    }

    internal const int HideVehicleSite = 0x4AD4A;
    internal static readonly byte[] HideVehicleOriginal = { 0x8B,0x87,0xDC,0,0,0 };

    internal static void ApplyHideFallenVehicles(IntPtr process, long image, Action<string> log)
    {
        RequireMotionSignature(process, image + HideVehicleSite, HideVehicleOriginal);
        IntPtr block = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)0x200, MemCommitReserve, PageExecuteReadWrite);
        if (block == IntPtr.Zero) throw new InvalidOperationException("无法分配倒地车辆隐藏代码区。");
        long cave = block.ToInt64();
        Write(process, cave, BuildHideFallenVehiclesCode(image, cave));
        Write(process, image + HideVehicleSite, Jmp(image + HideVehicleSite, cave, HideVehicleOriginal.Length));
        log("倒地后隐藏车辆已写入：玩家与 AI 下车时隐藏车体与车影，骑上后恢复。");
    }

    internal static byte[] BuildHideFallenVehiclesCode(long image, long cave)
    {
        CodeBuffer c = new CodeBuffer(cave); B(c,0x9C);
        // Filter the vehicle callback before dispatch, leaving rider/traffic drawing
        // and simulation untouched. This precedes the screen-capture/debug hook.
        B(c,0x83,0xBF); D(c,0xC4); B(c,4); Jcc(c,0x85,"original");
        B(c,0x83,0xBF); D(c,0x2F4); B(c,1); Jcc(c,0x82,"original");
        B(c,0x83,0xBF); D(c,0x2F4); B(c,3); Jcc(c,0x87,"original");
        B(c,0x9D); JmpAbs(c,image + 0x4AD5D);
        c.Label("original"); B(c,0x9D); B(c,HideVehicleOriginal);
        JmpAbs(c,image + HideVehicleSite + HideVehicleOriginal.Length);
        return c.Finish();
    }

    internal static void ApplyCharacter(IntPtr process, long image, bool durability, decimal playerSpeed, decimal aiSpeed, bool protection, bool everyone, Action<string> log,long aiCode=0,long aiConfig=0)
    {
        ValidateWalkSpeed(playerSpeed); ValidateWalkSpeed(aiSpeed);
        bool speed = playerSpeed != 1 || aiSpeed != 1 || aiCode != 0;
        bool[] enabled = { durability, durability, durability, speed, speed, protection };
        if (!durability && !speed && !protection) return;
        // Preflight all requested entries, then publish complete code before installing jumps.
        for (int i = 0; i < enabled.Length; i++) if (enabled[i]) {
            byte[] actual = Read(process, image + CharacterSites[i], CharacterOriginals[i].Length);
            for (int j = 0; j < actual.Length; j++) if (actual[j] != CharacterOriginals[i][j])
                throw new InvalidOperationException("耐久/人物补丁入口签名不匹配：RVA 0x" + CharacterSites[i].ToString("X"));
        }
        IntPtr block = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)0x1000, MemCommitReserve, PageExecuteReadWrite);
        if (block == IntPtr.Zero) throw new InvalidOperationException("无法分配耐久/人物补丁代码区。");
        long cave = block.ToInt64();
        for (int i = 0; i < enabled.Length; i++) if (enabled[i]) {
            long entry = cave + i * 0x200;
            byte[] code = i < 3 ? BuildDurabilityCode(image, entry, i) :
                (i < 5 ? BuildWalkSpeedCode(image, entry, i, playerSpeed, aiSpeed,aiCode,aiConfig) : BuildOnFootCode(image, entry, everyone));
            if (code.Length > 0x200) throw new InvalidOperationException("耐久/人物代码超出分配区。");
            Write(process, entry, code);
        }
        for (int i = 0; i < enabled.Length; i++) if (enabled[i])
            Write(process, image + CharacterSites[i], Jmp(image + CharacterSites[i], cave + i * 0x200, CharacterOriginals[i].Length));
        log("耐久/人物补丁已写入：玩家无限耐久=" + durability + "；玩家跑速=" + playerSpeed + "；AI 跑速=" + aiSpeed + "；下车防撞倒=" + (protection ? (everyone ? "所有人" : "仅玩家") : "关闭"));
    }

    internal static byte[] BuildDurabilityCode(long image, long cave, int site)
    {
        if (site < 0 || site > 2) throw new ArgumentOutOfRangeException("site");
        CodeBuffer c = new CodeBuffer(cave);
        B(c,0x9C,0x3B,0x35); D(c,image + RvaPlayer); Jcc(c,0x85,"original");
        B(c,0x9D);
        if (site == 2) JmpAbs(c,image + 0x17D9F); // skip forced wreck notification as well as zeroing durability
        else {
            B(c,0x8B,0x86); D(c,0x2EC); // maximum durability, not rider health
            if (site == 1) { B(c,0x8D,0x96); D(c,0x2F0); }
            JmpAbs(c,image + CharacterSites[site] + CharacterOriginals[site].Length);
        }
        c.Label("original"); B(c,0x9D); B(c,CharacterOriginals[site]);
        JmpAbs(c,image + CharacterSites[site] + CharacterOriginals[site].Length);
        return c.Finish();
    }

    internal static byte[] BuildWalkSpeedCode(long image, long cave, int site, decimal playerSpeed, decimal aiSpeed,long aiCode=0,long aiConfig=0)
    {
        if (site != 3 && site != 4) throw new ArgumentOutOfRangeException("site");
        ValidateWalkSpeed(playerSpeed); ValidateWalkSpeed(aiSpeed);
        CodeBuffer c = new CodeBuffer(cave);
        B(c,CharacterOriginals[site]); // freshly assigned each tick: never multiply a previous speed
        B(c,0x9C,0x60,0x8B,(byte)(site == 3 ? 0xCF : 0xCB)); // ECX=rider
        B(c,0x83,0xB9); D(c,0xC4); B(c,8); Jcc(c,0x85,"done");
        B(c,0x8B,0x81); D(c,0x1C4); B(c,0x85,0xC0); Jcc(c,0x84,"done");
        B(c,0x83,0xB8); D(c,0xC4); B(c,4); Jcc(c,0x85,"done");
        B(c,0x83,0xB8); D(c,0x2F4); B(c,2); Jcc(c,0x85,"done");
        B(c,0x3B,0x05); D(c,image + RvaPlayer); Jcc(c,0x84,"player");
        if(aiCode!=0) {
            B(c,0x51,0x89,0xC1,0xBA);D(c,aiConfig);CallAbs(c,aiCode);B(c,0x59);
            B(c,0x89,0x81);D(c,0xEC);
        } else { B(c,0xC7,0x81); D(c,0xEC); D(c,(int)(1200 * aiSpeed)); }
        Jmp(c,"done");
        c.Label("player"); B(c,0xC7,0x81); D(c,0xEC); D(c,(int)(1200 * playerSpeed));
        c.Label("done"); B(c,0x61,0x9D);
        JmpAbs(c,image + CharacterSites[site] + CharacterOriginals[site].Length);
        return c.Finish();
    }

    internal static byte[] BuildOnFootCode(long image, long cave, bool everyone)
    {
        CodeBuffer c = new CodeBuffer(cave);
        B(c,0x9C,0x60);
        B(c,0x83,0xBF); D(c,0xC4); B(c,8); Jcc(c,0x85,"original");
        B(c,0x8B,0x87); D(c,0x1C4); B(c,0x85,0xC0); Jcc(c,0x84,"original");
        B(c,0x83,0xB8); D(c,0xC4); B(c,4); Jcc(c,0x85,"original");
        B(c,0x83,0xB8); D(c,0x2F4); B(c,2); Jcc(c,0x85,"original");
        if (!everyone) { B(c,0x3B,0x05); D(c,image + RvaPlayer); Jcc(c,0x85,"original"); }
        // Keep the original callback tail: clears pending impulses and updates physics.
        // Skip impact animation, added height and state 2 -> 1 (knocked down again).
        B(c,0x61,0x9D); JmpAbs(c,image + 0x141D2);
        c.Label("original"); B(c,0x61,0x9D); B(c,CharacterOriginals[5]);
        JmpAbs(c,image + CharacterSites[5] + CharacterOriginals[5].Length);
        return c.Finish();
    }
}
