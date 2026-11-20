using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

internal static class TrackLengthUnits
{
    // User's measured calibration, not a recovered native HUD conversion.
    internal const decimal KilometersPerBlock=0.0046m;
    internal const decimal MinimumKm=100*KilometersPerBlock;
    internal const decimal MaximumKm=RuntimePatch.MaximumTrackLength*KilometersPerBlock;
    internal const decimal DefaultKm=1800*KilometersPerBlock;
    internal static decimal ToKilometers(int blocks) { return blocks*KilometersPerBlock; }
    internal static int ToBlocks(decimal kilometers)
    {
        if(kilometers<MinimumKm||kilometers>MaximumKm)throw new ArgumentOutOfRangeException("kilometers");
        return (int)decimal.Round(kilometers/KilometersPerBlock,0,MidpointRounding.AwayFromZero);
    }
    internal static string Format(decimal kilometers) { return kilometers.ToString("0.####",CultureInfo.InvariantCulture); }
    internal static void MigrateLegacySettings(IDictionary<string,string> values)
    {
        string legacy;
        if(!values.ContainsKey("trackLengthKm")&&values.TryGetValue("trackLength",out legacy))
        {
            int blocks;
            values["trackLengthKm"]=int.TryParse(legacy,NumberStyles.Integer,CultureInfo.InvariantCulture,out blocks)&&blocks>=100&&blocks<=RuntimePatch.MaximumTrackLength
                ?Format(ToKilometers(blocks)):"";
        }
        values.Remove("trackLength");
    }
    internal static decimal ReadKilometers(IDictionary<string,string> values)
    {
        MigrateLegacySettings(values);
        string raw;decimal km;
        if(!values.TryGetValue("trackLengthKm",out raw)||!decimal.TryParse(raw,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out km)
            ||km<MinimumKm||km>MaximumKm||decimal.Round(km,4)!=km)return DefaultKm;
        return km;
    }
}

internal sealed partial class LauncherForm
{
    private readonly CheckBox forceTrackLength=new CheckBox();
    private readonly NumericUpDown trackLength=new NumericUpDown();
    private Control BuildTrackLengthGroup()
    {
        GroupBox group=Group("赛道长度");TableLayoutPanel body=PageLayout(3);
        forceTrackLength.Text="修改赛道长度";forceTrackLength.AutoSize=true;
        trackLength.DecimalPlaces=4;trackLength.Minimum=TrackLengthUnits.MinimumKm;trackLength.Maximum=TrackLengthUnits.MaximumKm;trackLength.Value=TrackLengthUnits.DefaultKm;trackLength.Increment=0.1m;trackLength.Enabled=false;
        forceTrackLength.CheckedChanged+=delegate {trackLength.Enabled=forceTrackLength.Checked;};
        body.Controls.Add(forceTrackLength,0,0);body.Controls.Add(trackLength,0,1);
        body.Controls.Add(Paragraph("目标长度（公里），范围 0.46–23，默认 8.28。按实测换算：1000 街区约 4.6 公里。勾选后覆盖比赛终点，下次启动游戏生效。",Muted),0,2);
        group.Controls.Add(body);return group;
    }
}

internal static partial class RuntimePatch
{
    // Every shipped route has >=5423 segments. Keep post-finish road; no loops.
    internal const int MaximumTrackLength=5000;
    internal static readonly int[] TrackLengthSites={0x745E8,0x16645,0x167D7,0x15D6A};
    internal static byte[] TrackLengthOriginal(long image,int site)
    {
        if(site==0){CodeBuffer c=new CodeBuffer(0);B(c,0xC7,5);D(c,image+0x1F3A74);D(c,0);return c.Finish();}
        if(site==1)return new byte[]{0x83,0x79,0x2C,0,0x74,8};
        if(site==2)return new byte[]{0x83,0x7A,0x2C,0,0x74,8};
        return new byte[]{0x8B,0x86,4,3,0,0,0x8B,0x40,0x10,0x8B,0x78,0x2C};
    }
    internal static void ApplyTrackLength(IntPtr process,long image,bool enabled,int length,Action<string> log)
    {
        if(!enabled)return;
        if(length<100||length>MaximumTrackLength)throw new ArgumentOutOfRangeException("length");
        for(int i=0;i<4;i++)RequireMotionSignature(process,image+TrackLengthSites[i],TrackLengthOriginal(image,i));
        IntPtr block=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)0x1000,MemCommitReserve,PageExecuteReadWrite);
        if(block==IntPtr.Zero)throw new InvalidOperationException("无法分配赛道长度代码区。");
        long code=block.ToInt64();Write(process,code,BuildTrackFinishCode(image,code));
        // Initialize the shared finish before new racers receive distances.
        Write(process,image+0x745EE,BitConverter.GetBytes(length*256));
        // Use the absolute finish even for actors with no loaded road pointer.
        Write(process,image+0x16645,new byte[]{0x90,0x90,0x90,0x90,0x90,0x90});
        Write(process,image+0x167D7,new byte[]{0x90,0x90,0x90,0x90,0x90,0x90});
        Write(process,image+0x15D6A,Jmp(image+0x15D6A,code,12));
        log("赛道长度覆盖已写入：约 "+TrackLengthUnits.Format(TrackLengthUnits.ToKilometers(length))+" 公里（按实测比例换算）；新比赛统一终点、剩余距离和远处选手结算。");
    }
    internal static byte[] BuildTrackFinishCode(long image,long cave)
    {
        CodeBuffer c=new CodeBuffer(cave);B(c,0x9C,0x60);
        // ESI=bike, EBX=rider, after native offscreen movement copied progress.
        B(c,0xA1);D(c,image+0x1F3A74);B(c,0x2B,0x43,0x1C,0x89,0x83);D(c,0x1F8);
        B(c,0x85,0xC0);Jcc(c,0x8F,"done");
        B(c,0xF6,0x83);D(c,0xC0);B(c,0x10);Jcc(c,0x85,"done");
        B(c,0x89,0xD9);CallAbs(c,image+0x193D);
        c.Label("done");B(c,0x61,0x9D);JmpAbs(c,image+0x15DB2);return c.Finish();
    }
}
