using System;

internal static partial class RuntimePatch
{
    internal static readonly int[] ImpactSites={0x12F1C,0x13FBA};
    internal static byte[] ImpactOriginal(long image,int site)
    {
        if(site==1)return new byte[]{0x83,0xB8,0x24,3,0,0,0};
        CodeBuffer c=new CodeBuffer(0);B(c,0x3B,0x3D);D(c,image+RvaPlayer);return c.Finish();
    }
    internal static void ApplyNearbyAudio(IntPtr process,long image,bool enabled,bool ram,Action<string> log)
    {
        if(!enabled)return;
        for(int i=0;i<2;i++)RequireMotionSignature(process,image+ImpactSites[i],ImpactOriginal(image,i));
        RequireMotionSignature(process,image+0x28BD0,new byte[]{0x55,0x8B,0xEC,0x83,0xEC,8});
        IntPtr block=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)0x5000,MemCommitReserve,PageExecuteReadWrite);
        if(block==IntPtr.Zero)throw new InvalidOperationException("无法分配附近受撞音效代码区。");
        long code=block.ToInt64(),config=code+0x2000;
        Write(process,code,ImpactAudioNative.Code);
        CodeBuffer data=new CodeBuffer(config);D(data,image);D(data,image+0x28BD0);D(data,code+0x3000);D(data,0);D(data,0);D(data,ram?1:0);
        Write(process,config,data.Finish());
        for(int i=0;i<2;i++)Write(process,code+0x2100+i*0x100,BuildImpactCode(image,code+0x2100+i*0x100,i,code,config));
        for(int i=0;i<2;i++)Write(process,image+ImpactSites[i],Jmp(image+ImpactSites[i],code+0x2100+i*0x100,ImpactOriginal(image,i).Length));
        log("附近人物受撞补声已写入：距离衰减、左右声道、连续碰撞去重；保留玩家原有声音。");
    }
    internal static byte[] BuildImpactCode(long image,long cave,int site,long entry,long config)
    {
        CodeBuffer c=new CodeBuffer(cave);B(c,0x9C,0x60,0x89,0xF9,0xBA);D(c,config);CallAbs(c,entry);B(c,0x61,0x9D);
        B(c,ImpactOriginal(image,site));JmpAbs(c,image+ImpactSites[site]+ImpactOriginal(image,site).Length);return c.Finish();
    }
}
