using System;
using System.Windows.Forms;

internal sealed partial class LauncherForm
{
    private readonly CheckBox independentAudio = new CheckBox();
    private readonly CheckBox fallAnimationFix = new CheckBox();
    private readonly CheckBox nearbyImpactAudio = new CheckBox();

    private TabPage BuildFixesTab()
    {
        TabPage page = Tab("Fix 修复"); TableLayoutPanel body = PageLayout(3);
        body.Controls.Add(Paragraph("集中管理原版游戏问题的修复。各项可独立开关，适用于所有玩法；切换比赛预设会保留这里的选择。设置在下一次从启动器启动游戏时生效。", Muted), 0, 0);

        GroupBox animation = Group("人物动画"); TableLayoutPanel animationBody = PageLayout(2);
        fallAnimationFix.Text = "修复翻滚中闪出奔跑动作";
        fallAnimationFix.AutoSize = true; fallAnimationFix.Checked = true;
        animationBody.Controls.Add(fallAnimationFix, 0, 0);
        animationBody.Controls.Add(Paragraph("下车人物被撞倒后，受撞动作接续翻滚，完成起身后再恢复跑步。对玩家与 AI 生效，保留碰撞效果；普通比赛和长跑均可使用。", Muted), 0, 1);
        animation.Controls.Add(animationBody); body.Controls.Add(animation, 0, 1);

        GroupBox audio = Group("音乐与音效"); TableLayoutPanel audioBody = PageLayout(4);
        nearbyImpactAudio.Text="补全附近人物受撞音效";nearbyImpactAudio.AutoSize=true;
        audioBody.Controls.Add(nearbyImpactAudio,0,2);
        audioBody.Controls.Add(Paragraph("为附近 AI 的真实碰撞补充撞击与受击声，随距离衰减并避免连续重复播放。",Muted),0,3);
        independentAudio.Text = "修复音乐音量影响比赛音效";
        independentAudio.AutoSize = true; independentAudio.Checked = true;
        audioBody.Controls.Add(independentAudio, 0, 0);
        audioBody.Controls.Add(Paragraph("音量仍在游戏内调整。音乐设为 0 时保留引擎和碰撞声，总音量仍控制全部声音。比赛音乐音量调整后，从下一场比赛生效。", Muted), 0, 1);
        audio.Controls.Add(audioBody); body.Controls.Add(audio, 0, 2);
        page.Controls.Add(body); return page;
    }
}

internal static partial class RuntimePatch
{
    internal const int FallAnimationSite = 0x14124;
    internal static readonly byte[] FallAnimationOriginal = { 0xB2,0x26,0x8D,0x4F,0x70 };

    internal const int RiderPhysicsCall = 0x16242;
    private const int RiderFinishCheck = 0x162A8;

    internal static void ApplyRiderRoadGuard(IntPtr process, long image, Action<string> log)
    {
        long site = image + RiderPhysicsCall;
        RequireMotionSignature(process, image + RiderFinishCheck, new byte[] { 0x83,0x7E,0x2C,0,0x74,0x1F });
        byte[] current = Read(process, site, 5);
        if (current[0] == 0xE9) {
            long installed = unchecked((uint)(site + 5 + BitConverter.ToInt32(current, 1)));
            RequireMotionSignature(process, installed, BuildRiderRoadGuard(image, installed));
            return;
        }
        RequireMotionSignature(process, site, new byte[] { 0xE8,0xA9,0xB8,0xFE,0xFF });
        IntPtr block = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)0x1000, MemCommitReserve, PageExecuteReadWrite);
        if (block == IntPtr.Zero) throw new InvalidOperationException("无法分配离车人物路段保护代码区。");
        long entry = block.ToInt64();
        Write(process, entry, BuildRiderRoadGuard(image, entry));
        Write(process, site, Jmp(site, entry, 5));
        log("已启用离车人物路段保护。");
    }

    private static byte[] BuildRiderRoadGuard(long image, long entry)
    {
        var code = new CodeBuffer(entry);
        // ESI is the rider. The bike's offscreen update owns progress while the road is absent.
        B(code, 0x83,0x7E,0x2C,0); // cmp dword ptr [esi+2C],0
        Jcc(code, 0x84, "unloaded");
        CallAbs(code, image + 0x1AF0); // original on-foot physics
        JmpAbs(code, image + RiderPhysicsCall + 5);
        code.Label("unloaded");
        JmpAbs(code, image + RiderFinishCheck); // stock null-road exit, including stack cleanup
        return code.Finish();
    }

    // Original-game corrections are independent of race mode and race scope.
    internal static void ApplyFixes(IntPtr process, long image, bool independentAudio, bool fallAnimationFix, Action<string> log)
    {
        if (fallAnimationFix) {
            RequireMotionSignature(process, image + FallAnimationSite, FallAnimationOriginal);
            // Native impact has set state 1 and current action 0x22. Queue the
            // preceding native 0x1C tumble instead of 0x26 running. Changing only
            // the immediate preserves registers/flags and all original recovery.
            Write(process, image + FallAnimationSite + 1, new byte[] { 0x1C });
            log("Fix 修复：翻滚动画衔接已修复，适用于玩家与 AI 的所有玩法。");
        }
        if (independentAudio) {
            try { ApplyIndependentAudio(process, image, log); }
            catch (Exception ex) { log("音乐音量修复写入失败：" + ex.Message); }
        }
    }
}
