using System;
using System.Windows.Forms;

internal sealed partial class LauncherForm
{
    private readonly NumericUpDown sameTrafficSpeed=new NumericUpDown();
    private readonly NumericUpDown oppositeTrafficSpeed=new NumericUpDown();
    private readonly CheckBox crazyDrivers=new CheckBox();
    private readonly CheckBox sameTraffic=new CheckBox { Checked=true };
    private readonly CheckBox oppositeTraffic=new CheckBox { Checked=true };
    private static decimal ReadTrafficSpeed(System.Collections.Generic.Dictionary<string,string> d,string key)
    {
        string text;decimal value;
        return d.TryGetValue(key,out text)&&decimal.TryParse(text,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out value)&&value>=0?value:1m;
    }
    private Control BuildTrafficDrivingGroup()
    {
        GroupBox group=Group("交通车辆驾驶");TableLayoutPanel body=PageLayout(5);
        sameTraffic.Text="启用同向车流";oppositeTraffic.Text="启用对向车流";
        sameTraffic.AutoSize=true;oppositeTraffic.AutoSize=true;
        FlowLayoutPanel directions=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Top };
        directions.Controls.Add(sameTraffic);directions.Controls.Add(oppositeTraffic);body.Controls.Add(directions,0,4);
        NumericUpDown[] values={sameTrafficSpeed,oppositeTrafficSpeed};
        string[] names={"同向车辆速度倍率","对向车辆速度倍率"};
        for(int i=0;i<2;i++) {
            NumericUpDown n=values[i];n.Minimum=0;n.Maximum=decimal.MaxValue;n.DecimalPlaces=2;n.Increment=0.1m;n.Value=1;n.Width=180;
            FlowLayoutPanel row=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top};
            row.Controls.Add(new Label{Text=names[i],AutoSize=true,Padding=new Padding(8)});row.Controls.Add(n);body.Controls.Add(row,0,i);
        }
        crazyDrivers.Text="疯狂司机：频繁变道，可能驶上人行道";crazyDrivers.AutoSize=true;body.Controls.Add(crazyDrivers,0,2);
        body.Controls.Add(Paragraph("1 倍为原速，0 为停止；可直接输入倍率，不设玩法上限。疯狂司机会在两侧车道和路肩间随机选线。适用于普通交通车，骑行和长跑均可使用。",Muted),0,3);
        group.Controls.Add(body);return group;
    }
}

internal static partial class RuntimePatch
{
    internal static readonly int[] TrafficSpawnSites={0x6ABB0,0x6B0C0};
    internal static readonly byte[][] TrafficSpawnOriginals={
        new byte[]{0x55,0x8B,0xEC,0x83,0xEC,0x18},new byte[]{0x55,0x8B,0xEC,0x83,0xEC,0x4C}
    };
    internal static void ApplyTrafficDirections(IntPtr process,long image,bool same,bool opposite,Action<string> log)
    {
        if(same&&opposite)return;
        for(int i=0;i<2;i++)RequireMotionSignature(process,image+TrafficSpawnSites[i],TrafficSpawnOriginals[i]);
        IntPtr block=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)0x1000,MemCommitReserve,PageExecuteReadWrite);
        if(block==IntPtr.Zero)throw new InvalidOperationException("无法分配车流方向代码区。");
        long cave=block.ToInt64();
        for(int i=0;i<2;i++)Write(process,cave+i*0x400,BuildTrafficSpawnCode(image,cave+i*0x400,i,same,opposite));
        for(int i=0;i<2;i++)Write(process,image+TrafficSpawnSites[i],Jmp(image+TrafficSpawnSites[i],cave+i*0x400,6));
        log("车流方向已写入：同向="+same+"；对向="+opposite+"；两项关闭时不生成普通交通车（含横穿车）。");
    }
    internal static byte[] BuildTrafficSpawnCode(long image,long cave,int site,bool same,bool opposite)
    {
        if(site<0||site>1)throw new ArgumentOutOfRangeException("site");
        CodeBuffer c=new CodeBuffer(cave);
        B(c,0x9C,0x60);
        if(!same&&!opposite)Jmp(c,"skip");
        else {
            if(site==0)B(c,0x0F,0xB6,0x41,0x0A);else B(c,0x89,0xC8);
            if(!same){B(c,0x85,0xC0);Jcc(c,0x84,"skip");}
            if(!opposite){B(c,0x83,0xF8,2);Jcc(c,0x84,"skip");}
        }
        B(c,0x61,0x9D);B(c,TrafficSpawnOriginals[site]);JmpAbs(c,image+TrafficSpawnSites[site]+6);
        c.Label("skip");B(c,0x61,0x9D);
        if(site==0)B(c,0xC3);else B(c,0xC2,12,0);
        return c.Finish();
    }
    internal const int TrafficDriveSite=0x6A375;
    internal static readonly byte[] TrafficDriveOriginal={0x6B,0xC1,0x64,0x8B,0xCF};
    internal static byte[] TrafficConfig(long image,long drivers,decimal same,decimal opposite,bool crazy,int seed)
    {
        if(same<0||opposite<0)throw new ArgumentOutOfRangeException("traffic speed");
        int[] data=new int[8];data[0]=unchecked((int)(image+0x2036E8));data[1]=unchecked((int)drivers);
        decimal[] speeds={same,opposite};
        for(int i=0;i<2;i++) {
            decimal whole=decimal.Truncate(speeds[i]);
            data[2+i*2]=whole>=int.MaxValue?int.MaxValue:(int)whole;
            data[3+i*2]=whole>=int.MaxValue?0:(int)((speeds[i]-whole)*1000);
        }
        data[6]=crazy?1:0;data[7]=seed;byte[] bytes=new byte[32];Buffer.BlockCopy(data,0,bytes,0,32);return bytes;
    }
    internal static byte[] BuildTrafficDriveCode(long image,long cave,long entry,long config)
    {
        CodeBuffer c=new CodeBuffer(cave);
        B(c,0x6B,0xC1,0x64,0x9C,0x60); // native desired speed *100; preserve flags/registers
        B(c,0xFF,0x74,0x24,36,0x50); // stack arguments: original acceleration, speed
        B(c,0x8B,0xCF,0xBA);D(c,unchecked((int)config));CallAbs(c,entry);
        B(c,0x89,0x44,0x24,28,0x89,0x54,0x24,36); // saved EAX and original acceleration argument
        B(c,0x61,0x9D,0x8B,0xCF);JmpAbs(c,image+TrafficDriveSite+5);
        return c.Finish();
    }
    internal static void ApplyTrafficDriving(IntPtr process,long image,decimal same,decimal opposite,bool crazy,Action<string> log)
    {
        if(same==1&&opposite==1&&!crazy)return;
        RequireMotionSignature(process,image+TrafficDriveSite,TrafficDriveOriginal);
        IntPtr block=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)0x3000,MemCommitReserve,PageExecuteReadWrite);
        if(block==IntPtr.Zero)throw new InvalidOperationException("无法分配交通驾驶代码区。");
        long code=block.ToInt64(),config=code+0x2000,cave=code+0x2100;
        IntPtr drivers=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)(4096*16),MemCommitReserve,0x04);
        if(drivers==IntPtr.Zero)throw new InvalidOperationException("无法分配交通驾驶状态区。");
        if(TrafficDriveNative.Code.Length>0x2000)throw new InvalidOperationException("交通驾驶代码超出分配区。");
        Write(process,code,TrafficDriveNative.Code);Write(process,config,TrafficConfig(image,drivers.ToInt64(),same,opposite,crazy,Guid.NewGuid().GetHashCode()));
        Write(process,cave,BuildTrafficDriveCode(image,cave,code,config));
        Write(process,image+TrafficDriveSite,Jmp(image+TrafficDriveSite,cave,TrafficDriveOriginal.Length));
        log("交通驾驶：同向速度="+same+" 倍；对向速度="+opposite+" 倍；疯狂司机="+crazy+"。倍率仅在游戏整数速度溢出时饱和。");
    }
}
