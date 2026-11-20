using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Execute the shipped x86 callback in this fixture's own address space.
// Only DirectDraw system-memory surfaces, in normal cooperative mode. No window or primary surface.
internal static class KnockdownTests
{
    [DllImport("kernel32")] static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32")] static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);
    [DllImport("kernel32")] static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [DllImport("ddraw")] static extern int DirectDrawCreate(IntPtr guid, out IntPtr device, IntPtr outer);
    [DllImport("gdi32")] static extern int StretchDIBits(IntPtr dc,int dx,int dy,int dw,int dh,
        int sx,int sy,int sw,int sh,int[] bits,int[] info,int usage,uint rop);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint Release(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Cooperative(IntPtr self, IntPtr window, int flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateSurface(IntPtr self, byte[] desc, out IntPtr surface, IntPtr outer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Lock(IntPtr self, IntPtr rect, [In, Out] byte[] desc, int flags, IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Unlock(IntPtr self, IntPtr bits);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetKey(IntPtr self, int flags, int[] bounds);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int BltFast(IntPtr self, int x, int y, IntPtr source, int[] rect, int flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreatePalette(IntPtr self, int flags, byte[] entries, out IntPtr pal, IntPtr outer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetPalette(IntPtr self, IntPtr pal);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetEntries(IntPtr self, int flags, int start, int count, byte[] entries);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDC(IntPtr self, out IntPtr dc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReleaseDC(IntPtr self, IntPtr dc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint Clock();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Signal(int kind, IntPtr actor, IntPtr other);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int NewVoice(IntPtr self,IntPtr desc,out IntPtr buffer,IntPtr outer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int VoiceLock(IntPtr self,int offset,int bytes,out IntPtr data,out int length,IntPtr second,IntPtr secondSize,int flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int VoiceUnlock(IntPtr self,IntPtr data,int bytes,IntPtr second,int secondSize);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int VoicePlay(IntPtr self,int reserved,int priority,int flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int VoiceVolume(IntPtr self,int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int VoiceStatus(IntPtr self,out int status);
    static readonly Clock clock = delegate { return wall; };
    static uint wall;
    static Signal signal;
    static IntPtr image, state, canvas, player, rival, second, traffic, device;
    static byte[] palette;
    static readonly Dictionary<int,byte> expectedColors = new Dictionary<int,byte>();
    const int LayerWidth = RuntimePatch.KnockdownCanvasWidth, LayerHeight = RuntimePatch.KnockdownCanvasHeight;
    const int Total = 28, Spree = 32, Chain = 36, Pending = 56, Queued = 60, Blits = 92;
    const int CreditMs=108, WindowMs=112, PerVoice=116, Queue=124, ClipCount=128;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static IntPtr Alloc(int size)
    {
        IntPtr p = VirtualAlloc(IntPtr.Zero,(UIntPtr)size,0x3000,4);
        Check(p != IntPtr.Zero,"fixture allocation"); return p;
    }
    static IntPtr Executable(byte[] bytes)
    {
        IntPtr p = Alloc(bytes.Length);
        Marshal.Copy(bytes,0,p,bytes.Length);
        uint old;
        Check(VirtualProtect(p,(UIntPtr)bytes.Length,0x20,out old) &&
            FlushInstructionCache(new IntPtr(-1),p,(UIntPtr)bytes.Length),"fixture callback map");
        return p;
    }
    static void Put(IntPtr p, int offset, IntPtr value) { Marshal.WriteIntPtr(p,offset,value); }
    static int Read(int offset) { return Marshal.ReadInt32(state,offset); }
    static IntPtr Rider(IntPtr bike) { return Marshal.ReadIntPtr(bike,0x2fc); }
    static IntPtr MakeBike()
    {
        IntPtr bike=Alloc(0x350), rider=Alloc(0x340);
        Marshal.WriteInt32(bike,0xc4,4); Marshal.WriteInt32(rider,0xc4,8);
        Put(bike,0x2fc,rider); Put(rider,0x1c4,bike); return bike;
    }
    static T Method<T>(IntPtr com, int slot) where T : class
    {
        IntPtr proc=Marshal.ReadIntPtr(Marshal.ReadIntPtr(com),slot*IntPtr.Size);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(proc,typeof(T));
    }
    static void Setup()
    {
        Check(IntPtr.Size==4,"must use the x86 test process");
        image=Alloc(0x220000); state=Alloc(0x1000); canvas=Alloc(RuntimePatch.KnockdownCanvasBytes);
        byte[] atlas=RuntimePatch.LoadKnockdownAtlas(); IntPtr art=Alloc(atlas.Length);
        Marshal.Copy(atlas,0,art,atlas.Length);
        Put(state,0,image); Put(state,4,art); Put(state,12,canvas); Put(state,16,Alloc(32768*20));
        Marshal.WriteInt32(state,CreditMs,2000); Marshal.WriteInt32(state,WindowMs,18000);
        Marshal.WriteInt32(state,PerVoice,1); Marshal.WriteInt32(state,ClipCount,14); Put(state,Queue,Alloc(65536));
        Check(palette.Length==1024,"256 PALETTEENTRY values");
        IntPtr colors=Alloc(RuntimePatch.KnockdownColorMapBytes);
        Marshal.Copy(palette,0,colors,palette.Length); Put(state,20,colors);
        Marshal.ThrowExceptionForHR(DirectDrawCreate(IntPtr.Zero,out device,IntPtr.Zero));
        Marshal.ThrowExceptionForHR(Method<Cooperative>(device,20)(device,IntPtr.Zero,8));
        Put(image,0x1cd4ec,device);
        Put(image,0x21c414,Marshal.GetFunctionPointerForDelegate(clock));
        IntPtr entry=Executable(KnockdownNative.Code);
        // stdcall(event,actor,other) -> Knockdown(state,event,actor,other), fastcall.
        byte[] adapter = {0x55,0x8b,0xec,0xff,0x75,16,0xff,0x75,12,0x8b,0x55,8,0xb9,0,0,0,0,
            0xb8,0,0,0,0,0xff,0xd0,0x5d,0xc2,12,0};
        BitConverter.GetBytes(state.ToInt32()).CopyTo(adapter,13);
        BitConverter.GetBytes(entry.ToInt32()).CopyTo(adapter,18);
        signal=(Signal)Marshal.GetDelegateForFunctionPointer(Executable(adapter),typeof(Signal));
        player=MakeBike(); rival=MakeBike(); second=MakeBike(); traffic=Alloc(0x350);
        Marshal.WriteInt32(traffic,0xc4,16); Put(image,0x1a69ec,player);
    }
    static void Event(int kind) { signal(kind,IntPtr.Zero,IntPtr.Zero); }
    static void Fall(IntPtr bike) { signal(3,bike,IntPtr.Zero); }
    static void Score(IntPtr bike) { signal(2,player,bike); Fall(bike); }
    static void Rules()
    {
        wall=5; Event(0); signal(1,rival,second); Fall(rival);
        signal(2,rival,second); Fall(second); signal(2,player,player); signal(1,player,traffic); Fall(traffic);
        Check(Read(Total)==0,"unassisted AI, AI combat, self and traffic");
        signal(2,player,rival); wall+=1999; Fall(rival); Fall(rival);
        Check(Read(Total)==1 && Read(Pending)==1,"1999 ms and duplicate fall");
        signal(1,Rider(second),player); wall+=2000; Fall(second);
        Check(Read(Total)==1,"exactly 2000 ms");
        signal(1,Rider(player),Rider(second)); signal(1,rival,player); Fall(second); Fall(rival);
        Check(Read(Total)==3 && Read(Chain)==3 && Read(Pending)==1 && Read(Queued)==2,"independent tags, three awards in order");
        wall+=17999; Score(second); Check(Read(Chain)==4,"last millisecond of chain");
        wall+=18000; Score(rival); Check(Read(Chain)==1 && Read(Spree)==5,"chain timeout preserves unbroken spree");
        Fall(player); Check(Read(Total)==5 && Read(Spree)==0 && Read(Chain)==0,"player crash");
        Marshal.WriteInt32(player,0x2f4,2); signal(1,player,rival); Fall(rival);
        Check(Read(Total)==5,"abandoned player bike");
        signal(1,Rider(player),rival); Fall(rival); Marshal.WriteInt32(player,0x2f4,0);
        Check(Read(Total)==6,"actual on-foot player contact");
        Marshal.WriteInt32(rival,0x2f4,1); signal(2,player,rival); Marshal.WriteInt32(rival,0x2f4,0); Fall(rival);
        Check(Read(Total)==6,"hits to an already down rider");
        signal(2,player,rival); wall+=700; Event(4); wall+=90000; Event(5); wall+=1299; Fall(rival);
        Check(Read(Total)==7,"pause freezes attribution");
        signal(1,player,rival); Event(6); Event(0); Fall(rival);
        Check(Read(Total)==0 && Read(Chain)==0,"address reuse after restarting the race");
        wall=uint.MaxValue-500; signal(2,player,rival); wall=499; Fall(rival);
        Check(Read(Total)==1,"clock wrap");
        Marshal.WriteInt16(image,0x218059,1); Score(second); Marshal.WriteInt16(image,0x218059,0);
        Check(Read(Total)==1,"single-player awards only");
        Console.WriteLine("PASS attribution, duplicate falls, both timers, voice tiers, pause, restart and clock wrap");
    }

    static void ExpandedGrid()
    {
        Event(0); wall=100;
        const int count=10000, stride=0x350;
        IntPtr bikes=Alloc(count*stride), riders=Alloc(count*stride);
        for(int i=0;i<count;i++) {
            IntPtr bike=IntPtr.Add(bikes,i*stride), rider=IntPtr.Add(riders,i*stride);
            Marshal.WriteInt32(bike,0xc4,4); Marshal.WriteInt32(rider,0xc4,8);
            Put(bike,0x2fc,rider); Put(rider,0x1c4,bike);
            signal(1,player,bike);
        }
        for(int i=count-1;i>=0;i--) Fall(IntPtr.Add(bikes,i*stride));
        Check(Read(Total)==count && Read(Chain)==count && Read(Queued)==7,"10,000 simultaneous tags; only ordinary voice awards queue");
        Marshal.WriteInt32(state,Total,99998); Score(rival); Score(second);
        Check(Read(Total)==99999 && Read(Chain)==count+2,"five-digit saturation leaves streak independent");
        Console.WriteLine("PASS 10,000 tagged riders, 99998 -> 99999 -> 99999");
    }

    static void Announcer(string game,string output)
    {
        Event(0);
        int[] first={1,2,3,4,0,9,10,11,12,0,0};
        int ordinary=0;
        for(int i=0;i<first.Length;i++) {
            Score(rival);
            if(first[i]!=0) {
                Check((ordinary==0?Read(Pending):Marshal.ReadByte(Marshal.ReadIntPtr(state,Queue),ordinary-1))==first[i],"ordinary award at streak "+(i+1));
                ordinary++;
            }
            Check(Read(Queued)==ordinary-1,"top tier never joins the FIFO at streak "+(i+1));
        }
        Event(0); Marshal.WriteInt32(state,CreditMs,750); Marshal.WriteInt32(state,WindowMs,2500);
        Marshal.WriteInt32(state,PerVoice,3);
        signal(2,player,rival); wall+=750; Fall(rival); Check(Read(Total)==0,"configured attribution boundary");
        signal(2,player,rival); wall+=749; Fall(rival); wall+=2499; Score(second);
        Check(Read(Chain)==2 && Read(Pending)==0,"custom chain window and two silent awards");
        wall+=2500; Score(rival); Check(Read(Chain)==1 && Read(Pending)==6 && Read(Queued)==0,"third scores spree after exact chain expiry");
        Fall(player); Score(second); Score(rival); Score(second);
        Check(Read(Total)==6 && Read(Queued)==1,"player fall does not reset broadcast cadence");
        Event(0); Marshal.WriteInt32(state,PerVoice,1); Score(rival); wall+=2500; Score(rival);
        Check(Marshal.ReadByte(Marshal.ReadIntPtr(state,Queue))==14,"standalone single uses Ownage");
        Event(0); Marshal.WriteInt32(state,CreditMs,2000); Marshal.WriteInt32(state,WindowMs,18000);
        Console.WriteLine("PASS configured credit and combo boundaries, persistent every-N cadence and single-award FIFO");
        string path=Path.Combine(game,"Tools","KnockdownVoice.bank");
        if(File.Exists(path)) VoiceMix(File.ReadAllBytes(path));
        string packaged=output==null?null:Path.Combine(output,"build","Tools","KnockdownVoice.bank");
        if(File.Exists(packaged)) VoiceMix(File.ReadAllBytes(packaged));
    }

    // Record the bytes sent by the shipped callback. The sound interfaces
    // below return memory owned by this fixture and never open an audio device.
    static void VoiceMix(byte[] original)
    {
        Check(RuntimePatch.ValidKnockdownBank(original),"announcer bank structure");
        byte[] mastered=(byte[])original.Clone(); RuntimePatch.LevelKnockdownVoices(mastered);
        Check(RuntimePatch.ValidKnockdownBank(mastered),"mastering changed clip layout");
        IntPtr bank=Alloc(mastered.Length),pcm=Alloc(441000),dev=Alloc(4),buffer=Alloc(4);
        Marshal.Copy(mastered,0,bank,mastered.Length);
        Put(dev,0,Alloc(4*4)); Put(buffer,0,Alloc(21*4));
        var callbacks=new List<Delegate>();
        Action<IntPtr,int,Delegate> bind=delegate(IntPtr obj,int slot,Delegate fn) {
            callbacks.Add(fn); Put(Marshal.ReadIntPtr(obj),4*slot,Marshal.GetFunctionPointerForDelegate(fn));
        };
        int prepared=0,plays=0,attenuation=0,length=0,playing=0;
        bind(buffer,9,new VoiceStatus(delegate(IntPtr self,out int status){status=playing;return 0;}));
        bind(dev,3,new NewVoice(delegate(IntPtr self,IntPtr desc,out IntPtr voice,IntPtr outer) {
            length=Marshal.ReadInt32(desc,8); voice=buffer; prepared++; return 0;
        }));
        bind(buffer,2,new Release(delegate { return 0; }));
        bind(buffer,18,new Release(delegate { return 0; }));
        bind(buffer,11,new VoiceLock(delegate(IntPtr self,int pos,int bytes,out IntPtr data,out int n,IntPtr tail,IntPtr tailSize,int flags) {
            data=pcm; n=bytes; return 0;
        }));
        bind(buffer,19,new VoiceUnlock(delegate { return 0; }));
        bind(buffer,15,new VoiceVolume(delegate(IntPtr self,int value) { attenuation=value; return 0; }));
        bind(buffer,12,new VoicePlay(delegate { plays++; return 0; }));
        double minGain=double.MaxValue,maxGain=double.MinValue;
        int peak=0, savedVolume=Marshal.ReadInt32(image,0x1c5544);
        IntPtr savedBank=Marshal.ReadIntPtr(state,8),savedDevice=Marshal.ReadIntPtr(image,0x1abc98);
        try {
            Put(state,8,bank); Put(image,0x1abc98,dev);
            int clips=(BitConverter.ToInt32(original,4)-4)/8;
            Marshal.WriteInt32(state,ClipCount,clips);
            for(int id=0;id<clips;id++) {
                int offset=BitConverter.ToInt32(original,4+8*id),bytes=BitConverter.ToInt32(original,8+8*id);
                foreach(int volume in id==1?new[]{0,1,25,50,75,100,110}:new[]{100}) {
                    Event(0); Marshal.WriteInt32(image,0x1c5544,volume); Marshal.WriteInt32(state,Pending,id+1);
                    int previous=plays,created=prepared; attenuation=int.MinValue;
                    Event(5);
                    if(volume==0) { Check(plays==previous && prepared==created,"mute routed audio"); continue; }
                    Check(plays==previous+1 && prepared==created+1 && attenuation==0 && length==bytes,"voice submission or gain mismatch");
                    var actual=new short[bytes/2]; Marshal.Copy(pcm,actual,0,actual.Length);
                    long before=0,after=0; int sourcePeak=0;
                    for(int i=0;i<actual.Length;i++) {
                        int at=offset+2*i,old=BitConverter.ToInt16(original,at),sample=actual[i];
                        Check(sample==BitConverter.ToInt16(mastered,at)*Math.Min(volume,100)/100,"wrong effects-slider amplitude");
                        before+=(long)old*old; after+=(long)sample*sample;
                        sourcePeak=Math.Max(sourcePeak,Math.Abs(old)); peak=Math.Max(peak,Math.Abs(sample));
                    }
                    if(volume==100) {
                        Check(before>0,"empty announcer clip");
                        double db=10*Math.Log10((double)after/before);
                        if(sourcePeak<=20000)Check(db>=2.5,"quiet announcer clip did not receive the expected boost");
                        minGain=Math.Min(minGain,db); maxGain=Math.Max(maxGain,db);
                    }
                }
            }
            Event(0); Score(rival); Score(second); Score(rival);
            int start=plays; playing=1; Event(5); Event(5);
            Check(plays==start+1 && Read(88)==1 && Read(Queued)==1,"busy line keeps following awards queued");
            playing=0; Event(5); Check(Read(88)==2,"second line plays in order"); Event(5);
            Check(plays==start+3 && Read(88)==3 && Read(Pending)==0,"third line drains the queue");
            foreach(int tier in new[]{5,13}) {
                Event(0); start=plays;
                Marshal.WriteInt32(state,Spree,tier==5?4:9); Marshal.WriteInt32(state,Chain,tier==5?4:9);
                Marshal.WriteInt32(state,Total,tier==5?4:9); Score(rival);
                Check(plays==start+1 && Read(88)==tier && Read(Pending)==0,"idle top tier plays on the hit "+tier);
                playing=1;
                for(int n=0;n<50;n++) {
                    if(tier==5) Marshal.WriteInt32(state,Chain,4);
                    Score(rival); Event(5);
                }
                Check(plays==start+1 && Read(Pending)==0 && Read(Queued)==0,"50 busy top-tier hits leave no backlog "+tier);
                playing=0;
                for(int n=0;n<60;n++) Event(5);
                Check(plays==start+1,"no new hits means no delayed top-tier replay "+tier);
                if(tier==5) Marshal.WriteInt32(state,Chain,4);
                Score(second); Check(plays==start+2 && Read(88)==tier,"a fresh hit can announce after the voice is free "+tier);
            }
            Event(0); start=plays;
            for(int n=0;n<50;n++) Score(rival);
            foreach(int ordinaryId in new[]{1,2,3,4,9,10,11,12}) {
                Event(5); Check(Read(88)==ordinaryId,"ordinary FIFO with fifty simultaneous hits: "+ordinaryId);
            }
            Event(5); Check(plays==start+8 && Read(Pending)==0 && Read(Queued)==0,"only ordinary clips drain after a burst");
            Console.WriteLine("PASS Rampage and Holy Shit play on an idle hit, skip busy hits, never replay from a backlog");
        } finally {
            Event(6); Put(state,8,savedBank); Put(image,0x1abc98,savedDevice);
            Marshal.WriteInt32(state,ClipCount,14);
            Marshal.WriteInt32(image,0x1c5544,savedVolume); GC.KeepAlive(callbacks);
        }
        Check(peak<32767 && minGain>=0,"announcer signal clipped or attenuated");
        Console.WriteLine("PASS captured native voice buffers: RMS gain {0:F1}..{1:F1} dB at 100%, peak {2:F2} dBFS; mute and linear 1/25/50/75/100% slider",
                          minGain,maxGain,20*Math.Log10(peak/32768.0));
    }

    sealed class Surface : IDisposable
    {
        internal readonly int W,H;
        internal readonly IntPtr Native;
        internal Surface(int w,int h)
        {
            W=w; H=h;
            byte[] desc=new byte[108]; // 32-bit DDSURFACEDESC (DirectDraw 1).
            Put32(desc,0,108); Put32(desc,4,0x1007); Put32(desc,8,h); Put32(desc,12,w);
            Put32(desc,72,32); Put32(desc,76,0x60); Put32(desc,84,8); Put32(desc,104,0x840);
            Marshal.ThrowExceptionForHR(Method<CreateSurface>(device,6)(device,desc,out Native,IntPtr.Zero));
        }
        static void Put32(byte[] a,int offset,int value) { BitConverter.GetBytes(value).CopyTo(a,offset); }
        static void Transfer(IntPtr surface,int w,int h,byte[] pixels,bool writing)
        {
            Check(pixels.Length==w*h,"surface pixel count");
            var desc=new byte[108]; Put32(desc,0,desc.Length);
            Marshal.ThrowExceptionForHR(Method<Lock>(surface,25)(surface,IntPtr.Zero,desc,1,IntPtr.Zero));
            try {
                int pitch=BitConverter.ToInt32(desc,16);
                var bits=new IntPtr(BitConverter.ToInt32(desc,36));
                Check(pitch>=w && bits!=IntPtr.Zero && BitConverter.ToInt32(desc,84)==8,"locked indexed surface");
                for(int y=0;y<h;y++) {
                    IntPtr row=IntPtr.Add(bits,y*pitch);
                    if(writing) Marshal.Copy(pixels,y*w,row,w);
                    else Marshal.Copy(row,pixels,y*w,w);
                }
            } finally { Marshal.ThrowExceptionForHR(Method<Unlock>(surface,32)(surface,IntPtr.Zero)); }
        }
        internal void Write(byte[] pixels) { Transfer(Native,W,H,pixels,true); }
        internal byte[] ReadPixels() { return ReadSurface(Native,W,H); }
        internal static byte[] ReadSurface(IntPtr native,int w,int h) {
            var pixels=new byte[w*h]; Transfer(native,w,h,pixels,false); return pixels;
        }
        internal void DrawBob(int x,int y,int w,byte[] bob)
        {
            Check(bob.Length%w==0,"BOB row alignment");
            using(var source=new Surface(w,bob.Length/w)) {
                source.Write(bob);
                Marshal.ThrowExceptionForHR(Method<SetKey>(source.Native,29)(source.Native,8,new[]{0,0}));
                Marshal.ThrowExceptionForHR(Method<BltFast>(Native,7)(Native,x,y,source.Native,new[]{0,0,w,source.H},0x11));
            }
        }
        public void Dispose() { Method<Release>(Native,2)(Native); }
    }

    static int Channel(int argb,int shift) { return (argb>>shift)&255; }
    static byte ExpectedIndex(int r,int g,int b)
    {
        int key=(r<<16)|(g<<8)|b;
        byte index;
        if(expectedColors.TryGetValue(key,out index)) return index;
        int best=int.MaxValue;
        for(int i=1;i<256;i++) {
            int dr=palette[i*4]-r,dg=palette[i*4+1]-g,db=palette[i*4+2]-b;
            int distance=dr*dr+4*dg*dg+db*db+(dr-dg)*(dr-dg)+(db-dg)*(db-dg);
            if(distance<best) { best=distance; index=(byte)i; }
        }
        expectedColors[key]=index;
        return index;
    }
    static int Sample(int[] layer,int x,int y,int step,int shift)
    {
        int i=(y*LayerWidth+x)*step, a=Channel(layer[i],shift);
        if(step==1)return a;
        return (a+Channel(layer[i+1],shift)+Channel(layer[i+LayerWidth],shift)+Channel(layer[i+LayerWidth+1],shift)+2)/4;
    }
    static void SetViewport(int w,int h,int step,int left,int top)
    {
        Marshal.WriteInt32(image,0x1a2934,w); Marshal.WriteInt32(image,0x1a2938,h);
        Marshal.WriteInt32(image,0x1cd4b4,left); Marshal.WriteInt32(image,0x1cd4b8,top);
        Marshal.WriteByte(image,0x1a292a,(byte)(step==1?1:0));
    }
    static byte[] Pattern(int w,int h)
    {
        var pixels=new byte[w*h];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) pixels[y*w+x]=(byte)((x/4+17*(y/4))&255);
        return pixels;
    }
    static void Draw(Surface target)
    {
        int old=Read(Blits); signal(5,target.Native,IntPtr.Zero);
        Check(Read(Blits)==old+1,"the native color-key BltFast must succeed");
    }

    static void AtlasCoverage()
    {
        byte[] atlas=RuntimePatch.LoadKnockdownAtlas();
        for(int y=14;y<43;y++)
            for(int x=12;x<99;x++)
                Check(atlas[(y*320+x)*4+3]==255,"hole in solid LCD glass at "+x+","+y);
        Check(atlas[3]==0 && atlas[127*4+3]==0,"panel corners must show the road");
        Console.WriteLine("PASS 2,523 opaque LCD glass pixels and transparent exterior corners in the packed art");
    }

    static void StockInks(string game)
    {
        // The first two rows contain every stock index. The other 48 come from
        // the original meter to expose row, orientation and transparency errors.
        byte[] bob=new byte[128*50], stock=File.ReadAllBytes(Path.Combine(game,"IMAGES","BIGMETER.BOB"));
        Check(stock.Length==190*64,"stock meter dimensions");
        for(int i=0;i<256;i++)bob[i]=(byte)i;
        for(int y=0;y<48;y++)Array.Copy(stock,(y+16)*190+62,bob,(y+2)*128,128);
        byte[] atlas=new byte[320*112*4];
        for(int y=0;y<50;y++)for(int x=0;x<128;x++) {
            int index=bob[y*128+x], at=(y*320+x)*4;
            atlas[at]=palette[index*4+2]; atlas[at+1]=palette[index*4+1]; atlas[at+2]=palette[index*4];
            atlas[at+3]=(byte)(index==0?0:255);
        }
        IntPtr original=Marshal.ReadIntPtr(state,4), control=Alloc(atlas.Length);
        Marshal.Copy(atlas,0,control,atlas.Length); Put(state,4,control);
        try {
            Event(0); SetViewport(640,480,1,0,0);
            using(var d=new Surface(640,480)) {
                byte[] backdrop=Pattern(d.W,d.H); d.Write(backdrop);
                d.DrawBob(256,8,128,bob); byte[] stockFrame=d.ReadPixels();
                d.Write(backdrop); Draw(d);
                Check(System.Linq.Enumerable.SequenceEqual(stockFrame,d.ReadPixels()),"stock BOB and its RGBA equivalent differ");
                var source=Surface.ReadSurface(Marshal.ReadIntPtr(state,104),128,50);
                Check(System.Linq.Enumerable.SequenceEqual(bob,source),"palette IDs or row order changed before BltFast");
            }
        } finally { Put(state,4,original); }
        Console.WriteLine("PASS 256 original swatches and 128x48 BIGMETER pixels survive RGBA to sprite, byte for byte");
    }

    static byte[] BannerPixels()
    {
        var data=new byte[RuntimePatch.KnockdownBannerColumns*RuntimePatch.KnockdownBannerRows];
        Marshal.Copy(IntPtr.Add(canvas,128*50*4),data,0,data.Length);
        return data;
    }
    static IntPtr WithoutLetterMasks(byte[] art)
    {
        byte[] blank=(byte[])art.Clone(); Array.Clear(blank,57*320*4,26*320*4);
        IntPtr copy=Alloc(blank.Length); Marshal.Copy(blank,0,copy,blank.Length);
        return copy;
    }
    // Expand the two native inputs independently of the surface uploader.
    static int[] CanonicalLayer(bool active,int bannerStep=1,int logicalWidth=LayerWidth)
    {
        var layer=new int[LayerWidth*LayerHeight];
        int viewport=logicalWidth/bannerStep;
        int meterX=active?(viewport-128/bannerStep)/2*bannerStep:0;
        for(int y=0;y<50;y++)Marshal.Copy(IntPtr.Add(canvas,4*y*128),layer,y*LayerWidth+meterX,128);
        if(!active)return layer;
        byte[] banner=BannerPixels();
        var colors=new int[256];
        for(int i=1;i<256;i++)
            colors[i]=unchecked((int)0xff000000)|(palette[4*i]<<16)|(palette[4*i+1]<<8)|palette[4*i+2];
        int dockX=meterX+130;
        int dockWidth=Math.Min(viewport*9/40,(logicalWidth-dockX)/bannerStep);
        int dockHeight=dockWidth*140/400, dockY=(50/bannerStep-dockHeight)/2*bannerStep;
        for(int y=0;y<dockHeight*bannerStep;y++) {
            int row=(int)((y/bannerStep+0.5)*140/dockHeight);
            for(int x=0;x<dockWidth*bannerStep;x++) {
                int column=(int)((x/bannerStep+0.5)*RuntimePatch.KnockdownBannerColumns/dockWidth);
                layer[(dockY+y)*LayerWidth+dockX+x]=colors[banner[row*RuntimePatch.KnockdownBannerColumns+column]];
            }
        }
        return layer;
    }

    // Input comes from the packaged launcher's own resource decoder. Only the
    // known callback compiled into this fixture runs. Capture both sides of BltFast.
    static void TraceAtlas(string folder)
    {
        Check(System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(Path.Combine(folder,"native.bin")),KnockdownNative.Code),
              "fixture does not match the packaged sprite renderer");
        byte[] decoded=File.ReadAllBytes(Path.Combine(folder,"decoded.bgra"));
        Check(decoded.Length==320*112*4,"trace atlas dimensions");
        IntPtr atlas=Marshal.ReadIntPtr(state,4),withoutGlyphs=WithoutLetterMasks(decoded);
        Marshal.Copy(decoded,0,atlas,decoded.Length);
        foreach(int step in new[]{1,2})foreach(int chain in new[]{0,10}) {
            Event(0); wall=1000;
            for(int n=0;n<chain;n++)Score(rival);
            if(chain>0) { wall+=9000; Marshal.WriteInt32(state,Total,12345); }
            string stem=Path.Combine(folder,(step==1?"high-":"low-")+chain);
            SetViewport(640/step,480/step,step,0,0);
            using(var d=new Surface(640/step,480/step)) {
                foreach(byte backdrop in new byte[]{17,201}) {
                    byte[] bg=new byte[d.W*d.H];
                    for(int i=0;i<bg.Length;i++)bg[i]=backdrop;
                    d.Write(bg); Draw(d);
                    File.WriteAllBytes(stem+"-"+backdrop+".idx",d.ReadPixels());
                }
                var source=Surface.ReadSurface(Marshal.ReadIntPtr(state,104),LayerWidth,LayerHeight);
                File.WriteAllBytes(stem+"-source.bob",source);
                byte[] layer=new byte[LayerWidth*LayerHeight*4];
                Buffer.BlockCopy(CanonicalLayer(chain>=2),0,layer,0,layer.Length);
                File.WriteAllBytes(stem+"-layer.bgra",layer);
                if(chain>=2) {
                    Put(state,4,withoutGlyphs);
                    try { Draw(d); File.WriteAllBytes(stem+"-fire.idx",BannerPixels()); }
                    finally { Put(state,4,atlas); }
                }
            }
        }
        Console.WriteLine("PASS packaged atlas through native drawing on two independent indexed backgrounds");
    }
    static void Frame(int w,int h,int step,int chain,int age,bool inset=false,bool lowFullBacking=false)
    {
        Event(0); wall=1000;
        for(int n=0;n<chain;n++) Score(rival);
        wall+=(uint)age;
        int viewW=inset || lowFullBacking?320:w, viewH=inset || lowFullBacking?240:h;
        int originX=inset?160:0, originY=inset?120:0;
        SetViewport(lowFullBacking?640:viewW,lowFullBacking?480:viewH,step,originX,originY);
        if(viewW<300)step=2;
        bool active=chain>=2 && age<18000;
        int width=active?Math.Min(viewW,LayerWidth/step):128/step, height=active?LayerHeight/step:50/step;
        int left=originX+(viewW-width)/2, top=originY+8/step;
        using(var d=new Surface(w,h)) {
            byte[] original=Pattern(w,h);
            // All 256 possible destination indices under the transparent gap inside a streak sprite.
            const int gapX=144,gapY=12;
            if(step==1 && chain>=2) for(int i=0;i<256;i++) original[(top+gapY+i/32)*w+left+gapX+i%32]=(byte)i;
            d.Write(original); Draw(d);
            byte[] after=d.ReadPixels();
            var layer=CanonicalLayer(active,step,width*step);
            int meter=(width-128/step)/2, dock=meter+130/step;
            int dw=Math.Min(width*9/40,width-dock), dh=dw*140/400, dy=(height-dh)/2;
            int unchanged=0, ink=0, bannerInk=0,rightTop=LayerHeight,leftTop=LayerHeight;
            for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
                int px=x-left,py=y-top;
                bool inside=px>=0 && px<width && py>=0 && py<height && x<originX+viewW && y<originY+viewH;
                int a=inside?Sample(layer,px,py,step,24):0;
                int pos=y*w+x;
                if(a<128) {
                    Check(after[pos]==original[pos],"key zero must preserve the original index"); unchanged++;
                } else {
                    int r=(Sample(layer,px,py,step,16)*255+a/2)/a;
                    int g=(Sample(layer,px,py,step,8)*255+a/2)/a;
                    int b=(Sample(layer,px,py,step,0)*255+a/2)/a;
                    byte index=ExpectedIndex(r,g,b);
                    Check(after[pos]==index && index!=0,"visible sprite paint must copy exactly one index");
                    ink++;
                    if(py*step>=50 || px<meter || px>=meter+128/step) {
                        bannerInk++;
                        Check(px>=dock && px<dock+dw && py>=dy && py<dy+dh && py<50/step,"flames escape the meter band");
                        if(HotInk(index) || index==45 || index==60) {
                            int column=(px-dock)*400/dw;
                            if(column>=26 && column<70)leftTop=Math.Min(leftTop,py*step);
                            if(column>=172 && column<262)rightTop=Math.Min(rightTop,py*step);
                        }
                    }
                }
            }
            if(active) Check(bannerInk>dw*dh/8 && bannerInk<dw*dh*3/4 &&
                             leftTop<LayerHeight && rightTop<leftTop,"single slanted line beside the LCD");
            else Check(bannerInk==0,"idle or expired streak hidden");
            Check(ink>550 && unchanged>w*h/2,"indexed sprite coverage");
            Console.WriteLine("PASS DirectDraw {0}x{1} step={2} x{3} age={4} inset={5} lowFullBacking={6}: {7} indices preserved, {8} copied",w,h,step,chain,age,inset,lowFullBacking,unchanged,ink);
        }
    }

    static void Lcd()
    {
        int[] xs={8,13,12,7,2,3,8}, ys={2,8,19,24,18,8,13};
        int[] masks={0x3f,0x06,0x5b,0x4f,0x66,0x6d,0x7d,0x07,0x7f,0x6f};
        Event(0);
        foreach(int step in new[]{1,2}) using(var d=new Surface(640/step,480/step)) {
            SetViewport(d.W,d.H,step,0,0);
            foreach(int value in new[]{0,1,9,10,123,9999,10000,12345,67890,99998,99999}) {
                Marshal.WriteInt32(state,Total,value); Draw(d);
                byte[] pixels=d.ReadPixels(); string display=value.ToString("D5");
                for(int i=0;i<5;i++) {
                    int actual=0;
                    for(int s=0;s<7;s++) {
                        int x=(d.W-128/step)/2+(14+17*i+xs[s])/step, y=(8+15+ys[s])/step;
                        int index=4*pixels[y*d.W+x], r=palette[index],g=palette[index+1],b=palette[index+2];
                        if(g>155 && g>r+40 && g>b+40) actual|=1<<s;
                        else Check(g<125 && g>=r && g>=b,"dark green inactive segment");
                    }
                    Check(actual==masks[display[i]-'0'],"wrong seven-segment display at "+display+" digit "+i);
                }
            }
        }
        Console.WriteLine("PASS all ten numerals and all five LCD positions on both native surface sizes");
    }

    static void PaletteChange()
    {
        SetViewport(640,480,1,0,0); Event(0);
        IntPtr pal;
        Marshal.ThrowExceptionForHR(Method<CreatePalette>(device,5)(device,0x4c,palette,out pal,IntPtr.Zero));
        try {
            using(var d=new Surface(640,480)) {
                Marshal.ThrowExceptionForHR(Method<SetPalette>(d.Native,31)(d.Native,pal));
                byte[] original=Pattern(d.W,d.H); d.Write(original); Draw(d); byte[] first=d.ReadPixels();
                var fade=(byte[])palette.Clone();
                for(int i=0;i<256;i++) for(int c=0;c<3;c++) fade[4*i+c]/=2;
                Marshal.ThrowExceptionForHR(Method<SetEntries>(pal,6)(pal,0,0,256,fade));
                d.Write(original); Draw(d); byte[] faded=d.ReadPixels();
                for(int i=0;i<first.Length;i++) Check(faded[i]==first[i],"palette fade must preserve sprite indices");
            }
        } finally { Method<Release>(pal,2)(pal); }
        Console.WriteLine("PASS palette fade changes displayed colors while preserving the BOB indices");
    }

    // Letter and flame ramps use distinct original palette entries. Measure
    // the timer on the faces only and compare the fire relative to the text.
    static bool HotInk(byte index)
    {
        return index==183 || index==184 || index==186 || index==188 || index==255;
    }
    static int BrightStreak(Surface d,uint timestamp,out Point corner,HashSet<int> fire=null)
    {
        wall=timestamp;
        d.Write(new byte[d.W*d.H]); Draw(d); byte[] pixels=d.ReadPixels();
        int origin=(d.W+128)/2+2, left=d.W,top=d.H,bright=0;
        var firePoints=new List<int>();
        for(int y=8;y<58;y++) for(int x=origin;x<origin+144;x++) {
            byte i=pixels[y*d.W+x];
            if(fire!=null && FireInk(i)) firePoints.Add(y*d.W+x);
            if(!HotInk(i) && i!=45 && i!=60)continue;
            left=Math.Min(left,x);top=Math.Min(top,y);
            if(HotInk(i))bright++;
        }
        if(fire!=null)foreach(int point in firePoints)fire.Add(point-top*d.W-left);
        corner=new Point(left,top);return bright;
    }
    static void StreakDisplay()
    {
        SetViewport(640,480,1,0,0); Event(0); wall=1000;
        for(int i=0;i<10;i++) Score(rival);
        using(var d=new Surface(640,480)) {
            Point start,next;
            var firstFire=new HashSet<int>();
            int full=BrightStreak(d,1000,out start,firstFire),minX=start.X,maxX=start.X,changed=0;
            for(uint t=1040;t<=1600;t+=40) {
                var current=new HashSet<int>();
                BrightStreak(d,t,out next,current); minX=Math.Min(minX,next.X); maxX=Math.Max(maxX,next.X);
                current.SymmetricExceptWith(firstFire); changed=Math.Max(changed,current.Count);
            }
            Check(maxX>minX && maxX-minX<=4,"scoring motion stays within four screen pixels");
            Check(firstFire.Count>850 && changed>80,"small fire animates independently of text shake");
            var settled=new HashSet<int>(); BrightStreak(d,1650,out start,settled);
            for(uint t=1800;t<=5800;t+=250) {
                BrightStreak(d,t,out next);
                Check(start==next,"persistent label moves after the scoring pulse");
            }
            Check(settled.Count<firstFire.Count,"the hit flare settles after 650 ms");
            var lastFire=new HashSet<int>();
            int half=BrightStreak(d,10000,out next), almostEmpty=BrightStreak(d,18999,out next,lastFire);
            Check(full>350 && half>full/4 && half<full*3/4 && almostEmpty==0 && next.X<d.W,"compact glyph fill countdown");
            Check(lastFire.Count<firstFire.Count && lastFire.Count>640,"fire cools while the line stays lit");
            Score(second);
            Check(Read(Chain)==11 && BrightStreak(d,18999,out next)>half*3/2,"new knockdown refills the glyphs");
            var expired=new HashSet<int>();
            Check(BrightStreak(d,36999,out next,expired)==0 && next.X==d.W && expired.Count==0 && Read(Total)==11,"expiry clears text and fire");
            Marshal.WriteInt32(state,WindowMs,3000); Event(0); wall=50000; Score(rival); Score(second);
            int customFull=BrightStreak(d,50000,out next), customHalf=BrightStreak(d,51500,out next);
            Check(customHalf>customFull/4 && customHalf<3*customFull/4 && BrightStreak(d,53000,out next)==0 && Read(Chain)==0,"custom three-second window drains the drawn glyphs");
            Marshal.WriteInt32(state,WindowMs,18000);
        }
        Console.WriteLine("PASS animated fire independent of shake, cooling, large glyph drain/refill and 18-second expiry");
    }

    // Hide only the glyph atlas in a second draw at the same race time. This
    // exposes the native fire bed under every stroke, counter and word space.
    static int CheckFireBed(byte[] painted,byte[] fire,uint chain)
    {
        int width=RuntimePatch.KnockdownBannerColumns, start=-1, count=0, ink=0;
        int left=width,right=-1,top=fire.Length,bottom=-1;
        int lineTop=fire.Length,lineBottom=-1;
        int fireLeft=width,fireRight=-1,fireTop=fire.Length,fireBottom=-1;
        var column=new int[width];
        for(int i=0;i<fire.Length;i++) {
            int x=i%width,y=i/width;
            if(fire[i]!=0) {
                Check(FireInk(fire[i]),"unknown ink in the isolated flame");
                count++; column[x]++;
                fireLeft=Math.Min(fireLeft,x); fireRight=Math.Max(fireRight,x);
                fireTop=Math.Min(fireTop,y); fireBottom=Math.Max(fireBottom,y);
            }
            if(painted[i]==0 || FireInk(painted[i]))continue;
            Check(fire[i]!=0,"a letter or its outline has no fire behind it");
            start=i; ink++; left=Math.Min(left,x); right=Math.Max(right,x);
            top=Math.Min(top,y); bottom=Math.Max(bottom,y);
            int level=y+x/11;
            lineTop=Math.Min(lineTop,level); lineBottom=Math.Max(lineBottom,level);
        }
        Check(start>=0 && count>ink*4/3 && fireLeft+3<left && fireRight>right+2 &&
              fireTop+3<top && fireBottom>bottom+2,"fire must extend past all four edges of the slogan");
        Check(fireLeft>=2 && fireRight<width-2,"the flame boundary touches the screen edge");
        Check(lineBottom-lineTop<=57,"multiplier leaves the single slanted baseline");
        for(int x=left;x<=right;x++)Check(column[x]>=35,"gap in the continuous fire bed");
        var seen=new bool[fire.Length]; var queue=new Queue<int>(); queue.Enqueue(start); seen[start]=true;
        int joined=0; int[] offsets={-width,-1,1,width};
        while(queue.Count>0) {
            int at=queue.Dequeue(); joined++;
            foreach(int delta in offsets) {
                int pos=at+delta;
                if(pos<0 || pos>=fire.Length || seen[pos] || fire[pos]==0)continue;
                seen[pos]=true; queue.Enqueue(pos);
            }
        }
        Check(joined*100>=count*99,"fire breaks apart between letters");
        if(chain==44)Console.WriteLine("PASS x44 fire bed: {0} cells, {1} lettering cells, {2:P2} connected",count,ink,(double)joined/count);
        return count;
    }
    static void FlameTiers()
    {
        Event(0); wall=1000; Score(rival); Score(second);
        IntPtr atlas=Marshal.ReadIntPtr(state,4),withoutGlyphs=WithoutLetterMasks(RuntimePatch.LoadKnockdownAtlas());
        using(var d=new Surface(640,480)) {
            SetViewport(d.W,d.H,1,0,0);
            Marshal.WriteInt32(state,Chain,0); Draw(d);
            var meter=new int[128*50]; Marshal.Copy(canvas,meter,0,meter.Length);
            int previous=0;
            foreach(uint chain in new uint[]{2,5,10,44,999,1000,99999,uint.MaxValue}) {
                Marshal.WriteInt32(state,Chain,unchecked((int)chain));
                d.Write(new byte[d.W*d.H]); Draw(d); byte[] frame=d.ReadPixels(),painted=BannerPixels();
                byte[] fire;
                Put(state,4,withoutGlyphs);
                try { Draw(d); fire=BannerPixels(); }
                finally { Put(state,4,atlas); }
                int area=CheckFireBed(painted,fire,chain);
                if(chain==2 || chain==5 || chain==10) {
                    Check(area>previous+50,"fire grows at streak milestones"); previous=area;
                }
                var lcd=new int[meter.Length]; Marshal.Copy(canvas,lcd,0,lcd.Length);
                Check(System.Linq.Enumerable.SequenceEqual(lcd,meter),"fire overlaps the LCD or housing");
                Event(4); wall+=90000; d.Write(new byte[d.W*d.H]); Draw(d);
                Check(System.Linq.Enumerable.SequenceEqual(frame,d.ReadPixels()),"pause freezes the whole flame and type");
            }
        }
        Console.WriteLine("PASS continuous oversize fire, tiers, time freeze and single-line multipliers through ten digits");
    }

    static void SaveIndexed(string file,int w,int h,byte[] indices)
    {
        using(var frame=new Bitmap(w,h,PixelFormat.Format8bppIndexed)) {
            ColorPalette p=frame.Palette;
            for(int i=0;i<256;i++) p.Entries[i]=Color.FromArgb(palette[4*i],palette[4*i+1],palette[4*i+2]);
            frame.Palette=p;
            BitmapData bits=frame.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format8bppIndexed);
            try {
                for(int y=0;y<h;y++) Marshal.Copy(indices,y*w,IntPtr.Add(bits.Scan0,y*bits.Stride),w);
            } finally { frame.UnlockBits(bits); }
            frame.Save(file,ImageFormat.Png);
        }
    }

    // Replay the old launcher's two StretchDIBits calls on a real indexed DD DC.
    // The default control is entirely transparent. The atlas audit can also supply
    // both unmodified DIB inputs recorded at the old renderer's drawing calls.
    static void LegacyMask(string game,string folder,bool recorded=false)
    {
        const int w=386,h=76,left=8,top=366;
        int[] mask=new int[w*h],ink=new int[w*h];
        int drawWidth=recorded?128:w;
        if(recorded) {
            byte[] andBits=File.ReadAllBytes(Path.Combine(folder,"legacy-mask.bgra"));
            byte[] orBits=File.ReadAllBytes(Path.Combine(folder,"legacy-ink.bgra"));
            Check(andBits.Length==4*w*h && orBits.Length==4*w*h,"recorded DIB geometry");
            Buffer.BlockCopy(andBits,0,mask,0,andBits.Length);
            Buffer.BlockCopy(orBits,0,ink,0,orBits.Length);
        } else for(int i=0;i<mask.Length;i++)mask[i]=0xffffff;
        int[] bitmapInfo={40,w,-h,1|(32<<16),0,0,0,0,0,0};
        IntPtr pal;
        Marshal.ThrowExceptionForHR(Method<CreatePalette>(device,5)(device,0x4c,palette,out pal,IntPtr.Zero));
        try {
            using(var d=new Surface(640,480)) {
                Marshal.ThrowExceptionForHR(Method<SetPalette>(d.Native,31)(d.Native,pal));
                d.Write(Pattern(d.W,d.H));
                byte[] bob=File.ReadAllBytes(Path.Combine(game,"IMAGES","BIKES","SPORT","MHIDASH.BOB"));
                d.DrawBob(0,d.H-bob.Length/d.W,d.W,bob);
                byte[] before=d.ReadPixels();
                IntPtr dc;
                Marshal.ThrowExceptionForHR(Method<GetDC>(d.Native,17)(d.Native,out dc));
                int andRows,orRows;
                try {
                    andRows=StretchDIBits(dc,left,top,drawWidth,h,0,0,drawWidth,h,mask,bitmapInfo,0,0x008800c6);
                    orRows=StretchDIBits(dc,left,top,drawWidth,h,0,0,drawWidth,h,ink,bitmapInfo,0,0x00ee0086);
                } finally { Marshal.ThrowExceptionForHR(Method<ReleaseDC>(d.Native,26)(d.Native,dc)); }
                byte[] after=d.ReadPixels();
                int changes=0,blackOrWhite=0,transparent=0,damaged=0;
                for(int i=0;i<before.Length;i++) {
                    int x=i%d.W,y=i/d.W;
                    if(x<left || x>=left+drawWidth || y<top || y>=top+h) {
                        Check(before[i]==after[i],"legacy control outside the rectangle");
                        continue;
                    }
                    int source=(y-top)*w+x-left;
                    if(mask[source]==0xffffff && ink[source]==0) {
                        transparent++;
                        if(before[i]!=after[i])damaged++;
                    }
                    if(before[i]!=after[i]) {
                        changes++;
                        if(after[i]==1 || after[i]==255)blackOrWhite++;
                    }
                }
                if(folder!=null)File.WriteAllBytes(Path.Combine(folder,recorded?"legacy-after.idx":"legacy-mask.idx"),after);
                if(recorded) Console.WriteLine("MEASURE original GDI calls with {0} atlas at 000: {1}/{2} transparent destination pixels overwritten",
                                               new DirectoryInfo(folder).Name,damaged,transparent);
                else Console.WriteLine("MEASURE legacy transparent 386x76 on DirectDraw DC: GDI returned {0}/{1} rows, {2}/{3} indices changed, {4} changed to black/white",andRows,orRows,changes,w*h,blackOrWhite);
            }
        } finally { Method<Release>(pal,2)(pal); }
    }

    static bool FireInk(byte index)
    {
        return index==52 || index==50 || index==37 || index==36 || index==187 || index==185 || index==201 || index==229;
    }
    static void FlamePreview(string backgroundFile,string output,bool full,int chain,bool fromHit)
    {
        byte[] background=File.ReadAllBytes(backgroundFile);
        using(var surface=new Surface(640,480))
        using(var frames=File.Create(output)) {
            SetViewport(surface.W,surface.H,1,0,0);
            Check(chain>=2 && chain<=99999,"preview score outside LCD range");
            Event(0); wall=1000; for(int i=0;i<chain;i++)Score(rival);
            for(int i=0;i<(chain==44 || (fromHit && chain==23)?64:1);i++) {
                wall=(uint)((fromHit?1000:1600)+i*40); surface.Write(background); Draw(surface);
                byte[] pixels=surface.ReadPixels();
                if(full)frames.Write(pixels,0,pixels.Length);
                else for(int y=0;y<152;y++)frames.Write(pixels,y*surface.W+208,432);
            }
        }
    }

    static void Cockpit(string game,string folder,string animation)
    {
        if(folder!=null) Directory.CreateDirectory(folder);
        foreach(int step in new[]{1,2}) using(var d=new Surface(640/step,480/step)) {
            byte[] background=Pattern(d.W,d.H);
            foreach(string bike in new[]{"RAT","SPORT","SUPER"}) {
                byte[] bob=File.ReadAllBytes(Path.Combine(game,"IMAGES","BIKES",bike,step==1?"MHIDASH.BOB":"MLODASH.BOB"));
                int firstRow=d.H-bob.Length/d.W;
                d.Write(background); d.DrawBob(0,firstRow,d.W,bob);
                byte[] cockpit=d.ReadPixels();
                for(int i=0;i<cockpit.Length;i++) {
                    int j=i-firstRow*d.W;
                    Check(cockpit[i]==(j<0 || bob[j]==0?background[i]:bob[j]),"stock BOB source key or pitch");
                }
                Event(0); wall=1000; for(int i=0;i<10;i++) Score(rival); wall+=9000;
                SetViewport(d.W,d.H,step,0,0); Draw(d);
                byte[] result=d.ReadPixels();
                for(int i=firstRow*d.W;i<result.Length;i++) Check(result[i]==cockpit[i],"dashboard index overwritten");
                if(folder==null || step!=1 || bike!="SPORT") continue;
                File.WriteAllBytes(Path.Combine(folder,"base.idx"),background);
                File.WriteAllBytes(Path.Combine(folder,"cockpit.idx"),cockpit);
                File.WriteAllBytes(Path.Combine(folder,"with-hud.idx"),result);
                SaveIndexed(Path.Combine(folder,"HUD-indexed.png"),d.W,d.H,result);
                if(animation!=null) {
                    Event(0); wall=1000; for(int i=0;i<9;i++) Score(second);
                    using(FileStream stream=File.Create(animation)) for(int n=0;n<=480;n++) {
                        wall=(uint)(1000+40*n); if(n==15) Score(rival);
                        d.Write(cockpit); Draw(d); byte[] pixels=d.ReadPixels();
                        for(int y=0;y<128;y++) stream.Write(pixels,y*d.W+202,353);
                    }
                }
            }
        }
        Console.WriteLine("PASS six original BOB cockpits, padded surface pitch, key zero and byte-exact dashboard with x10");
    }
    static int Main(string[] args)
    {
        try {
            palette=File.ReadAllBytes(Path.Combine(args[0],"DATA","PALETTE.RAW"));
            Setup();
            if(args.Length==3 && args[1]=="--trace-atlas") { TraceAtlas(args[2]); return 0; }
            if(args.Length==3 && args[1]=="--legacy-atlas") { LegacyMask(args[0],args[2],true); return 0; }
            if((args.Length==4 || args.Length==5) && (args[1]=="--flame-preview" || args[1]=="--inferno-preview" || args[1]=="--compact-preview")) {
                FlamePreview(args[2],args[3],args[1]!="--flame-preview",args.Length==5?int.Parse(args[4]):44,args[1]=="--compact-preview"); return 0;
            }
            AtlasCoverage(); StockInks(args[0]); Rules(); ExpandedGrid(); Announcer(args[0],args.Length>1?args[1]:null); Lcd(); PaletteChange(); StreakDisplay(); FlameTiers();
            foreach(int step in new[]{1,2}) {
                Frame(640/step,480/step,step,0,0); Frame(640/step,480/step,step,10,9000);
                Frame(640/step,480/step,step,10,17999); Frame(640/step,480/step,step,10,18000);
            }
            Frame(854,480,1,10,4000); Frame(428,240,2,10,12000);
            Frame(428,240,1,23,0); Frame(320,240,1,999,0);
            Frame(512,384,1,10,4000); Frame(200,240,1,10,4000);
            Frame(640,480,2,10,3000,true); Frame(640,480,2,10,3000,false,true);
            Cockpit(args[0],args.Length>1?args[1]:null,args.Length>2?args[2]:null);
            LegacyMask(args[0],args.Length>1?args[1]:null);
            Console.WriteLine("All knockdown DirectDraw fixtures passed."); return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if(signal!=null)Event(6); if(device!=IntPtr.Zero)Method<Release>(device,2)(device); }
    }
}
