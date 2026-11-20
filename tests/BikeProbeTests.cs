using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// 在独立的 32 位测试进程中执行生成的诊断机器码，不访问游戏进程。
internal static class BikeProbeTests
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")]
    private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll")]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RunCode();
    private static readonly List<IntPtr> Blocks = new List<IntPtr>();
    private static long Allocate(int size)
    {
        IntPtr p = VirtualAlloc(IntPtr.Zero, (UIntPtr)size, 0x3000, 0x40);
        if (p == IntPtr.Zero) throw new Exception("VirtualAlloc failed");
        Blocks.Add(p);
        return (uint)p.ToInt32();
    }
    private static void Write(long address, byte[] bytes)
    {
        Marshal.Copy(bytes, 0, new IntPtr(address), bytes.Length);
        FlushInstructionCache(new IntPtr(-1), new IntPtr(address), (UIntPtr)bytes.Length);
    }
    private static void Put(long address, long value) { Marshal.WriteInt32(new IntPtr(address), unchecked((int)value)); }
    private static int Get(long address) { return Marshal.ReadInt32(new IntPtr(address)); }
    private static void D(List<byte> code, long value) { code.AddRange(BitConverter.GetBytes(unchecked((int)value))); }
    private static void Mov(List<byte> code, byte opcode, long value) { code.Add(opcode); D(code, value); }

    private static void Check(long module, long allocation, long actor, long player, bool hasSprite)
    {
        long state = allocation + 0x1000, results = allocation + 0x3000;
        Write(state, new byte[0x100]);
        Put(module + 0x1A69EC, player);
        Put(module + 0x1A69F0, player);
        Put(actor + 0xA8, hasSprite ? 0x12345678 : 0);
        Put(actor + 0x98, 0x23456789);
        Write(allocation + 0xA00, RuntimePatch.BuildBikeProbeCode(module, allocation + 0xA00, state));

        // 原 cmp 之后的落点：记录寄存器/标志后返回测试桥。
        List<byte> continuation = new List<byte>(new byte[] { 0x9C, 0x60, 0x89, 0xE6 });
        Mov(continuation, 0xBF, results);
        Mov(continuation, 0xB9, 9);
        continuation.AddRange(new byte[] { 0xFC, 0xF3, 0xA5, 0x61, 0x9D, 0xC3 });
        Write(module + 0x5C04C, continuation.ToArray());

        List<byte> bridge = new List<byte>(new byte[] { 0x60, 0x9C });
        Mov(bridge, 0xB8, 0x11223344);
        Mov(bridge, 0xBB, 0x22334455);
        Mov(bridge, 0xB9, 0x33445566);
        Mov(bridge, 0xBA, 0x44556677);
        Mov(bridge, 0xBE, actor);
        Mov(bridge, 0xBF, 0x55667788);
        Mov(bridge, 0xBD, 0x66778899);
        bridge.Add(0xE8);
        D(bridge, allocation + 0xA00 - (allocation + bridge.Count + 4));
        bridge.AddRange(new byte[] { 0x9D, 0x61, 0xC3 });
        Write(allocation, bridge.ToArray());
        RunCode run = (RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(allocation), typeof(RunCode));
        run();

        int[] regs = new int[9];
        Marshal.Copy(new IntPtr(results), regs, 0, regs.Length);
        if (regs[0] != 0x55667788 || regs[1] != (int)actor || regs[2] != 0x66778899 ||
            regs[4] != 0x22334455 || regs[5] != 0x44556677 || regs[6] != 0x33445566 || regs[7] != 0x11223344)
            throw new Exception("Probe corrupted a register");
        if (((regs[8] & 0x40) != 0) == hasSprite || (regs[8] & 0x801) != 0)
            throw new Exception("Probe did not preserve the original CMP result");
        bool isPlayer = actor == player;
        if (Get(state + 0xE0) != 1 || Get(state + 0xE4) != (isPlayer ? 1 : 0) ||
            Get(state + 0xE8) != (isPlayer && !hasSprite ? 1 : 0) ||
            Get(state + 0xEC) != (int)actor || Get(state + 0xF0) != (int)player)
            throw new Exception("Incorrect probe counters/pointers");
        if (isPlayer && (Get(state + 0xF4) != (hasSprite ? 0x12345678 : 0) ||
            Get(state + 0xF8) != (int)player || Get(state + 0xFC) != 0x23456789))
            throw new Exception("Incorrect player snapshot");
        Console.WriteLine("PASS native probe: player=" + isPlayer + " sprite=" + hasSprite);
    }

    private static void CheckRider(long module, long allocation, long actor, long player, long expectedRider,
        bool hasSprite, int worldX, int bounce, int scale)
    {
        long state = allocation + 0x1000, results = allocation + 0x3000;
        long count = allocation + 0x3100, calls = allocation + 0x3200;
        Write(state, new byte[0x140]);
        Put(count, 0);
        Put(module + 0x1A69EC, player);
        if (player != 0) Put(player + 0x2FC, expectedRider);
        Put(actor + 0x1C4, 0x112200);
        Put(actor + 0xA8, hasSprite ? 0x12345678 : 0);
        Put(actor + 0x98, 0x23456789);
        Put(actor + 0x44, 12800);
        Put(actor + 0x48, bounce);
        Put(actor + 0x4C, 31);
        Put(actor + 0x50, worldX);
        long context = Allocate(0x1000);
        Put(context + 0x18, 100);
        Put(module + 0x197C90 + 50 * 4, scale);
        Write(allocation + 0x1200, RuntimePatch.BuildRiderCopyHookCode(module, allocation + 0x1200, state));

        // 假绘制函数捕获两个寄存器参数和五个栈参数，然后主动破坏易失寄存器。
        List<byte> draw = new List<byte>(new byte[] { 0x60, 0xA1 }); D(draw, count);
        draw.AddRange(new byte[] { 0x6B, 0xC0, 0x20, 0x05 }); D(draw, calls);
        draw.AddRange(new byte[] { 0x89, 0x08, 0x89, 0x50, 0x04 });
        for (int i = 0; i < 5; i++)
            draw.AddRange(new byte[] { 0x8B, 0x54, 0x24, (byte)(36 + i * 4), 0x89, 0x50, (byte)(8 + i * 4) });
        draw.AddRange(new byte[] { 0xFF, 0x05 }); D(draw, count);
        draw.Add(0x61);
        Mov(draw, 0xB8, 0xAABBCCDD); Mov(draw, 0xB9, 0xAABBCCDD); Mov(draw, 0xBA, 0xAABBCCDD);
        draw.AddRange(new byte[] { 0xC2, 0x14, 0x00 });
        Write(module + 0x46EC, draw.ToArray());

        List<byte> continuation = new List<byte>(new byte[] { 0x9C, 0x60, 0x89, 0xE6 });
        Mov(continuation, 0xBF, results); Mov(continuation, 0xB9, 9);
        continuation.AddRange(new byte[] { 0xFC, 0xF3, 0xA5, 0x61, 0x9D, 0xC3 });
        Write(module + 0x5CE1A, continuation.ToArray());
        List<byte> bridge = new List<byte>(new byte[] { 0x60, 0x9C });
        Mov(bridge, 0xB8, 0x11223344); Mov(bridge, 0xBB, 0x22334455);
        Mov(bridge, 0xB9, 0x33445566); Mov(bridge, 0xBA, 0x44556677);
        Mov(bridge, 0xBE, actor); Mov(bridge, 0xBF, context); Mov(bridge, 0xBD, 0x66778899);
        bridge.AddRange(new byte[] { 0x3B, 0xC0, 0xF9 }); // cmp eax,eax; stc：ZF=1,CF=1
        bridge.Add(0xE8); D(bridge, allocation + 0x1200 - (allocation + bridge.Count + 4));
        bridge.AddRange(new byte[] { 0x9D, 0x61, 0xC3 });
        Write(allocation, bridge.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(allocation), typeof(RunCode)))();

        bool match = player != 0 && actor == expectedRider;
        int expectedCalls = match && hasSprite && scale > 0 ? 4 : 0;
        if (Get(count) != expectedCalls || Get(state + 0x128) != expectedCalls ||
            Get(state + 0x100) != 1 || Get(state + 0x104) != (match ? 1 : 0) ||
            Get(state + 0x12C) != (match && !hasSprite ? 1 : 0) ||
            Get(state + 0x134) != (match && hasSprite && scale <= 0 ? 1 : 0))
            throw new Exception("Incorrect rider branch or copy call count");
        int[] offsets = { -96, -48, 48, 96 };
        for (int i = 0; i < expectedCalls; i++)
        {
            int[] args = new int[7]; Marshal.Copy(new IntPtr(calls + i * 32), args, 0, args.Length);
            if (args[0] != (int)context || args[1] != (int)(actor + 0xA8) || args[2] != 0x200 ||
                args[3] != worldX / 256 + offsets[i] * 65536 / scale || args[4] != (bounce >> 8) + 31 ||
                args[5] != 12800 || args[6] != 1)
                throw new Exception("Incorrect rider sprite call arguments");
        }
        int[] regs = new int[9]; Marshal.Copy(new IntPtr(results), regs, 0, regs.Length);
        if (regs[0] != (int)context || regs[1] != (int)actor || regs[2] != 0x66778899 ||
            regs[4] != 0x22334455 || regs[5] != 0x112200 || regs[6] != 0x33445566 || regs[7] != 0x11223344 ||
            (regs[8] & 0x41) != 0x41)
            throw new Exception("Rider hook corrupted registers/flags or omitted displaced instruction");
        Console.WriteLine("PASS native rider: match=" + match + " sprite=" + hasSprite + " x=" + worldX + " calls=" + expectedCalls);
    }

    private static void CheckCar(long module, long allocation, long rider, long player, int scenario, bool hideFallenVehicles = false, int bikeState = 0)
    {
        long state = allocation + 0x1000, results = allocation + 0x3000, argsOut = allocation + 0x3100;
        long sprite = Allocate(0x1000), car = Allocate(0x1000), context = Allocate(0x1000);
        long originalSprite = Allocate(0x1000), resources = Allocate(0x1000), captured = Allocate(0x1000);
        int yaw = scenario == 11 ? -0x80000 : (scenario == 12 ? 0x80000 : 0);
        int pitch = scenario == 13 ? 0x40000 : 0;
        byte[] spriteBytes = new byte[0x80];
        for (int i = 0; i < spriteBytes.Length; i++) spriteBytes[i] = (byte)(i * 3 + scenario);
        Write(sprite, spriteBytes);
        Put(sprite + 8, scenario == 10 ? 0 : resources + 0x400);
        Marshal.Copy(new IntPtr(sprite), spriteBytes, 0, spriteBytes.Length);
        Put(rider + 0xC4, 8); Put(rider + 0xA8, originalSprite);
        Put(rider + 0x68, 0x7F0000); // 故意与玩家车辆方向不同，不能抄骑手朝向
        Put(player + 0x2FC, scenario == 6 ? car : rider);
        Put(player + 0x2F4, bikeState);
        for (int offset = 0x44; offset < 0x70; offset += 4) Put(player + offset, offset * 257);
        Put(player + 0x68, yaw); Put(player + 0x6C, pitch);
        byte[] playerBytes = new byte[0x300]; Marshal.Copy(new IntPtr(player), playerBytes, 0, playerBytes.Length);
        byte[] riderBytes = new byte[0x220]; Marshal.Copy(new IntPtr(rider), riderBytes, 0, riderBytes.Length);
        Put(module + 0x1A69EC, scenario == 7 ? 0 : player);
        Put(module + 0x1CD300, scenario == 1 ? 0 : 5);
        Put(module + 0x1CD308, scenario == 2 ? 0 : resources + 0x200);
        Put(module + 0x1CD30C, scenario == 3 ? 0 : resources + 0x300);
        Put(module + 0x1A2474, scenario == 4 ? 0 : resources);
        Put(module + 0x1CD308 + 16, resources + 0x200);
        Put(module + 0x1CD30C + 16, resources + 0x300);
        Put(module + 0x1A2474 + 8, resources);
        Put(module + 0x1CD308 + 8, resources + 0x200);
        Put(module + 0x1CD30C + 8, resources + 0x300);
        Put(module + 0x1A2474 + 4, resources);
        for (int i = 0; i < 15; i++) Put(module + 0x204B20 + i * 4, scenario == 18 ? 0x80000 : 0);
        if (scenario == 16 || scenario == 17) Put(module + 0x204B20 + 8, 0x80000);
        if (scenario == 17) { Put(module + 0x204B20, 0x40000); Put(module + 0x204B20 + 4, 0x100000); }
        Put(resources, scenario == 5 ? 0 : resources + 0x100);
        Put(module + 0x1C55D8, 0); // 没有路面交通车也必须可以绘制
        Write(state, new byte[0x1A0]);
        Put(argsOut + 32, 0);
        int selectedStyle = scenario == 14 || scenario >= 16 ? 2 : (scenario == 15 ? 14 : 0);
        int actualStyle = scenario == 14 ? 2 : (scenario == 17 ? 1 : 0);
        Write(allocation + 0x1200, RuntimePatch.BuildPlayerCarCode(module, allocation + 0x1200, state, allocation + 0x2000, car, selectedStyle, 0, hideFallenVehicles));

        // 独立 stub 捕获实际调用参数，再主动破坏易失寄存器。
        List<byte> init = new List<byte>(new byte[] { 0x89, 0x0D }); D(init, captured + 0x100);
        init.AddRange(new byte[] { 0x89, 0x15 }); D(init, captured + 0x104);
        init.AddRange(new byte[] { 0xFF, 0x05 }); D(init, captured + 0x108);
        init.AddRange(new byte[] { 0xC7, 0x41, 0x0C }); D(init, scenario == 8 ? 0 : resources + 0x100);
        Mov(init, 0xB8, 0xDEADBEEF); Mov(init, 0xB9, 0xDEADBEEF); Mov(init, 0xBA, 0xDEADBEEF);
        init.Add(0xC3); Write(module + 0x5D010, init.ToArray());

        List<byte> prepare = new List<byte>(new byte[] { 0x89, 0x0D }); D(prepare, captured + 0x110);
        prepare.AddRange(new byte[] { 0x89, 0x15 }); D(prepare, captured + 0x114);
        prepare.AddRange(new byte[] { 0xFF, 0x05 }); D(prepare, captured + 0x118);
        prepare.AddRange(new byte[] { 0x60, 0x89, 0xCE });
        Mov(prepare, 0xBF, captured); Mov(prepare, 0xB9, 0xE0 / 4);
        prepare.AddRange(new byte[] { 0xFC, 0xF3, 0xA5, 0x61 });
        foreach (long[] field in new long[][] {
            new long[] { 0xA8, scenario == 9 ? 0 : sprite }, new long[] { 0xAC, 0xABC },
            new long[] { 0xB0, 123 }, new long[] { 0xB4, 45 }, new long[] { 0xB8, 1 }, new long[] { 0xBC, 0 } })
        {
            prepare.AddRange(new byte[] { 0xC7, 0x81 }); D(prepare, field[0]); D(prepare, field[1]);
        }
        Mov(prepare, 0xB8, 0xDEADBEEF); Mov(prepare, 0xB9, 0xDEADBEEF); Mov(prepare, 0xBA, 0xDEADBEEF);
        prepare.Add(0xC3); Write(module + 0x2F72, prepare.ToArray());

        List<byte> draw = new List<byte>(new byte[] { 0x60 });
        Mov(draw, 0xB8, argsOut);
        draw.AddRange(new byte[] { 0x89, 0x08, 0x89, 0x50, 0x04 });
        for (int i = 0; i < 5; i++)
            draw.AddRange(new byte[] { 0x8B, 0x54, 0x24, (byte)(36 + i * 4), 0x89, 0x50, (byte)(8 + i * 4) });
        draw.AddRange(new byte[] { 0xFF, 0x05 }); D(draw, argsOut + 32);
        draw.Add(0x61);
        Mov(draw, 0xB8, 0x77AA55CC); Mov(draw, 0xB9, 0x22335566); Mov(draw, 0xBA, 0x12345678);
        draw.AddRange(new byte[] { 0x3B, 0xC0, 0xF9, 0xC2, 0x14, 0x00 });
        Write(module + 0x46EC, draw.ToArray());

        List<byte> continuation = new List<byte>(new byte[] { 0x9C, 0x60, 0x89, 0xE6 });
        Mov(continuation, 0xBF, results); Mov(continuation, 0xB9, 9);
        continuation.AddRange(new byte[] { 0xFC, 0xF3, 0xA5, 0x61, 0x9D, 0x9D, 0x61, 0xC3 });
        Write(module + 0x5CE14, continuation.ToArray());
        List<byte> bridge = new List<byte>(new byte[] { 0x60, 0x9C });
        Mov(bridge, 0xB8, 0x11223344); Mov(bridge, 0xBB, 0x22334455);
        Mov(bridge, 0xB9, context); Mov(bridge, 0xBA, rider + 0xA8);
        Mov(bridge, 0xBE, rider); Mov(bridge, 0xBF, context); Mov(bridge, 0xBD, 0x66778899);
        foreach (int arg in new int[] { 1, 12800, 59, -123, 0x200 }) { bridge.Add(0x68); D(bridge, arg); }
        bridge.Add(0xE9); D(bridge, allocation + 0x1200 - (allocation + bridge.Count + 4));
        Write(allocation, bridge.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(allocation), typeof(RunCode)))();

        bool hidden = hideFallenVehicles && bikeState >= 1 && bikeState <= 3;
        bool replace = !hidden && (scenario == 0 || (scenario >= 11 && scenario != 18)), match = !hidden && scenario != 6 && scenario != 7;
        bool initialized = match && (scenario == 0 || scenario >= 8) && scenario != 18;
        bool prepared = initialized && scenario != 8;
        if (Get(captured + 0x108) != (initialized ? 1 : 0) || Get(captured + 0x118) != (prepared ? 1 : 0))
            throw new Exception("Called helper with missing resources, scenario=" + scenario);
        if (initialized && (Get(captured + 0x100) != (int)(car + 0x8C) || Get(captured + 0x104) != actualStyle))
            throw new Exception("Car type or private animation state was not fixed");
        if (prepared)
        {
            if (Get(captured + 0x110) != (int)car || Get(captured + 0x114) != (int)context || Get(captured + 0xC4) != 16)
                throw new Exception("Frame selection got wrong actor type/context");
            for (int offset = 0x44; offset < 0x70; offset += 4)
                if (Get(captured + offset) != (offset == 0x6C ? 0 : Get(player + offset)))
                    throw new Exception("Position/heading changed, or motorcycle lean was retained");
        }
        int reason = !match || replace ? 0 : (scenario <= 3 || scenario == 18 ? 1 : (scenario <= 8 ? 2 : scenario - 6));
        if (Get(state + 0x170) != reason || Get(state + 0x158) != (prepared ? 1 : 0))
            throw new Exception("Incorrect fallback reason/frame counter, scenario=" + scenario);
        if (match && (Get(state + 0x174) != yaw || Get(state + 0x178) != pitch || Get(state + 0x15C) != actualStyle))
            throw new Exception("Incorrect fixed type/heading telemetry");
        int[] args = new int[7]; Marshal.Copy(new IntPtr(argsOut), args, 0, args.Length);
        if (Get(argsOut + 32) != 1 || args[0] != (int)context ||
            args[1] != (int)(replace ? allocation + 0x2080 : rider + 0xA8) ||
            args[2] != (replace ? 0x500 : 0x200) || args[3] != -123 || args[4] != 59 || args[5] != 12800 || args[6] != 1)
            throw new Exception("Car replacement changed wrong call arguments/count, scenario=" + scenario);
        if (Get(state + 0x140) != 1 || Get(state + 0x144) != (match ? 1 : 0) ||
            Get(state + 0x148) != (replace ? 1 : 0) || Get(state + 0x14C) != (match && !replace ? 1 : 0))
            throw new Exception("Incorrect car state counters, scenario=" + scenario);
        if (replace)
        {
            byte[] copy = new byte[0x80]; Marshal.Copy(new IntPtr(allocation + 0x2000), copy, 0, copy.Length);
            for (int i = 0; i < copy.Length; i++) if (copy[i] != spriteBytes[i]) throw new Exception("Sprite byte copy mismatch");
            if (Get(allocation + 0x2080) != (int)(allocation + 0x2000)) throw new Exception("Wrapper points to shared sprite");
            for (int i = 4; i < 0x18; i += 4)
                if (Get(allocation + 0x2080 + i) != Get(car + 0xA8 + i)) throw new Exception("Incomplete wrapper copy");
        }
        byte[] after = new byte[playerBytes.Length]; Marshal.Copy(new IntPtr(player), after, 0, after.Length);
        for (int i = 0; i < after.Length; i++) if (after[i] != playerBytes[i]) throw new Exception("Player bike was modified");
        after = new byte[riderBytes.Length]; Marshal.Copy(new IntPtr(rider), after, 0, after.Length);
        for (int i = 0; i < after.Length; i++) if (after[i] != riderBytes[i]) throw new Exception("Player rider was modified");
        after = new byte[0x80]; Marshal.Copy(new IntPtr(sprite), after, 0, after.Length);
        for (int i = 0; i < after.Length; i++) if (after[i] != spriteBytes[i]) throw new Exception("Shared sprite was modified");
        int[] regs = new int[9]; Marshal.Copy(new IntPtr(results), regs, 0, regs.Length);
        if (regs[0] != (int)context || regs[1] != (int)rider || regs[2] != 0x66778899 || regs[4] != 0x22334455 ||
            regs[5] != 0x12345678 || regs[6] != 0x22335566 || regs[7] != 0x77AA55CC || (regs[8] & 0x41) != 0x41)
            throw new Exception("Car hook corrupted preserved registers/call results");
        Console.WriteLine("PASS native car: scenario=" + scenario + " hideFallen=" + hideFallenVehicles + " bikeState=" + bikeState + " replacement=" + replace);
    }

    private static void CheckContact(long module, long allocation, int scenario)
    {
        long bike = Allocate(0x1000), rider = Allocate(0x1000), other = Allocate(0x1000), fourth = Allocate(0x1000);
        long state = allocation + 0x1000, scratch = Allocate(0x1000), cave = allocation + 0x2000;
        long first = scenario == 1 || scenario == 3 ? other : (scenario == 2 ? rider : bike);
        long second = scenario == 1 ? bike : (scenario == 3 ? rider : other);
        if (scenario == 4) first = fourth;
        int oldHeight = scenario == 8 ? 900 : 0, oldVelocity = scenario == 8 ? -123 : 0;
        Put(bike + 0x2FC, scenario == 9 ? 0 : rider);
        Put(bike + 0x2F4, scenario == 6 ? 1 : 0);
        Put(module + 0x1A69EC, scenario == 5 ? 0 : bike);
        foreach (long actor in new long[] { bike, rider, other, fourth })
        {
            Put(actor + 0x20, oldHeight); Put(actor + 0xF4, oldVelocity); Put(actor + 0x18C, 17);
            Put(actor + 0xEC, 1000); Put(actor + 0xF0, 10); Put(actor + 0x28, 100);
            Put(actor + 0x1A4, scenario == 10 ? 0 : scratch + 0x600);
        }
        Write(state, new byte[0x1A0]);
        Write(cave, RuntimePatch.BuildCarContactCode(module, cave, state));
        // 碰撞阶段改变高度/垂直冲量，同时保留可观察的横向位移。
        List<byte> collision = new List<byte>(new byte[] { 0x60, 0x89, 0x0D }); D(collision, scratch + 0x100);
        collision.AddRange(new byte[] { 0x89, 0x15 }); D(collision, scratch + 0x104);
        collision.AddRange(new byte[] { 0xFF, 0x05 }); D(collision, scratch + 0x108);
        foreach (byte mod in new byte[] { 0x81, 0x82 })
        {
            collision.AddRange(new byte[] { 0x81, mod }); D(collision, 0x20); D(collision, 32);
            collision.AddRange(new byte[] { 0x81, mod }); D(collision, 0x18C); D(collision, 64);
            collision.AddRange(new byte[] { 0x81, mod }); D(collision, 0x28); D(collision, 10);
        }
        collision.AddRange(new byte[] { 0x61, 0xC3 }); Write(module + 0x48B10, collision.ToArray());
        // 双方回调再增加弹起，且改变速度。wrapper 必须等双方回调都结束再恢复垂直快照。
        List<byte> callback = new List<byte>(new byte[] { 0x60, 0xA1 }); D(callback, scratch + 0x110);
        callback.AddRange(new byte[] { 0x89, 0x0C, 0x85 }); D(callback, scratch + 0x114);
        callback.AddRange(new byte[] { 0xFF, 0x05 }); D(callback, scratch + 0x110);
        foreach (int[] change in new int[][] { new int[] { 0x20, 256 }, new int[] { 0xF4, 128 },
            new int[] { 0x18C, 16 }, new int[] { 0xEC, -5 }, new int[] { 0xF0, 7 } })
        {
            callback.AddRange(new byte[] { 0x81, 0x81 }); D(callback, change[0]); D(callback, change[1]);
        }
        if (scenario == 2 || scenario == 3)
        {
            callback.AddRange(new byte[] { 0x81, 0x05 }); D(callback, bike + 0x20); D(callback, 512);
            callback.AddRange(new byte[] { 0x81, 0x05 }); D(callback, bike + 0xF4); D(callback, 256);
        }
        if (scenario == 7)
        {
            callback.AddRange(new byte[] { 0xC7, 0x05 }); D(callback, bike + 0x2F4); D(callback, 1);
        }
        callback.Add(0x61);
        Mov(callback, 0xB8, 0x11223344); Mov(callback, 0xB9, 0x22334455); Mov(callback, 0xBA, 0x33445566);
        callback.AddRange(new byte[] { 0x3B, 0xC0, 0xF9, 0xC3 }); Write(scratch + 0x600, callback.ToArray());
        List<byte> continuation = new List<byte>(new byte[] { 0x9C, 0x60, 0x89, 0xE6 });
        Mov(continuation, 0xBF, scratch); Mov(continuation, 0xB9, 9);
        continuation.AddRange(new byte[] { 0xFC, 0xF3, 0xA5, 0x61, 0x9D, 0x9D, 0x61, 0xC3 });
        Write(module + 0x496C2, continuation.ToArray());
        List<byte> bridge = new List<byte>(new byte[] { 0x60, 0x9C });
        Mov(bridge, 0xBB, first); Mov(bridge, 0xBE, second); Mov(bridge, 0xB9, first); Mov(bridge, 0xBA, second);
        Mov(bridge, 0xBF, 0x55667788); Mov(bridge, 0xBD, 0x66778899);
        bridge.Add(0xE9); D(bridge, cave - (allocation + bridge.Count + 4)); Write(allocation, bridge.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(allocation), typeof(RunCode)))();
        if (Get(scratch + 0x108) != 1 || Get(scratch + 0x100) != (int)first || Get(scratch + 0x104) != (int)second ||
            Get(scratch + 0x110) != (scenario == 10 ? 0 : 2)) throw new Exception("Contact calling convention/count changed");
        if (scenario != 10 && (Get(scratch + 0x114) != (int)first || Get(scratch + 0x118) != (int)second))
            throw new Exception("Contact callback order/actor changed");
        bool restore = scenario != 4 && scenario != 5 && scenario != 6 && scenario != 7;
        foreach (long actor in new long[] { bike, rider, other, fourth })
        {
            bool affected = actor == first || actor == second;
            bool protectedActor = restore && (actor == bike || (actor == rider && scenario != 9));
            int extraHeight = affected ? (scenario == 10 ? 32 : 288) : 0;
            int extraVelocity = affected && scenario != 10 ? 128 : 0;
            if (actor == bike && (scenario == 2 || scenario == 3)) { extraHeight += 1024; extraVelocity += 512; }
            if (Get(actor + 0x20) != oldHeight + (protectedActor ? 0 : extraHeight) ||
                Get(actor + 0xF4) != oldVelocity + (protectedActor ? 0 : extraVelocity) ||
                Get(actor + 0x18C) != 17 + (affected && !protectedActor ? (scenario == 10 ? 64 : 80) : 0))
                throw new Exception("Incorrect vertical contact response, scenario=" + scenario);
            if (Get(actor + 0x28) != 100 + (affected ? 10 : 0) ||
                Get(actor + 0xEC) != 1000 - (affected && scenario != 10 ? 5 : 0) ||
                Get(actor + 0xF0) != 10 + (affected && scenario != 10 ? 7 : 0))
                throw new Exception("Horizontal collision response was altered");
        }
        if (Get(state + 0x180) != 1 || Get(state + 0x184) != (scenario == 4 || scenario == 5 || scenario == 6 ? 0 : 1))
            throw new Exception("Incorrect contact telemetry");
        if (Get(scratch) != 0x55667788 || Get(scratch + 4) != (int)second || Get(scratch + 8) != 0x66778899 || Get(scratch + 16) != (int)first)
            throw new Exception("Contact corrupted callee-saved registers");
        if (scenario != 10 && (Get(scratch + 20) != 0x33445566 || Get(scratch + 24) != 0x22334455 ||
            Get(scratch + 28) != 0x11223344 || (Get(scratch + 32) & 0x41) != 0x41))
            throw new Exception("Contact changed callback return values/flags");
        Console.WriteLine("PASS native contact: scenario=" + scenario + " restore=" + restore);
    }

    private static void CheckRam(long module, long allocation, int scenario)
    {
        long player = Allocate(0x1000), opponent = Allocate(0x1000), rider = Allocate(0x1000), other = Allocate(0x1000);
        long result = Allocate(0x1000), frame = result + 0x800;
        foreach (long actor in new long[] { player, opponent, other }) Put(actor + 0xC4, 4);
        Put(rider + 0xC4, 8); Put(rider + 0x1C4, player);
        Put(player + 0x2FC, rider); Put(opponent + 0x2FC, other);
        Put(module + 0x1A69EC, scenario == 7 ? 0 : player);
        long first = scenario == 1 ? opponent : (scenario == 2 || scenario == 6 ? other : (scenario == 5 ? rider : player));
        long second = scenario == 1 || scenario == 8 ? player : opponent;
        if (scenario == 3) Put(opponent + 0xC4, 16);
        if (scenario == 4) Put(opponent + 0x2F4, 1);
        Put(frame - 8, first);
        Write(module + 0x6100, new byte[0x40]);
        Put(module + 0x2162B0, result + 0x500);
        Write(module + 0x6200, RuntimePatch.BuildRamCode(module, scenario != 6));
        List<byte> crash = new List<byte>(new byte[] { 0x89, 0x0D }); D(crash, result + 0x100);
        crash.AddRange(new byte[] { 0xFF, 0x05 }); D(crash, result + 0x104);
        if (scenario != 9) { crash.AddRange(new byte[] { 0xC7, 0x81 }); D(crash, 0x2F4); D(crash, 1); }
        crash.Add(0xC3); Write(module + 0x13B60, crash.ToArray());
        List<byte> sound = new List<byte>(new byte[] { 0xFF, 0x05 }); D(sound, result + 0x108);
        sound.AddRange(new byte[] { 0xC2, 0x0C, 0x00 }); Write(module + 0x28BD0, sound.ToArray());
        long[] returns = { module + 0x496C2, module + 0x49693, module + 0x2345D3 };
        for (int path = 0; path < returns.Length; path++)
        {
            List<byte> landing = new List<byte>(new byte[] { 0x9C, 0x60, 0x89, 0xE6 });
            Mov(landing, 0xBF, result); Mov(landing, 0xB9, 9);
            landing.AddRange(new byte[] { 0xFC, 0xF3, 0xA5, 0xC7, 0x05 }); D(landing, result + 0x10C); D(landing, path + 1);
            landing.AddRange(new byte[] { 0x61, 0x9D, 0x9D, 0x61, 0xC3 }); Write(returns[path], landing.ToArray());
        }
        List<byte> bridge = new List<byte>(new byte[] { 0x60, 0x9C });
        Mov(bridge, 0xB8, 0x11223344); Mov(bridge, 0xBB, 0x22334455); Mov(bridge, 0xB9, 0x33445566);
        Mov(bridge, 0xBA, 0x44556677); Mov(bridge, 0xBE, second); Mov(bridge, 0xBF, 0x55667788); Mov(bridge, 0xBD, frame);
        bridge.AddRange(new byte[] { 0x3B, 0xC0, 0xF9, 0xE9 }); D(bridge, module + 0x6200 - (allocation + bridge.Count + 4));
        Write(allocation, bridge.ToArray());
        ((RunCode)Marshal.GetDelegateForFunctionPointer(new IntPtr(allocation), typeof(RunCode)))();
        bool hit = scenario == 0 || scenario == 1 || scenario == 5 || scenario == 9;
        int expectedPath = hit ? 1 : (scenario == 6 ? 3 : 2);
        if (Get(result + 0x10C) != expectedPath || Get(result + 0x104) != (hit ? 1 : 0) ||
            Get(result + 0x108) != (hit ? 1 : 0) || Get(module + 0x6108) != (hit ? 1 : 0))
            throw new Exception("Ram branch/call count incorrect, scenario=" + scenario);
        if (hit && (Get(result + 0x100) != (int)opponent || Get(opponent + 0x2F4) != 1 || Get(player + 0x2F4) != 0))
            throw new Exception("Ram targeted wrong actor or failed knockdown");
        if (Get(result) != 0x55667788 || Get(result + 4) != (int)second || Get(result + 8) != (int)frame ||
            Get(result + 16) != (expectedPath == 2 ? (int)first : 0x22334455) ||
            Get(result + 20) != (expectedPath == 2 ? (int)second : 0x44556677) ||
            Get(result + 24) != 0x33445566 || Get(result + 28) != 0x11223344 || (Get(result + 32) & 0x41) != 0x41)
            throw new Exception("Ram displaced instructions/registers/flags incorrect");
        Console.WriteLine("PASS ram: scenario=" + scenario + " hit=" + hit + " path=" + expectedPath);
    }

    private static void Main()
    {
        if (IntPtr.Size != 4) throw new Exception("Run this test as x86");
        try
        {
            long module = Allocate(0x240000), allocation = Allocate(0x4000);
            long player = Allocate(0x1000), opponent = Allocate(0x1000);
            Check(module, allocation, player, player, false);
            Check(module, allocation, player, player, true);
            Check(module, allocation, opponent, player, false);
            Check(module, allocation, opponent, player, true);
            Console.WriteLine("4 native probe cases passed; registers and branch flags preserved.");
            long rider = Allocate(0x1000), otherRider = Allocate(0x1000);
            CheckRider(module, allocation, rider, player, rider, true, 344832, 7243, 65536);
            CheckRider(module, allocation, rider, player, rider, true, -257, -257, 655);
            CheckRider(module, allocation, rider, player, rider, true, 344832, 0, 32768);
            CheckRider(module, allocation, otherRider, player, rider, true, 344832, 0, 65536);
            CheckRider(module, allocation, rider, 0, rider, true, 344832, 0, 65536);
            CheckRider(module, allocation, rider, player, rider, false, 344832, 0, 65536);
            CheckRider(module, allocation, rider, player, rider, true, 344832, 0, 0);
            Console.WriteLine("7 native rider cases passed; projection spacing, arguments, stack, registers and flags verified.");
            for (int scenario = 0; scenario <= 18; scenario++) CheckCar(module, allocation, rider, player, scenario);
            for(int bikeState=0;bikeState<=3;bikeState++) {
                CheckCar(module,allocation,rider,player,0,true,bikeState);
                CheckCar(module,allocation,rider,player,0,false,bikeState);
            }
            Console.WriteLine("27 native car cases passed; selected type, heading, fallback and on-foot visibility verified.");
            for (int scenario = 0; scenario <= 10; scenario++) CheckContact(module, allocation, scenario);
            Console.WriteLine("11 native contact cases passed; callback order, player/rider, existing airborne motion, horizontal response and registers verified.");
            for (int scenario = 0; scenario <= 9; scenario++) CheckRam(module, allocation, scenario);
            Console.WriteLine("10 native ram cases passed; original/enhanced continuation, target, knockdown, sound and calling convention verified.");
        }
        finally { foreach (IntPtr block in Blocks) VirtualFree(block, UIntPtr.Zero, 0x8000); }
    }
}
