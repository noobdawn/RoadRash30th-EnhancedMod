using System;
using System.Collections.Generic;

internal static partial class RuntimePatch
{
    internal const int OpponentStats = 0x14D85, OpponentSprites = 0x151B0,
        OpponentAwareness = 0x19870, OpponentSwing = 0x18C00, OpponentThreat = 0x1B376,
        OpponentTexture = 0x5C240, OpponentBikeShadow = 0x5C033, OpponentRiderShadow = 0x5CCEC;
    internal const int OfficerGear = 0x14E47, OfficerSprites = 0x15208;

    internal static byte[] OpponentOriginal(long image, int site)
    {
        switch (site)
        {
            case OpponentStats: return new byte[] { 0x8B, 0xC1, 0xC1, 0xE0, 6 };
            case OpponentSprites: return new byte[] { 0x8B, 0x45, 0x10, 0x83, 0xF8, 1 };
            case OfficerGear: return new byte[] { 0x8B,0x8B,0xFC,2,0,0 };
            case OfficerSprites: return new byte[] { 0x8B,0xD0,0x8D,0x4B,0x70 };
            case OpponentTexture:
                var texture = new CodeBuffer(0);
                B(texture, 0x53, 0x8A, 0x3D); D(texture, image + 0x21805A);
                return texture.Finish();
            case OpponentBikeShadow:
            case OpponentRiderShadow:
                var shadow = new CodeBuffer(0);
                MovEaxMem(shadow, image + 0x2061E4);
                return shadow.Finish();
            case OpponentAwareness:
                var c = new CodeBuffer(0);
                B(c, 0x55, 0x8B, 0xEC); MovEaxMem(c, image + 0x1A69F0);
                return c.Finish();
            case OpponentSwing: return new byte[] { 0x56, 0x8B, 0xB1, 0xFC, 2, 0, 0 };
            case OpponentThreat: return new byte[] { 0x89, 0xBB, 0x68, 1, 0, 0 };
            default: throw new ArgumentOutOfRangeException("site");
        }
    }

    // The word comprises the two stock network-mode flags.
    private static void SkipNetworkRace(CodeBuffer c, long image, string skip)
    {
        B(c, 0x66, 0x83, 0x3D); D(c, image + 0x218059); B(c, 0);
        Jcc(c, 0x85, skip);
    }

    private static byte[] BuildOpponentEntry(long image, long entry, long config, long native, int site, int identity)
    {
        var c = new CodeBuffer(entry);
        switch (site)
        {
            case OfficerGear:
                SkipNetworkRace(c, image, "original");
                CmpEbpDwordImm8(c, 0x10, 0); Jcc(c, 0x85, "original");
                // Clone the player's current stats before choosing the patrol bike and baton.
                // This preserves the career's own motorcycle and the player controller.
                B(c, 0x9C,0x60,0x8B,0xB3); D(c, 0x2FC);
                B(c, 0x8B,0xB6); D(c, 0x1E0);
                B(c, 0xBF); D(c, config + 0x80); B(c,0xB9); D(c,16); B(c, 0xFC,0xF3,0xA5);
                B(c, 0x8B,0xBB); D(c, 0x2FC);
                B(c, 0xC7,0x87); D(c,0x1E0); D(c,config + 0x80);
                B(c, 0x0F,0xB6,0x0D); D(c,image + 0x2041A2); // stock race level 0..4
                B(c, 0x8A,0x81); D(c,image + 0x18E8A8 + 15*5);
                B(c, 0xA2); D(c,config + 0x85);
                B(c, 0x8B,0x04,0x8D); D(c,image + 0x18EA98 + 15*64 + 0x18);
                B(c, 0x89,0x87); D(c,0x1D4); B(c,0x61,0x9D);
                break;
            case OfficerSprites:
                SkipNetworkRace(c, image, "original");
                CmpEbpDwordImm8(c, 0x10, 0); Jcc(c, 0x85, "original");
                // Only the two sprite bindings and palette still read the role at this point.
                MovEbpDword(c, 0x10, 2); B(c, 0xB8); D(c,2);
                break;
            case OpponentStats:
                // The single-player arm of the constructor. EDI=rider, ESI=role,
                // [EBP-8]=caller's full grid index, ECX=stock character index.
                B(c, 0x83, 0xFE, 1); Jcc(c, 0x85, "original");
                B(c, 0x51, 0x52, 0xFF, 0x75, 0xF8, 0x89, 0xFA, 0xB9); D(c, config);
                CallAbs(c, native);
                B(c, 0x5A, 0x59, 0x85, 0xC0); Jcc(c, 0x84, "original");
                MovEbpDword(c, 0x18, identity); // native level-specific bike lookup
                JmpAbs(c, image + 0x14D90);      // assign private stats and derive weapon/health
                break;
            case OpponentSprites:
                SkipNetworkRace(c, image, "original");
                CmpEbpDwordImm8(c, 0x10, 1); Jcc(c, 0x85, "original");
                // The role on the bike was assigned earlier. Only the two sprite
                // bindings and the palette initialization still use this argument.
                MovEbpDword(c, 0x10, 2);
                break;
            case OpponentTexture:
            case OpponentBikeShadow:
            case OpponentRiderShadow:
                SkipNetworkRace(c, image, "original");
                // The renderer snapshots the constructor's CANS bank (+70) at +8C.
                // This remains valid across both passes while +90 changes for shadows.
                MovEaxMem(c, image + 0x2061F8);
                B(c, 0x85, 0xC0); Jcc(c, 0x84, "original");
                B(c, 0x39, (byte)(site == OpponentTexture ? 0x81 : 0x86)); D(c, 0x8C);
                Jcc(c, 0x85, "original");
                if (site == OpponentTexture)
                {
                    // Both bike and rider drawing use this key for the pixel atlas.
                    // The native shadow pass also maps it to the cop shadow key.
                    B(c, 0xB8); D(c, 0xDE); B(c, 0xC3);
                }
                else
                {
                    MovEaxMem(c, image + 0x2061EC); // matching cop shadow frames
                    JmpAbs(c, image + site + 5);
                }
                break;
            case OpponentAwareness:
            case OpponentSwing:
                SkipNetworkRace(c, image, "original");
                B(c, 0x83, 0xB9); D(c, 0x324); B(c, 1); Jcc(c, 0x85, "original");
                if (site == OpponentAwareness)
                {
                    // Stock fastcall supplies direction in DL and range on the stack.
                    B(c, 0xFF, 0x74, 0x24, 4, 0x0F, 0xB6, 0xC2, 0x50);
                    B(c, 0x89, 0xCA, 0xB9); D(c, config);
                    CallAbs(c, native + OpponentRulesNative.TargetOffset);
                    B(c, 0xC2, 4, 0);
                }
                else
                {
                    B(c, 0x51, 0x68); D(c, 0x1E0); B(c, 0x6A, 2);
                    B(c, 0x89, 0xCA, 0xB9); D(c, config);
                    CallAbs(c, native + OpponentRulesNative.TargetOffset);
                    B(c, 0x59, 0x8B, 0x89); D(c, 0x2FC);
                    B(c, 0x8B, 0x89); D(c, 0x1D8);
                    B(c, 0x89, 0x81); D(c, 0x168); // swing uses this final recipient
                    B(c, 0x31, 0xC0, 0xC3);
                }
                break;
            case OpponentThreat:
                SkipNetworkRace(c, image, "original");
                B(c, 0x85, 0xFF); Jcc(c, 0x84, "original");
                B(c, 0x3B, 0x3D); D(c, image + RvaPlayer); Jcc(c, 0x85, "original");
                MovRegEbp(c, 0, -8);
                B(c, 0x83, 0xB8); D(c, 0x324); B(c, 1); Jcc(c, 0x85, "original");
                B(c, 0xBE); D(c, 3); // native hostile rating for an eligible player
                break;
        }
        c.Label("original");
        byte[] original = OpponentOriginal(image, site);
        B(c, original);
        JmpAbs(c, image + site + original.Length);
        return c.Finish();
    }

    internal static void ApplyOpponents(IntPtr process, long image, int identity, bool playerOnly, Action<string> log, bool officer = false)
    {
        if (identity == 0 && !playerOnly && !officer) return;
        if (identity != 15 && (identity < 0 || identity > 8)) throw new ArgumentOutOfRangeException("identity");
        var sites = new List<int>();
        if (identity != 0) sites.Add(OpponentStats);
        if (identity == 15) sites.Add(OpponentSprites);
        if (officer) sites.AddRange(new[] { OfficerGear, OfficerSprites });
        if (identity == 15 || officer) sites.AddRange(new[] { OpponentTexture, OpponentBikeShadow, OpponentRiderShadow });
        if (playerOnly) sites.AddRange(new[] { OpponentAwareness, OpponentSwing, OpponentThreat });
        foreach (int site in sites) CheckOpponentBytes(process, image, site, OpponentOriginal(image, site));
        if (identity != 0)
        {
            CheckOpponentBytes(process, image, 0x14C20, new byte[] {0x55,0x8B,0xEC,0x51,0x53,0x56});
            int personality = identity == 15 ? 9 : identity;
            CheckOpponentBytes(process, image, 0x18F360 + personality * 0x204 + 0x165, new[] { (byte)personality });
            if (identity == 15) CheckOpponentBytes(process, image, 0x1A1B70, new byte[] {0,0,1});
        }

        int bytes = 0x1000 + (identity == 0 ? 0 : 10001 * (0x210 + 0x40));
        long config = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)bytes, MemCommitReserve, 4).ToInt64();
        const int entryOffset = 0x800, entrySize = 0x100;
        long native = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)(entryOffset + sites.Count * entrySize), MemCommitReserve, PageExecuteReadWrite).ToInt64();
        if (config == 0 || native == 0) throw new InvalidOperationException("无法分配 AI 对手数据。");
        var data = new CodeBuffer(0);
        D(data, image); D(data, config + 0x1000); D(data, identity);
        if (identity != 0) B(data, Read(process, image + 0x18EA98 + 64 * identity, 64));
        Write(process, config, data.Finish());
        Write(process, native, OpponentRulesNative.Code);
        long firstEntry = native + entryOffset;
        for (int i = 0; i < sites.Count; ++i)
        {
            long entry = firstEntry + i * entrySize;
            Write(process, entry, BuildOpponentEntry(image, entry, config, native, sites[i], identity));
        }
        int installed = 0;
        try
        {
            for (; installed < sites.Count; ++installed)
            {
                int site = sites[installed];
                Write(process, image + site, Jmp(image + site, firstEntry + installed * entrySize, OpponentOriginal(image, site).Length));
            }
        }
        catch
        {
            while (installed > 0)
            {
                int site = sites[--installed];
                Write(process, image + site, OpponentOriginal(image, site));
            }
            throw;
        }
        if (identity != 0 || playerOnly) log("单人 AI 对手设置已启用。角色编号 " + identity + "，仅攻击玩家 " + playerOnly + "。");
        if (officer) log("玩家使用原版警察车手、当级警用摩托和警棍。");
    }

    private static void CheckOpponentBytes(IntPtr process, long image, int site, byte[] expected)
    {
        byte[] actual = Read(process, image + site, expected.Length);
        for (int i = 0; i < expected.Length; ++i)
            if (actual[i] != expected[i])
                throw new InvalidOperationException("AI 对手设置与当前游戏不兼容，位置 0x" + site.ToString("X") + "。");
    }
}
