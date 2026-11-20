using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class MidiAudioTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protection);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RunCode();
    static long Alloc(int size) { IntPtr p = VirtualAlloc(IntPtr.Zero, (UIntPtr)size, 0x3000, 0x40); if (p == IntPtr.Zero) throw new Exception("Allocation failed"); return (uint)p.ToInt32(); }
    static void Write(long p, byte[] b) { Marshal.Copy(b, 0, new IntPtr(p), b.Length); }
    static void Put(long p, long v) { Marshal.WriteInt32(new IntPtr(p), unchecked((int)v)); }
    static int Get(long p) { return Marshal.ReadInt32(new IntPtr(p)); }
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    static byte[] Event(uint e) { byte[] b = new byte[12]; Array.Copy(BitConverter.GetBytes(e), 0, b, 8, 4); return b; }
    static byte[] Scale(long image, long code, long mem, byte[] data, int master, int music, bool hook)
    {
        Put(image + 0x18E010, master); Put(image + 0x18E070, music);
        Put(mem, mem + 0x100); Put(mem + 4, data.Length); Put(mem + 8, data.Length);
        Write(mem + 0x100, data); Put(mem + 0x100 + data.Length, 0x12345678);
        Write(code, RuntimePatch.BuildMidiScaleCode(image, code, hook));
        List<byte> bridge = new List<byte>(); bridge.Add(0x60); // preserve callee-saved EDI/EBX
        bridge.Add(hook ? (byte)0xBF : (byte)0xB9); bridge.AddRange(BitConverter.GetBytes((uint)mem));
        if (hook) { bridge.Add(0xBB); bridge.AddRange(BitConverter.GetBytes((uint)(mem+0x50))); Put(mem+0x50, 0x42424242); }
        bridge.Add(0xB8); bridge.AddRange(BitConverter.GetBytes((uint)code)); bridge.AddRange(new byte[] { 0xFF, 0xD0, 0x61, 0xC3 });
        Write(code + 0x800, bridge.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(code + 0x800), typeof(RunCode)))();
        byte[] output = new byte[data.Length]; Marshal.Copy(new IntPtr(mem + 0x100), output, 0, output.Length);
        if (Get(mem + 0x100 + data.Length) != 0x12345678 || Get(mem + 4) != data.Length || Get(mem + 8) != data.Length) throw new Exception("MIDI buffer bounds/header changed");
        return output;
    }
    static int Main(string[] args)
    {
        try {
            long image = Alloc(0x220000), code = Alloc(0x1000), mem = Alloc(0x110000);
            // The hook must replay prepare arguments and return with a balanced stack.
            Write(image + 0x45ADA, new byte[] { 0x83, 0xC4, 0x0C, 0xC3 });
            byte[] sample = Event(0x007F4090);
            Check(Scale(image,code,mem,sample,100,0,false)[10] == 0, "music zero mutes MIDI notes");
            Check(Scale(image,code,mem,sample,0,100,false)[10] == 0, "master zero still mutes music");
            Check(Scale(image,code,mem,sample,100,100,false)[10] == 127, "full volume preserves original velocity");
            Check(Scale(image,code,mem,sample,50,50,false)[10] == 31, "master and music combine within MIDI");
            Check(Scale(image,code,mem,sample,1000,-4,false)[10] == 0, "invalid gain values clamp safely");
            Check(Scale(image,code,mem,sample,100,50,true)[10] == 63, "native prepare hook preserves calling convention");
            foreach (uint untouched in new uint[] { 0x007F4080, 0x006407B0, 0x0107A120, 0x02000000, 0x00004090 })
                Check(BitConverter.ToUInt32(Scale(image,code,mem,Event(untouched),100,0,false),8) == untouched, "preserve MIDI event " + untouched.ToString("X8"));
            List<byte> longEvent = new List<byte>(Event(0x80000004)); longEvent.AddRange(new byte[] {0x90,0x40,0x7F,0xF7}); longEvent.AddRange(Event(0x407F4099));
            byte[] scaled = Scale(image,code,mem,longEvent.ToArray(),100,0,false);
            Check(scaled[14] == 127 && scaled[26] == 0, "skip SysEx payload and scale callback-flagged notes");
            Check(Scale(image,code,mem,new byte[11],100,0,false).Length == 11, "ignore truncated event");
            Check(Scale(image,code,mem,Event(0x80FFFFFF),100,0,false).Length == 12, "reject oversized long event");
            // Execute the installer against isolated memory in this test process only.
            Write(image+0x45AD5,new byte[] {0x6A,0x40,0x57,0xFF,0x33});
            Write(image+0x4546D,new byte[] {0xFF,0x15,0,0,0,0}); Put(image+0x4546F,image+0x21C884);
            MethodInfo install=typeof(RuntimePatch).GetMethod("ApplyIndependentAudio",BindingFlags.Static|BindingFlags.NonPublic);
            install.Invoke(null,new object[] {Process.GetCurrentProcess().Handle,image,new Action<string>(delegate(string s){})});
            Check(Marshal.ReadByte(new IntPtr(image+0x45AD5))==0xE9 && Get(image+0x4546D)==unchecked((int)0x3308C483), "installer removes shared device gain call");
            bool rejected=false; try {install.Invoke(null,new object[] {Process.GetCurrentProcess().Handle,image,new Action<string>(delegate(string s){})});} catch(TargetInvocationException){rejected=true;}
            Check(rejected,"unexpected signatures reject safely");
            string[] musicFiles = args.Length > 0 ? Directory.GetFiles(args[0], "*.MID") : new string[0];
            if (args.Length > 0 && musicFiles.Length == 0) throw new Exception("No MID files in the supplied music directory");
            if (args.Length == 0) Console.WriteLine("SKIP real music files (pass a game directory to scripts/test.ps1)");
            foreach(string file in musicFiles) {
                byte[] b=File.ReadAllBytes(file); int at=12, count=0;
                while(at+8<=b.Length) { int size=BitConverter.ToInt32(b,at+4); if(System.Text.Encoding.ASCII.GetString(b,at,4)=="data") break; at+=8+((size+1)&~1); }
                at+=8; int blocks=BitConverter.ToInt32(b,at); at+=4;
                for(int j=0;j<blocks;j++) {
                    int n=BitConverter.ToInt32(b,at+4);at+=8;byte[] original=new byte[n];Array.Copy(b,at,original,0,n);at+=n;
                    byte[] result=Scale(image,code,mem,original,100,0,false);
                    for(int e=0;e+12<=n;) {
                        uint value=BitConverter.ToUInt32(original,e+8);
                        if((value&0x80000000)!=0){e+=12+(int)(((value&0xFFFFFF)+3)&~3u);continue;}
                        bool note=(value&0x3F0000F0)==0x90;
                        for(int k=0;k<12;k++) if(result[e+k]!=(note&&k==10?(byte)0:original[e+k])) throw new Exception("Altered non-note data in "+file);
                        if(note)count++;e+=12;
                    }
                }
                Check(count>0,"real music muted without altering timing/controllers: "+Path.GetFileName(file));
            }
            Console.WriteLine("MIDI audio tests: "+passed+" passed");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
