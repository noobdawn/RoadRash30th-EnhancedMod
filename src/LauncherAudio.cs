using System;
using System.Diagnostics;
using System.Text;

internal static partial class RuntimePatch
{
    internal static string ReadAudioState(Process process)
    {
        long image = GetBase(process).ToInt64();
        if (image == 0) return "audio: 游戏模块不可用";
        IntPtr h = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, process.Id);
        if (h == IntPtr.Zero) return "audio: 游戏声音参数不可读";
        try
        {
            StringBuilder result = new StringBuilder("audio: pid=" + process.Id);
            string[] names = { "master", "menuMusic", "menuEffects", "menuSpeech", "raceMusic", "raceEffects", "raceSpeech" };
            for (int i = 0; i < names.Length; i++)
            {
                byte[] value = Read(h, image + 0x18E010 + i * 24, 8);
                result.Append(" " + names[i] + "=" + BitConverter.ToInt32(value, 0) + "/mute:" + value[4]);
            }
            result.Append(" midiVolumeCall=" + BitConverter.ToString(Read(h, image + 0x4546D, 6)));
            result.Append(" midiPrepare=" + BitConverter.ToString(Read(h, image + 0x45AD5, 5)));
            result.Append(" effectFlags=" + BitConverter.ToInt32(Read(h, image + 0x1C5558, 4), 0));
            return result.ToString();
        }
        finally { CloseHandle(h); }
    }
    // The stock MIDI path calls midiOutSetVolume(MIDI_MAPPER, volume).
    // On modern Windows that device gain can also silence the game's other audio.
    // Apply gain to note velocities before preparing each MIDI buffer instead.
    // Loop callbacks reuse the already scaled buffer, so gain is applied exactly once.
    private static void ApplyIndependentAudio(IntPtr process, long image, Action<string> log)
    {
        long prepare = image + 0x45AD5, volume = image + 0x4546D;
        byte[] a = Read(process, prepare, 5), b = Read(process, volume, 6);
        byte[] expected = { 0x6A, 0x40, 0x57, 0xFF, 0x33 };
        for (int i = 0; i < expected.Length; i++)
            if (a[i] != expected[i]) throw new InvalidOperationException("MIDI 缓冲区入口与预期不一致。");
        if (b[0] != 0xFF || b[1] != 0x15 || unchecked((uint)BitConverter.ToInt32(b, 2)) != (uint)(image + 0x21C884))
            throw new InvalidOperationException("MIDI 设备音量调用与预期不一致。");
        IntPtr allocated = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)0x1000, MemCommitReserve, PageExecuteReadWrite);
        if (allocated == IntPtr.Zero) throw new InvalidOperationException("无法分配音乐修复代码区。");
        long code = allocated.ToInt64();
        Write(process, code, BuildMidiScaleCode(image, code, true));
        Write(process, prepare, Jmp(prepare, code, 5));
        // Consume the two stdcall arguments, return MMSYSERR_NOERROR, never change device/session gain.
        Write(process, volume, new byte[] { 0x83, 0xC4, 0x08, 0x33, 0xC0, 0x90 });
        log("比赛音乐音量修复已写入：在 MIDI 音符内缩放音量，不再修改共享设备音量；从下一场比赛播放的音乐生效。");
    }

    internal static byte[] BuildMidiScaleCode(long image, long code, bool hook)
    {
        CodeBuffer c = new CodeBuffer(code);
        B(c, 0x9C, 0x60); // preserve all registers and flags
        if (hook) B(c, 0x89, 0xF9); // EDI=MIDIHDR at the prepare call
        B(c, 0x85, 0xC9); Jcc(c, 0x84, "done");
        B(c, 0x8B, 0x31, 0x8B, 0x79, 0x04); // data, bufferLength
        B(c, 0x85, 0xF6); Jcc(c, 0x84, "done");
        B(c, 0x81, 0xFF); D(c, 0x100000); Jcc(c, 0x87, "done");
        B(c, 0x03, 0xFE); Jcc(c, 0x82, "done"); // end; reject wrap
        B(c, 0x8B, 0x1D); D(c, image + 0x18E010); // master [0,100]
        B(c, 0x85, 0xDB); Jcc(c, 0x89, "master_positive"); B(c, 0x33, 0xDB);
        c.Label("master_positive"); B(c, 0x83, 0xFB, 100); Jcc(c, 0x86, "master_ready"); B(c, 0xBB); D(c, 100);
        c.Label("master_ready"); B(c, 0xA1); D(c, image + 0x18E070); // race music [0,100]
        B(c, 0x85, 0xC0); Jcc(c, 0x89, "music_positive"); B(c, 0x33, 0xC0);
        c.Label("music_positive"); B(c, 0x83, 0xF8, 100); Jcc(c, 0x86, "music_ready"); B(c, 0xB8); D(c, 100);
        c.Label("music_ready"); B(c, 0x0F, 0xAF, 0xD8); // ebx=master * music
        B(c, 0xBD); D(c, 10000);
        c.Label("event");
        B(c, 0x8B, 0xCF, 0x2B, 0xCE, 0x83, 0xF9, 12); Jcc(c, 0x82, "done");
        B(c, 0x8B, 0x46, 0x08, 0xA9); D(c, 0x80000000); Jcc(c, 0x85, "long_event");
        B(c, 0xA9); D(c, 0x3F000000); Jcc(c, 0x85, "next"); // tempo/NOP/etc; callback bit does not change short-message type
        B(c, 0x24, 0xF0, 0x3C, 0x90); Jcc(c, 0x85, "next"); // NOTE_ON only, preserve all controllers and note-offs
        B(c, 0x0F, 0xB6, 0x46, 0x0A, 0x83, 0xE0, 0x7F, 0x0F, 0xAF, 0xC3, 0x33, 0xD2, 0xF7, 0xF5);
        B(c, 0x88, 0x46, 0x0A);
        c.Label("next"); B(c, 0x83, 0xC6, 12); Jmp(c, "event");
        c.Label("long_event");
        B(c, 0x25); D(c, 0x00FFFFFF); B(c, 0x83, 0xC0, 3, 0x83, 0xE0, 0xFC, 0x83, 0xC0, 12);
        B(c, 0x3B, 0xC1); Jcc(c, 0x87, "done");
        B(c, 0x03, 0xF0); Jmp(c, "event");
        c.Label("done"); B(c, 0x61, 0x9D);
        if (hook) { B(c, 0x6A, 0x40, 0x57, 0xFF, 0x33); JmpAbs(c, image + 0x45ADA); }
        else B(c, 0xC3);
        return c.Finish();
    }
}
