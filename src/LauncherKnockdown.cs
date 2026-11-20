using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static partial class RuntimePatch
{
    private const int KnockdownListenersOffset = 0x3000;
    internal const int KnockdownStateOffset = 0x5000;
    private const int WreckPhaseOffset = 140;
    internal const int KnockdownBannerColumns = 400, KnockdownBannerRows = 140;
    internal const int KnockdownCanvasWidth = 640, KnockdownCanvasHeight = 50;
    internal const int KnockdownCanvasBytes = 128 * 50 * 4 + KnockdownBannerColumns * KnockdownBannerRows;
    // ColorMap contains the original PALETTEENTRY array and a zero-initialized RGB cache.
    internal const int KnockdownColorMapBytes = 256 * 4 + 16384 * 4;
    private const int KdStart = 0x15598, KdEnd = 0x14ABD, KdContact = 0x49688, KdDamage = 0x136F8,
        KdFall = 0x13DCB, KdOtherFall = 0x145BD, KdFocusPause = 0x58490, KdMenuPause = 0x58736, KdPaint = 0x5D7E1;
    private const int KdFallDone = 0x13E83, KdOtherDone = 0x14640;
    private const int KdRemount = 0x12BD0, KdDistance = 0x167C0, KdFinish = 0x4A640;
    private const int KdGround = 0x4B6A1, KdOtherGround = 0x4BB5B, KdRaceTick = 0x57AFD,
        KdRecoverySpeed = 0x19C92, KdRunningDismount = 0x13E5E;

    private static byte[] KnockdownOriginal(long image, int site)
    {
        CodeBuffer c = new CodeBuffer(0);
        switch (site)
        {
            case KdStart: B(c, 0xA3); D(c, image + RvaPlayer); break;
            case KdEnd: B(c, 0xC7, 5); D(c, image + RvaPlayer); D(c, 0); break;
            case KdContact: return new byte[] { 0x2B,0xCA,0x3B,0xC8,0x7F,0x34 };
            case KdDamage: return new byte[] { 0x8B,0x8F,0xE0,1,0,0 };
            case KdFall: case KdOtherFall: return new byte[] { 0xC7,0x86,0xF4,2,0,0,1,0,0,0 };
            case KdFallDone: return new byte[] {0x5F,0x5B,0x8B,0x4D,0xFC};
            case KdOtherDone: return new byte[] {0x5F,0x5E,0x5B,0x8B,0xE5};
            case KdRemount: B(c,0x8A,0x15); D(c,image + 0x218059); break;
            case KdDistance: return new byte[] {0x55,0x8B,0xEC,0x83,0xEC,8};
            case KdFinish: return new byte[] {0x8A,0x81,0xC0,0,0,0};
            case KdGround: return new byte[] {0x0F,0x8F,0x6D,1,0,0};
            case KdOtherGround: return new byte[] {0x0F,0x8F,0x19,1,0,0};
            case KdRaceTick: B(c,0x83,0x3D); D(c,image + 0x1CD4E4); B(c,0); break;
            case KdRecoverySpeed: return new byte[] {0x8B,0x8F,0xEC,0,0,0};
            case KdRunningDismount: return new byte[] {0xB2,0x26,0x8D,0x4F,0x70};
            case KdFocusPause: B(c, 0x88,0x1D); D(c, image + 0x1CD4D4); break;
            case KdMenuPause: B(c, 0xC6,5); D(c, image + 0x1CD4D4); B(c, 1); break;
            case KdPaint: MovEaxMem(c, image + 0x1CD4BC); break;
            default: throw new ArgumentOutOfRangeException("site");
        }
        return c.Finish();
    }

    // ECX = module state, EDX = event, stack = (actor, other). The shared
    // callback runs before the original instruction with its registers restored.
    private static void FinishKnockdownCall(CodeBuffer c, long entry, int kind)
    {
        B(c, 0xB9); D(c, entry + KnockdownStateOffset);
        B(c, 0xBA); D(c, kind);
        CallAbs(c, entry);
    }

    private static byte[] BuildKnockdownSite(long image, long address, long entry, int site, long extraDamageReturn)
    {
        CodeBuffer c = new CodeBuffer(address);
        byte[] original = KnockdownOriginal(image, site);
        long wreckPhase = entry + KnockdownStateOffset + WreckPhaseOffset;
        if (site == KdGround || site == KdOtherGround) {
            // Stock signed JG takes the airborne path. Fallthrough resolved contact
            // against the terrain height in this physics step, including raised ground.
            Jcc(c,0x8F,"air"); B(c,0x9C,0x83,0x3D); D(c,wreckPhase); B(c,1);
            Jcc(c,0x85,"done"); B(c,0x60,0x6A,0,site == KdGround ? (byte)0x57 : (byte)0x53);
            FinishKnockdownCall(c,entry,8); B(c,0x61);
            c.Label("done"); B(c,0x9D); JmpAbs(c,image + site + original.Length);
            c.Label("air"); JmpAbs(c,image + (site == KdGround ? 0x4B814 : 0x4BC7A));
            return c.Finish();
        }
        if (site == KdRecoverySpeed || site == KdRunningDismount) {
            if (site == KdRecoverySpeed) B(c,original); // ECX is the speed used by the animator only.
            B(c,0x9C,0x83,0x3D); D(c,wreckPhase); B(c,0); Jcc(c,0x84,"stock");
            B(c,0x3B,site == KdRecoverySpeed ? (byte)0x1D : (byte)0x35); D(c,image + RvaPlayer);
            Jcc(c,0x85,"stock");
            if (site == KdRunningDismount) { B(c,0x9D); JmpAbs(c,image + KdFallDone); }
            else {
                // Keep the stock fall/impact/tumble animations below walking speed.
                // Both stock get-up paths read ECX; the physical velocity is untouched.
                B(c,0x81,0xF9); D(c,2500); Jcc(c,0x8D,"stock"); B(c,0xB9); D(c,2500);
            }
            c.Label("stock"); B(c,0x9D);
            if (site == KdRunningDismount) B(c,original);
            JmpAbs(c,image + site + original.Length); return c.Finish();
        }
        if (site == KdRemount || site == KdDistance || site == KdFinish) {
            // A callback already on the stack can still reach these paths before
            // the world performs its deferred unlink. Only retired pairs use
            // the non-freeing park routine as their streaming callback.
            B(c,0x9C,0x81,0xB9); D(c,0xE8); D(c,image + 0x15F5);
            Jcc(c,0x85,"active"); B(c,0x9D,0xC3);
            c.Label("active"); B(c,0x9D); B(c,original);
            JmpAbs(c,image + site + original.Length);
            return c.Finish();
        }
        if (site == KdContact)
        {
            // The last vertical overlap test is immediately before either ram handler.
            B(c, 0x2B,0xCA,0x3B,0xC8); Jcc(c, 0x8F, "miss");
        }
        B(c, 0x9C,0x60);
        switch (site)
        {
            case KdDamage:
                // This write actually subtracts health. Accept only the stock
                // melee caller or the exact additional-target call installed by this launcher.
                B(c, 0x85,0xC0); Jcc(c, 0x89, "restore");
                B(c, 0x81,0x7D,4); D(c, image + RvaAoePatch);
                if (extraDamageReturn != 0) {
                    Jcc(c, 0x84, "creditedCaller");
                    B(c, 0x81,0x7D,4); D(c, extraDamageReturn);
                }
                Jcc(c, 0x85, "restore"); c.Label("creditedCaller");
                B(c, 0x56,0xFF,0x75,0xCC); // victim ESI; attacker's saved EDI
                FinishKnockdownCall(c, entry, 2); break;
            case KdContact:
                B(c, 0x56,0xFF,0x75,0xF8); FinishKnockdownCall(c, entry, 1); break;
            case KdFall: case KdOtherFall:
                B(c, 0x6A,0,0x56); FinishKnockdownCall(c, entry, 3); break;
            case KdFallDone: case KdOtherDone:
                B(c, 0x6A,0,0x56); FinishKnockdownCall(c, entry, 7); break;
            case KdPaint:
                // The dashboard's keyed BltFast has finished. The following stock GetDC
                // acquires this same DirectDraw back buffer for text.
                B(c, 0x6A,0,0xFF,0x35); D(c, image + 0x1CD4BC); FinishKnockdownCall(c, entry, 5); break;
            case KdRaceTick:
                B(c,0x6A,0,0x6A,0); FinishKnockdownCall(c,entry,9); break;
            default:
                B(c, 0x6A,0,0x6A,0);
                FinishKnockdownCall(c, entry, site == KdStart ? 0 : site == KdEnd ? 6 : 4); break;
        }
        c.Label("restore"); B(c, 0x61,0x9D);
        if (site != KdContact) B(c, original);
        JmpAbs(c, image + site + original.Length);
        if (site == KdContact) { c.Label("miss"); JmpAbs(c, image + RvaRamBack); }
        return c.Finish();
    }

    internal static byte[] LoadKnockdownAtlas()
    {
        using (Stream resource = typeof(RuntimePatch).Assembly.GetManifestResourceStream("KnockdownHud.png"))
        {
            if (resource == null) throw new InvalidDataException("找不到内嵌击倒仪表。");
            using (Bitmap bitmap = new Bitmap(resource))
            {
                if (bitmap.Width != 320 || bitmap.Height != 112) throw new InvalidDataException("击倒仪表图集尺寸错误。");
                byte[] pixels = new byte[320 * 112 * 4];
                BitmapData bits = bitmap.LockBits(new Rectangle(0,0,320,112), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try { for (int y = 0; y < 112; ++y) Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * 1280, 1280); }
                finally { bitmap.UnlockBits(bits); }
                return pixels;
            }
        }
    }

    internal static bool ValidKnockdownBank(byte[] bytes)
    {
        if (bytes.Length < 8 || BitConverter.ToInt32(bytes, 0) != 0x3156444B) return false;
        int header = BitConverter.ToInt32(bytes, 4);
        if ((header != 4 + 8 * 13 && header != 4 + 8 * 14) || bytes.Length < header) return false;
        int end = header;
        for (int i = 0; i < (header - 4) / 8; ++i) {
            int offset = BitConverter.ToInt32(bytes, 4 + i * 8), size = BitConverter.ToInt32(bytes, 8 + i * 8);
            if (offset != end || size < 4410 || size > 441000 || (size & 1) != 0 || size > bytes.Length - end) return false;
            end += size;
        }
        return end == bytes.Length;
    }

    // Master the private playback copy once per race. Measure 20 ms speech
    // blocks, leave quiet tails in place and roll off peaks above the knee.
    internal static void LevelKnockdownVoices(byte[] bank)
    {
        const double targetRms = 8800, knee = 24800, ceiling = 29200, playbackHeadroom = 0.92;
        int clips = (BitConverter.ToInt32(bank, 4) - 4) / 8;
        for (int clip = 0; clip < clips; ++clip) {
            int start = BitConverter.ToInt32(bank, 4 + clip * 8);
            int end = start + BitConverter.ToInt32(bank, 8 + clip * 8);
            long activeEnergy = 0;
            int activeSamples = 0, peak = 0;
            for (int pos = start; pos < end;) {
                int samples = Math.Min(441, (end - pos) / 2);
                long energy = 0;
                for (int n = 0; n < samples; ++n, pos += 2) {
                    int sample = BitConverter.ToInt16(bank, pos);
                    peak = Math.Max(peak, Math.Abs(sample));
                    energy += (long)sample * sample;
                }
                if (energy >= (long)samples * 1024 * 1024) {
                    activeEnergy += energy;
                    activeSamples += samples;
                }
            }
            if (peak == 0) continue;
            double gain = ceiling / peak;
            if (activeSamples > 0)
                gain = Math.Max(gain, targetRms / Math.Sqrt((double)activeEnergy / activeSamples));
            gain = Math.Min(gain, 2.82);
            if (gain <= 1) continue;
            for (int pos = start; pos < end; pos += 2) {
                int input = BitConverter.ToInt16(bank, pos);
                double level = Math.Abs(input) * gain;
                if (level > knee) {
                    double excess = level - knee;
                    level = knee + (ceiling - knee) * excess / (ceiling - knee + excess);
                }
                int output = (int)(level * playbackHeadroom + 0.5) * Math.Sign(input);
                bank[pos] = unchecked((byte)output);
                bank[pos + 1] = unchecked((byte)(output >> 8));
            }
        }
    }

    internal static long InstallKnockdown(IntPtr process, long image, bool meter, bool voice, bool aoe,
        int creditMs, int windowMs, int perVoice, bool hardcore, bool officer, Action<string> log)
    {
        if (creditMs < 1 || creditMs > 600000 || windowMs < 1 || windowMs > 600000 || perVoice < 1 || perVoice > 10000)
            throw new ArgumentOutOfRangeException("击倒仪表参数");
        var sites = new List<int> { KdStart, KdEnd, KdFall, KdOtherFall };
        if (meter) sites.AddRange(new[] { KdContact, KdDamage, KdPaint });
        if (meter || hardcore || officer) sites.AddRange(new[] { KdFocusPause, KdMenuPause });
        if (hardcore || officer) sites.AddRange(new[] { KdGround, KdOtherGround, KdRaceTick, KdRecoverySpeed, KdRunningDismount });
        if (officer) sites.AddRange(new[] { KdFallDone, KdOtherDone, KdRemount, KdDistance, KdFinish });
        foreach (int site in sites) RequireMotionSignature(process, image + site, KnockdownOriginal(image, site));
        if (hardcore || officer) RequireMotionSignature(process, image + 0x5A01, new byte[] {0xE9,0x8A,0x5B,2,0});
        if (officer) RequireMotionSignature(process, image + 0x15F5, new byte[] {0xE9,0x86,0x8A,4,0});
        long extraDamageReturn = 0;
        if (aoe && meter)
        {
            byte[] group = Read(process, image + RvaAoeCave, 0x400);
            for (int i = 0; i <= group.Length - 5; ++i)
                if (group[i] == 0xE8 && RvaAoeCave + i + 5 + BitConverter.ToInt32(group, i + 1) == RvaDamageFunction) {
                    extraDamageReturn = image + RvaAoeCave + i + 5; break;
                }
            if (extraDamageReturn == 0) throw new InvalidDataException("找不到群体攻击的受伤事件。");
        }
        byte[] atlas = meter ? LoadKnockdownAtlas() : new byte[0], bank = new byte[0];
        byte[] colors = meter ? File.ReadAllBytes(Path.Combine(Launcher.GameDir, "DATA", "PALETTE.RAW")) : new byte[0];
        if (meter && colors.Length != 256 * 4) throw new InvalidDataException("击倒仪表需要原版 256 色调色板。");
        if (meter && voice) {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "KnockdownVoice.bank");
            if (!File.Exists(path)) path = Path.Combine(Launcher.GameDir, "Tools", "KnockdownVoice.bank");
            if (File.Exists(path) && new FileInfo(path).Length <= 6000000) {
                byte[] candidate = File.ReadAllBytes(path);
                if (ValidKnockdownBank(candidate)) {
                    LevelKnockdownVoices(candidate);
                    bank = candidate;
                }
            }
            if (bank.Length == 0) log("击倒 HUD 已使用静音模式。找不到有效的本机 Dota 2 播报音库。");
        }
        if (KnockdownNative.Code.Length > KnockdownListenersOffset) throw new InvalidDataException("击倒模块容量不足。");
        const int atlasOffset = KnockdownStateOffset + 0x1000;
        int colorsOffset = atlasOffset + atlas.Length, tagsOffset = colorsOffset + (meter ? KnockdownColorMapBytes : 0);
        int canvasOffset = tagsOffset + (meter || officer ? 32768 * 20 : 0);
        int queueOffset = canvasOffset + (meter ? KnockdownCanvasBytes : 0);
        int bankOffset = queueOffset + (bank.Length == 0 ? 0 : 65536);
        IntPtr block = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)(bankOffset + bank.Length), MemCommitReserve, 4);
        if (block == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        long entry = block.ToInt64(), state = entry + KnockdownStateOffset;
        CodeBuffer config = new CodeBuffer(state);
        D(config, image); D(config, entry + atlasOffset); D(config, bank.Length != 0 ? entry + bankOffset : 0);
        D(config, entry + canvasOffset); D(config, entry + tagsOffset);
        D(config, entry + colorsOffset);
        Write(process, state, config.Finish());
        CodeBuffer rules = new CodeBuffer(state + 108);
        D(rules, creditMs); D(rules, windowMs); D(rules, perVoice);
        D(rules, (meter ? 0 : 1) | (hardcore || officer ? 2 : 0) | (officer ? 4 : 0));
        D(rules, entry + queueOffset); D(rules, bank.Length == 0 ? 0 : (BitConverter.ToInt32(bank, 4) - 4) / 8);
        Write(process, state + 108, rules.Finish());
        if (meter) { Write(process, entry + atlasOffset, atlas); Write(process, entry + colorsOffset, colors); }
        if (bank.Length != 0) Write(process, entry + bankOffset, bank);
        Write(process, entry, KnockdownNative.Code);
        for (int i = 0; i < sites.Count; ++i) {
            long listener = entry + KnockdownListenersOffset + i * 0x100;
            Write(process, listener, BuildKnockdownSite(image, listener, entry, sites[i], extraDamageReturn));
        }
        uint old;
        if (!VirtualProtectEx(process, block, (IntPtr)KnockdownStateOffset, 0x20, out old)) throw new Win32Exception(Marshal.GetLastWin32Error());
        for (int i = 0; i < sites.Count; ++i) {
            int site = sites[i];
            Write(process, image + site, Jmp(image + site, entry + KnockdownListenersOffset + i * 0x100, KnockdownOriginal(image, site).Length));
        }
        if (meter) log("击倒仪表已启用。表右小火焰，与仪表同高。归因 " + creditMs / 1000m + " 秒，连击 " + windowMs / 1000m +
            " 秒，Dota 2 播报" + (bank.Length != 0 ? "每 " + perVoice + " 次击倒一次（人声增益）" : "关闭") + "。HUD=0x" + state.ToString("X"));
        if (hardcore || officer) log(officer ? "我是警察：玩家落地 3 秒后以撞毁出局，倒地 AI 本场退赛。" : "硬核模式：玩家落地 3 秒后以撞毁出局。");
        return entry;
    }
}
