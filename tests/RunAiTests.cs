using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class RunAiTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at,UIntPtr size,uint type,uint protection);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr process,IntPtr at,UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong Evaluate(IntPtr bike,IntPtr config);
    static long Alloc(int n) { IntPtr p=VirtualAlloc(IntPtr.Zero,(UIntPtr)n,0x3000,0x40);if(p==IntPtr.Zero)throw new Exception("allocation");return (uint)p.ToInt32(); }
    static void Put(long p,long v) { Marshal.WriteInt32(new IntPtr(p),unchecked((int)v)); }
    static void Write(long p,byte[] b) { Marshal.Copy(b,0,new IntPtr(p),b.Length);FlushInstructionCache(new IntPtr(-1),new IntPtr(p),(UIntPtr)b.Length); }
    static int passed;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name); }
    static int Speed(ulong n) { return (int)(n&0xffffffff); }
    static int Lane(ulong n) { return unchecked((int)(n>>32)); }
    static int Main(string[] args)
    {
        try {
            if(args.Length>0)using(SHA256 sha=SHA256.Create()) {
                string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[0]))).Replace("-","").ToLowerInvariant();
                Check(hash==LongRunAiNative.SourceSha256,"native AI bytes match tracked C source");
            }
            long image=Alloc(0x240000),config,entry=RuntimePatch.PrepareRunAi(Process.GetCurrentProcess().Handle,image,9.5m,2,2,2,2,delegate(string s){},out config);
            Put(config+28,1234567);
            long bridge=Alloc(0x1000),player=Alloc(0x1000),pr=Alloc(0x1000),road=Alloc(0x1000);
            List<byte> b=new List<byte>{0x8B,0x4C,0x24,4,0x8B,0x54,0x24,8,0xE9};b.AddRange(BitConverter.GetBytes((int)(entry-bridge-13)));Write(bridge,b.ToArray());
            Evaluate run=(Evaluate)Marshal.GetDelegateForFunctionPointer(new IntPtr(bridge),typeof(Evaluate));
            Put(image+0x1A69EC,player);Put(player+0xC4,4);Put(player+0x2FC,pr);Put(pr+0xC4,8);Put(pr+0x1F8,1000000);Put(pr+0x2C,road);
            Put(road+0xE0,-160);Put(road+0xE4,160);
            long actors=Alloc(64*0x800);long[] bikes=new long[32],riders=new long[32];
            for(int i=0;i<32;i++) {
                long bike=actors+i*0x1000,rider=bike+0x800;bikes[i]=bike;riders[i]=rider;
                Put(bike+0xC4,4);Put(bike+0x2F4,2);Put(bike+0x2FC,rider);
                Put(rider+0xC4,8);Put(rider+0x1C4,bike);Put(rider+0x2C,road);Put(rider+0x140,road);Put(rider+0x1F8,1000000);
            }
            Func<int,ulong> sample=delegate(int index){return run(new IntPtr(bikes[index]),new IntPtr(config));};
            for(int setting=12;setting<=24;setting+=4)Put(config+setting,0);
            bool baseline=true;for(int t=0;t<3000;t+=7){Put(image+0x2036E8,t);baseline &= Speed(sample(0))==11400&&Lane(sample(0))==0;}
            Check(baseline,"all four levels off preserve exact base speed and straight pursuit");
            for(int setting=12;setting<=24;setting+=4)Put(config+setting,2);
            HashSet<string> patterns=new HashSet<string>();bool bounded=true,smooth=true,lanes=false;int min=int.MaxValue,max=0;
            for(int i=0;i<32;i++) {
                int prev=0;string pattern="";
                for(int t=0;t<3600;t++) {
                    Put(image+0x2036E8,t);ulong value=sample(i);int speed=Speed(value),lane=Lane(value);
                    bounded &= speed>=7410&&speed<=16530&&Math.Abs(lane)<=48*256;
                    if(t>0)smooth &= Math.Abs(speed-prev)<=400;
                    if(t%600==0)pattern+=speed+":"+lane+",";
                    lanes |= lane!=0;min=Math.Min(min,speed);max=Math.Max(max,speed);prev=speed;
                }
                patterns.Add(pattern);
            }
            Check(patterns.Count==32,"32 AI have independent pacing and lane patterns");
            Check(bounded&&smooth&&lanes&&min<11400&&max>12000,"smooth bounded bursts recovery and lateral motion; speed range="+min+".."+max);
            Put(image+0x2036E8,777);ulong paused=sample(0);for(int i=0;i<500;i++)sample(i%32);
            Check(sample(0)==paused,"repeated hooks and paused simulation do not advance or reset rhythm");
            Put(riders[0]+0x2C,0);Put(riders[0]+0x140,0);ulong offline=sample(0);
            Check(Speed(offline)==Speed(paused)&&Lane(offline)==0,"unloaded AI shares exact same speed without reading a null road");
            Put(riders[0]+0x2C,road);Put(riders[0]+0x140,road);
            Check(sample(0)==paused,"re-entry restores same rhythm without a new sprint");
            Put(config+12,0);Put(config+16,0);Put(config+24,0);
            Put(riders[0]+0x1F8,1003000);int behind=Speed(sample(0));Put(riders[0]+0x1F8,997000);int ahead=Speed(sample(0));
            Check(behind>11400&&ahead<11400&&behind<=13110&&ahead>=9690,"distance assistance is symmetric and capped at medium 15 percent");
            Put(riders[0]+0x1F8,1000000);Put(config+16,2);Put(config+20,0);
            int bursts=0;bool rests=false;bool wasBurst=false;
            for(int t=0;t<7200;t++) { Put(image+0x2036E8,t);int s=Speed(sample(0));bool burst=s>12540;if(burst&&!wasBurst)bursts++;rests|=s<10830;wasBurst=burst; }
            Check(bursts>=3&&bursts<=8&&rests,"medium sprint has separated bursts cooldown and slower recovery");
            int[] counts=new int[4];
            for(int level=0;level<=3;level++) {
                Put(config+16,level);bool active=false;
                for(int t=0;t<14400;t++) {Put(image+0x2036E8,t);bool now=Speed(sample(0))>12540;if(now&&!active)counts[level]++;active=now;}
            }
            Check(counts[0]==0&&counts[1]<counts[2]&&counts[2]<counts[3],"sprint frequency levels increase independent burst counts: "+string.Join(",",counts));
            for(int setting=12;setting<=24;setting+=4)Put(config+setting,2);
            foreach(int state in new int[]{0,1,3}) {Put(bikes[0]+0x2F4,state);Check(Speed(sample(0))==11400&&Lane(sample(0))==0,"riding falling and remount states excluded "+state);}
            Put(bikes[0]+0x2F4,2);Put(riders[0]+0xC0,16);
            Check(Speed(sample(0))==11400&&Lane(sample(0))==0,"finished runner excluded from variation");Put(riders[0]+0xC0,0);
            Check(Lane(run(new IntPtr(player),new IntPtr(config)))==0,"player steering untouched");
            Put(road+0xE0,150);Put(road+0xE4,-150);
            Check(Lane(sample(0))==0,"invalid road edges suppress lane adjustment");Put(road+0xE0,-160);Put(road+0xE4,160);
            Put(config+8,2147483640);bool huge=true;
            for(int t=0;t<3000;t+=17){Put(image+0x2036E8,t);huge &= Speed(sample(0))>0&&Speed(sample(0))<=int.MaxValue;}
            Check(huge,"large configured speed cannot wrap negative during a sprint");Put(config+8,11400);
            // Behavioral race model: equal starting pace can produce overtakes in both
            // directions, using actual native outputs rather than expected code bytes.
            double playerPos=0;double[] pos=new double[32];bool[] passedPlayer=new bool[32],passedAi=new bool[32];
            for(int i=0;i<32;i++)pos[i]=30+i*8;
            for(int t=0;t<7200;t++) {
                Put(image+0x2036E8,t);Put(pr+0x1F8,1000000-(int)playerPos);
                for(int i=0;i<32;i++) {
                    double before=pos[i]-playerPos;Put(riders[i]+0x1F8,1000000-(int)pos[i]);
                    pos[i]+=Speed(sample(i))/256.0;double after=pos[i]-(playerPos+12000/256.0);
                    if(before>=0&&after<0)passedAi[i]=true;if(before<=0&&after>0)passedPlayer[i]=true;
                }
                playerPos+=12000/256.0;
            }
            int exchanges=0;for(int i=0;i<32;i++)if(passedAi[i]&&passedPlayer[i])exchanges++;
            Check(exchanges>=4,"10x player and 9.5x AI exchange overtakes; AI count="+exchanges);
            Console.WriteLine("Run AI tests: "+passed+" passed");return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
