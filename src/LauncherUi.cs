using System;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// Presets and detailed controls share one configuration store.
internal sealed partial class LauncherForm
{
    private static readonly Color Ink = Color.FromArgb(28, 42, 57);
    private static readonly Color Muted = Color.FromArgb(89, 105, 120);
    private static readonly Color Canvas = Color.FromArgb(242, 245, 248);
    private static readonly Color Accent = Color.FromArgb(0, 111, 119);
    private readonly TabControl modes = new TabControl();
    private readonly TabControl advancedTabs = new TabControl();
    private readonly PresetComboBox quickScale = new PresetComboBox();
    private readonly PresetComboBox quickVehicle = new PresetComboBox();
    private readonly PresetComboBox quickTraffic = new PresetComboBox();
    private readonly PresetComboBox quickDisplay = new PresetComboBox();
    private readonly Label quickSummary = new Label();
    private readonly Label vehicleDetails = new Label();
    private readonly Label changeStatus = new Label();
    private readonly CheckBox carStability = new CheckBox();
    private readonly CheckBox autoCarCollision = new CheckBox();
    private bool syncingPresets;
    private bool trackingChanges;
    private TableLayoutPanel shellLayout;
    private bool logExpanded;
    private static readonly int[] ScaleValues = { 4, 15, 100, 500, 10000 };
    private static readonly int[] TrafficValues = { 0, 1, 4, 10 };
    private static readonly int[] PoliceValues = { 0, 1, 2, 4 };

    private void BuildModernInterface()
    {
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(1080, 800);
        MinimumSize = new Size(960, 700);
        shellLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18) };
        shellLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(shellLayout);

        Panel header = new Panel { Dock = DockStyle.Fill, BackColor = Ink, Padding = new Padding(18, 10, 18, 10), Margin = new Padding(0, 0, 0, 12) };
        Label title = new Label { Text = "暴力摩托  /  增强启动器", AutoSize = true, ForeColor = Color.White, Font = new Font(Font.FontFamily, 19, FontStyle.Bold), Location = new Point(18, 10) };
        Label subtitle = new Label { Text = "ROAD RASH     30TH ANNIVERSARY     ·     选择玩法，即刻出发", AutoSize = true, ForeColor = Color.FromArgb(183, 204, 216), Location = new Point(21, 48) };
        header.Controls.Add(title); header.Controls.Add(subtitle);
        shellLayout.Controls.Add(header, 0, 0);

        ConfigureTabs(modes, 126);
        ConfigureTabs(advancedTabs, 122);
        // Construct all detailed controls before wiring preset changes.
        advancedTabs.TabPages.Add(BuildVehicleTab());
        advancedTabs.TabPages.Add(BuildMapTab());
        advancedTabs.TabPages.Add(BuildPlayerTab());
        advancedTabs.TabPages.Add(BuildAiTab());
        advancedTabs.TabPages.Add(BuildDisplayTab());
        advancedTabs.TabPages.Add(BuildMaintenanceTab());
        TabPage advanced = Tab("高级");
        advanced.Padding = new Padding(4, 10, 4, 0);
        advanced.AutoScroll = false;
        advanced.Controls.Add(advancedTabs);
        modes.TabPages.Add(BuildQuickTab());
        modes.TabPages.Add(advanced);
        modes.TabPages.Add(BuildFixesTab());
        shellLayout.Controls.Add(modes, 0, 1);

        TableLayoutPanel stateRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        changeStatus.Text = "设置将在下一次启动时生效";
        changeStatus.Dock = DockStyle.Fill; changeStatus.TextAlign = ContentAlignment.MiddleLeft;
        changeStatus.ForeColor = Muted;
        Button toggleLog = new Button { Text = "运行日志 ▾", AutoSize = true, Anchor = AnchorStyles.Right };
        toggleLog.Click += delegate {
            logExpanded = !logExpanded;
            shellLayout.RowStyles[3].Height = logExpanded ? 130 : 0;
            log.Visible = logExpanded;
            toggleLog.Text = logExpanded ? "收起日志 ▴" : "运行日志 ▾";
        };
        stateRow.Controls.Add(changeStatus, 0, 0); stateRow.Controls.Add(toggleLog, 1, 0);
        shellLayout.Controls.Add(stateRow, 0, 2);
        log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
        log.Dock = DockStyle.Fill; log.BackColor = Color.White; log.Visible = false;
        shellLayout.Controls.Add(log, 0, 3);
        shellLayout.Controls.Add(BuildButtons(), 0, 4);
        StyleControls(this);
        applyLaunch.BackColor = Accent; applyLaunch.ForeColor = Color.White;
        applyLaunch.FlatAppearance.BorderSize = 0;
        AcceptButton = applyLaunch;

        foreach (FieldInfo field in typeof(LauncherForm).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            Control control = field.GetValue(this) as Control;
            if (control != null) { control.Name = field.Name; control.AccessibleName = control.Text; }
        }
        WireSettingChanges(advancedTabs);
        WireSettingChanges(modes.TabPages[2]);
        SyncQuickSettings();
        trackingChanges = true;
    }

    private static void ConfigureTabs(TabControl tabs, int width)
    {
        tabs.Dock = DockStyle.Fill;
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(width, 40);
        tabs.DrawItem += delegate(object sender, DrawItemEventArgs e) {
            bool active = tabs.SelectedIndex == e.Index;
            using (Brush b = new SolidBrush(active ? Color.White : Canvas)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds,
                active ? Accent : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (active) using (Pen pen = new Pen(Accent, 3))
                e.Graphics.DrawLine(pen, e.Bounds.Left + 16, e.Bounds.Bottom - 2, e.Bounds.Right - 16, e.Bounds.Bottom - 2);
        };
    }

    private static void StyleControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            Button b = c as Button;
            if (b != null) {
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(207, 216, 224);
                b.BackColor = Color.White; b.ForeColor = Ink; b.Padding = new Padding(12, 6, 12, 6);
                b.Cursor = Cursors.Hand; b.MinimumSize = new Size(0, 34);
            }
            if (c is CheckBox) { c.Margin = new Padding(3, 6, 3, 6); c.Cursor = Cursors.Hand; }
            if (c is ComboBox || c is NumericUpDown || c is TextBox) c.Margin = new Padding(3, 6, 3, 6);
            StyleControls(c);
        }
    }

    private TabPage BuildQuickTab()
    {
        TabPage page = Tab("一键设置");
        TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Padding = new Padding(8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 59));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));
        Label intro = Paragraph("选择预设会同步修改高级参数；切到高级后，可继续逐项微调。", Muted);
        intro.Margin = new Padding(4, 6, 4, 16);
        grid.Controls.Add(intro, 0, 0); grid.SetColumnSpan(intro, 2);
        TableLayoutPanel cards = PageLayout(4); cards.Padding = Padding.Empty;
        cards.Controls.Add(PresetCard("01  比赛规模", quickScale, new string[] { "自定义", "孤独 · 4 人", "正常 · 15 人", "大型 · 100 人", "超大型 · 500 人", "最大型 · 10000 人" },
            "人数包含玩家。最低 4 人；10000 人为实验规模，加载和运行可能较慢。", ApplyScalePreset), 0, 0);
        cards.Controls.Add(PresetCard("02  比赛设置", quickVehicle, new string[] { "自定义", "正常", "车辆", "泥头车", "长跑" },
            "车辆与泥头车可在高级中选择外观。长跑默认所有人开局翻车、隐藏车辆、持续追车，并关闭超时恢复上车。", ApplyVehiclePreset), 0, 1);
        cards.Controls.Add(PresetCard("03  道路交通", quickTraffic, new string[] { "自定义", "清静 · 无交通车 / 无警车", "正常 · 汽车 ×1 / 警车 ×1", "热闹 · 汽车 ×4 / 警车 ×2", "拥挤 · 汽车 ×10 / 警车 ×4" },
            "只调整交通与警车数量，保留你设置的道路宽度。", ApplyTrafficPreset), 0, 2);
        cards.Controls.Add(PresetCard("04  画面表现", quickDisplay, new string[] { "自定义", "经典 · 原版画面 / 60 帧", "高清 · 宽屏 / 60 帧", "流畅 · 宽屏 / 120 帧" },
            "更高帧率及汉化、过场动画选项可在高级中调整。", ApplyDisplayPreset), 0, 3);
        grid.Controls.Add(cards, 0, 1);
        GroupBox summary = Group("本次出发"); summary.Dock = DockStyle.Top;
        TableLayoutPanel content = PageLayout(4); content.Padding = new Padding(2);
        quickSummary.AutoSize = true; quickSummary.Dock = DockStyle.Top;
        quickSummary.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        quickSummary.Margin = new Padding(3, 8, 3, 16);
        vehicleDetails.AutoSize = true; vehicleDetails.Dock = DockStyle.Top;
        vehicleDetails.ForeColor = Muted; vehicleDetails.Margin = new Padding(3, 0, 3, 16);
        content.Controls.Add(quickSummary, 0, 0); content.Controls.Add(vehicleDetails, 0, 1);
        Button details = new Button { Text = "微调车辆与碰撞 →", AutoSize = true, Anchor = AnchorStyles.Left };
        details.Click += delegate { modes.SelectedIndex = 1; advancedTabs.SelectedIndex = 0; };
        content.Controls.Add(details, 0, 2);
        content.Controls.Add(Paragraph("预设按项目独立生效。高级参数与预设不一致时，对应项目会显示“自定义”。", Muted), 0, 3);
        summary.Controls.Add(content); grid.Controls.Add(summary, 1, 1);
        page.Controls.Add(grid);
        return page;
    }

    private Control PresetCard(string title, ComboBox combo, string[] items, string description, Action<int> apply)
    {
        GroupBox card = Group(title);
        card.Padding = new Padding(14, 14, 14, 2);
        TableLayoutPanel body = PageLayout(2); body.Padding = new Padding(0);
        combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.Dock = DockStyle.Top;
        combo.Items.AddRange(items); combo.SelectedIndex = 0;
        combo.SelectedIndexChanged += delegate {
            if (syncingPresets) return;
            if (combo.SelectedIndex <= 0) {
                modes.SelectedIndex = 1;
                advancedTabs.SelectedIndex = combo == quickVehicle ? (longRun.Checked ? 1 : 0) : (combo == quickDisplay ? 4 : 1);
                SyncQuickSettings();
                return;
            }
            syncingPresets = true;
            try { apply(combo.SelectedIndex - 1); }
            finally { syncingPresets = false; }
            SettingsChanged();
        };
        body.Controls.Add(combo, 0, 0);
        Label hint = Paragraph(description, Muted); hint.Margin = new Padding(3, 3, 3, 3);
        body.Controls.Add(hint, 0, 1);
        card.Controls.Add(body);
        return card;
    }

    private static Label Paragraph(string text, Color color)
    {
        Label label = new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = color, Margin = new Padding(3, 7, 3, 8) };
        label.ParentChanged += delegate {
            if (label.Parent != null) {
                Action fit = delegate { label.MaximumSize = new Size(Math.Max(100, label.Parent.ClientSize.Width - label.Parent.Padding.Horizontal - 12), 0); };
                label.Parent.SizeChanged += delegate { fit(); }; fit();
            }
        };
        return label;
    }

    private TabPage BuildVehicleTab()
    {
        TabPage page = Tab("车辆与碰撞"); TableLayoutPanel body = PageLayout(5);
        body.Controls.Add(BuildCarShellGroup(), 0, 0);
        body.Controls.Add(BuildDurabilityGroup(), 0, 1);
        body.Controls.Add(BuildRamGroup(), 0, 2);
        body.Controls.Add(BuildMotionGroup(), 0, 3);
        body.Controls.Add(BuildLoadoutGroup(), 0, 4);
        page.Controls.Add(body); return page;
    }

    private TabPage BuildMaintenanceTab()
    {
        TabPage page = Tab("工具与调试"); TableLayoutPanel body = PageLayout(4);
        GroupBox path = Group("游戏目录"); path.Controls.Add(BuildPathRow()); body.Controls.Add(path, 0, 0);
        GroupBox diagnostic = Group("绘制调试");
        FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown };
        carDebugCopies.Text = "调试：把玩家摩托 sprite 横向复制 5 份"; carDebugCopies.AutoSize = true;
        row.Controls.Add(carDebugCopies); row.Controls.Add(Paragraph("调试复制开启时，会暂停汽车外观与透明叠加层。", Muted));
        diagnostic.Controls.Add(row); body.Controls.Add(diagnostic, 0, 1);
        GroupBox legacy = Group("透明图片叠加（旧方案）");
        TableLayoutPanel legacyBody = PageLayout(3); legacyBody.Padding = Padding.Empty;
        Control imageRow = overlayImage.Parent;
        legacyBody.Controls.Add(overlayCar, 0, 0); legacyBody.Controls.Add(imageRow, 0, 1);
        legacyBody.Controls.Add(Paragraph("仅覆盖一张图片，不随游戏方向选帧。开启后会暂停游戏内汽车外观。", Muted), 0, 2);
        legacy.Controls.Add(legacyBody); body.Controls.Add(legacy, 0, 2);
        GroupBox maintenance = Group("维护与帮助");
        TableLayoutPanel actions = PageLayout(2); actions.Padding = Padding.Empty;
        status.AutoSize = true; status.Dock = DockStyle.Top; status.Text = "正在读取游戏状态…";
        actions.Controls.Add(status, 0, 0);
        FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        restore.Text = "还原官方原版"; restore.AutoSize = true;
        readme.Text = "使用说明"; readme.AutoSize = true;
        startGame.Text = "使用已应用配置启动"; startGame.AutoSize = true;
        buttons.Controls.Add(startGame); buttons.Controls.Add(readme); buttons.Controls.Add(restore);
        actions.Controls.Add(buttons, 0, 1); maintenance.Controls.Add(actions); body.Controls.Add(maintenance, 0, 3);
        page.Controls.Add(body); return page;
    }

    private void ApplyScalePreset(int index) { racers.Value = ScaleValues[index]; }
    private void ApplyTrafficPreset(int index) { traffic.Value = TrafficValues[index]; police.Value = PoliceValues[index]; }
    private void ApplyDisplayPreset(int index) { hd.Checked = index != 0; frameRate.Value = index == 2 ? 120 : 60; }
    private void ApplyVehiclePreset(int index)
    {
        infiniteDurability.Checked = true;
        overlayCar.Checked = false; carDebugCopies.Checked = false; carCollision.Checked = false;
        bool car = index == 1 || index == 2;
        carShell.Checked = car; carStability.Checked = car; autoCarCollision.Checked = car;
        invincible.Checked = index == 2; ramKnockdown.Checked = index == 2;
        rockSolid.Checked = car; exaggeratedLaunch.Checked = index == 2;
        longRun.Checked = index == 3; remountPolicy.SelectedIndex = index == 3 ? 3 : 0;
        hideFallenVehicles.Checked = index == 3;
        aiRunVariation.Checked=index==3;
        if(index==3) { aiRunRhythm.SelectedIndex=2;aiRunSprint.SelectedIndex=2;aiRunChase.SelectedIndex=2;aiRunWeave.SelectedIndex=2; }
        if (index == 3) { longRunScope.SelectedIndex = 2; chaseDistance.Value = 96; }
        if (index == 2) { launchScope.SelectedIndex = 0; launchMultiplier.Value = 1.5m; }
    }

    private void WireSettingChanges(Control root)
    {
        foreach (Control c in root.Controls) {
            CheckBox check = c as CheckBox; ComboBox combo = c as ComboBox; NumericUpDown number = c as NumericUpDown; TextBox text = c as TextBox;
            if (check != null) check.CheckedChanged += delegate { SettingsChanged(); };
            if (combo != null) combo.SelectedIndexChanged += delegate { SettingsChanged(); };
            if (number != null) number.ValueChanged += delegate { SettingsChanged(); };
            if (text != null && !text.ReadOnly) text.TextChanged += delegate { SettingsChanged(); };
            WireSettingChanges(c);
        }
    }

    private void SettingsChanged()
    {
        if (syncingPresets) return;
        SyncQuickSettings();
        if (trackingChanges) changeStatus.Text = "设置已修改 · 点击“应用并启动”生效，关闭时会记住设置";
    }

    private void SyncQuickSettings()
    {
        syncingPresets = true;
        try {
            quickScale.SelectedIndex = Array.IndexOf(ScaleValues, (int)racers.Value) + 1;
            int road = -1;
            for (int i = 0; i < TrafficValues.Length; i++) if (traffic.Value == TrafficValues[i] && police.Value == PoliceValues[i]) road = i;
            quickTraffic.SelectedIndex = road + 1;
            quickDisplay.SelectedIndex = frameRate.Value == 60 ? (hd.Checked ? 2 : 1) : (hd.Checked && frameRate.Value == 120 ? 3 : 0);
            int vehicle = 0;
            if (infiniteDurability.Checked && !overlayCar.Checked && !carDebugCopies.Checked && !carCollision.Checked) {
                if (!carShell.Checked && !invincible.Checked && !ramKnockdown.Checked && !rockSolid.Checked && !exaggeratedLaunch.Checked) vehicle = 1;
                if (carShell.Checked && carStability.Checked && autoCarCollision.Checked && rockSolid.Checked) {
                    if (!invincible.Checked && !ramKnockdown.Checked && !exaggeratedLaunch.Checked) vehicle = 2;
                    if (invincible.Checked && ramKnockdown.Checked && exaggeratedLaunch.Checked && launchScope.SelectedIndex == 0 && launchMultiplier.Value == 1.5m) vehicle = 3;
                }
            }
            if (longRun.Checked) vehicle = vehicle == 1 && DefaultRunAiSettings() && hideFallenVehicles.Checked && remountPolicy.SelectedIndex == 3 && longRunScope.SelectedIndex == 2 && chaseDistance.Value == 96 ? 4 : 0;
            else if (remountPolicy.SelectedIndex != 0 || hideFallenVehicles.Checked) vehicle = 0;
            quickVehicle.SelectedIndex = vehicle;
            longRunScope.Enabled = longRun.Checked; chaseDistance.Enabled = longRun.Checked;
            aiRunVariation.Enabled=longRun.Checked&&longRunScope.SelectedIndex!=1;
            foreach(Control option in new Control[]{aiRunRhythm,aiRunSprint,aiRunChase,aiRunWeave})option.Enabled=aiRunVariation.Enabled&&aiRunVariation.Checked;
            launchScope.Enabled = exaggeratedLaunch.Checked; launchMultiplier.Enabled = exaggeratedLaunch.Checked;
            onFootScope.Enabled = onFootProtection.Checked;
            carStyle.Enabled = carShell.Checked;
            autoCarCollision.Enabled = carShell.Checked; carStability.Enabled = carShell.Checked;
            carCollision.Enabled = !carShell.Checked;
            overlayImage.Enabled = overlayCar.Checked; overlayBrowse.Enabled = overlayCar.Checked;
            quickSummary.Text = racers.Value + " 名车手\n\n" + quickVehicle.Text + "  ·  " + (hd.Checked ? "高清宽屏" : "原版画面") + "\n\n" + frameRate.Value + " 帧  /  交通 ×" + traffic.Value;
            bool native = carShell.Checked && !overlayCar.Checked && !carDebugCopies.Checked;
            vehicleDetails.Text = "当前比赛配置\n\n" +
                "长跑：" + (longRun.Checked ? longRunScope.Text + " / 领先 " + chaseDistance.Value : "关闭") + "\n" +
                "倒地后隐藏车辆：" + (hideFallenVehicles.Checked ? "开启" : "关闭") + "\n" +
                "AI 长跑变化：" + (aiRunVariation.Enabled&&aiRunVariation.Checked?"开启":"关闭") + "\n" +
                "超时恢复上车：" + remountPolicy.Text + "\n" +
                (native ? "汽车款式 " + (carStyle.SelectedIndex + 1).ToString("00") : (carDebugCopies.Checked ? "调试摩托复制" : (overlayCar.Checked ? "透明图片叠加" : "摩托车外观"))) + "\n" +
                "碰撞体：" + (native && autoCarCollision.Checked ? "随车型大小匹配" : (carCollision.Checked ? "固定放大" : "原版")) + "\n" +
                "碰撞稳定：" + (native && carStability.Checked ? "开启" : "关闭") + "\n" +
                "撞倒对手：" + (ramKnockdown.Checked ? "开启" : "关闭") + "\n" +
                "自身霸体：" + (invincible.Checked ? "开启" : "关闭") + "\n" +
                "无限耐久：" + (infiniteDurability.Checked ? "开启" : "关闭") + "\n" +
                "稳如泰山：" + (rockSolid.Checked ? "开启" : "关闭") + "\n" +
                "夸张击飞：" + (exaggeratedLaunch.Checked ? launchScope.Text + " / " + launchMultiplier.Value + " 倍" : "关闭") + "\n\n" +
                (forceMap.Checked ? "开局：" + mapLevel.Text + " / " + mapCourse.Text : "开局赛道：跟随游戏菜单");
        } finally { syncingPresets = false; }
    }
}

internal sealed class SettingsCard : GroupBox
{
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.White);
        using (Pen border = new Pen(Color.FromArgb(222, 229, 235)))
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        float scale = e.Graphics.DpiX / 96f;
        using (Font title = new Font(Font, FontStyle.Bold))
            TextRenderer.DrawText(e.Graphics, Text, title, new Rectangle((int)(14 * scale), (int)(10 * scale), Width - (int)(28 * scale), (int)(28 * scale)), Color.FromArgb(28, 42, 57), TextFormatFlags.VerticalCenter);
    }
}

// Owner-drawn selection also renders reliably in our offscreen layout previews.
internal sealed class PresetComboBox : ComboBox
{
    public PresetComboBox() { DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Math.Max(24, Font.Height + 8); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Math.Max(24, Font.Height + 8); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index >= 0 && e.Index < Items.Count)
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                Enabled ? e.ForeColor : SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // The native combo omits its selected text in WM_PRINT on some Windows themes.
        if ((m.Msg == 0x317 || m.Msg == 0x318) && m.WParam != IntPtr.Zero && SelectedIndex >= 0)
        {
            using (Graphics g = Graphics.FromHdc(m.WParam))
            {
                Rectangle box = new Rectangle(3, 3, Math.Max(1, Width - 24), Math.Max(1, Height - 6));
                using (Brush background = new SolidBrush(Enabled ? BackColor : SystemColors.Control)) g.FillRectangle(background, box);
                TextRenderer.DrawText(g, GetItemText(SelectedItem), Font, box, Enabled ? ForeColor : SystemColors.GrayText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
