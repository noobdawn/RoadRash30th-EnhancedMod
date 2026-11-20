using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

internal static class CharacterTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at, UIntPtr size, uint type, uint protection);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr process, IntPtr at, UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RunCode();
    static long Alloc(int size) { IntPtr p = VirtualAlloc(IntPtr.Zero,(UIntPtr)size,0x3000,0x40); if(p==IntPtr.Zero)throw new Exception("allocation"); return (uint)p.ToInt32(); }
    static void Write(long p, byte[] b) { Marshal.Copy(b,0,new IntPtr(p),b.Length); FlushInstructionCache(new IntPtr(-1),new IntPtr(p),(UIntPtr)b.Length); }
    static void Put(long p, long v) { Marshal.WriteInt32(new IntPtr(p),unchecked((int)v)); }
    static int Get(long p) { return Marshal.ReadInt32(new IntPtr(p)); }
    static void D(List<byte> b, long v) { b.AddRange(BitConverter.GetBytes(unchecked((int)v))); }
    static void Mov(List<byte> b, byte op, long v) { b.Add(op);D(b,v); }
    static int passed;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name); passed++;Console.WriteLine("PASS " + name); }
    static void Tail(long at, long result, int marker)
    {
        List<byte> b = new List<byte>{0xC7,0x05}; D(b,result+40); D(b,marker);
        b.AddRange(new byte[]{0x9C,0x60,0x89,0xE6}); Mov(b,0xBF,result); Mov(b,0xB9,9);
        b.AddRange(new byte[]{0xFC,0xF3,0xA5,0x61,0x9D,0xC3});
        long target=result+0x100+marker*0x100; Write(target,b.ToArray());
        List<byte> jump=new List<byte>{0xE9};D(jump,target-at-5);Write(at,jump.ToArray());
    }
    static void Run(long code,long target,long bike,long rider,long result,long ebx)
    {
        List<byte> b = new List<byte>{0x9C,0x60,0x83,0xEC,0x40,0x8D,0x6C,0x24,0x20,0xC7,0x45,0xE0}; D(b,250);
        Mov(b,0xB8,12345); Mov(b,0xB9,23456); Mov(b,0xBA,34567); Mov(b,0xBB,ebx);
        Mov(b,0xBE,bike); Mov(b,0xBF,rider);
        b.AddRange(new byte[]{0x89,0x25});D(b,result+44);
        b.AddRange(new byte[]{0x39,0xC0,0xF9,0xE8});D(b,target-(code+b.Count+4));
        b.AddRange(new byte[]{0x83,0xC4,0x40,0x61,0x9D,0xC3}); Write(code,b.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
        if(Get(result+12)!=Get(result+44)-8 || Get(result)!=rider || Get(result+24)!=23456 || Get(result+16)!=ebx)
            throw new Exception("stack/register corruption");
    }
    static int Main(string[] args)
    {
        try {
            long image=Alloc(0x220000),cave=Alloc(0x2000),bike=Alloc(0x1000),rider=Alloc(0x1000),result=Alloc(0x1000);
            Put(bike+0xC4,4);Put(rider+0xC4,8);Put(rider+0x1C4,bike);Put(bike+0x2FC,rider);
            Put(bike+0x2EC,1000);Put(bike+0x2F0,700);
            for(int site=0;site<3;site++) for(int player=0;player<2;player++) {
                Put(image+0x1A69EC,player==1?bike:0);
                Tail(image+RuntimePatch.CharacterSites[site]+RuntimePatch.CharacterOriginals[site].Length,result,0);
                Tail(image+0x17D9F,result,1);
                Write(cave+0x400,RuntimePatch.BuildDurabilityCode(image,cave+0x400,site));
                Run(cave,cave+0x400,bike,rider,result,250);
                Check(site==2?Get(result+40)==player:Get(result+28)==(player==1?1000:450),"durability path="+site+" player="+player);
                Check(Get(result+4)==bike && Get(result+20)==(site==1?bike+0x2F0:(site==2&&player==0?180:34567)),"durability preserves continuation registers path="+site+" player="+player);
            }
            for(int site=3;site<=4;site++) for(int scenario=0;scenario<7;scenario++) {
                Put(image+0x1A69EC,scenario==0||scenario==2?bike:result);
                Put(bike+0x2F4,scenario==3?0:(scenario==4?1:2));
                Put(rider+0x1C4,scenario==5?0:bike);Put(rider+0xC4,scenario==6?16:8);
                Put(rider+0xF0,531);Put(rider+0xF4,941);Put(bike+0xEC,9999);
                decimal playerSpeed=scenario==2?0.1m:2.5m,aiSpeed=scenario==1?8m:0.5m;
                Tail(image+RuntimePatch.CharacterSites[site]+10,result,0);
                Write(cave+0x400,RuntimePatch.BuildWalkSpeedCode(image,cave+0x400,site,playerSpeed,aiSpeed));
                for(int tick=0;tick<2;tick++) Run(cave,cave+0x400,bike,rider,result,rider);
                Check(Get(rider+0xEC)==(scenario>=3?1200:(scenario==0?3000:(scenario==1?9600:120))),"walk speed path="+site+" scenario="+scenario+" without compounding");
                Check(Get(result+28)==12345&&Get(result+20)==34567&&Get(result+4)==bike&&(Get(result+32)&0x41)==0x41&&Get(rider+0xF0)==531&&Get(rider+0xF4)==941&&Get(bike+0xEC)==9999,"walk retains heading flags bike speed and launch velocity");
            }
            Put(rider+0xC4,8);Put(rider+0x1C4,bike);Put(rider+0x1A0,result);
            for(int everyone=0;everyone<2;everyone++) for(int scenario=0;scenario<7;scenario++) {
                Put(image+0x1A69EC,scenario==0?bike:(scenario==6?0:result));
                Put(bike+0x2F4,scenario==2?0:(scenario==3?1:(scenario==4?3:2)));
                Put(rider+0x1C4,scenario==5?0:bike);
                Tail(image+0x13F90,result,0);Tail(image+0x141D2,result,1);
                Write(cave+0x400,RuntimePatch.BuildOnFootCode(image,cave+0x400,everyone==1));
                Run(cave,cave+0x400,bike,rider,result,250);
                bool protect=(scenario==0||everyone==1)&&(scenario==0||scenario==1||scenario==6);
                Check(Get(result+40)==(protect?1:0)&&Get(result+4)==(protect?bike:result)&&Get(result+28)==12345&&Get(result+20)==34567&&(Get(result+32)&0x41)==0x41,"on-foot scope="+everyone+" scenario="+scenario);
            }
            Put(bike+0x2F4,2);Put(rider+0x1C4,bike);Put(rider+0xC4,8);
            for(int site=3;site<=4;site++) foreach(decimal speed in new decimal[]{8.1m,99m,1234.5m,RuntimePatch.MaximumWalkSpeed}) for(int player=0;player<2;player++) {
                Put(image+0x1A69EC,player==1?bike:result);
                Tail(image+RuntimePatch.CharacterSites[site]+10,result,0);
                Write(cave+0x400,RuntimePatch.BuildWalkSpeedCode(image,cave+0x400,site,speed,speed));
                Run(cave,cave+0x400,bike,rider,result,rider);
                Check(Get(rider+0xEC)==(int)(speed*1200),"speed above eight encoded without clipping path="+site+" player="+player+" speed="+speed);
            }
            CheckHideVehicles(image,cave,bike,rider,result);
            long aiConfig,aiCode=RuntimePatch.PrepareRunAi(Process.GetCurrentProcess().Handle,image,1,2,2,2,2,delegate(string s){},out aiConfig);
            Put(bike+0xC4,4);Put(bike+0x2F4,2);Put(rider+0xC4,8);Put(rider+0x1C4,bike);Put(rider+0xC0,0);Put(rider+0x140,0);Put(rider+0x2C,0);
            Put(image+0x1A69EC,0);Put(image+0x2036E8,150);
            for(int site=3;site<=4;site++) {
                Tail(image+RuntimePatch.CharacterSites[site]+10,result,0);
                Write(cave+0x400,RuntimePatch.BuildWalkSpeedCode(image,cave+0x400,site,1,1,aiCode,aiConfig));
                Run(cave,cave+0x400,bike,rider,result,rider);
                Check(Get(rider+0xEC)>=780&&Get(rider+0xEC)<=1740&&Get(result+28)==12345&&Get(result+20)==34567&&Get(result+4)==bike&&(Get(result+32)&0x41)==0x41,
                    "native variable speed helper preserves hook stack registers and flags path="+site);
            }
            foreach(decimal bad in new decimal[]{0,RuntimePatch.MaximumWalkSpeed+0.1m,1.25m,decimal.MaxValue}) {
                bool rejected=false;try{RuntimePatch.BuildWalkSpeedCode(image,cave,3,bad,1);}catch(ArgumentOutOfRangeException){rejected=true;}
                Check(rejected,"reject unsupported speed "+bad);
            }
            for(int i=0;i<6;i++)Write(image+RuntimePatch.CharacterSites[i],RuntimePatch.CharacterOriginals[i]);
            Write(image+RuntimePatch.CharacterSites[5],new byte[]{0xCC});
            bool mismatch=false;try{RuntimePatch.ApplyCharacter(Process.GetCurrentProcess().Handle,image,true,2,3,true,false,delegate(string s){});}catch(InvalidOperationException){mismatch=true;}
            Check(mismatch&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.CharacterSites[0]))==0x8B,"signature mismatch leaves all character sites untouched");
            Write(image+RuntimePatch.CharacterSites[5],RuntimePatch.CharacterOriginals[5]);
            RuntimePatch.ApplyCharacter(Process.GetCurrentProcess().Handle,image,false,1,1,false,false,delegate(string s){});
            Check(Marshal.ReadByte(new IntPtr(image+RuntimePatch.CharacterSites[0]))==0x8B,"disabled options install nothing");
            RuntimePatch.ApplyCharacter(Process.GetCurrentProcess().Handle,image,true,2,3,true,false,delegate(string s){});
            bool all=true;for(int i=0;i<6;i++)all &= Marshal.ReadByte(new IntPtr(image+RuntimePatch.CharacterSites[i]))==0xE9;
            Check(all,"combined installer publishes all six hooks");
            for(int i=0;i<6;i++)Write(image+RuntimePatch.CharacterSites[i],RuntimePatch.CharacterOriginals[i]);
            RuntimePatch.ApplyCharacter(Process.GetCurrentProcess().Handle,image,false,1,1,false,false,delegate(string s){},aiCode,aiConfig);
            Check(Marshal.ReadByte(new IntPtr(image+RuntimePatch.CharacterSites[3]))==0xE9&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.CharacterSites[4]))==0xE9&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.CharacterSites[0]))==0x8B,
                "AI variation installs both speed hooks even at default one-times speed");
            if(args.Length>0) CheckGame(args[0]);
            Console.WriteLine("Character tests: "+passed+" passed");return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }

    static void CheckHideVehicles(long image,long cave,long bike,long actor,long result)
    {
        Tail(image+RuntimePatch.HideVehicleSite+6,result,0);
        Tail(image+0x4AD5D,result,1);
        Write(image+RuntimePatch.HideVehicleSite,RuntimePatch.HideVehicleOriginal);
        RuntimePatch.ApplyHideFallenVehicles(Process.GetCurrentProcess().Handle,image,delegate(string s){});
        foreach(int type in new int[]{4,8,16}) foreach(int state in new int[]{0,1,2,3,4,-1}) for(int player=0;player<2;player++) {
            Put(image+0x1A69EC,player==1?actor:bike);
            Put(actor+0xC4,type);Put(actor+0x2F4,state);Put(actor+0xDC,67890);
            byte[] before=new byte[0x400];Marshal.Copy(new IntPtr(actor),before,0,before.Length);
            Run(cave,image+RuntimePatch.HideVehicleSite,bike,actor,result,250);
            bool hide=type==4&&state>=1&&state<=3;
            bool same=true;for(int i=0;i<before.Length;i++)same &= before[i]==Marshal.ReadByte(new IntPtr(actor+i));
            Check(Get(result+40)==(hide?1:0)&&Get(result+28)==(hide?12345:67890)&&Get(result+20)==34567&&(Get(result+32)&0x41)==0x41&&same,
                "hide fallen vehicle preserves rider traffic physics and registers type="+type+" state="+state+" player="+player);
        }
        Write(image+RuntimePatch.HideVehicleSite,new byte[]{0xCC});
        bool rejected=false;try{RuntimePatch.ApplyHideFallenVehicles(Process.GetCurrentProcess().Handle,image,delegate(string s){});}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&Marshal.ReadByte(new IntPtr(image+RuntimePatch.HideVehicleSite))==0xCC,"hide vehicle installer rejects mismatched dispatch signature");
    }

    private static long MapGame(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(file, 0x3C), opt = pe + 24;
        if (BitConverter.ToUInt32(file, pe) != 0x4550 || BitConverter.ToUInt16(file, opt) != 0x10B)
            throw new Exception("Expected PE32 game executable");
        int size = BitConverter.ToInt32(file, opt + 56);
        long image = Alloc(size), preferred = BitConverter.ToUInt32(file, opt + 28);
        Marshal.Copy(file, 0, new IntPtr(image), BitConverter.ToInt32(file, opt + 60));
        int section = opt + BitConverter.ToUInt16(file, pe + 20);
        for (int i = 0; i < BitConverter.ToUInt16(file, pe + 6); i++, section += 40)
        {
            int rva = BitConverter.ToInt32(file, section + 12);
            int length = BitConverter.ToInt32(file, section + 16), raw = BitConverter.ToInt32(file, section + 20);
            if (length != 0) Marshal.Copy(file, raw, new IntPtr(image + rva), length);
        }
        int reloc = BitConverter.ToInt32(file, opt + 96 + 5 * 8);
        int end = reloc + BitConverter.ToInt32(file, opt + 100 + 5 * 8);
        while (reloc < end)
        {
            int page = Get(image + reloc), block = Get(image + reloc + 4);
            if (block < 8 || reloc + block > end) throw new Exception("Invalid relocation block");
            for (int entry = 8; entry < block; entry += 2)
            {
                int fix = (ushort)Marshal.ReadInt16(new IntPtr(image + reloc + entry));
                if ((fix >> 12) == 3)
                {
                    long at = image + page + (fix & 0xFFF);
                    Put(at, unchecked((uint)Get(at)) + image - preferred);
                }
                else if ((fix >> 12) != 0) throw new Exception("Unexpected relocation type");
            }
            reloc += block;
        }
        FlushInstructionCache(new IntPtr(-1), new IntPtr(image), (UIntPtr)size);
        return image;
    }

    static void CheckGameCollision(string path)
    {
        long image=MapGame(path),code=Alloc(0x2000),bike=Alloc(0x1000),rider=Alloc(0x1000),car=Alloc(0x1000),counts=Alloc(0x1000);
        Put(bike+0xC4,4);Put(bike+0x2FC,rider);Put(rider+0xC4,8);Put(rider+0x1C4,bike);Put(rider+0x1A0,car);
        Put(car+0xC4,16);Put(car+0xEC,3000);Put(car+0x174,100);Put(car+0x17C,100);
        Put(rider+0x174,100);Put(bike+0x18,1);
        // Run the real collision function and its real tail. Replace external audio,
        // animation and integration calls with counters; never enter the game's main loop.
        Write(image+0x2A6D,new byte[]{0xC2,4,0});Write(image+0x35D5,new byte[]{0xC2,4,0});
        Write(image+0x3882,new byte[]{0x33,0xC0,0xC3});
        List<byte> tick=new List<byte>{0xFF,0x05};D(tick,counts);tick.Add(0xC3);Write(image+0x12CB,tick.ToArray());
        List<byte> animation=new List<byte>{0xFF,0x05};D(animation,counts+4);animation.Add(0xC3);
        Write(image+0x428C,animation.ToArray());Write(image+0x5FB0,animation.ToArray());
        List<byte> bridge=new List<byte>{0x60};Mov(bridge,0xB9,rider);bridge.Add(0xE8);D(bridge,image+0x13F80-(code+bridge.Count+4));
        bridge.AddRange(new byte[]{0x61,0xC3});Write(code,bridge.ToArray());
        for(int scenario=0;scenario<4;scenario++) {
            Put(counts,0);Put(counts+4,0);Put(bike+0x2F4,2);Put(rider+0x20,0);Put(rider+0xEC,2000);Put(rider+0xF4,0);
            Put(rider+0x184,111);Put(rider+0x188,222);Put(rider+0x18C,333);
            Put(image+0x1A69EC,scenario==1?bike:car);
            Write(image+0x13F8A,RuntimePatch.CharacterOriginals[5]);
            if(scenario>0) {
                Write(code+0x400,RuntimePatch.BuildOnFootCode(image,code+0x400,scenario==3));
                List<byte> jump=new List<byte>{0xE9};D(jump,code+0x400-(image+0x13F8A+5));jump.Add(0x90);Write(image+0x13F8A,jump.ToArray());
            }
            ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(RunCode)))();
            bool protect=scenario==1||scenario==3;
            Check(Get(bike+0x2F4)==(protect?2:1)&&Get(counts)==1&&Get(counts+4)==(protect?0:4)&&Get(rider+0x184)==0&&Get(rider+0x188)==0&&Get(rider+0x18C)==0&&(!protect||Get(rider+0x20)==0),"original game collision scenario="+scenario+" protection="+protect);
        }
    }

    static void CheckGame(string path)
    {
        long drawImage=MapGame(path);
        RuntimePatch.ApplyHideFallenVehicles(Process.GetCurrentProcess().Handle,drawImage,delegate(string s){});
        Check(Marshal.ReadByte(new IntPtr(drawImage+RuntimePatch.HideVehicleSite))==0xE9&&Marshal.ReadByte(new IntPtr(drawImage+0x4AD58))==0xFF,
            "real game dispatch hook installed without overwriting screen capture or rider hooks");
        CheckGameCollision(path);
        byte[] file=File.ReadAllBytes(path);int pe=BitConverter.ToInt32(file,0x3C),opt=pe+24;
        for(int i=0;i<RuntimePatch.CharacterSites.Length;i++) {
            int rva=RuntimePatch.CharacterSites[i],section=opt+BitConverter.ToUInt16(file,pe+20),raw=-1;
            for(int n=0;n<BitConverter.ToUInt16(file,pe+6);n++,section+=40) {
                int start=BitConverter.ToInt32(file,section+12),length=BitConverter.ToInt32(file,section+16);
                if(rva>=start&&rva<start+length)raw=BitConverter.ToInt32(file,section+20)+rva-start;
            }
            bool match=raw>=0;
            for(int n=0;match&&n<RuntimePatch.CharacterOriginals[i].Length;n++)match &= file[raw+n]==RuntimePatch.CharacterOriginals[i][n];
            Check(match,"installed game signature RVA "+rva.ToString("X"));
        }
    }
}
