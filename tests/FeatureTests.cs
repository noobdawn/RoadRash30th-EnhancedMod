using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

// Isolated x86 fixtures only; never execute the game entry point or open it.
internal static class FeatureTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at,UIntPtr size,uint type,uint protect);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr process,IntPtr at,UIntPtr size);
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")] static extern IntPtr AddVectoredExceptionHandler(uint first,ExceptionHandler handler);
    [DllImport("kernel32.dll")] static extern void ExitProcess(uint code);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ExceptionHandler(IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RunCode();
    static long diagnosticImage;
    static ExceptionHandler handler=delegate(IntPtr p){uint error=unchecked((uint)Marshal.ReadInt32(Marshal.ReadIntPtr(p)));if(error==0xC0000005||error==0xC0000094||error==0xC000001D){IntPtr c=Marshal.ReadIntPtr(p,4);Console.Error.WriteLine("Feature native error {0:X}: RVA={1:X}",error,(uint)Marshal.ReadInt32(c,184)-diagnosticImage);ExitProcess(77);}return 0;};
    static long Alloc(int n){IntPtr p=VirtualAlloc(IntPtr.Zero,(UIntPtr)n,0x3000,0x40);if(p==IntPtr.Zero)throw new Exception("allocation");return (uint)p.ToInt32();}
    static void Put(long p,long v){Marshal.WriteInt32(new IntPtr(p),unchecked((int)v));}
    static int Get(long p){return Marshal.ReadInt32(new IntPtr(p));}
    static void Write(long p,byte[] b){Marshal.Copy(b,0,new IntPtr(p),b.Length);FlushInstructionCache(new IntPtr(-1),new IntPtr(p),(UIntPtr)b.Length);}
    static void D(List<byte> b,long v){b.AddRange(BitConverter.GetBytes(unchecked((int)v)));}
    static void Mov(List<byte> b,byte op,long v){b.Add(op);D(b,v);}
    static void Call(List<byte> b,long code,long target){b.Add(0xe8);D(b,target-(code+b.Count+4));}
    static int passed;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
    static long Map(string path)
    {
        byte[] f=File.ReadAllBytes(path);int pe=BitConverter.ToInt32(f,0x3c),opt=pe+24;
        long image=Alloc(BitConverter.ToInt32(f,opt+56)),preferred=BitConverter.ToUInt32(f,opt+28);
        Marshal.Copy(f,0,new IntPtr(image),BitConverter.ToInt32(f,opt+60));int sec=opt+BitConverter.ToUInt16(f,pe+20);
        for(int i=0;i<BitConverter.ToUInt16(f,pe+6);i++,sec+=40){int n=BitConverter.ToInt32(f,sec+16);if(n>0)Marshal.Copy(f,BitConverter.ToInt32(f,sec+20),new IntPtr(image+BitConverter.ToInt32(f,sec+12)),n);}
        int reloc=BitConverter.ToInt32(f,opt+136),end=reloc+BitConverter.ToInt32(f,opt+140);
        while(reloc<end){int page=Get(image+reloc),block=Get(image+reloc+4);if(block<8)throw new Exception("relocations");for(int e=8;e<block;e+=2){int fix=(ushort)Marshal.ReadInt16(new IntPtr(image+reloc+e));if((fix>>12)==3){long at=image+page+(fix&4095);Put(at,(uint)Get(at)+image-preferred);}}reloc+=block;}
        diagnosticImage=image;return image;
    }
    static void Run(long target,long ecx,long edx,long ebx,long esi,long edi,long eax,int stackArgs)
    {
        long code=Alloc(0x1000);List<byte>b=new List<byte>{0x9c,0x60};
        Mov(b,0xb9,ecx);Mov(b,0xba,edx);Mov(b,0xbb,ebx);Mov(b,0xbe,esi);Mov(b,0xbf,edi);Mov(b,0xb8,eax);
        for(int i=0;i<stackArgs;i++)b.AddRange(new byte[]{0x6a,0});Call(b,code,target);b.AddRange(new byte[]{0x61,0x9d,0xc3});Write(code,b.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
    }
    static void Directions(string path)
    {
        long image=Map(path),count=Alloc(0x1000),descriptor=Alloc(0x1000);IntPtr h=Process.GetCurrentProcess().Handle;
        // The native prologues and calling conventions execute; the rest of
        // generation is replaced by an allocation counter (no game resources).
        for(int i=0;i<2;i++){
            List<byte>b=new List<byte>{0xff,5};D(b,count);b.AddRange(new byte[]{0x8b,0xe5,0x5d});
            if(i==0)b.Add(0xc3);else b.AddRange(new byte[]{0xc2,12,0});Write(image+RuntimePatch.TrafficSpawnSites[i]+6,b.ToArray());
        }
        for(int mask=0;mask<4;mask++){
            for(int i=0;i<2;i++)Write(image+RuntimePatch.TrafficSpawnSites[i],RuntimePatch.TrafficSpawnOriginals[i]);
            RuntimePatch.ApplyTrafficDirections(h,image,(mask&1)!=0,(mask&2)!=0,delegate(string s){});
            for(int site=0;site<2;site++)for(int direction=0;direction<4;direction++){
                Put(count,0);Marshal.WriteByte(new IntPtr(descriptor+10),(byte)direction);
                Run(image+RuntimePatch.TrafficSpawnSites[site],site==0?descriptor:direction,0,0,0,0,0,site==0?0:3);
                bool allowed=mask!=0&&(direction==0?(mask&1)!=0:direction==2?(mask&2)!=0:true);
                Check(Get(count)==(allowed?1:0),"spawn allocation filter + stack ABI site="+site+" flags="+mask+" direction="+direction);
            }
        }
        for(int i=0;i<2;i++)Write(image+RuntimePatch.TrafficSpawnSites[i],RuntimePatch.TrafficSpawnOriginals[i]);
        Write(image+RuntimePatch.TrafficSpawnSites[1],new byte[]{0xcc});bool fail=false;
        try{RuntimePatch.ApplyTrafficDirections(h,image,false,true,delegate(string s){});}catch(InvalidOperationException){fail=true;}
        Check(fail&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.TrafficSpawnSites[0]))==0x55,"direction signatures validated before either entry is modified");
    }
    static void Length(string path)
    {
        Check(TrackLengthUnits.ToBlocks(4.6m)==1000&&TrackLengthUnits.ToKilometers(1000)==4.6m,"user measured calibration 1000 blocks equals approximately 4.6 km");
        Check(TrackLengthUnits.ToBlocks(10m)==2174&&TrackLengthUnits.ToBlocks(0.4623m)==101,"kilometer input rounds to nearest native block with midpoint upward");
        bool exact=true;
        for(int blocks=100;blocks<=5000;blocks++){
            Dictionary<string,string> values=new Dictionary<string,string>{{"trackLength",blocks.ToString(System.Globalization.CultureInfo.InvariantCulture)}};
            exact&=TrackLengthUnits.ToBlocks(TrackLengthUnits.ReadKilometers(values))==blocks&&!values.ContainsKey("trackLength");
        }
        Check(exact,"all 4901 supported legacy lengths preserve exact native finish through kilometer migration");
        foreach(decimal invalid in new decimal[]{-1m,0m,0.4599m,23.0001m,decimal.MaxValue}){
            bool rejected=false;try{TrackLengthUnits.ToBlocks(invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"kilometer conversion rejects unsupported bounds "+invalid);
        }
        long image=Map(path),rider=Alloc(0x1000),bike=Alloc(0x1000),road=Alloc(0x1000),calls=Alloc(0x1000);IntPtr h=Process.GetCurrentProcess().Handle;
        Put(rider+0x1c4,bike);Put(bike+0x324,2);Put(image+0x1a69f0,0);
        Write(image+0x745fa,new byte[]{0x5f,0x5e,0x8b,0xe5,0x5d,0xc3});
        Write(image+0x15db2,new byte[]{0xc3});Write(image+0x49f8,new byte[]{0xc3});Write(image+0x1af0,new byte[]{0xc3});
        List<byte> finish=new List<byte>{0xff,5};D(finish,calls);finish.AddRange(new byte[]{0x80,0x89,0xc0,0,0,0,16,0xc3});Write(image+0x193d,finish.ToArray());
        RuntimePatch.ApplyTrackLength(h,image,false,1800,delegate(string s){});
        Check(Get(image+0x745ee)==0,"disabled length retains native initialization");
        foreach(decimal kilometers in new decimal[]{0.46m,4.6m,8.28m,10m,23m}){
            int length=TrackLengthUnits.ToBlocks(kilometers),target=length*256;
            for(int i=0;i<4;i++)Write(image+RuntimePatch.TrackLengthSites[i],RuntimePatch.TrackLengthOriginal(image,i));
            RuntimePatch.ApplyTrackLength(h,image,true,length,delegate(string s){});
            for(int race=0;race<2;race++){
                Put(image+0x1f3a74,-1);Run(image+0x745e0,road,0,0,0,0,0,0);
                Check(Get(image+0x1f3a74)==target,"new race resets absolute finish length="+length+" repeat="+race);
            }
            for(int loaded=0;loaded<2;loaded++)foreach(int relative in new int[]{-100,0,100})foreach(int entry in new int[]{0x16630,0x167c0}){
                Put(rider+0x2c,loaded==0?0:road);Put(rider+0x1c,target+relative);Put(rider+0x1f8,999999);
                Run(image+entry,rider,0,0,0,0,0,0);
                Check(Get(rider+0x1f8)==-relative,"native distance uses override loaded="+loaded+" entry="+entry+" offset="+relative+" length="+length);
            }
            foreach(int relative in new int[]{-1,0,1}){
                Put(calls,0);Put(rider+0xc0,0);Put(rider+0x1c,target+relative);
                Run(image+0x15d6a,0,0,rider,bike,0,0,0);Run(image+0x15d6a,0,0,rider,bike,0,0,0);
                Check(Get(calls)==(relative<0?0:1)&&Get(rider+0x1f8)==-relative,"offscreen finish is timely and idempotent offset="+relative+" length="+length);
                Put(calls,0);Put(rider+0xc0,0);Put(rider+0x2c,road);Put(rider+0x30,target+relative);Put(rider+0x1f4,int.MaxValue);
                Run(image+0x16230,rider,0,0,0,0,0,0);
                Check(Get(calls)==(relative<0?0:1),"original on-foot finish uses overridden line offset="+relative+" length="+length);
            }
        }
        for(int i=0;i<4;i++)Write(image+RuntimePatch.TrackLengthSites[i],RuntimePatch.TrackLengthOriginal(image,i));
        Write(image+0x15d6a,new byte[]{0xcc});bool fail=false;
        try{RuntimePatch.ApplyTrackLength(h,image,true,100,delegate(string s){});}catch(InvalidOperationException){fail=true;}
        Check(fail&&Get(image+0x745ee)==0,"length signatures validated before mutation");
        foreach(int invalid in new int[]{0,99,5001,int.MaxValue}){
            fail=false;try{RuntimePatch.ApplyTrackLength(h,image,true,invalid,delegate(string s){});}catch(ArgumentOutOfRangeException){fail=true;}
            Check(fail,"length rejects unsupported bounds "+invalid);
        }
    }
    static void Audio(string path,string source)
    {
        using(SHA256 sha=SHA256.Create())Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-","").ToLowerInvariant()==ImpactAudioNative.SourceSha256,"impact native bytes match source");
        long image=Map(path),entry=Alloc(0x1000),config=Alloc(0x1000),hits=Alloc(0x1000),capture=Alloc(0x1000),stats=Alloc(0x1000);
        long actor=Alloc(0x1000),owner=Alloc(0x1000),player=Alloc(0x1000),listener=Alloc(0x1000),other=Alloc(0x1000);
        Write(entry,ImpactAudioNative.Code);Put(config,image);Put(config+4,capture);Put(config+8,hits);
        List<byte>b=new List<byte>{0xff,5};D(b,stats);b.AddRange(new byte[]{0x89,0x0d});D(b,stats+4);b.AddRange(new byte[]{0x89,0x15});D(b,stats+8);
        b.AddRange(new byte[]{0x8b,0x44,0x24,4,0xa3});D(b,stats+12);b.AddRange(new byte[]{0xc2,12,0});Write(capture,b.ToArray());
        Action reset=delegate{
            Write(hits,new byte[4096]);Put(stats,0);Put(config+12,0);Put(config+16,0);Put(config+20,0);
            Put(image+0x2036e8,100);Put(image+0x1a69ec,player);Put(player+0x2fc,listener);Put(image+0x1c5544,100);Put(image+0x1c5400,0);
            Put(image+0x2162b0,123);for(int i=0;i<6;i++)Put(image+0x2162bc+i*4,200+i);
            Put(actor+0xc4,8);Put(actor+0xc0,0);Put(actor+0x1c4,owner);Put(actor+0x1a0,other);Put(actor+0xec,2000);Put(other+0xec,3000);
            Put(actor+0x1c,0);Put(actor+0x28,0);Put(listener+0x1c,0);Put(listener+0x28,0);
        };
        Action play=delegate {Run(entry,actor,config,0,0,0,0,0);};
        reset();play();Check(Get(stats)==2&&Get(stats+4)==201&&Get(stats+8)==12288,"near AI impact plays collision and hurt using native playback ABI");
        play();Check(Get(stats)==2,"continuous contact is not replayed each callback");Put(image+0x2036e8,112);play();Check(Get(stats)==4,"new collision after cooldown can play");
        reset();Put(actor+0x1c,2048);play();Check(Get(stats)==2&&Get(stats+8)==6144,"distance halves gain at half radius");
        reset();Put(actor+0x28,-100*256);play();Check(Get(stats+12)<0,"sound is panned left");reset();Put(actor+0x28,100*256);play();Check(Get(stats+12)>0,"sound is panned right");
        foreach(int distance in new int[]{-4096,4096,int.MinValue,int.MaxValue}){reset();Put(actor+0x1c,distance);play();Check(Get(stats)==0,"distant sound omitted "+distance);}
        reset();Put(listener+0x1c,int.MinValue);Put(actor+0x1c,int.MaxValue);play();Check(Get(stats)==0,"distance arithmetic cannot wrap to nearby");
        reset();Put(image+0x1c5544,0);play();Check(Get(stats)==0,"effects mute respected");
        reset();Put(actor+0x1c4,player);play();Check(Get(stats)==0,"player keeps native sound without duplication");
        reset();Put(actor+0xc0,16);play();Check(Get(stats)==0,"finished actor excluded");
        reset();Put(actor+0xc4,16);play();Check(Get(stats)==0,"ordinary cars do not receive human hurt voices");
        reset();Put(config+20,1);Put(actor+0x1a0,player);play();Check(Get(stats)==0,"player ram sound not duplicated");
        reset();Put(actor+0xc4,4);Put(actor+0x184,1500);play();Check(Get(stats)==1,"minor bike collision has impact only");
        reset();Put(actor+0xc4,4);Put(actor+0x184,int.MinValue);play();Check(Get(stats)==2,"extreme impulses are bounded without signed overflow");
        reset();Put(actor+0xec,0);Put(other+0xec,0);play();Check(Get(stats)==0,"harmless stationary contact omitted");
        reset();Put(config+12,100);Put(config+16,2);play();Check(Get(stats)==0,"per-tick sound budget bounded");
        // Execute the ORIGINAL on-foot collision with the real hook installed;
        // substitute animation, physics and audio device calls only.
        RuntimePatch.ApplyNearbyAudio(Process.GetCurrentProcess().Handle,image,true,false,delegate(string s){});
        Write(image+0x28bd0,new byte[]{0xe9,0,0,0,0});Put(image+0x28bd1,capture-(image+0x28bd0+5));
        Write(image+0x2a6d,new byte[]{0xc2,4,0});Write(image+0x35d5,new byte[]{0xc2,4,0});Write(image+0x3882,new byte[]{0x33,0xc0,0xc3});
        foreach(int rva in new int[]{0x12cb,0x428c,0x5fb0})Write(image+rva,new byte[]{0xc3});
        reset();Put(owner+0xc4,4);Put(owner+0x2fc,actor);Put(owner+0x324,2);Put(owner+0x2f4,2);Put(owner+0x18,1);Put(other+0xc4,16);Put(other+0x174,100);Put(other+0x17c,100);Put(actor+0x174,100);
        RuntimePatch.ApplyFixes(Process.GetCurrentProcess().Handle,image,false,true,delegate(string s){});
        Run(image+0x13f80,actor,0,0,0,0,0,0);
        Check(Get(stats)==2&&Get(owner+0x2f4)==1,"real AI collision emits nearby sound and preserves native knockdown with animation fix");
        long protect=Alloc(0x1000);Write(protect,RuntimePatch.BuildOnFootCode(image,protect,true));Write(image+0x13f8a,new byte[]{0xe9,0,0,0,0,0x90});Put(image+0x13f8b,protect-(image+0x13f8a+5));
        Put(stats,0);Put(owner+0x2f4,2);Put(image+0x2036e8,200);Run(image+0x13f80,actor,0,0,0,0,0,0);
        Check(Get(stats)==0&&Get(owner+0x2f4)==2,"protected walking target does not emit a false knockdown sound");
        // Exercise the original bike audio gate as well: AI should use our
        // spatial playback; the player should still enqueue only native audio.
        Write(image+0x12ff6,new byte[]{0xc3});
        List<byte> queued=new List<byte>{0xff,5};D(queued,stats+16);queued.AddRange(new byte[]{0xc2,4,0});Write(image+0x2a6d,queued.ToArray());
        reset();Put(stats+16,0);Put(actor+0xc4,4);Put(actor+0x184,7000);Put(actor+0x2f4,0);Put(image+0x2036e8,300);
        Run(image+0x12f1c,0,0,0,0,actor,0,0);
        Check(Get(stats)==2&&Get(stats+16)==0,"original riding audio gate supplies spatial AI sound without native duplicate");
        Put(stats,0);Put(stats+16,0);Put(image+0x1a69ec,actor);Put(image+0x2036e8,400);
        Run(image+0x12f1c,0,0,0,0,actor,0,0);
        Check(Get(stats)==0&&Get(stats+16)==2,"original player riding impact and voice queue remain intact");
    }
    // Call an interior constructor/AI block with an isolated native stack frame.
    static int OpponentCall(long address, long frame, long first, long second, long bike, long role, long rider, int? argument)
    {
        long thunk = Alloc(256), result = Alloc(16);
        var code = new List<byte> { 0x55 };
        Mov(code, 0xbd, frame);
        if (argument.HasValue) { code.Add(0x68); D(code, argument.Value); }
        Call(code, thunk, address);
        code.Add(0xa3); D(code, result);
        code.AddRange(new byte[]{0x5d,0xc3}); Write(thunk, code.ToArray());
        Run(thunk, first, second, bike, role, rider, 0, 0);
        return Get(result);
    }

    static void Opponents(string path, string source)
    {
        using (var sha = SHA256.Create())
            Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-", "").ToLowerInvariant() ==
                OpponentRulesNative.SourceSha256, "opponent native bytes match the tracked source");
        long image = Map(path), frame = Alloc(1024) + 256, bike = Alloc(1024), rider = Alloc(1024);
        long player = Alloc(1024), playerRider = Alloc(1024), road = Alloc(512);
        IntPtr process = Process.GetCurrentProcess().Handle;
        int[] sites = { RuntimePatch.OpponentStats, RuntimePatch.OpponentSprites, RuntimePatch.OpponentAwareness, RuntimePatch.OpponentSwing, RuntimePatch.OpponentThreat,
            RuntimePatch.OpponentTexture, RuntimePatch.OpponentBikeShadow, RuntimePatch.OpponentRiderShadow };
        foreach (int site in sites) {
            var expected = RuntimePatch.OpponentOriginal(image, site);
            for (int i = 0; i < expected.Length; ++i)
                if (Marshal.ReadByte(new IntPtr(image + site + i)) != expected[i]) throw new Exception("stock opponent site " + site);
        }
        RuntimePatch.ApplyOpponents(process, image, 0, false, delegate(string ignored){});
        Check(Marshal.ReadByte(new IntPtr(image + sites[0])) == 0x8b, "disabled rules leave constructor original");

        // Execute the actual game's bike/weapon/health initialization up to the next unrelated stage.
        Write(image + 0x14e59, new byte[]{0xc3});
        Put(bike + 0x2fc, rider); Put(rider + 0x1c4, bike); Put(bike + 0x324, 1); Put(frame + 0x10, 1);
        long originalProfile = image + 0x219ca4, extended = Alloc(1024);
        // Observe both original sprite bindings and the palette branch without loading graphics.
        Write(image + 0x22c0, new byte[]{0x89,0x11,0xc3});
        Write(image + 0x57f9, new byte[]{0xc3});
        Write(image + 0x1523f, new byte[]{0xc3});
        Put(image + 0x218059, 0);
        // The enhancement can give the player private stats for a custom bike/weapon.
        long originalPlayerProfile = image + 0x219aa0;
        Put(rider + 0x1d8, originalPlayerProfile); Put(frame + 0x18, 0);
        Marshal.WriteByte(new IntPtr(image + 0x2041a2), 0);
        OpponentCall(image + sites[0], frame, 0, 0, bike, 0, rider, null);
        long originalPlayerStats = (uint)Get(rider + 0x1e0);
        foreach (int id in new[]{1,2,3,4,5,6,7,8,15})
        {
            foreach (int site in sites) Write(image + site, RuntimePatch.OpponentOriginal(image, site));
            RuntimePatch.ApplyOpponents(process, image, id, id == 15, delegate(string ignored){});
            int personality = id == 15 ? 9 : id;
            long firstStats = 0, firstProfile = 0;
            foreach (int slot in new[]{1,10000}) {
                long old = slot == 1 ? originalProfile : extended;
                Put(old + 0x190, slot + 4); Put(old + 0x194, slot + 9);
                Put(extended + 0x204, 567); Put(rider + 0x1d8, old); Put(frame - 8, slot);
                Put(frame + 0x18, 2);
                Marshal.WriteByte(new IntPtr(image + 0x2041a2), (byte)(slot == 1 ? 0 : 4));
                OpponentCall(image + sites[0], frame, 2, 0, bike, 1, rider, null);
                long stats = (uint)Get(rider + 0x1e0), profile = (uint)Get(rider + 0x1d8);
                int level = slot == 1 ? 0 : 4;
                Check(Marshal.ReadByte(new IntPtr(profile + 0x165)) == personality && Get(profile + 8) == Get(image + 0x18f360 + personality * 0x204 + 8),
                    "selected identity and hostility id=" + id + " slot=" + slot);
                Check(Get(frame + 0x18) == id && Marshal.ReadByte(new IntPtr(stats + 5)) == Marshal.ReadByte(new IntPtr(image + 0x18e8a8 + id * 5 + level)) &&
                    Get(rider + 0x1d4) == Get(image + 0x18ea98 + id * 64 + 0x18 + level * 4), "native level-specific bike and weapon id=" + id);
                Check(Get(profile + 0x190) == slot + 4 && Get(profile + 0x194) == slot + 9 &&
                    (slot == 1 ? profile != old : profile == extended && Get(extended + 0x204) == 567), "grid positions and extended state survive id=" + id);
                if (slot == 1) { firstStats = stats; firstProfile = profile; Put(stats + 0x3c, 7); Put(profile + 0x168, 8); }
                else Check(stats != firstStats && profile != firstProfile && Get(firstStats + 0x3c) == 7 && Get(firstProfile + 0x168) == 8, "repeated identity owns separate health and AI state");
            }
        }
        Put(rider + 0x1d8, originalPlayerProfile); Put(frame + 0x18, 0);
        Marshal.WriteByte(new IntPtr(image + 0x2041a2), 0);
        OpponentCall(image + sites[0], frame, 0, 0, bike, 0, rider, null);
        Check(originalPlayerStats != 0 && (uint)Get(rider + 0x1e0) == originalPlayerStats && (uint)Get(rider + 0x1d8) == originalPlayerProfile, "player retains original identity and stats");
        foreach (int role in new[]{0,1,2}) {
            Put(frame + 0x10, role); Put(bike + 0x324, role);
            OpponentCall(image + sites[1], frame, 0, 0, bike, role, rider, null);
            int expected = role == 1 ? 2 : role;
            Check(Get(bike + 0x70) == expected && Get(rider + 0x70) == expected && Get(bike + 0x324) == role, "police sprites on both objects preserve controller " + role);
        }
        Put(rider + 0x1d8, extended); Put(bike + 0x324, 1);
        Put(player + 0x2fc, playerRider); Put(playerRider + 0xc4, 8);
        Put(rider + 0x2c, road); Put(playerRider + 0x2c, road); Put(road + 0x36, 3);
        Put(road + 0xe0, -5); Put(road + 0xe4, 5); Put(rider + 0x1c, 3000); Put(image + 0x1a69ec, player);
        foreach (int gap in new[]{-3000,-100,100,3000})
        {
            Put(playerRider + 0x1c, 3000 + gap);
            foreach (int dir in new[]{0,1})
                Check((uint)OpponentCall(image + sites[2], frame, bike, dir, 0, 0, 0, 2560) ==
                    (Math.Abs(gap) < 2560 && (gap > 0) == (dir == 1) ? player : 0), "player awareness with stock direction and range " + gap + "/" + dir);
            Put(extended + 0x168, bike);
            OpponentCall(image + sites[3], frame, bike, 0, 0, 0, 0, null);
            Check((uint)Get(extended + 0x168) == (Math.Abs(gap) < 480 ? player : 0), "swing discards other AI even for a stale target " + gap);
        }
        Put(playerRider + 0x1c, 3100); Put(playerRider + 0x2c, 0);
        Check(OpponentCall(image + sites[2], frame, bike, 1, 0, 0, 0, 2560) == 0, "unloaded player cannot be targeted");
        Put(playerRider + 0x2c, road); Put(playerRider + 0x28, 5 * 256);
        Check(OpponentCall(image + sites[2], frame, bike, 1, 0, 0, 0, 2560) == 0, "awareness keeps stock road bounds");
        Put(image + 0x1a69ec, 0);
        OpponentCall(image + sites[3], frame, bike, 0, 0, 0, 0, null);
        Check(Get(extended + 0x168) == 0, "missing player clears attack target");
        Write(image + 0x1b382, new byte[]{0xc3}); Put(image + 0x1a69ec, player); Put(frame - 8, bike);
        OpponentCall(image + sites[4], frame, 0, bike, extended, 1, player, null);
        Check(Get(extended + 0x174) == 3 && (uint)Get(extended + 0x168) == player, "player receives native hostile rating");
        Put(image + 0x218059, 1);
        OpponentCall(image + sites[4], frame, 0, bike, extended, 1, player, null);
        Check(Get(extended + 0x174) == 1, "network race keeps original threat rating");
        Put(image + 0x218059, 0);
        var aoe = RuntimePatch.BuildAoeCode(image, 0, true);
        Check(aoe.Length <= 912, "player-only filter fits native AOE code region");
        Write(image + 0x21a30, aoe);
        Write(image + 0x19044, new byte[]{0x83,0xc4,4,0xc3});
        Put(image + 0x11280, 0x10000); Put(image + 0x11284, 2); Put(image + 0x18f1d8, 2);
        OpponentCall(image + 0x21a30, frame, 0, 0, player, 0, bike, null);
        Check(true, "AI does not enumerate additional AOE victims");
        foreach (int site in sites) Write(image + site, RuntimePatch.OpponentOriginal(image, site));
        Write(image + sites[4], new byte[]{0xcc});
        bool refused = false;
        try { RuntimePatch.ApplyOpponents(process, image, 15, true, delegate(string ignored){}); }
        catch (InvalidOperationException) { refused = true; }
        Check(refused && Marshal.ReadByte(new IntPtr(image + sites[0])) == 0x8b, "incompatible target logic leaves identity entry untouched");
    }

    // Read the same width, height and indexed pixel records as the game's DAT loader.
    static byte[] LoadBikerFrames(string directory, string name, int count, long table)
    {
        string path = Path.Combine(directory, "HI" + name + ".DAT");
        if (!File.Exists(path)) path = Path.Combine(directory, "LO" + name + ".DAT");
        byte[] data = File.ReadAllBytes(path);
        long pixels = Alloc(data.Length);
        Marshal.Copy(data, 0, new IntPtr(pixels), data.Length);
        int offset = 0;
        for (int i = 0; i < count; i++) {
            int size = data[offset] * data[offset + 1];
            Put(table + i * 8, size == 0 ? 0 : pixels + offset + 2);
            Put(table + i * 8 + 4, data[offset] | data[offset + 1] << 8);
            offset += 2 + size;
        }
        if (offset != data.Length) throw new InvalidDataException(path);
        return data;
    }

    static void CheckBikerPixels(long image, int key, byte[] expected, string name)
    {
        long request = Alloc(128), surface = Alloc(128), buffer = Alloc(256 * 256);
        Put(request + 4, surface); Put(surface + 0x38, buffer);
        Marshal.WriteByte(new IntPtr(request + 0x3c), (byte)key);
        // Ordinary riders have a per-racer palette mapping. Use an identity palette.
        if (key < 15)
            for (int i = 0; i < 256; i++) Marshal.WriteByte(new IntPtr(image + 0x196a90 + 256 * key + i), (byte)i);
        int offset = 0;
        for (int frame = 0; frame < 45; frame++) {
            int width = expected[offset], height = expected[offset + 1];
            Put(request + 0x38, frame);
            Run(image + 0x58f30, request, 0, 0, 0, 0, 0, 0); // stock texture upload, also called by HD
            if (Get(request + 0x10) != width || Get(request + 0x14) != height) throw new Exception(name + " dimensions at frame " + frame);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (Marshal.ReadByte(new IntPtr(buffer + 256 * y + x)) != expected[offset + 2 + width * y + x])
                        throw new Exception(name + " pixels at frame " + frame);
            offset += 2 + width * height;
        }
        Check(true, name + ": 45 original DAT frames match native texture output");
    }

    static void PoliceGraphics(string path, string directory)
    {
        long image = Map(path), bike = Alloc(1024), rider = Alloc(1024), frame = Alloc(512) + 128;
        long player = Alloc(1024), playerRider = Alloc(1024), stats = Alloc(128), draw = Alloc(256);
        long normalCans = Alloc(32), copCans = Alloc(32), capture = Alloc(32);
        Put(image + 0x2061f0, normalCans); Put(image + 0x2061f4, normalCans + 4);
        Put(image + 0x2061f8, copCans); Put(image + 0x2061fc, copCans + 4);
        Put(image + 0x2061e4, normalCans + 8); Put(image + 0x2061ec, copCans + 8);
        Put(image + 0x1a69ec, player); Put(image + 0x1a69f0, 0); Put(image + 0x218059, 0);
        Put(player + 0x2fc, playerRider); Put(playerRider + 0x1e0, stats);
        Marshal.WriteByte(new IntPtr(stats + 5), 3);
        // Read the stock or expanded roster operands from the mapped executable.
        long bikes = (uint)Get(image + 0x5c320), riders = (uint)Get(image + 0x5c292);
        Put(bikes, player); Put(riders, playerRider); Put(bikes + 4, bike); Put(riders + 4, rider);
        Put(bike + 0x2fc, rider); Put(rider + 0x1c4, bike);
        Put(bike + 0xc4, 4); Put(rider + 0xc4, 8);
        Put(bike + 0xa8, draw); Put(rider + 0xa8, draw);
        // Run the two native bank bindings and render snapshot. Skip animation timing and camera projection.
        Write(image + 0x5c4e1, new byte[]{0x5f,0x5e,0xc3});
        Write(image + 0x57f9, new byte[]{0xc3}); Write(image + 0x1523f, new byte[]{0xc3});
        Write(image + 0x4a1b8, new byte[]{0xc3});
        Action<int> bind = delegate(int role) {
            Put(frame + 0x10, role);
            OpponentCall(image + RuntimePatch.OpponentSprites, frame, 0, 0, bike, role, rider, null);
            foreach (long actor in new[]{bike,rider}) Run(image + 0x4a194, 0, 0, 0, 0, 0, actor, 0);
        };
        // Original bike/rider draw callbacks emit a shadow and a body. Capture the
        // active frame bank at projection and the actual texture key at submission.
        var prepare = new List<byte>{0x8b,0x81,0x90,0,0,0,0xa3}; D(prepare, capture + 16);
        prepare.Add(0xc3); Write(image + 0x2f72, prepare.ToArray());
        var submit = new List<byte>{0x8b,0x4c,0x24,0x14,0x8b,0x02,0x8b,0x40,0x38,0x89,0x04,0xcd};
        D(submit, capture); submit.Add(0xa1); D(submit, capture + 16);
        submit.AddRange(new byte[]{0x89,0x04,0xcd}); D(submit, capture + 4);
        submit.AddRange(new byte[]{0xc2,0x14,0}); Write(image + 0x46ec, submit.ToArray());
        Func<bool,bool,string,int> views = delegate(bool copPose, bool copSkin, string label) {
            long pose = copPose ? copCans : normalCans, shadow = copSkin ? copCans : normalCans;
            int bodyKey = -1;
            foreach (long actor in new[]{bike,rider}) {
                Write(capture, new byte[20]);
                Run(image + (actor == bike ? 0x5c000 : 0x5ccc0), actor, 0, 0, 0, 0, 0, 0);
                bodyKey = Get(capture + 8);
                Check((uint)Get(capture + 4) == shadow + 8 && (uint)Get(capture + 12) == pose + 4 &&
                    Get(capture) == (copSkin ? 0xdf : 0xe0) && (copSkin ? bodyKey == 0xde : bodyKey >= 0 && bodyKey < 15),
                    label + (actor == bike ? " bike" : " rider") + " body and shadow");
            }
            return bodyKey;
        };
        bind(2); Put(image + 0x1a69f0, bike); views(true, true, "native patrol");
        Put(image + 0x1a69f0, 0);
        int oldKey = views(true, false, "previous police animation with ordinary textures");
        var bob = LoadBikerFrames(directory, "BOB", 326, image + 0x1cab58);
        var cop = LoadBikerFrames(directory, "COP", 45, image + 0x1cb588);
        var copShadow = LoadBikerFrames(directory, "SHADOC", 45, image + 0x1cc120);
        CheckBikerPixels(image, oldKey, bob, "previous opponent skin");

        RuntimePatch.ApplyOpponents(Process.GetCurrentProcess().Handle, image, 15, false, delegate(string ignored){});
        bind(1);
        int correctedKey = views(true, true, "single-player police opponent without a patrol");
        CheckBikerPixels(image, correctedKey, cop, "corrected opponent skin");
        CheckBikerPixels(image, Get(capture), copShadow, "corrected opponent shadow");
        Put(image + 0x1a69f0, player); views(true, true, "opponent alongside a separate patrol");
        if (bikes - image >= 0x230000 && riders - image >= 0x230000) {
            Put(image + 0x18f1d8, 500); Put(bikes + 4, 0); Put(riders + 4, 0);
            Put(bikes + 499 * 4, bike); Put(riders + 499 * 4, rider);
            views(true, true, "police appearance at expanded grid slot 499");
        }
        Put(image + 0x1a69ec, bike); Put(bikes, bike); Put(riders, rider); Put(rider + 0x1e0, stats);
        bind(0); Check(views(false, false, "player") == 3, "player keeps selected texture");
        Put(image + 0x1a69ec, player); Put(bikes, player); Put(riders, playerRider);
        foreach (int flag in new[]{1,256}) {
            Put(image + 0x218059, flag); bind(1); views(false, false, "network mode " + flag);
        }
        Put(image+0x218059,0);
        foreach(int site in new[]{RuntimePatch.OpponentStats,RuntimePatch.OpponentSprites,RuntimePatch.OpponentTexture,RuntimePatch.OpponentBikeShadow,RuntimePatch.OpponentRiderShadow})
            Write(image+site,RuntimePatch.OpponentOriginal(image,site));
        RuntimePatch.ApplyOpponents(new IntPtr(-1),image,0,false,delegate{},true);
        Put(image+0x1a69ec,bike); Put(bikes,bike); Put(riders,rider); Put(bike+0x324,0);
        bind(0); Check(views(true,true,"player police without a patrol")==0xde && Get(bike+0x324)==0,"officer keeps the player controller");
        CheckBikerPixels(image,0xde,cop,"officer skin");
        Write(image+0x14e59,new byte[]{0xc3});
        for(int level=0;level<5;++level){
            Marshal.WriteByte(new IntPtr(image+0x2041a2),(byte)level);
            Put(frame+0x10,0); Put(rider+0x1e0,stats); Put(stats+0xc,54321); Put(rider+0x1d4,0);
            OpponentCall(image+RuntimePatch.OfficerGear,frame,0,0,bike,0,rider,null);
            long privateStats=(uint)Get(rider+0x1e0);
            Check(privateStats!=stats && Get(privateStats+0xc)==54321 && Marshal.ReadByte(new IntPtr(stats+5))==3 &&
                  Marshal.ReadByte(new IntPtr(privateStats+5))==Marshal.ReadByte(new IntPtr(image+0x18e8a8+15*5+level)) &&
                  Get(rider+0x1d4)==1 && Get(rider+0x1c8)==1,"level-specific patrol bike and baton preserve career equipment "+level);
        }
        Put(image+0x1a69ec,player); Put(bikes,player); Put(riders,playerRider); Put(bikes+4,bike); Put(riders+4,rider);
        bind(1); views(false,false,"officer preset leaves other racers ordinary");
        Put(image+0x218059,1); Put(frame+0x10,0); Put(rider+0x1e0,stats); Put(rider+0x1d4,2);
        OpponentCall(image+RuntimePatch.OfficerGear,frame,0,0,bike,0,rider,null);
        Check((uint)Get(rider+0x1e0)==stats && Get(rider+0x1c8)==2,"network player retains stock equipment");
    }

    static void RaceRules(string path)
    {
        Launcher.GameDir = Path.GetDirectoryName(Path.GetFullPath(path));
        foreach (int mode in new[] {0,1,2,3}) {
            bool officer=mode>=2, meter=mode==0 || mode==3;
            long image=Map(path), player=Alloc(1024), ai=Alloc(1024), patrol=Alloc(1024);
            foreach (long bike in new[]{player,ai,patrol}) {
                long rider=Alloc(1024); Put(bike+0x2fc,rider); Put(rider+0x1c4,bike);
                Put(bike+0xc4,4); Put(rider+0xc4,8); Put(bike+0xc0,7); Put(rider+0xc0,4);
                Put(bike+0x324,bike==player?0:bike==ai?1:2);
            }
            long clock=Alloc(16), now=Alloc(4); Put(now,1000);
            var clockCode=new List<byte>{0xa1}; D(clockCode,now); clockCode.Add(0xc3);
            Write(clock,clockCode.ToArray()); Put(image+0x21c414,clock);
            Put(image+0x218059,0); Put(image+0x1cd4d4,0); Put(image+0x1cd4e4,0); Put(image+0x1cd47f,0);
            long entry=RuntimePatch.InstallKnockdown(new IntPtr(-1),image,meter,false,false,750,2500,3,mode==1,officer,delegate{});
            long state=entry+RuntimePatch.KnockdownStateOffset;
            Check(Get(state+108)==750 && Get(state+112)==2500 && Get(state+116)==3,"installer passes exact rule units "+mode);
            Write(image+0x1559d,new byte[]{0xc3}); // resume immediately after the stock player pointer assignment
            Action start=delegate { Run(image+0x15598,0,0,0,0,0,player,0); };
            start();
            foreach(int service in new[]{0x428c,0x5fb0,0x12cb,0x2c98}) Write(image+service,new byte[]{0xc3});
            // Enter each stock fall at its commit, with its real stack layout.
            // Only animation, position refresh and the synthetic frame cookie are stubbed.
            Action<long,bool> fall=delegate(long bike,bool other) {
                long thunk=Alloc(64); var code=new List<byte>{0x55,0x89,0xe5,0x83,0xec,0x34};
                if (other) code.AddRange(new byte[]{0x53,0x56,0x57});
                else code.AddRange(new byte[]{0x56,0x53,0x57});
                code.Add(0xe9); D(code,image+(other?0x145bd:0x13dcb)-(thunk+code.Count+4)); Write(thunk,code.ToArray());
                Run(thunk,7,0,1,bike,(uint)Get(bike+0x2fc),0,other?2:0);
            };
            long riderAi=(uint)Get(ai+0x2fc), riderPlayer=(uint)Get(player+0x2fc);
            if(meter) {
                long contact=Alloc(64); var code=new List<byte>{0x68}; D(code,ai); code.Add(0x68); D(code,player);
                Mov(code,0xb9,state); Mov(code,0xba,1); Call(code,contact,entry); code.Add(0xc3); Write(contact,code.ToArray());
                Run(contact,0,0,0,0,0,0,0);
            }
            Put(image+0x218059,1); fall(ai,false); Put(image+0x218059,0);
            Check(Get(state+144)==0 && Get(state+28)==0,"network fall excluded "+mode);
            Put(ai+0x2f4,0); fall(ai,mode%2==0);
            Check(Get(state+28)==(meter?1:0) && Get(state+144)==(officer?1:0),"credited fall and independent retirement "+mode);
            fall(patrol,false);
            Check((Get(patrol+0xc0)&4)!=0 && Get(state+144)==(officer?1:0),"patrol excluded from racer retirement "+mode);
            if(officer) {
                Check((Get(ai+0xc0)&15)==0 && (Get(riderAi+0xc0)&15)==0 && Get(riderAi+0x1f8)==0x3fffffff &&
                    Get(image+0x215ffc)==0,"retired rider and bike are inactive without a finish time");
                Run(image+0x12bd0,ai,0,0,0,0,0,0); Run(image+0x167c0,riderAi,0,0,0,0,0,0);
                Run(image+0x4a640,riderAi,0,0,0,0,0,0);
                Check(Get(ai+0x2f4)==1 && (Get(riderAi+0xc0)&16)==0 && Get(riderAi+0x1f8)==0x3fffffff &&
                    Get(image+0x215ffc)==0,"same-frame remount, distance refresh and finish registration all ignore a retiree");
                long manager=Alloc(32), head=Alloc(32), tail=Alloc(32);
                Put(image+0x1c55d0,manager); Put(manager,head);
                long[] objects={head,ai,riderAi,patrol,tail};
                for(int i=1;i<objects.Length;i++){Put(objects[i-1],objects[i]);Put(objects[i]+4,objects[i-1]);}
                Run(image+0x49ff0,0,0,0,0,0,0,0); // original deferred world sweep
                Check((uint)Get(head)==patrol && (uint)Get(patrol+4)==head && (uint)Get(patrol)==tail &&
                    (uint)Get(riderAi+0x1c4)==ai && (uint)Get(ai+0x2fc)==riderAi,"only active racer remains scheduled; both retired roster references survive");
                fall(ai,false); Check(Get(state+144)==1,"repeated fall is idempotent");
            }
            Put(riderPlayer+0xc0,20); fall(player,false);
            Check(Get(state+140)==0,"already finished player protected "+mode);
            Put(riderPlayer+0xc0,4); fall(player,mode%2==1);
            Check(Get(state+140)==(mode==0?0:1) && Get(image+0x1cd47f)==0,"ejection leaves the race visible "+mode);
            start(); Check(Get(state+140)==0 && Get(state+144)==0 && Get(state+28)==0,"new race clears rules and awards "+mode);
            if(mode!=0) {
                // Execute the original terrain-height comparisons in both integrators.
                // Return at their first downstream instruction, with the fixture's EBP restored.
                Action<bool,long,int> physics=delegate(bool other,long actor,int heightAboveSurface) {
                    int slot=other?0x24:0x28;
                    long stub=Alloc(64); var body=new List<byte>{0x55,0x89,0xe5,0x83,0xec,0x30,0x89,0x55,(byte)(256-slot),0xe9};
                    int compare=other?0x4bb58:0x4b69e;
                    D(body,image+compare-(stub+body.Count+4)); Write(stub,body.ToArray());
                    Run(stub,0,heightAboveSurface,actor,0,actor,0,0);
                };
                foreach(int pc in new[]{0x4b6a7,0x4b814,0x4bb61,0x4bc7a}) Write(image+pc,new byte[]{0xc9,0xc3});
                Write(image+0x57b04,new byte[]{0xc3});
                Action tick=delegate{ Run(image+0x57afd,0,0,0,0,0,0,0); };
                long animSpeed=Alloc(4); var readSpeed=new List<byte>{0x89,0x0d}; D(readSpeed,animSpeed); readSpeed.Add(0xc3);
                Write(image+0x19c98,readSpeed.ToArray());
                foreach(bool other in new[]{false,true}) {
                    start(); Put(image+0x1cd47f,0); Put(image+0x1cd4e4,180); Put(image+0x2185bc,5);
                    Put(now,1000); fall(player,other);
                    Put(now,21000); physics(other,riderPlayer,1); physics(other,player,0); physics(other,riderAi,-1); tick();
                    Check(Get(state+140)==1 && Get(image+0x1cd4e4)==0 && Get(image+0x1cd47f)==0,"no airborne timeout, bike/AI ground contacts ignored");
                    physics(other,riderPlayer,0); Check(Get(state+140)==2,"native ground branch starts the hold "+other);
                    Put(now,23000); fall(player,other); physics(!other,riderPlayer,-1); tick();
                    Put(image+0x1cd4d4,1); tick(); Put(now,83000); tick(); Put(image+0x1cd4d4,0); tick();
                    Run(image+0x19c92,0,0,ai,0,riderAi,0,0); Check(Get(animSpeed)==Get(riderAi+0xec),"AI recovery decision is original");
                    Put(riderPlayer+0xec,0); Run(image+0x19c92,0,0,player,0,riderPlayer,0,0);
                    Check(Get(animSpeed)==2500 && Get(riderPlayer+0xec)==0,"player stays in fall animation without adding velocity");
                    Put(now,83999); tick(); Check(Get(state+140)==2 && Get(image+0x1cd47f)==0,"2999 active ms, repeated contact and sixty paused seconds");
                    Put(now,84000); tick(); Check(Get(state+140)==3 && Get(image+0x2185bc)==5 && Get(image+0x1cd47f)==1,"wreck exactly 3000 active ms after first rider contact");
                    Put(image+0x1cd47f,0); tick(); Check(Get(image+0x1cd47f)==0,"wreck is issued once");
                }
                start(); Put(image+0x1cd4e4,90); Put(image+0x2185bc,4); fall(player,false); tick();
                Check(Get(state+140)==0 && Get(image+0x1cd4e4)==90,"an earlier unrelated native verdict survives"); Put(image+0x1cd4e4,0);
                fall(player,false); physics(false,riderPlayer,0); start(); Put(now,100000); tick();
                Check(Get(state+140)==0 && Get(image+0x1cd47f)==0,"restarting during the hold cancels the old verdict");
            }
            if(officer){Put(ai+0xc0,7);Put(riderAi+0xc0,4);fall(ai,false);Check(Get(state+144)==1,"reused roster slot retires in the new race");}
        }
    }

    static int Main(string[] args)
    {
        SetErrorMode(0x8003);AddVectoredExceptionHandler(1,handler);
        try{
            if (args[0] == "--race-rules") RaceRules(args[1]);
            else if (args[0] == "--opponents") Opponents(args[1], args[2]);
            else if (args[0] == "--police-graphics") PoliceGraphics(args[1], args[2]);
            else { Directions(args[0]); Length(args[0]); Audio(args[0],args[1]); Opponents(args[0],args[2]);
                PoliceGraphics(args[0], Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])), "DATA", "BIKERS")); RaceRules(args[0]); }
            Console.WriteLine("Feature tests: "+passed+" passed");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
