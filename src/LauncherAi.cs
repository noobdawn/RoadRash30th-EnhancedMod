using System;
using System.Windows.Forms;

internal sealed partial class LauncherForm
{
    private readonly CheckBox aiRunVariation = new CheckBox();
    private readonly PresetComboBox aiRunRhythm = new PresetComboBox();
    private readonly PresetComboBox aiRunSprint = new PresetComboBox();
    private readonly PresetComboBox aiRunChase = new PresetComboBox();
    private readonly PresetComboBox aiRunWeave = new PresetComboBox();

    private Control BuildRunAiGroup()
    {
        GroupBox group=Group("AI 长跑变化"); TableLayoutPanel body=PageLayout(6);
        aiRunVariation.Text="启用 AI 长跑变化"; aiRunVariation.AutoSize=true;
        body.Controls.Add(aiRunVariation,0,0);
        ComboBox[] controls={aiRunRhythm,aiRunSprint,aiRunChase,aiRunWeave};
        string[] names={"节奏变化","冲刺频率","追赶强度","横向走位"};
        for(int i=0;i<controls.Length;i++) {
            ComboBox combo=controls[i]; combo.DropDownStyle=ComboBoxStyle.DropDownList;
            combo.Items.AddRange(new string[]{"关闭","低","中","高"});combo.SelectedIndex=2;combo.Width=150;
            FlowLayoutPanel row=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top};
            row.Controls.Add(new Label{Text=names[i],AutoSize=true,Padding=new Padding(8)});row.Controls.Add(combo);
            body.Controls.Add(row,0,i+1);
        }
        body.Controls.Add(Paragraph("只影响参加长跑的 AI。各自变速、冲刺后恢复，并适度追赶；按骑行车道偏好选线，独立随机换线，交通车靠近时才尝试闪避，仍可能被撞。不预判弯道、不追击选手。屏幕内外共用配速节奏，玩家保持手动控制。",Muted),0,5);
        group.Controls.Add(body);return group;
    }
    private static int ReadRunAiLevel(System.Collections.Generic.Dictionary<string,string> settings,string key)
    { int n=ReadInt(settings,key,2);return n>=0&&n<=3?n:2; }
    private bool DefaultRunAiSettings()
    { return aiRunVariation.Checked&&aiRunRhythm.SelectedIndex==2&&aiRunSprint.SelectedIndex==2&&aiRunChase.SelectedIndex==2&&aiRunWeave.SelectedIndex==2; }
}

internal static partial class RuntimePatch
{
    internal static void ValidateRunAi(int rhythm,int sprint,int chase,int weave)
    { foreach(int n in new int[]{rhythm,sprint,chase,weave})if(n<0||n>3)throw new ArgumentOutOfRangeException("AI long-run level"); }

    internal static long PrepareRunAi(IntPtr process,long image,decimal speed,int rhythm,int sprint,int chase,int weave,Action<string> log,out long config,int lead=96)
    {
        ValidateWalkSpeed(speed);ValidateRunAi(rhythm,sprint,chase,weave);ValidateRace(2,3,lead);
        IntPtr block=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)0x3000,MemCommitReserve,PageExecuteReadWrite);
        if(block==IntPtr.Zero)throw new InvalidOperationException("无法分配 AI 长跑变化代码区。");
        long code=block.ToInt64();config=code+0x2000;
        if(LongRunAiNative.Code.Length>0x2000)throw new InvalidOperationException("AI 长跑代码超出分配区。");
        Write(process,code,LongRunAiNative.Code);
        IntPtr lanes=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)(32768*32),MemCommitReserve,0x04);
        if(lanes==IntPtr.Zero)throw new InvalidOperationException("无法分配 AI 长跑跑线状态区。");
        int[] values={unchecked((int)(image+0x2036E8)),unchecked((int)(image+RvaPlayer)),(int)(speed*1200),rhythm,sprint,chase,weave,Guid.NewGuid().GetHashCode(),unchecked((int)lanes.ToInt64()),lead};
        byte[] data=new byte[values.Length*4];Buffer.BlockCopy(values,0,data,0,data.Length);Write(process,config,data);
        log("AI 长跑变化已写入：节奏="+rhythm+"；冲刺="+sprint+"；追赶="+chase+"；骑行选线="+weave+"（0 关 / 1 低 / 2 中 / 3 高；原版车道偏好，独立换线，近距离交通反应）。");
        if (weave > 0) log("已启用长跑人群起步避让。");
        return code;
    }
}
