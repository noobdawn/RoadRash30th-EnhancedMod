using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

internal static partial class RaceTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at, UIntPtr size, uint type, uint protection);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr process, IntPtr at, UIntPtr size);
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")] static extern IntPtr AddVectoredExceptionHandler(uint first,NativeException handler);
    [DllImport("kernel32.dll")] static extern void ExitProcess(uint code);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int NativeException(IntPtr pointers);
    static long diagnosticImage;
    static NativeException diagnosticHandler=delegate(IntPtr pointers) {
        IntPtr record=Marshal.ReadIntPtr(pointers),context=Marshal.ReadIntPtr(pointers,4);
        if(unchecked((uint)Marshal.ReadInt32(record))==0xC0000005) {
            Console.Error.WriteLine("Native AV: image={0:X8} RVA={1:X} EAX={2:X8} ECX={3:X8} EDX={4:X8} ESI={5:X8} EDI={6:X8}",diagnosticImage,(uint)Marshal.ReadInt32(context,184)-diagnosticImage,Marshal.ReadInt32(context,176),Marshal.ReadInt32(context,172),Marshal.ReadInt32(context,168),Marshal.ReadInt32(context,160),Marshal.ReadInt32(context,156));
            ExitProcess(77);
        }
        return 0;
    };
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RunCode();
    static long Alloc(int n) { return (uint)VirtualAlloc(IntPtr.Zero,(UIntPtr)n,0x3000,0x40).ToInt32(); }
    static void Write(long p, byte[] b) { Marshal.Copy(b,0,new IntPtr(p),b.Length); FlushInstructionCache(new IntPtr(-1),new IntPtr(p),(UIntPtr)b.Length); }
    static void Put(long p, long v) { Marshal.WriteInt32(new IntPtr(p),unchecked((int)v)); }
    static int Get(long p) { return Marshal.ReadInt32(new IntPtr(p)); }
    static void D(List<byte> b, long n) { b.AddRange(BitConverter.GetBytes(unchecked((int)n))); }
    static void Jump(long at, long to, int n) { List<byte> b=new List<byte>{0xE9};D(b,to-at-5);while(b.Count<n)b.Add(0x90);Write(at,b.ToArray()); }
    static void Run(long code,long target,long ecx) {
        List<byte> b=new List<byte>{0x9C,0x60,0xB9};D(b,ecx);b.Add(0xE8);D(b,target-(code+b.Count+4));b.AddRange(new byte[]{0x61,0x9D,0xC3});Write(code,b.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
    }
    static int passed;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name); }
    static long MapGame(string path)
    {
        byte[] file=File.ReadAllBytes(path);int pe=BitConverter.ToInt32(file,0x3C),opt=pe+24,size=BitConverter.ToInt32(file,opt+56);
        long image=Alloc(size),preferred=BitConverter.ToUInt32(file,opt+28);
        Marshal.Copy(file,0,new IntPtr(image),BitConverter.ToInt32(file,opt+60));
        int section=opt+BitConverter.ToUInt16(file,pe+20);
        for(int i=0;i<BitConverter.ToUInt16(file,pe+6);i++,section+=40) {
            int length=BitConverter.ToInt32(file,section+16);
            if(length!=0)Marshal.Copy(file,BitConverter.ToInt32(file,section+20),new IntPtr(image+BitConverter.ToInt32(file,section+12)),length);
        }
        int reloc=BitConverter.ToInt32(file,opt+136),end=reloc+BitConverter.ToInt32(file,opt+140);
        while(reloc<end) {
            int page=Get(image+reloc),block=Get(image+reloc+4);if(block<8)throw new Exception("relocation");
            for(int entry=8;entry<block;entry+=2) {
                int fix=(ushort)Marshal.ReadInt16(new IntPtr(image+reloc+entry));
                if((fix>>12)==3) { long at=image+page+(fix&0xFFF);Put(at,(uint)Get(at)+image-preferred); }
            }
            reloc+=block;
        }
        diagnosticImage=image;return image;
    }
    static void StubAnimation(long image) {
        // Audio/animation and UI notifications are external to the decision code under test.
        Write(image+0x428C,new byte[]{0x88,0x51,0x10,0xC3});
        Write(image+0x5FB0,new byte[]{0xC3});Write(image+0x1DFC,new byte[]{0x88,0x51,0x10,0xB0,1,0xC3});
        Write(image+0x4AA2,new byte[]{0xB0,1,0xC3});Write(image+0x12CB,new byte[]{0xC3});
        Write(image+0x2C98,new byte[]{0xC3});
    }
    static void CheckGame(string path) {
        long image=MapGame(path),code=Alloc(0x4000),bike=Alloc(0x1000),rider=Alloc(0x1000),profile=Alloc(0x1000),road=Alloc(0x1000),other=Alloc(0x1000);
        StubAnimation(image);
        for(int i=0;i<3;i++) {
            bool match=true;for(int n=0;n<RuntimePatch.RaceOriginals[i].Length;n++)match &= Marshal.ReadByte(new IntPtr(image+RuntimePatch.RaceSites[i]+n))==RuntimePatch.RaceOriginals[i][n];
            Check(match,"installed game race signature "+i);
            match=true;for(int n=0;n<RuntimePatch.RaceWorldOriginals[i].Length;n++)match &= Marshal.ReadByte(new IntPtr(image+RuntimePatch.RaceWorldSites[i]+n))==RuntimePatch.RaceWorldOriginals[i][n];
            Check(match,"installed game streaming signature "+i);
        }
        Put(bike+0xC4,4);Put(bike+0x2FC,rider);Put(rider+0xC4,8);Put(rider+0x1C4,bike);Put(rider+0x1D8,profile);
        Put(road+8,road+0x400);Put(road+12,road+0x400);Put(road+0x418,road);Put(road+0x41C,road);
        Put(road+0x408,road);Put(road+0x40C,road);Put(road+0x18,road+0x400);Put(road+0x1C,road+0x400);
        for(int site=1;site<=2;site++) for(int scope=0;scope<3;scope++) for(int player=0;player<2;player++) {
            Write(image+RuntimePatch.RaceSites[site],RuntimePatch.RaceOriginals[site]);
            Write(code+0x800,RuntimePatch.BuildRecoveryCode(image,code+0x800,site,true,scope,3,96));
            Jump(image+RuntimePatch.RaceSites[site],code+0x800,11);
            Put(image+0x1A69EC,player==1?bike:other);Put(bike+0x324,site==1?2:-1);Put(bike+0x2F4,2);Put(bike+0x2F0,1000);
            Put(rider+0x80,0x26);Put(rider+0xC0,0);Put(profile+0x138,5000);Put(rider+0x1C,220);Put(bike+0x1C,900000);
            Put(rider+0x28,200);Put(bike+0x28,6200);Put(rider+0x2C,road);Put(rider+0x140,road);Put(bike+0x140,road);
            Run(code,image+(site==1?0x17C60:0x19F20),bike);
            bool selected=scope==2||(scope==0&&player==0)||(scope==1&&player==1);
            Check(Get(bike+0x2F4)==2 && Get(rider+0xEC)==1200 && Get(rider+0x128)==0,"original chase keeps running beyond timeout path="+site+" scope="+scope+" player="+player);
            Check(Get(bike+0x1C)==(selected?316:900000) && (!selected||Get(bike+0x2C)==road+0x400),"bike scope and native segment transition path="+site+" scope="+scope+" player="+player);
            Write(image+RuntimePatch.RaceSites[site],RuntimePatch.RaceOriginals[site]);
        }
        for(int site=1;site<=2;site++) for(int policy=0;policy<4;policy++) for(int player=0;player<2;player++) {
            Write(code+0x800,RuntimePatch.BuildRecoveryCode(image,code+0x800,site,false,2,policy,96));Jump(image+RuntimePatch.RaceSites[site],code+0x800,11);
            Put(image+0x1A69EC,player==1?bike:other);Put(bike+0x324,site==1?2:-1);Put(bike+0x2F4,2);Put(rider+0x80,0x26);Put(profile+0x138,5000);
            Put(rider+0x1C,200);Put(bike+0x1C,900000);
            Run(code,image+(site==1?0x17C60:0x19F20),bike);
            bool allow=policy==0||(policy==1&&player==0)||(policy==2&&player==1);
            Check(Get(bike+0x2F4)==(allow?3:2),"native timed recovery policy="+policy+" player="+player+" path="+site);
            // Disabling the timer must preserve the ordinary nearby-bike remount.
            Put(bike+0x2F4,2);Put(rider+0x80,0x26);Put(bike+0x1C,200);Put(bike+0x28,Get(rider+0x28)+6000);
            Run(code,image+(site==1?0x17C60:0x19F20),bike);
            Check(Get(bike+0x2F4)==3,"normal nearby remount policy="+policy+" path="+site);
            Write(image+RuntimePatch.RaceSites[site],RuntimePatch.RaceOriginals[site]);
        }
        // Repeated original decisions, with advancing rider coordinates and an increasing timer.
        Write(code+0x800,RuntimePatch.BuildRecoveryCode(image,code+0x800,1,true,2,3,96));Jump(image+0x17CA7,code+0x800,11);
        Put(image+0x1A69EC,other);Put(bike+0x324,2);Put(bike+0x2F4,2);Put(rider+0x80,0x26);Put(rider+0xC0,0);
        bool continuous=true;
        for(int tick=0;tick<10000;tick++) {
            Put(rider+0x1C,tick*4);Put(rider+0x2C,road);Put(rider+0x140,road);Put(profile+0x138,tick);
            Run(code,image+0x17C60,bike);
            continuous &= Get(bike+0x1C)==tick*4+96 && Get(rider+0xEC)==1200 && Get(rider+0x128)==0 && Get(bike+0x2F4)==2;
        }
        Check(continuous,"10000 original AI decisions continuously chase ahead beyond recovery deadline");
        Put(rider+0xC0,0x10);Put(bike+0x1C,77777);Run(code,image+0x17C60,bike);
        Check(Get(bike+0x1C)==77777,"finished riders stop moving their bikes");
        CheckUnloadCollision(path);
        CheckReentryPose(path);
        CheckFallAnimation(path);
        CheckFixInstallation(path);
        CheckRunAiIntegration(path);
        CheckPavedRoad(path);
        CheckGuideCollision(path);
        CheckGuideCollisionSweep(path);
        CheckPackedRunStart(path, 128, 60);
        CheckPackedRunStart(path, 9999, 1);
        CheckStart(image,code,bike,rider,other);
        CheckWorld(image,code,bike,rider,other);
        CheckRiderRoadGuard(path);
    }
    static void CheckFallAnimation(string path) {
        long image=MapGame(path),code=Alloc(0x4000),bike=Alloc(0x1000),rider=Alloc(0x1000),car=Alloc(0x1000),animations=Alloc(0x4000),profile=Alloc(0x1000);
        Put(bike+0xC4,4);Put(bike+0x2FC,rider);Put(bike+0x324,2);Put(bike+0x18,1);
        Put(rider+0xC4,8);Put(rider+0x1C4,bike);Put(rider+0x1A0,car);Put(rider+0x174,100);
        Put(rider+0x1D8,profile);Put(bike+0x2F0,1000);
        Put(car+0xC4,16);Put(car+0xEC,3000);Put(car+0x174,100);Put(car+0x17C,100);
        Put(rider+0x78,animations);Put(image+0x1A69EC,car);
        // Two-frame animation resources with an end/loop marker. Execute the real
        // setters, queue, timer and falling-state decisions; stub only external
        // audio, collision integration and randomness. No game window or main loop.
        for(int animation=0;animation<=0x26;animation++) {
            long frames=animations+0x400+animation*0x40;Put(animations+animation*4,frames);
            for(int frame=0;frame<2;frame++) {
                Marshal.WriteByte(new IntPtr(frames+frame*20+12),1);
                Marshal.WriteByte(new IntPtr(frames+frame*20+13),(byte)frame);
                Put(frames+frame*20+16,1);
            }
        }
        Write(image+0x2A6D,new byte[]{0xC2,4,0});Write(image+0x35D5,new byte[]{0xC2,4,0});
        Write(image+0x3882,new byte[]{0x33,0xC0,0xC3});Write(image+0x12CB,new byte[]{0xC3});
        RuntimePatch.ApplyRace(Process.GetCurrentProcess().Handle,image,true,2,3,96,delegate(string s){});
        RuntimePatch.ApplyFixes(Process.GetCurrentProcess().Handle,image,false,true,delegate(string s){});
        RuntimePatch.ApplyTrafficDriving(Process.GetCurrentProcess().Handle,image,1,1,true,delegate {});
        Put(bike+0x2F4,2);Put(rider+0x80,0xFF0026);Put(rider+0xEC,4800);Put(image+0x2036E8,100);
        Run(code,image+0x13F80,rider);
        Check(Get(bike+0x2F4)==1&&Marshal.ReadByte(new IntPtr(rider+0x80))==0x22,"native traffic impact enters fallen state and hit animation");
        List<string> trace=new List<string>();bool noRunningFrame=true;
        for(int tick=100;tick<108;tick++) {
            Put(image+0x2036E8,tick);Run(code,image+0x19920,bike);
            int current=Marshal.ReadByte(new IntPtr(rider+0x80)),next=Marshal.ReadByte(new IntPtr(rider+0x82));
            trace.Add("tick="+tick+" state="+Get(bike+0x2F4)+" current="+current.ToString("X2")+" next="+next.ToString("X2"));
            noRunningFrame &= current!=0x26;
        }
        Console.WriteLine("FALL ANIMATION TRACE: "+String.Join("; ",trace.ToArray()));
        Check(noRunningFrame,"fixed impact never inserts running animation during native tumbling");
        Dictionary<int,string> nativePhysics=new Dictionary<int,string>();
        for(int enabled=0;enabled<2;enabled++)for(int longRun=0;longRun<2;longRun++)for(int scope=0;scope<3;scope++)for(int player=0;player<2;player++) {
            for(int i=0;i<3;i++)Write(image+RuntimePatch.RaceSites[i],RuntimePatch.RaceOriginals[i]);
            for(int i=0;i<RuntimePatch.RaceWorldSites.Length;i++)Write(image+RuntimePatch.RaceWorldSites[i],RuntimePatch.RaceWorldOriginals[i]);
            Write(image+RuntimePatch.RunGuideCollisionSite,RuntimePatch.RunGuideCollisionOriginal);
            Write(image+RuntimePatch.FallAnimationSite,RuntimePatch.FallAnimationOriginal);
            int recovery=(scope+player)%4;
            RuntimePatch.ApplyRace(Process.GetCurrentProcess().Handle,image,longRun==1,scope,recovery,96,delegate(string s){});
            RuntimePatch.ApplyFixes(Process.GetCurrentProcess().Handle,image,false,enabled==1,delegate(string s){});
            Put(image+0x1A69EC,player==1?bike:car);
            bool selected=enabled==1;
            foreach(int speed in new int[]{1200,4800,12000,120000}) {
                string label=" fix="+enabled+" run="+longRun+" scope="+scope+" player="+player+" recovery="+recovery+" speed="+speed;
                Put(bike+0x2F4,2);Put(rider+0x80,0xFF0026);Put(rider+0xEC,speed);Put(rider+0xF4,0);
                Put(rider+0x20,0);Put(rider+0x28,0);Put(rider+0x184,111);Put(rider+0x188,222);Put(rider+0x18C,333);
                Put(image+0x2036E8,100);Run(code,image+0x13F80,rider);
                string physics=Get(bike+0x2F4)+","+Get(rider+0xEC)+","+Get(rider+0xF4)+","+Get(rider+0x20)+","+Get(rider+0x28)+","+Get(rider+0x184)+","+Get(rider+0x188)+","+Get(rider+0x18C);
                if(enabled==0&&scope==0&&player==0)nativePhysics[speed]=physics;
                Check(physics==nativePhysics[speed],"animation correction preserves native collision physics and pending-impulse cleanup"+label);
                bool sawRun=false,allFallen=true;
                for(int tick=100;tick<108;tick++) {
                    Put(image+0x2036E8,tick);Run(code,image+0x19920,bike);
                    sawRun |= Marshal.ReadByte(new IntPtr(rider+0x80))==0x26;allFallen &= Get(bike+0x2F4)==1;
                }
                Check(allFallen&&sawRun==!selected,"animation fix toggle works independently of race mode and scope"+label);
                if(selected) {
                    // Another traffic impact before recovery must not restart running.
                    Run(code,image+0x13F80,rider);bool repeatedFall=true;
                    for(int tick=108;tick<116;tick++) {
                        Put(image+0x2036E8,tick);Run(code,image+0x19920,bike);
                        repeatedFall &= Get(bike+0x2F4)==1&&Marshal.ReadByte(new IntPtr(rider+0x80))!=0x26;
                    }
                    Check(repeatedFall,"second impact during tumbling keeps falling animation"+label);
                }
                // Model the end of sliding, then let original decisions and the
                // real animation clock complete get-up -> standing -> running.
                Put(rider+0xEC,0);Put(rider+0x20,0);Put(image+0x2036E8,120);Run(code,image+0x19920,bike);
                Check(Get(bike+0x2F4)==2&&Marshal.ReadByte(new IntPtr(rider+0x80))==0x24&&Marshal.ReadByte(new IntPtr(rider+0x82))==0x25,"native recovery still begins get-up and queues standing"+label);
                bool resumed=false;
                for(int tick=121;tick<136;tick++) {
                    Put(image+0x2036E8,tick);Run(code,image+0x17C60,bike);Run(code,image+0x19920,bike);
                    if(Marshal.ReadByte(new IntPtr(rider+0x80))==0x26){resumed=true;break;}
                }
                Check(resumed&&Get(bike+0x2F4)==2,"native stand-up completes and running resumes without animation lock"+label);
            }
        }
    }
    static void CheckFixInstallation(string path) {
        for(int audio=0;audio<2;audio++)for(int animation=0;animation<2;animation++) {
            long image=MapGame(path);byte[] before=new byte[0x2E6],after=new byte[0x2E6];
            Marshal.Copy(new IntPtr(image+0x13F80),before,0,before.Length);
            List<string> logs=new List<string>();RuntimePatch.ApplyFixes(Process.GetCurrentProcess().Handle,image,audio==1,animation==1,logs.Add);
            Marshal.Copy(new IntPtr(image+0x13F80),after,0,after.Length);bool exact=true;
            for(int i=0;i<after.Length;i++)exact &= after[i]==(animation==1&&i==0x14125-0x13F80?(byte)0x1C:before[i]);
            Check(exact,"independent animation fix changes only the native next-action immediate audio="+audio+" animation="+animation);
            Check(Marshal.ReadByte(new IntPtr(image+0x45AD5))==(audio==1?0xE9:0x6A)&&Marshal.ReadByte(new IntPtr(image+0x4546D))==(audio==1?0x83:0xFF),"audio fix installation follows its own toggle audio="+audio+" animation="+animation);
            Check(logs.Count==audio+animation,"enabled fixes report installation independently audio="+audio+" animation="+animation);
        }
    }
    static void CheckReentryPose(string path) {
        long image=MapGame(path),code=Alloc(0x4000),bike=Alloc(0x1000),rider=Alloc(0x1000),road=Alloc(0x1000);
        long camera=Alloc(0x1000),angles=Alloc(0x1000),other=Alloc(0x1000);
        StubAnimation(image);Write(image+0x16046,new byte[]{0xC3});
        Put(bike+0xC4,4);Put(bike+0x2FC,rider);Put(rider+0xC4,8);Put(rider+0x1C4,bike);
        Put(bike+0x2C,road);Put(bike+0x140,road);Put(bike+0x1C,220);Put(bike+0x28,0);Put(camera+0x18,10000);
        // Native render-angle helper: ECX=actor, EDX=angles, stack=camera.
        List<byte> render=new List<byte>{0x9C,0x60,0xB9};D(render,rider);render.Add(0xBA);D(render,angles);
        render.Add(0x68);D(render,camera);render.Add(0xE8);D(render,image+0x28140-(code+0x1000+render.Count+4));
        render.AddRange(new byte[]{0x61,0x9D,0xC3});Write(code+0x1000,render.ToArray());
        RunCode renderPose=(RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code+0x1000),typeof(RunCode));
        Write(code+0x2000,RuntimePatch.BuildRaceWorldCode(image,code+0x2000,2,2,3,1,1));
        foreach(int bank in new int[]{-0x280000,0,0x280000})foreach(int heading in new int[]{-0x100000,0,0x100000})for(int player=0;player<2;player++) {
            Put(image+0x1A69EC,player==1?bike:other);Put(bike+0x2F4,2);
            Put(bike+0x58,0x080000);Put(bike+0x5C,-0x400000);Put(bike+0x60,bank);
            Put(rider+0x58,0);Put(rider+0x5C,heading);Put(rider+0x60,0);Put(rider+0x128,heading);
            Put(rider+0x64,123);Put(rider+0x68,456);Put(rider+0x6C,789);
            Put(rider+0xC0,0xA4);Put(rider+0x80,0);WorldRun(code,code+0x2000,bike);
            string label=" bank="+bank+" heading="+heading+" player="+player;
            if(Get(rider+0x60)!=0||Get(rider+0x5C)!=heading) {
                Run(code,image+0x4A0B0,rider);renderPose();
                Console.Error.WriteLine("POSE REGRESSION: movement={0}, display heading={1}, actor bank={2}, native render bank={3}",Get(rider+0x128),Get(rider+0x5C),Get(rider+0x60),Get(angles+8));
            }
            Check(Get(rider+0x58)==0&&Get(rider+0x60)==0&&Get(rider+0x5C)==heading,"re-entry discards hidden bike pose and faces running direction"+label);
            Check(Get(rider+0x128)==heading&&Get(rider+0x1C)==220&&Get(rider+0x28)==0&&Get(bike+0x60)==bank,"pose reset preserves actual direction and streamed position"+label);
            renderPose();
            Check(Get(angles+4)==heading&&Get(angles+8)==0,"ORIGINAL sprite angle helper receives upright runner with correct heading"+label);
            for(int frame=0;frame<3;frame++){Run(code,image+0x4A0B0,rider);renderPose();}
            Check(Get(angles+4)==heading&&Get(angles+8)==0,"subsequent native render refresh cannot restore the motorcycle lean"+label);
        }
        Put(bike+0x2F4,0);Put(bike+0x58,0x080000);Put(bike+0x5C,-0x400000);Put(bike+0x60,-0x280000);
        WorldRun(code,code+0x2000,bike);
        Check(Get(bike+0x2F4)==0&&Get(rider+0x58)==0x080000&&Get(rider+0x5C)==-0x400000&&Get(rider+0x60)==-0x280000,"normal mounted re-entry still copies motorcycle pose unchanged");
    }
    static void CheckGuideCollision(string path) {
        long image=MapGame(path),code=Alloc(0x4000),first=Alloc(0x1000),second=Alloc(0x1000),outcome=Alloc(0x1000);
        bool signature=true;for(int i=0;i<6;i++)signature &= Marshal.ReadByte(new IntPtr(image+RuntimePatch.RunGuideCollisionSite+i))==RuntimePatch.RunGuideCollisionOriginal[i];
        Check(signature,"installed game guide collision entry matches before original ram/contact hooks");
        int[] exits = { 0x49616, 0x496C2, 0x496D0 };
        for(int i=0;i<exits.Length;i++) {
            List<byte> stub=new List<byte>{0xC7,0x05};D(stub,outcome);D(stub,i);stub.Add(0xC3);Write(image+exits[i],stub.ToArray());
        }
        for(int scope=0;scope<3;scope++)for(int player=0;player<2;player++)for(int side=0;side<2;side++)foreach(int kind in new int[]{4,8,16})foreach(int state in new int[]{0,1,2,3}) {
            long selected=side==0?first:second,other=side==0?second:first;
            Put(selected+0xC4,kind);Put(selected+0x2F4,state);Put(other+0xC4,8);Put(other+0x2F4,2);
            Put(selected+0xC0,7);Put(other+0xC0,7);Put(image+0x1A69EC,player==1?selected:0);
            Write(code+0x1000,RuntimePatch.BuildRunGuideCollisionCode(image,code+0x1000,scope));
            Jump(image+RuntimePatch.RunGuideCollisionSite,code+0x1000,6);
            List<byte> b=new List<byte>{0x9C,0x60,0xBB};D(b,first);b.Add(0xBE);D(b,second);b.Add(0xBF);D(b,0x13572468);
            b.Add(0xE8);D(b,image+RuntimePatch.RunGuideCollisionSite-(code+b.Count+4));
            b.AddRange(new byte[]{0x89,0x1D});D(b,outcome+4);b.AddRange(new byte[]{0x89,0x35});D(b,outcome+8);b.AddRange(new byte[]{0x89,0x3D});D(b,outcome+12);b.AddRange(new byte[]{0x61,0x9D,0xC3});Write(code,b.ToArray());
            ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
            bool skip=kind==4&&state==2&&(scope==2||(scope==0&&player==0)||(scope==1&&player==1));
            int expectedExit = !skip ? 0 : (side==0 ? 2 : 1);
            Check(Get(outcome)==expectedExit&&Get(outcome+4)==first&&Get(outcome+8)==second&&Get(outcome+12)==0x13572468,
                "guide pair filter preserves real collisions and registers scope="+scope+" player="+player+" side="+side+" kind="+kind+" state="+state);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RecordContact(IntPtr outer, IntPtr candidate);

    static void CheckGuideCollisionSweep(string path) {
        // All actors overlap. Record the ordered contacts produced by the real
        // game's sweep, replacing only the contact response in this offline image.
        int[] kinds = { 8, 4, 16, 4, 4, 8, 4, 4, 4 };
        int[] states = { 2, 2, 0, 0, 2, 2, 1, 3, 2 };
        const int stride = 0x320, player = 1, tick = 43;
        long image = MapGame(path), code = Alloc(0x4000), actors = Alloc((kinds.Length + 1) * stride);
        long chain = Alloc(0x1000), road = Alloc(0x1000);
        Put(chain, chain + 0x100); Put(chain + 0x100, actors);
        Put(image + 0x2036E8, tick); Put(image + 0x1A69EC, actors + player * stride);
        Marshal.WriteByte(new IntPtr(road + 0x36), 3);
        for (int i = 0; i < kinds.Length; i++) {
            long actor = actors + i * stride;
            Put(actor, actor + stride); Put(actor + 0x10, tick); Put(actor + 0x2C, road);
            Put(actor + 0xC0, 7); Put(actor + 0xC4, kinds[i]); Put(actor + 0x2F4, states[i]);
            Put(actor + 0x174, 32); Put(actor + 0x178, 32); Put(actor + 0x17C, 24);
        }
        var contacts = new List<int>();
        RecordContact record = delegate(IntPtr first, IntPtr second) {
            int i = (int)((first.ToInt64() - actors) / stride);
            int j = (int)((second.ToInt64() - actors) / stride);
            contacts.Add(i * kinds.Length + j);
        };
        var callback = new List<byte> { 0x56,0xFF,0x75,0xF8,0xB8 }; // candidate and outer
        D(callback, Marshal.GetFunctionPointerForDelegate(record).ToInt64());
        callback.AddRange(new byte[]{0xFF,0xD0});
        Write(code + 0x2000, callback.ToArray());
        Jump(code + 0x2000 + callback.Count, image + 0x496C2, 5);
        Jump(image + 0x4968E, code + 0x2000, 5);
        for (int scope = 0; scope < 3; scope++) {
            Write(code + 0x1000, RuntimePatch.BuildRunGuideCollisionCode(image, code + 0x1000, scope));
            Jump(image + RuntimePatch.RunGuideCollisionSite, code + 0x1000, 6);
            var expected = new List<int>();
            bool[] guide = new bool[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
                guide[i] = kinds[i] == 4 && states[i] == 2 && (scope == 2 || (scope == 1) == (i == player));
            for (int i = 0; i < kinds.Length; i++)
                for (int j = i + 1; j < kinds.Length; j++)
                    if (!guide[i] && !guide[j]) expected.Add(i * kinds.Length + j);
            contacts.Clear();
            Run(code, image + 0x49590, chain);
            Check(contacts.SequenceEqual(expected), "native mixed sweep preserves riders, traffic, recovering bikes and contact order; scope=" + scope);
        }
        GC.KeepAlive(record);
    }
    static void CheckPackedRunStart(string path, int packed, int ticks) {
        // Start overlapping rows at rest, plus one runner clear of the pack.
        // Use native chase, steering, and integer position updates without any
        // collision response. This isolates decisions from contact impulses.
        const int stride = 0x1000;
        int count = packed + 1;
        long image = MapGame(path), code = Alloc(0x4000), actors = Alloc(count * stride);
        long road = Alloc(0x1000), ends = Alloc(0x1000), config;
        StubAnimation(image);
        long ai = RuntimePatch.PrepareRunAi(Process.GetCurrentProcess().Handle, image, 1, 2, 2, 2, 2, delegate {}, out config);
        Put(config + 28, 1234567);
        foreach (int offset in new int[] { 8, 12, 0x18, 0x1C }) Put(road + offset, road);
        Put(road + 0x2C, 512); Put(road + 0x30, 512); Put(road + 0xE0, -500); Put(road + 0xE4, 500);
        Marshal.WriteByte(new IntPtr(road + 0x36), 3);
        Write(code + 0x800, RuntimePatch.BuildRecoveryCode(image, code + 0x800, 1, true, 2, 3, 96, ai, config));
        Jump(image + RuntimePatch.RaceSites[1], code + 0x800, 11);
        Write(code + 0x1000, RuntimePatch.BuildWalkSpeedCode(image, code + 0x1000, 3, 1, 1, ai, config));
        Jump(image + RuntimePatch.CharacterSites[3], code + 0x1000, 10);
        var bikes = new long[count]; var riders = new long[count]; var start = new int[count];
        for (int i = 0; i < count; i++) {
            bikes[i] = actors + i * stride; riders[i] = bikes[i] + 0x400;
            start[i] = i == packed ? packed + 1024 : (i / 8) * 8;
        }
        foreach (int weave in new int[] { 0, 2 }) {
            Marshal.Copy(new byte[count * stride], 0, new IntPtr(actors), count * stride);
            Put(config + 24, weave);
            for (int i = 0; i < count; i++) {
                long b = bikes[i], r = riders[i], profile = b + 0x800;
                Put(b + 0xC4, 4); Put(b + 0x2F4, 2); Put(b + 0x2F0, 1000); Put(b + 0x324, 2); Put(b + 0x2FC, r);
                Put(r + 0xC4, 8); Put(r + 0xC0, 7); Put(r + 0x80, 0x26); Put(r + 0x18, 1);
                Put(r + 0x1C4, b); Put(r + 0x1D8, profile); Put(r + 0x2C, road); Put(r + 0x140, road);
                Put(r + 0x174, 12*256); Put(r + 0x178, 8); Put(r + 0x1C, start[i]); Put(r + 0x28, (i%8-4)*18*256);
            }
            int firstStep = 0;
            bool guidesAhead = true;
            for (int tick = 1; tick <= ticks; tick++) {
                Put(image + 0x2036E8, tick);
                long[] ordered = riders.OrderBy(r => Get(r + 0x1C)).ToArray();
                for (int i = 0; i < count; i++) {
                    Put(ordered[i], i+1 < count ? ordered[i+1] : ends);
                    Put(ordered[i]+4, i > 0 ? ordered[i-1] : ends+16);
                }
                // Plan at the same tick, then advance the entire pack on the same straight road.
                for (int i = 0; i < count; i++) {
                    Run(code, image + 0x17C60, bikes[i]);
                    guidesAhead &= Get(bikes[i]+0x1C) - Get(riders[i]+0x1C) == 96;
                }
                for (int i = 0; i < count; i++) Run(code, image + 0x4C8D0, riders[i]);
                if (tick == 1) firstStep = Enumerable.Range(0, packed).Count(i => Get(riders[i]+0x1C) > start[i]);
            }
            int moved = Enumerable.Range(0, packed).Count(i => Get(riders[i]+0x1C) > start[i]);
            int minimum = Enumerable.Range(0, packed).Min(i => Get(riders[i]+0x1C) - start[i]);
            int lead = Get(riders[packed]+0x1C) - start[packed];
            Console.WriteLine("PACK START weave={0} firstStep={1}/{2} advanced={3}/{2} minDistance={4} leaderDistance={5}", weave, firstStep, packed, moved, minimum, lead);
            Check(guidesAhead && lead >= ticks, "pack and isolated leader retain forward chase targets; weave=" + weave);
            Check(firstStep == packed && moved == packed && minimum >= ticks, "all packed runners make forward progress from rest at 1x; weave=" + weave);
        }
    }

    static void CheckPavedRoad(string path) {
        long image=MapGame(path),code=Alloc(0x4000),road=Alloc(0x1000),bike=Alloc(0x1000),rider=Alloc(0x1000),config;
        long ai=RuntimePatch.PrepareRunAi(Process.GetCurrentProcess().Handle,image,6,0,0,0,2,delegate(string s){},out config);
        Put(bike+0xC4,4);Put(bike+0x2F4,2);Put(bike+0x2FC,rider);Put(rider+0xC4,8);Put(rider+0x2C,road);Put(rider+0x140,road);
        // Execute the actual road-builder arithmetic, with its upstream decoded widths
        // and center as inputs. Stop before unrelated scenery/resource construction.
        Write(image+0x74DA6,new byte[]{0xC3});
        foreach(int center in new int[]{-750,0,900})foreach(int lanes in new int[]{1,2,4}) {
            int left=lanes*256,right=(lanes+1)*256;
            List<byte> b=new List<byte>{0x9C,0x60,0x55,0x89,0xE5,0x83,0xEC,0x10,0xC7,0x45,0xF8};D(b,center);
            b.AddRange(new byte[]{0xC7,0x45,0xFC});D(b,0);b.Add(0xB9);D(b,left);b.Add(0xBF);D(b,right);b.Add(0xBE);D(b,road);
            b.Add(0xE8);D(b,image+0x74D5D-(code+b.Count+4));b.AddRange(new byte[]{0x89,0xEC,0x5D,0x61,0x9D,0xC3});Write(code,b.ToArray());
            ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
            int lo=center-lanes*250,hi=center+(lanes+1)*250;
            Check(Get(road+0xE0)==lo&&Get(road+0xE4)==hi,"native road builder confirms paved edges center="+center+" lanes="+lanes);
            foreach(int x in new int[]{lo-10,(lo+hi)/2,hi+10}) {
                List<byte> q=new List<byte>{0xBA};D(q,x*256);q.Add(0xE8);D(q,image+0x4E180-(code+0x1000+q.Count+4));q.Add(0xA3);D(q,code+0x2000);q.Add(0xC3);Write(code+0x1000,q.ToArray());
                Run(code,code+0x1000,road);
                Check((Get(code+0x2000)==0)==(x>lo&&x<hi),"native surface classifier agrees with pavement x="+x);
            }
            Put(road+0x14C,20000);Put(road+0x158,30000);Put(rider+0x28,(lo+hi)*128);
            for(int tick=0;tick<300;tick++) {
                Put(image+0x2036E8,tick);
                List<byte> q=new List<byte>{0xBA};D(q,config);q.Add(0xE8);D(q,ai-(code+0x1000+q.Count+4));q.AddRange(new byte[]{0x89,0x15});D(q,code+0x2004);q.Add(0xC3);Write(code+0x1000,q.ToArray());
                Run(code,code+0x1000,bike);Put(rider+0x28,Get(rider+0x28)+Get(code+0x2004)/4);
            }
            Check(Get(rider+0x28)>lo*256&&Get(rider+0x28)<hi*256,"runner follows native-generated pavement despite misleading terrain extents");
        }
    }
    static void CheckRunAiIntegration(string path) {
        long image=MapGame(path),code=Alloc(0x4000),bike=Alloc(0x1000),rider=Alloc(0x1000),profile=Alloc(0x1000);
        long road=Alloc(0x1000),other=Alloc(0x1000),otherRider=Alloc(0x1000),node=Alloc(0x1000),desc=Alloc(0x1000),config;
        StubAnimation(image);
        long ai=RuntimePatch.PrepareRunAi(Process.GetCurrentProcess().Handle,image,9.5m,2,2,2,2,delegate(string s){},out config);
        Put(config+28,1234567);
        Put(bike+0xC4,4);Put(bike+0x2FC,rider);Put(rider+0xC4,8);Put(rider+0x1C4,bike);Put(rider+0x1D8,profile);
        Put(other+0xC4,4);Put(other+0x2FC,otherRider);Put(otherRider+0xC4,8);Put(otherRider+0x1F8,1000000);Put(otherRider+0x2C,road);
        Put(road+8,road+0x400);Put(road+12,road+0x400);Put(road+0x418,road);Put(road+0x41C,road);
        Put(road+0x408,road);Put(road+0x40C,road);Put(road+0x18,road+0x400);Put(road+0x1C,road+0x400);
        Put(road+0xE0,-160);Put(road+0xE4,160);
        List<byte> eval=new List<byte>{0xBA};D(eval,config);eval.Add(0xE8);D(eval,ai-(code+0x3000+eval.Count+4));
        eval.Add(0xA3);D(eval,code+0x3800);eval.AddRange(new byte[]{0x89,0x15});D(eval,code+0x3804);eval.Add(0xC3);Write(code+0x3000,eval.ToArray());
        bool turns=false;
        for(int site=1;site<=2;site++)for(int player=0;player<2;player++)foreach(int tick in new int[]{30,330,1234,2500}) {
            Put(image+0x1A69EC,player==1?bike:other);Put(image+0x2036E8,tick);
            Write(code+0x800,RuntimePatch.BuildRecoveryCode(image,code+0x800,site,true,2,3,96,ai,config));Jump(image+RuntimePatch.RaceSites[site],code+0x800,11);
            int speedSite=site==1?3:4;
            Write(code+0x1000,RuntimePatch.BuildWalkSpeedCode(image,code+0x1000,speedSite,10,9.5m,ai,config));Jump(image+RuntimePatch.CharacterSites[speedSite],code+0x1000,10);
            Put(bike+0x324,site==1?2:-1);Put(bike+0x2F4,2);Put(bike+0x2F0,1000);Put(bike+0x1C,900000);Put(bike+0x28,10000);Put(bike+0x140,road);
            Put(rider+0x80,0x26);Put(rider+0xC0,0);Put(profile+0x138,5000);Put(rider+0x1C,220);Put(rider+0x28,4000);Put(rider+0x2C,road);Put(rider+0x140,road);Put(rider+0x1F8,1000000);
            for(int warm=tick-20;warm<tick;warm++){Put(image+0x2036E8,warm);Run(code,code+0x3000,bike);}Put(image+0x2036E8,tick);
            Run(code,code+0x3000,bike);int expectedSpeed=player==1?12000:Get(code+0x3800),lane=Get(code+0x3804);
            Run(code,image+(site==1?0x17C60:0x19F20),bike);
            Check(Get(bike+0x2F4)==2&&Get(rider+0xEC)==expectedSpeed&&Get(bike+0x28)==10000+lane,
                "original chase combines variable speed and lane target path="+site+" player="+player+" tick="+tick);
            if(player==1)Check(Get(rider+0x128)==0&&lane==0,"player heading remains under original control path="+site+" tick="+tick);
            else turns |= Get(rider+0x128)!=0;
        }
        Check(turns,"native chase converts changing AI lane target into actual steering");
        Put(image+0x1A69EC,other);Put(node+0x10,desc);Put(desc+0x2C,100000);Put(node+0x14,100000);
        Write(code+0x2000,RuntimePatch.BuildRaceWorldCode(image,code+0x2000,1,2,3,10,9.5m,ai,config));Jump(image+0x15BE0,code+0x2000,6);
        foreach(int tick in new int[]{30,330,1234,2500}) {
            Put(image+0x2036E8,tick);Put(bike+0x304,node);Put(bike+0x300,0);Put(bike+0xC0,0);Put(bike+0x18,2);Put(bike+0x2F4,2);
            Put(bike+0x1C,1000);Put(bike+0x308,1000);Put(bike+0x314,9000);Put(bike+0x318,0);Put(rider+0x1F8,1000000);Put(rider+0x2C,0);Put(rider+0x140,0);
            Run(code,code+0x3000,bike);int step=Get(code+0x3800)*2/256;
            Run(code,image+0x15980,bike);
            Check(Get(bike+0x1C)==1000+step&&Get(rider+0x1F8)==1000000-step,"original offscreen simulation uses shared AI pace tick="+tick);
        }
        Put(road+8,road+0x400);Put(road+12,road+0x800);Put(road+0x4E4,0);
        foreach(int site in new int[]{1,2}) {
            Write(code+0x800,RuntimePatch.BuildRecoveryCode(image,code+0x800,site,true,2,3,96));Jump(image+RuntimePatch.RaceSites[site],code+0x800,11);
            int speedSite=site==1?3:4;
            Write(code+0x1000,RuntimePatch.BuildWalkSpeedCode(image,code+0x1000,speedSite,10,9.5m,ai,config));Jump(image+RuntimePatch.CharacterSites[speedSite],code+0x1000,10);
            Put(bike+0x324,site==1?2:-1);Put(bike+0x2F4,2);Put(bike+0x2F0,1000);Put(rider+0x80,0x26);
            Put(rider+0x1C,220);Put(rider+0x28,-12*256);Put(rider+0x2C,road);Put(rider+0x140,road);
            Run(code,image+(site==1?0x17C60:0x19F20),bike);
            Check(Get(bike+0x2C)==road+0x400&&Get(bike+0x28)==-12*256+6000,"native fork follows intended runner line before mounting offset path="+site);
        }
    }
    static void CheckStart(long image,long code,long bike,long rider,long other) {
        long chain=Alloc(0x1000),rider2=Alloc(0x1000),sentinel=Alloc(0x1000),count=Alloc(0x1000);
        Put(image+0x1C55D0,chain);Put(chain,chain+0x100);Put(chain+0x100,bike);Put(bike,other);Put(other,sentinel);
        Put(other+0xC4,4);Put(other+0x2FC,rider2);Put(rider2+0xC4,8);Put(rider2+0x1C4,other);Put(image+0x1A69EC,bike);
        Put(bike+0x300,1);Put(other+0x300,1);
        Write(image+0x6630C,new byte[]{0xC3});
        List<byte> poll=new List<byte>{0xFF,0x05};D(poll,count);poll.Add(0xC3);Write(image+0x3A08,poll.ToArray());
        for(int scope=0;scope<3;scope++) for(int tick=0;tick<3;tick++) {
            Put(image+0x2036E8,tick);Put(bike+0x2F4,0);Put(other+0x2F4,0);
            Write(code+0x1800,RuntimePatch.BuildRaceStartCode(image,code+0x1800,image+0x13B60,scope));
            Run(code,code+0x1800,0);
            Check(Get(bike+0x2F4)==(tick==1&&scope!=0?1:0)&&Get(other+0x2F4)==(tick==1&&scope!=1?1:0),"native start crash scope="+scope+" tick="+tick);
        }
        Check(Get(count)==9,"input polling replayed exactly once per tick");
        Put(other+0x300,0);Put(other+0x2F4,0);Put(image+0x2036E8,1);Run(code,code+0x1800,0);
        Check(Get(other+0x2F4)==2,"offscreen starter enters virtual running without unstreamed physics");
    }
    static void WorldRun(long code,long target,long bike) {
        List<byte> b=new List<byte>{0x9C,0x60,0xBE};D(b,bike);b.Add(0xBF);D(b,bike);b.Add(0xB9);D(b,bike);
        b.Add(0xE8);D(b,target-(code+b.Count+4));b.AddRange(new byte[]{0x61,0x9D,0xC3});Write(code,b.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
    }
    static void UnloadRun(long code,long image,long bike) {
        List<byte> b=new List<byte>{0x9C,0x60,0xB9};D(b,bike);
        b.AddRange(new byte[]{0x31,0xD2,0xE8});D(b,image+0x16300-(code+b.Count+4));
        b.AddRange(new byte[]{0x61,0x9D,0xC3});Write(code,b.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
    }
    static void CheckUnloadCollision(string path) {
        // Execute the complete native unload and collision passes, not just a hook
        // returning at 0x1641C: that missed the later clearing of both road pointers.
        long image=MapGame(path),code=Alloc(0x4000),bike=Alloc(0x1000),rider=Alloc(0x1000);
        long other=Alloc(0x1000),road=Alloc(0x1000),model=Alloc(0x1000),chain=Alloc(0x1000),sentinel=Alloc(0x1000);
        StubAnimation(image);
        Put(bike+0x2FC,rider);Put(rider+0x1C4,bike);Put(rider+0x1E0,model);
        Put(bike+0x324,2);Put(bike+0x30C,1);Put(bike+0x310,100);
        Put(image+0x2036E8,1);Put(rider+0x10,1);Put(other+0x10,1);
        Put(other+0xC0,7);Put(other+0x2C,road);Put(other+0x1C,128);
        Put(chain,chain+0x100);
        for(int scope=0;scope<3;scope++) for(int policy=0;policy<4;policy++) for(int player=0;player<2;player++) {
            bool protect=(scope==2||(scope==0&&player==0)||(scope==1&&player==1)) && (policy==3||(policy==1&&player==1)||(policy==2&&player==0));
            Put(image+0x1A69EC,player==1?bike:other);
            Write(code+0x2000,RuntimePatch.BuildRaceWorldCode(image,code+0x2000,0,scope,policy,1,1));
            Jump(image+0x16417,code+0x2000,5);
            Put(bike+0x300,1);Put(bike+0x2F4,2);Put(bike+0xC0,7);Put(rider+0xC0,0xA7);
            Put(bike+0x2C,road);Put(rider+0x2C,road);Put(bike+0x1C,128);Put(rider+0x1C,128);
            UnloadRun(code,image,bike);
            string label=" scope="+scope+" recovery="+policy+" player="+player;
            Check(Get(bike+0x2F4)==(protect?2:0)&&Get(bike+0x300)==0&&Get(bike+0x2C)==0&&Get(rider+0x2C)==0,"native unload clears road context"+label);
            Check(Get(rider+0xC0)==0xA4,"unloaded rider disables collision flags and preserves other flags"+label);
            // Rider can occur as either the outer actor or the candidate of an
            // on-road actor. Both must skip it before reading road + 0x36.
            Put(chain+0x100,rider);Put(rider,other);Put(other,sentinel);
            Run(code,image+0x49590,chain);
            Put(chain+0x100,other);Put(other,rider);Put(rider,sentinel);
            Run(code,image+0x49590,chain);
            Check(Get(rider+0x2C)==0&&Get(bike+0x2F4)==(protect?2:0),"native collision safely skips unloaded rider in both list positions"+label);
        }
    }
    static void CheckWorld(long image,long code,long bike,long rider,long other) {
        long node=Alloc(0x1000),desc=Alloc(0x1000);
        Put(node+0x10,desc);Put(desc+0x2C,100000);Put(node+0x14,100000);Put(bike+0x304,node);Put(bike+0x300,0);
        Put(rider+0xC0,0);Put(bike+0xC0,0);Put(bike+0x18,30);Put(other+0x2F4,0);
        Write(image+0x1641C,new byte[]{0xC3});
        Write(image+0x26E9,new byte[]{0xC7,0x81,0xF4,2,0,0,0,0,0,0,0xC3});
        Write(image+0x16046,new byte[]{0xC3});
        for(int scope=0;scope<3;scope++) for(int policy=0;policy<4;policy++) for(int player=0;player<2;player++) {
            Put(image+0x1A69EC,player==1?bike:other);
            bool protect=(scope==2||(scope==0&&player==0)||(scope==1&&player==1)) && (policy==3||(policy==1&&player==1)||(policy==2&&player==0));
            Write(code+0x2000,RuntimePatch.BuildRaceWorldCode(image,code+0x2000,0,scope,policy,2,0.5m));
            Put(bike+0x2F4,2);Put(bike+0x318,200);WorldRun(code,code+0x2000,bike);
            Check(Get(bike+0x2F4)==(protect?2:0)&&(!protect||Get(bike+0x318)==0),"unload retains walking scope="+scope+" recovery="+policy+" player="+player);
            Write(code+0x2000,RuntimePatch.BuildRaceWorldCode(image,code+0x2000,1,scope,policy,16,12.5m));Jump(image+0x15BE0,code+0x2000,6);
            Put(bike+0x2F4,2);Put(bike+0x1C,1000);Put(bike+0x308,1000);Put(bike+0x314,9000);Put(bike+0x318,0);Put(rider+0x1F8,100000);
            Run(code,image+0x15980,bike);
            int expected=(protect?(player==1?19200:15000):(player==1?4500:9000))*30/256;
            Check(Get(bike+0x1C)==1000+expected&&Get(rider+0x1F8)==100000-expected,"original virtual progress uses running speed scope="+scope+" recovery="+policy+" player="+player);
            Write(code+0x2000,RuntimePatch.BuildRaceWorldCode(image,code+0x2000,2,scope,policy,2,0.5m));
            Put(bike+0x2F4,2);Put(bike+0x2C,node);Put(bike+0x140,node);Put(bike+0x1C,2222);Put(rider+0x1C,1111);Put(rider+0x80,0);Put(rider+0xC0,0xA4);
            Put(bike+0x58,0x080000);Put(bike+0x5C,-0x400000);Put(bike+0x60,-0x280000);
            Put(rider+0x58,123);Put(rider+0x5C,456);Put(rider+0x60,789);Put(rider+0x128,0x100000);
            WorldRun(code,code+0x2000,bike);
            Check(Get(bike+0x2F4)==2&&Get(rider+0x1C)==(protect?2222:1111)&&Get(rider+0x80)==(protect?0x26:0),"native re-entry restores rider context and run animation scope="+scope+" recovery="+policy+" player="+player);
            Check(Get(rider+0xC0)==(protect?0xA7:0xA4)&&(!protect||Get(rider+0x2C)==node),"re-entry enables collision only after restoring rider road scope="+scope+" recovery="+policy+" player="+player);
            Check(Get(rider+0x58)==(protect?0:123)&&Get(rider+0x5C)==(protect?0x100000:456)&&Get(rider+0x60)==(protect?0:789),"re-entry pose repair respects long-run scope and recovery policy scope="+scope+" recovery="+policy+" player="+player);
        }
    }
    static void CheckRiderRoadGuard(string path) {
        IntPtr process = new IntPtr(-1); // current-process pseudo-handle remains valid across GC
        foreach (string mode in new[]{"ordinary", "police opponents", "player-only long run", "all long run"}) {
            long image = MapGame(path), code = Alloc(1024), bike = Alloc(1024), rider = Alloc(1024);
            long player = Alloc(1024), road = Alloc(512), calls = Alloc(16);
            RuntimePatch.ApplyRiderRoadGuard(process, image, delegate(string ignored){});
            RuntimePatch.ApplyRiderRoadGuard(process, image, delegate(string ignored){ throw new Exception("duplicate installation"); });
            if (mode == "police opponents") RuntimePatch.ApplyOpponents(process, image, 15, true, delegate(string ignored){});
            bool run = mode.EndsWith("long run"), restricted = mode == "player-only long run";
            RuntimePatch.ApplyRace(process, image, run, restricted ? 1 : 2, mode == "all long run" ? 3 : 0, 96, delegate(string ignored){});
            Put(image + 0x1a69ec, player); Put(image + 0x1f3a74, 1000);
            Put(bike + 0x324, 1); Put(bike + 0x328, 119); Put(bike + 0x2fc, rider); Put(bike + 0x1c, 1500);
            Put(rider + 0x1c4, bike); Put(rider + 0x1c, 800); Put(rider + 0x30, 750);
            Put(rider + 0x1f8, 200); Put(rider + 0x160, 1); Put(rider + 0x18, 1);
            Put(rider + 0x114, 64); Put(rider + 0xec, 100);
            Put(road + 0xe0, -20); Put(road + 0xe4, 20);
            Write(image + 0x5bc3, new byte[]{0xc3}); Write(image + 0x49f8, new byte[]{0xc3});
            Write(image + 0x193d, new byte[]{0x80,0x89,0xc0,0,0,0,0x10,0xc3});
            byte[] nativeCall = new byte[5];
            Marshal.Copy(new IntPtr(image + 0x1af0), nativeCall, 0, nativeCall.Length);
            var countCall = new List<byte>{0xff,0x05}; D(countCall, calls); countCall.Add(0xc3);
            Write(image + 0x1af0, countCall.ToArray());
            for (int state = 1; state <= 3; state++) {
                Put(bike + 0x2f4, state);
                Run(code, image + 0x16230, rider);
                Check(Get(calls)==0 && Get(rider+0xec)==100 && Get(rider+0x1f8)==200 && Get(bike+0x2f4)==state,
                    mode + " skips unloaded rider physics and duplicate progress, state " + state);
            }
            // Reattach the same rider. Exercise native friction and the surface classifier
            // from the dump; stop before lateral integration, which needs a full scene.
            Write(image + 0x1af0, nativeCall); Write(image + 0x4debc, new byte[]{0x5e,0xc3});
            Put(bike + 0x2f4, 2); Put(rider + 0x2c, road);
            Run(code, image + 0x16230, rider);
            Check(Get(rider+0xec)==164 && Get(rider+0x1f8)==150 && (Get(rider+0xc0)&16)==0,
                mode + " resumes native road physics and counts distance once before finish");
            Put(rider + 0x1c, 1000); Put(rider + 0x30, 1000);
            Run(code, image + 0x16230, rider);
            Check((Get(rider+0xc0)&16)!=0 && Get(bike+0x2f4)==2, mode + " preserves native on-foot finish");
        }
    }
    static int Main(string[] args) {
        try {
            SetErrorMode(0x8003);AddVectoredExceptionHandler(1,diagnosticHandler);
            long image=Alloc(0x240000);
            for(int i=0;i<3;i++)Write(image+RuntimePatch.RaceSites[i],RuntimePatch.RaceOriginals[i]);
            for(int i=0;i<RuntimePatch.RaceWorldSites.Length;i++)Write(image+RuntimePatch.RaceWorldSites[i],RuntimePatch.RaceWorldOriginals[i]);
            Write(image+RuntimePatch.RunGuideCollisionSite,RuntimePatch.RunGuideCollisionOriginal);
            Write(image+RuntimePatch.RunGuideEndPairs,RuntimePatch.RunGuideEndPairsOriginal);
            Write(image+RuntimePatch.FallAnimationSite,RuntimePatch.FallAnimationOriginal);
            Write(image+0x13B60,new byte[]{0x55,0x8B,0xEC,0x83,0xEC,0x34});
            RuntimePatch.ApplyRace(Process.GetCurrentProcess().Handle,image,false,2,0,96,delegate(string s){});
            Check(Marshal.ReadByte(new IntPtr(image+0x66307))==0xE8,"default race settings install no hooks");
            Write(image+0x19F4E,new byte[]{0xCC});bool mismatch=false;
            try { RuntimePatch.ApplyRace(Process.GetCurrentProcess().Handle,image,true,2,3,96,delegate(string s){}); } catch(InvalidOperationException) { mismatch=true; }
            Check(mismatch&&Marshal.ReadByte(new IntPtr(image+0x66307))==0xE8,"all signatures checked before any installation");
            Write(image+0x19F4E,RuntimePatch.RaceOriginals[2]);
            Write(image+RuntimePatch.RunGuideEndPairs,new byte[]{0xCC});mismatch=false;
            try { RuntimePatch.ApplyRace(Process.GetCurrentProcess().Handle,image,true,2,3,96,delegate(string s){}); } catch(InvalidOperationException) { mismatch=true; }
            Check(mismatch&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.RunGuideCollisionSite))==0x8A,"unknown collision continuation rejected before installing the guide filter");
            Write(image+RuntimePatch.RunGuideEndPairs,RuntimePatch.RunGuideEndPairsOriginal);
            Write(image+RuntimePatch.FallAnimationSite,new byte[]{0xCC});mismatch=false;
            RuntimePatch.ApplyFixes(Process.GetCurrentProcess().Handle,image,false,false,delegate(string s){});
            try { RuntimePatch.ApplyFixes(Process.GetCurrentProcess().Handle,image,false,true,delegate(string s){}); } catch(InvalidOperationException) { mismatch=true; }
            Check(mismatch&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.FallAnimationSite))==0xCC&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.FallAnimationSite+1))==0x26,"animation fix validates its signature when enabled and leaves incompatible code untouched");
            Write(image+RuntimePatch.FallAnimationSite,RuntimePatch.FallAnimationOriginal);
            RuntimePatch.ApplyRace(Process.GetCurrentProcess().Handle,image,true,2,3,96,delegate(string s){});
            Check(Marshal.ReadByte(new IntPtr(image+0x66307))==0xE9&&Marshal.ReadByte(new IntPtr(image+0x17CA7))==0xE9&&Marshal.ReadByte(new IntPtr(image+0x19F4E))==0xE9&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.RunGuideCollisionSite))==0xE9,"all long-run hooks including guide collision filter installed");
            Check(Marshal.ReadByte(new IntPtr(image+RuntimePatch.FallAnimationSite+1))==0x26,"long-run mode alone does not force a disabled original-game fix");
            RuntimePatch.ApplyFixes(Process.GetCurrentProcess().Handle,image,false,true,delegate(string s){});
            Check(Marshal.ReadByte(new IntPtr(image+RuntimePatch.FallAnimationSite+1))==0x1C,"independent falling animation fix installed alongside long-run hooks");
            List<string> failureLogs=new List<string>();
            RuntimePatch.ApplyFixes(Process.GetCurrentProcess().Handle,image,true,false,failureLogs.Add);
            Check(failureLogs.Count==1&&failureLogs[0].Contains("失败")&&Marshal.ReadByte(new IntPtr(image+0x45AD5))==0,
                "incompatible audio signature reports failure to launcher and leaves memory untouched");
            if(args.Length>0) {
                CheckGame(args[0]);
                CheckRankModes(args[0], args.Length > 1 ? args[1] : null);
            }
            Console.WriteLine("Race tests: "+passed+" passed");return 0;
        }catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
