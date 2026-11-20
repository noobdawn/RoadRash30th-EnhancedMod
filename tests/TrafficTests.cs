using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class TrafficTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at,UIntPtr size,uint type,uint protect);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr process,IntPtr at,UIntPtr size);
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")] static extern IntPtr AddVectoredExceptionHandler(uint first,ExceptionHandler handler);
    [DllImport("kernel32.dll")] static extern void ExitProcess(uint code);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ExceptionHandler(IntPtr p);
    static long diagnosticImage;
    static ExceptionHandler handler=delegate(IntPtr p){if(unchecked((uint)Marshal.ReadInt32(Marshal.ReadIntPtr(p)))==0xC0000005){IntPtr c=Marshal.ReadIntPtr(p,4);Console.Error.WriteLine("Traffic native AV: RVA={0:X} EAX={1:X8} ECX={2:X8} EDX={3:X8} ESI={4:X8} EDI={5:X8}",(uint)Marshal.ReadInt32(c,184)-diagnosticImage,Marshal.ReadInt32(c,176),Marshal.ReadInt32(c,172),Marshal.ReadInt32(c,168),Marshal.ReadInt32(c,160),Marshal.ReadInt32(c,156));ExitProcess(77);}return 0;};
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong Evaluate(IntPtr car,IntPtr config,int speed,int acceleration);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RunCode();
    static long Alloc(int n){return (uint)VirtualAlloc(IntPtr.Zero,(UIntPtr)n,0x3000,0x40).ToInt32();}
    static void Put(long p,long v){Marshal.WriteInt32(new IntPtr(p),unchecked((int)v));}
    static int Get(long p){return Marshal.ReadInt32(new IntPtr(p));}
    static void Write(long p,byte[] bytes){Marshal.Copy(bytes,0,new IntPtr(p),bytes.Length);FlushInstructionCache(new IntPtr(-1),new IntPtr(p),(UIntPtr)bytes.Length);}
    static void D(List<byte> b,long v){b.AddRange(BitConverter.GetBytes(unchecked((int)v)));}
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static long MapGame(string path)
    {
        byte[] f=File.ReadAllBytes(path);int pe=BitConverter.ToInt32(f,0x3c),opt=pe+24;
        long image=Alloc(BitConverter.ToInt32(f,opt+56)),preferred=BitConverter.ToUInt32(f,opt+28);
        Marshal.Copy(f,0,new IntPtr(image),BitConverter.ToInt32(f,opt+60));
        int sec=opt+BitConverter.ToUInt16(f,pe+20);
        for(int i=0;i<BitConverter.ToUInt16(f,pe+6);i++,sec+=40){int len=BitConverter.ToInt32(f,sec+16);if(len>0)Marshal.Copy(f,BitConverter.ToInt32(f,sec+20),new IntPtr(image+BitConverter.ToInt32(f,sec+12)),len);}
        int reloc=BitConverter.ToInt32(f,opt+136),end=reloc+BitConverter.ToInt32(f,opt+140);
        while(reloc<end){int page=Get(image+reloc),block=Get(image+reloc+4);if(block<8)throw new Exception("PE relocation");for(int e=8;e<block;e+=2){int fix=(ushort)Marshal.ReadInt16(new IntPtr(image+reloc+e));if((fix>>12)==3){long at=image+page+(fix&4095);Put(at,(uint)Get(at)+image-preferred);}}reloc+=block;}
        return image;
    }
    static void NativeHook(string path)
    {
        long image=MapGame(path),car=Alloc(0x1000),code=Alloc(0x1000);diagnosticImage=image;
        bool signature=true;for(int i=0;i<RuntimePatch.TrafficDriveOriginal.Length;i++)signature &= Marshal.ReadByte(new IntPtr(image+RuntimePatch.TrafficDriveSite+i))==RuntimePatch.TrafficDriveOriginal[i];
        Check(signature,"installed traffic call site matches complete instruction signature");
        RuntimePatch.ApplyTrafficDriving(Process.GetCurrentProcess().Handle,image,1,1,false,delegate(string s){});
        Check(Marshal.ReadByte(new IntPtr(image+RuntimePatch.TrafficDriveSite))==0x6b,"default traffic settings install no hook");
        RuntimePatch.ApplyTrafficDriving(Process.GetCurrentProcess().Handle,image,3.5m,0.5m,true,delegate(string s){});
        Write(image+0x6A380,new byte[]{0xC3}); // stop after ORIGINAL native turn/acceleration call
        Put(car+0xc4,16);Put(car+0x18,256);
        List<byte> b=new List<byte>{0x9c,0x60,0xbf};D(b,car);b.Add(0xb9);D(b,100);
        b.AddRange(new byte[]{0x8d,0x97});D(b,0x1dc);
        // This site is in the middle of a function: jump in with acceleration
        // already on top of the stack, and put our return address BELOW it.
        b.Add(0x68);D(b,code+0x200);b.AddRange(new byte[]{0x6a,18,0xe9});D(b,image+RuntimePatch.TrafficDriveSite-(code+b.Count+4));Write(code,b.ToArray());
        Write(code+0x200,new byte[]{0x61,0x9d,0xc3});
        RunCode run=(RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode));
        for(int direction=0;direction<=2;direction+=2) {
            Put(car+0x1cc,direction);Put(car+0xec,0);Put(car+0x1dc,0);Put(car+0x1e0,direction==0?800:-800);
            run();Check(Get(car+0xec)==(direction==0?16128:2304),"native hook scales acceleration and keeps stack ABI direction="+direction);
            for(int i=0;i<8;i++)run();Check(Get(car+0xec)==(direction==0?35000:5000),"original movement reaches independently scaled target direction="+direction);
            Check(direction==0?Get(car+0x128)==0:Math.Abs(Get(car+0x128))==0x800000,"original turn function preserves travel direction="+direction);
        }
        long road=Alloc(0x1000);Put(car+0x2c,road);Put(car+0x140,road);Put(road+0xe0,-500);Put(road+0xe4,500);Put(road+0x2c,512);Put(road+0x30,512);
        for(int direction=0;direction<=2;direction+=2) {
            Put(car+0x1cc,direction);HashSet<int> headings=new HashSet<int>();
            for(int tick=0;tick<7200;tick+=60){Put(image+0x2036e8,tick);Put(car+0xec,0);run();headings.Add(Get(car+0x128));}
            Check(headings.Count>=2&&Get(car+0x28)==0,"crazy targets drive ORIGINAL native turn function without teleport direction="+direction);
        }
        bool rejected=false;try{RuntimePatch.ApplyTrafficDriving(Process.GetCurrentProcess().Handle,image,2,2,true,delegate(string s){});}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"mismatched or already patched traffic signature is rejected");
    }
    static int Main(string[] args)
    {
        SetErrorMode(0x8003);AddVectoredExceptionHandler(1,handler);
        try {
            using(SHA256 sha=SHA256.Create())Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[0]))).Replace("-","").ToLowerInvariant()==TrafficDriveNative.SourceSha256,"traffic native bytes match C source");
            long entry=Alloc(0x3000),config=entry+0x2000,bridge=Alloc(0x1000),image=Alloc(0x240000),states=Alloc(4096*16),road=Alloc(0x1000),car=Alloc(0x1000);
            Write(entry,TrafficDriveNative.Code);
            List<byte> b=new List<byte>{0x8b,0x4c,0x24,4,0x8b,0x54,0x24,8,0xff,0x74,0x24,16,0xff,0x74,0x24,16,0xe8};D(b,entry-(bridge+b.Count+4));b.Add(0xc3);Write(bridge,b.ToArray());
            Evaluate run=(Evaluate)Marshal.GetDelegateForFunctionPointer(new IntPtr(bridge),typeof(Evaluate));
            Action<decimal,decimal,bool> setup=delegate(decimal same,decimal opposite,bool crazy){Write(config,RuntimePatch.TrafficConfig(image,states,same,opposite,crazy,12345));};
            Put(car+0xc4,16);Put(car+0x18,256);Put(car+0x2c,road);Put(car+0x140,road);
            Put(road+0xe0,-500);Put(road+0xe4,500);Put(road+0x2c,512);Put(road+0x30,512);Put(car+0x174,45*256);
            foreach(decimal factor in new decimal[]{0,0.01m,0.5m,1,8,12.34m,100,1000000,decimal.MaxValue}) {
                setup(factor,0.75m,false);Put(car+0x1cc,0);
                ulong value=run(new IntPtr(car),new IntPtr(config),10000,18);
                int expected=factor>int.MaxValue/10000m?int.MaxValue:(int)(10000*factor);
                Check((uint)value==expected,"same-direction multiplier including zero and uncapped input "+factor);
                if(factor==0.01m)Check((value>>32)>0,"fractional traffic speed still accelerates from rest");
                Put(car+0x1cc,2);value=run(new IntPtr(car),new IntPtr(config),10000,18);
                Check((uint)value==7500,"opposite multiplier remains independent of same direction "+factor);
            }
            setup(3,4,true);
            foreach(int kind in new int[]{4,8,16})foreach(int direction in new int[]{0,1,2,3}) {
                if(kind==16&&(direction==0||direction==2))continue;
                Put(car+0xc4,kind);Put(car+0x1cc,direction);Put(car+0x1dc,123);
                ulong value=run(new IntPtr(car),new IntPtr(config),10000,18);
                Check((uint)value==10000&&(value>>32)==18&&Get(car+0x1dc)==123,"excludes motorcycles riders and crossing traffic kind="+kind+" direction="+direction);
            }
            Put(car+0xc4,16);setup(1,1,false);Put(car+0x1cc,0);Put(car+0x1dc,123);
            run(new IntPtr(car),new IntPtr(config),10000,18);Check(Get(car+0x1dc)==123,"crazy off retains native steering");
            setup(1,1,true);Put(config+28,car^0x12345); // cancel allocation address for repeatable scenario
            bool left=false,right=false,negative=false,positive=false,unchanged=true,paused=true;int changes=0;
            for(int direction=0;direction<=2;direction+=2) {
                Put(car+0x1cc,direction);Put(car+0x28,0);Put(car+0x1c,1000);int prev=0;
                for(int t=0;t<1800;t++) {
                    Put(image+0x2036e8,t);run(new IntPtr(car),new IntPtr(config),10000,18);int delta=Get(car+0x1dc);
                    if(t>0&&delta!=prev)changes++;prev=delta;negative |= delta<0;positive |= delta>0;
                    run(new IntPtr(car),new IntPtr(config),10000,18);paused &= delta==Get(car+0x1dc);
                    unchanged &= Get(car+0x28)==0&&Get(car+0x1c)==1000&&Get(car+0x1e0)==(direction==0?800:-800);
                    Put(car+0x28,-520*256);run(new IntPtr(car),new IntPtr(config),10000,18);left |= Get(car+0x1dc)<0;
                    Put(car+0x28,520*256);run(new IntPtr(car),new IntPtr(config),10000,18);right |= Get(car+0x1dc)>0;Put(car+0x28,0);
                }
            }
            Check(changes>=8&&negative&&positive,"crazy drivers repeatedly choose both road halves within 30 seconds");
            Check(left&&right,"crazy targets can extend beyond both pavement edges");
            Check(unchanged&&paused,"crazy steering changes no coordinates and repeated calls do not advance decisions");
            Put(car+0x2c,0);Put(car+0x140,0);Put(car+0x1dc,987);run(new IntPtr(car),new IntPtr(config),10000,18);Check(Get(car+0x1dc)==987,"unloaded traffic skips road access");
            setup(decimal.MaxValue,1,false);Put(car+0x1cc,0);Put(car+0xec,int.MaxValue-100);ulong saturated=run(new IntPtr(car),new IntPtr(config),10000,18);
            Check((uint)saturated==int.MaxValue&&(saturated>>32)==0,"extreme multiplier cannot wrap native acceleration add");
            if(args.Length>1)NativeHook(args[1]);
            Console.WriteLine("Traffic tests: "+passed+" passed");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
