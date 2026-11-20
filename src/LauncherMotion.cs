using System;
using System.Windows.Forms;

internal sealed partial class LauncherForm
{
    private readonly CheckBox rockSolid = new CheckBox();
    private readonly CheckBox exaggeratedLaunch = new CheckBox();
    private readonly PresetComboBox launchScope = new PresetComboBox();
    private readonly NumericUpDown launchMultiplier = new NumericUpDown();

    private Control BuildMotionGroup()
    {
        GroupBox group = Group("受击与翻车");
        TableLayoutPanel body = PageLayout(4);
        rockSolid.Text = "稳如泰山（抵御侧踹和近战的推力）";
        rockSolid.AutoSize = true;
        exaggeratedLaunch.Text = "夸张击飞（翻车时增强骑手与赛车的抛飞）";
        exaggeratedLaunch.AutoSize = true;
        launchScope.DropDownStyle = ComboBoxStyle.DropDownList;
        launchScope.Items.AddRange(new string[] { "仅对手", "所有人（包含玩家）" });
        launchScope.SelectedIndex = 0; launchScope.Width = 200;
        launchMultiplier.Minimum = 1; launchMultiplier.Maximum = 8;
        launchMultiplier.DecimalPlaces = 1; launchMultiplier.Increment = 0.1m;
        launchMultiplier.Value = 1.5m; launchMultiplier.Width = 70;
        FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        row.Controls.Add(launchScope); row.Controls.Add(new Label { Text = "击飞倍率", AutoSize = true, Padding = new Padding(8) });
        row.Controls.Add(launchMultiplier);
        body.Controls.Add(rockSolid, 0, 0); body.Controls.Add(exaggeratedLaunch, 0, 1);
        body.Controls.Add(row, 0, 2);
        body.Controls.Add(Paragraph("稳如泰山保留受伤，不代替霸体。击飞仅在翻车时触发，1 倍保持原效果；霸体阻止的翻车不会触发击飞。高倍率可能使车手远离赛道。", Muted), 0, 3);
        group.Controls.Add(body); return group;
    }
}

internal static partial class RuntimePatch
{
    internal static readonly int[] SolidSites = { 0x19115, 0x1911F, 0x19140, 0x19150, 0x19174 };
    internal static readonly byte[][] SolidOriginals = {
        new byte[] { 0x81,0x83,0xEC,0,0,0,0xD4,0xFE,0xFF,0xFF },
        new byte[] { 0x81,0x87,0xEC,0,0,0,0x2C,1,0,0 },
        new byte[] { 0x01,0x83,0x88,2,0,0 },
        new byte[] { 0x01,0x87,0x88,2,0,0 },
        new byte[] { 0x01,0x83,0x88,2,0,0 }
    };
    internal static readonly int[] LaunchSites = { 0x13DB8, 0x145AA };
    internal static readonly byte[] LaunchOriginal = { 0x81,0x47,0x20,0,0x4B,0,0 };

    private static void RequireMotionSignature(IntPtr process, long address, byte[] expected)
    {
        byte[] actual = Read(process, address, expected.Length);
        for (int i = 0; i < actual.Length; i++)
            if (actual[i] != expected[i]) throw new InvalidOperationException("受击/翻车入口签名不匹配：0x" + address.ToString("X"));
    }

    internal static void ApplyMotion(IntPtr process, long image, bool solid, bool launch, bool everyone, decimal factor, Action<string> log)
    {
        if (!solid && !launch) return;
        if (factor < 1 || factor > 8 || factor * 10 != decimal.Truncate(factor * 10)) throw new ArgumentOutOfRangeException("factor");
        // Validate every requested site before modifying any of them.
        if (solid) for (int i = 0; i < SolidSites.Length; i++) RequireMotionSignature(process, image + SolidSites[i], SolidOriginals[i]);
        if (launch) foreach (int rva in LaunchSites) RequireMotionSignature(process, image + rva, LaunchOriginal);
        IntPtr block = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)0x1000, MemCommitReserve, PageExecuteReadWrite);
        if (block == IntPtr.Zero) throw new InvalidOperationException("无法分配受击/翻车补丁代码区。");
        long cave = block.ToInt64();
        if (solid) for (int i = 0; i < SolidSites.Length; i++) {
            long entry = cave + i * 0x100;
            Write(process, entry, BuildSolidCode(image, entry, i));
            Write(process, image + SolidSites[i], Jmp(image + SolidSites[i], entry, SolidOriginals[i].Length));
        }
        if (launch) for (int i = 0; i < LaunchSites.Length; i++) {
            long entry = cave + 0x600 + i * 0x400;
            byte[] code = BuildLaunchCode(image, entry, image + LaunchSites[i] + LaunchOriginal.Length, everyone, factor);
            if (code.Length > 0x400) throw new InvalidOperationException("击飞代码超出分配区。");
            Write(process, entry, code);
            Write(process, image + LaunchSites[i], Jmp(image + LaunchSites[i], entry, LaunchOriginal.Length));
        }
        log("受击/翻车补丁已写入：稳如泰山=" + solid + "；夸张击飞=" + launch + "；范围=" + (everyone ? "所有人" : "仅对手") + "；倍率=" + factor);
    }

    internal static byte[] BuildSolidCode(long image, long cave, int site)
    {
        CodeBuffer c = new CodeBuffer(cave);
        B(c, 0x9C); // preserve flags used by the caller's direction logic
        B(c, 0x3B, (byte)(SolidOriginals[site][1] == 0x83 ? 0x1D : 0x3D)); D(c, image + RvaPlayer);
        Jcc(c, 0x84, "protected");
        B(c, 0x9D); B(c, SolidOriginals[site]);
        JmpAbs(c, image + SolidSites[site] + SolidOriginals[site].Length);
        c.Label("protected"); B(c, 0x9D);
        JmpAbs(c, image + SolidSites[site] + SolidOriginals[site].Length);
        return c.Finish();
    }

    internal static byte[] BuildLaunchCode(long image, long cave, long back, bool everyone, decimal factor)
    {
        if (factor < 1 || factor > 8 || factor * 10 != decimal.Truncate(factor * 10)) throw new ArgumentOutOfRangeException("factor");
        CodeBuffer c = new CodeBuffer(cave);
        B(c, LaunchOriginal); // replay rider separation height, including original flags
        if (factor == 1) { JmpAbs(c, back); return c.Finish(); }
        B(c, 0x9C, 0x60);
        B(c, 0x85,0xF6); Jcc(c,0x84,"done"); B(c,0x85,0xFF); Jcc(c,0x84,"done");
        B(c, 0x83,0xBE); D(c,0xC4); B(c,4); Jcc(c,0x85,"done");
        B(c, 0x83,0xBF); D(c,0xC4); B(c,8); Jcc(c,0x85,"done");
        if (!everyone) {
            B(c,0xA1); D(c,image + RvaPlayer); B(c,0x85,0xC0); Jcc(c,0x84,"done");
            B(c,0x39,0xC6); Jcc(c,0x84,"done");
        }
        // ESI=bike, EDI=detached rider. Both paths have finished velocity initialization.
        for (int actor = 0; actor < 2; actor++) {
            B(c,0x89,(byte)(actor == 0 ? 0xF1 : 0xF9)); // ecx = actor
            string tag = actor.ToString();
            B(c,0x8B,0x81); D(c,0xF4); // positive Z is upward
            EmitClamp(c,1800,4000,"z" + tag);
            B(c,0x69,0xC0); D(c,(int)(factor * 10)); B(c,0x99,0xBB); D(c,10); B(c,0xF7,0xFB); B(c,0x89,0x81); D(c,0xF4);
            B(c,0x8B,0x81); D(c,0xF0);
            B(c,0x85,0xC0); Jcc(c,0x85,"side" + tag);
            // A stationary fall also gets a modest sideways arc; retain existing direction otherwise.
            B(c,0xB8); D(c,actor == 0 ? 400 : -400);
            c.Label("side" + tag); EmitClamp(c,-2000,2000,"x" + tag);
            B(c,0x69,0xC0); D(c,(int)(factor * 10)); B(c,0x99,0xBB); D(c,10); B(c,0xF7,0xFB); B(c,0x89,0x81); D(c,0xF0);
        }
        c.Label("done"); B(c,0x61,0x9D); JmpAbs(c,back);
        return c.Finish();
    }

    private static void EmitClamp(CodeBuffer c, int low, int high, string tag)
    {
        B(c,0x3D); D(c,low); Jcc(c,0x8D,tag + "low"); B(c,0xB8); D(c,low);
        c.Label(tag + "low"); B(c,0x3D); D(c,high); Jcc(c,0x8E,tag + "high"); B(c,0xB8); D(c,high);
        c.Label(tag + "high");
    }
}
