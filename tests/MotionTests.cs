using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class MotionTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at, UIntPtr size, uint type, uint protection);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RunCode();
    static long Alloc(int size) { IntPtr p = VirtualAlloc(IntPtr.Zero,(UIntPtr)size,0x3000,0x40); if(p==IntPtr.Zero)throw new Exception("allocation"); return (uint)p.ToInt32(); }
    static void Write(long p, byte[] b) { Marshal.Copy(b,0,new IntPtr(p),b.Length); }
    static void Put(long p, long v) { Marshal.WriteInt32(new IntPtr(p),unchecked((int)v)); }
    static int Get(long p) { return Marshal.ReadInt32(new IntPtr(p)); }
    static void D(List<byte> b, long v) { b.AddRange(BitConverter.GetBytes(unchecked((int)v))); }
    static void Mov(List<byte> b, byte op, long v) { b.Add(op);D(b,v); }
    static int passed;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name); passed++;Console.WriteLine("PASS " + name); }
    static void Run(long code, long target, long back, long bike, long rider, long result)
    {
        List<byte> end = new List<byte> { 0xA3 };D(end,result); // capture EAX
        end.AddRange(new byte[]{0x89,0x0D});D(end,result+4);
        end.AddRange(new byte[]{0x89,0x15});D(end,result+8);
        end.AddRange(new byte[]{0x9C,0x58,0xA3});D(end,result+12);
        end.AddRange(new byte[]{0x61,0xC3});Write(back,end.ToArray());
        List<byte> b = new List<byte>{0x60};
        Mov(b,0xBB,bike);Mov(b,0xBE,bike);Mov(b,0xBF,rider);
        Mov(b,0xB8,1000);Mov(b,0xB9,123);Mov(b,0xBA,456);
        b.AddRange(new byte[]{0x39,0xC0,0xF9,0xE9});D(b,target-(code+b.Count+4));Write(code,b.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
        if(Get(result)!=1000 || Get(result+4)!=123 || Get(result+8)!=456)throw new Exception("register corruption");
    }
    static int Main()
    {
        try {
            long image=Alloc(0x220000),cave=Alloc(0x2000),bike=Alloc(0x1000),rider=Alloc(0x1000),result=Alloc(0x1000);
            Put(bike+0xC4,4);Put(rider+0xC4,8);
            for(int site=0;site<5;site++) for(int protect=0;protect<2;protect++) {
                bool usesBike=RuntimePatch.SolidOriginals[site][1]==0x83;
                long actor=usesBike?bike:rider;
                Put(image+0x1A69EC,protect==1?actor:result);
                Put(actor+0x288,17);Put(actor+0xEC,3000);
                Write(cave+0x400,RuntimePatch.BuildSolidCode(image,cave+0x400,site));
                Run(cave,cave+0x400,image+RuntimePatch.SolidSites[site]+RuntimePatch.SolidOriginals[site].Length,bike,rider,result);
                Check(Get(actor+(site<2?0xEC:0x288))==(site<2?3000:17)+(protect==1?0:(site==0?-300:(site==1?300:1000))),"combat impulse site="+site+" protected="+protect);
            }
            for(int entry=0;entry<2;entry++) for(int scenario=0;scenario<10;scenario++) {
                decimal factor=scenario==4?1:(scenario==5?8:(scenario==8?1.5m:(scenario==9?1.6m:3)));
                bool everyone=scenario==2;
                Put(image+0x1A69EC,scenario==1||scenario==2?bike:(scenario==3?0:result));
                Put(bike+0xC4,scenario==6?16:4);Put(rider+0xC4,8);
                int z=scenario==5?int.MaxValue:(scenario==7?-999:2000);
                int side=scenario==5?int.MinValue:(scenario==7?0:500);
                Put(bike+0xF4,z);Put(rider+0xF4,z);Put(bike+0xF0,side);Put(rider+0xF0,side);
                Put(bike+0xEC,4000);Put(rider+0xEC,5000);Put(rider+0x20,0);
                long back=image+RuntimePatch.LaunchSites[entry]+7;
                Write(cave+0x400,RuntimePatch.BuildLaunchCode(image,cave+0x400,back,everyone,factor));
                Run(cave,cave+0x400,back,bike,rider,result);
                bool enabled=scenario!=1&&scenario!=3&&scenario!=4&&scenario!=6;
                int expectedZ=enabled?(int)(Math.Max(1800,Math.Min(4000,z))*factor):z;
                int expectedX=enabled?(int)(Math.Max(-2000,Math.Min(2000,side==0?400:side))*factor):side;
                int expectedRiderX=enabled?(int)(Math.Max(-2000,Math.Min(2000,side==0?-400:side))*factor):side;
                Check(Get(bike+0xF4)==expectedZ&&Get(rider+0xF4)==expectedZ&&Get(bike+0xF0)==expectedX&&Get(rider+0xF0)==expectedRiderX&&Get(rider+0x20)==0x4B00&&Get(bike+0xEC)==4000&&Get(rider+0xEC)==5000&&(Get(result+12)&0x41)==0,"launch entry="+entry+" scenario="+scenario);
            }
            foreach(int rva in RuntimePatch.LaunchSites)Write(image+rva,RuntimePatch.LaunchOriginal);
            for(int i=0;i<5;i++)Write(image+RuntimePatch.SolidSites[i],RuntimePatch.SolidOriginals[i]);
            Write(image+RuntimePatch.LaunchSites[1],new byte[]{0xCC});
            bool rejected=false;try{RuntimePatch.ApplyMotion(Process.GetCurrentProcess().Handle,image,true,true,false,3,delegate(string s){});}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.SolidSites[0]))==0x81,"all signatures checked before any writes");
            Write(image+RuntimePatch.LaunchSites[1],RuntimePatch.LaunchOriginal);
            RuntimePatch.ApplyMotion(Process.GetCurrentProcess().Handle,image,true,true,false,3,delegate(string s){});
            Check(Marshal.ReadByte(new IntPtr(image+RuntimePatch.SolidSites[0]))==0xE9&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.LaunchSites[1]))==0xE9,"combined installer writes both feature hooks");
            bool invalid=false;try{RuntimePatch.BuildLaunchCode(image,cave,cave+0x1000,false,9);}catch(ArgumentOutOfRangeException){invalid=true;}
            Check(invalid,"reject multiplier outside supported range");
            Console.WriteLine("Motion tests: "+passed+" passed");return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
