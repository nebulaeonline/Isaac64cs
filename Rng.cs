namespace Isaac64
{
    public class Rng
    {
        private static readonly int LOG2_ISSAC64_SZ = 8;
        private static readonly int ISAAC64_SZ = (int)(1 << LOG2_ISSAC64_SZ);
        private static readonly int ISAAC64_SEED_SZ_MAX = (ISAAC64_SZ << 3);
        private static readonly ulong IND_MASK = (ulong)(((ISAAC64_SZ) - 1) << 3);
        private static readonly ulong MAGIC = 0x9E3779B97F4A7C13;
        private static readonly ulong HIGH32 = 0xFFFF_FFFF_0000_0000;
        private static readonly ulong LOW32 = 0x0000_0000_FFFF_FFFF;
        private static readonly int[] MIX_SHIFT = { 9, 9, 23, 15, 14, 20, 17, 14 };

        private class Context
        {
            internal ulong[] rng_buf = new ulong[ISAAC64_SZ];       // randrsl
            internal ulong[] rng_state = new ulong[ISAAC64_SZ];     // mm
            internal ulong aa, bb, cc;
            internal int rng_count;                                 // randcnt
            internal uint banked32 = 0;
            internal Stack<ushort> banked16 = new Stack<ushort>();
            internal Stack<Byte> banked8 = new Stack<Byte>();
        }

        private Context ctx = new Context();

        public Rng()
        {
            Reseed(0);
        }
        
        public Rng(ulong NumericSeed)
        {
            Reseed(NumericSeed);
        }

        public Rng(byte[] SeedBytes)
        {
            Reseed(SeedBytes);
        }

        public Rng(ulong[] SeedULongs)
        {
            Reseed(SeedULongs);
        }

        // state_idx1 -> m  - first index into rng_state
        // state_idx2 -> m2 - second index into rng_state
        // rng_idx    -> r  - index into rng_buf
        // a, b, x, y -> isaac64() local vars (Context.aa, Context.bb, temporaries x & y)
        private void rng_step(ref int state_idx1, ref int state_idx2, ref int rng_idx, ref ulong a, ref ulong b, ref ulong x, ref ulong y)
        {
            x = ctx.rng_state[state_idx1];

            switch (state_idx1 % 4)
            {
                case 0:
                    a = ~(a ^ (a << 21)) + ctx.rng_state[state_idx2++];
                    break;
                case 1:
                    a = (a ^ (a >> 5)) + ctx.rng_state[state_idx2++];
                    break;
                case 2:
                    a = (a ^ (a << 12)) + ctx.rng_state[state_idx2++];
                    break;
                case 3:
                    a = (a ^ (a >> 33)) + ctx.rng_state[state_idx2++];
                    break;
            }

            ctx.rng_state[state_idx1++] = y = ind(x) + a + b;
            ctx.rng_buf[rng_idx++] = b = ind(y >> LOG2_ISSAC64_SZ) + x;
        }

        private void isaac64()
        {
            ulong a, b, x, y;
            x = y = 0;

            int state_idx1, state_idx2, rng_idx, end_idx;
            rng_idx = 0;

            a = ctx.aa;
            b = ctx.bb + (++ctx.cc);

            for (state_idx1 = 0, end_idx = state_idx2 = (ISAAC64_SZ / 2); state_idx1 < end_idx;)
                for (int i = 0; i < 4; i++)
                    rng_step(ref state_idx1, ref state_idx2, ref rng_idx, ref a, ref b, ref x, ref y);

            for (state_idx2 = 0; state_idx2 < end_idx;)
                for (int i = 0; i < 4; i++)
                    rng_step(ref state_idx1, ref state_idx2, ref rng_idx, ref a, ref b, ref x, ref y);

            ctx.bb = b;
            ctx.aa = a;
        }

        private ulong ind(ulong x)
        {
            ulong index = (x & IND_MASK) / 8;
            return ctx.rng_state[index];
        }
        
        private static void mix(ref ulong[] _x)
        {
            for (uint i = 0; i < 8; i++)
            {
                _x[i] -= _x[(i + 4) & 7];
                _x[(i + 5) & 7] ^= _x[(i + 7) & 7] >> MIX_SHIFT[i];
                _x[(i + 7) & 7] += _x[i];
                i++;
                _x[i] -= _x[(i + 4) & 7];
                _x[(i + 5) & 7] ^= _x[(i + 7) & 7] << MIX_SHIFT[i];
                _x[(i + 7) & 7] += _x[i];
            }
        }

        private void init(bool Zero = false)
        {
            int i;
            ulong[] x = { MAGIC, MAGIC,
                          MAGIC, MAGIC,
                          MAGIC, MAGIC,
                          MAGIC, MAGIC };

            ctx.aa = ctx.bb = ctx.cc = 0;

            for (i = 0; i < 4; i++)
                mix(ref x);

            for (i = 0; i < ISAAC64_SZ; i += 8)
            {
                if (!Zero)
                    for (int j = 0; j < 8; j++)
                        x[j] += ctx.rng_buf[i + j];

                mix(ref x);

                for (int j = 0; j < 8; j++)
                    ctx.rng_state[i + j] = x[j];
            }

            if (!Zero)
            { 
                for (i = 0; i < ISAAC64_SZ; i += 8)
                { 
                    for (int j = 0; j < 8; j++)
                        x[j] += ctx.rng_state[i + j];

                    mix(ref x);

                    for (int j = 0; j < 8; j++)
                        ctx.rng_state[i + j] = x[j];
                }
            }

            isaac64();
            ctx.rng_count = ISAAC64_SZ;
        }

        private void clear_state()
        {
            for (int i = 0; i < ISAAC64_SZ; i++) ctx.rng_state[i] = 0;
        }

        public void Shuffle()
        {
            isaac64();
            ctx.rng_count = ISAAC64_SZ;
        }
        
        public void Reseed(byte[] SeedBytes)
        {
            if (SeedBytes.Length == 0)
            {
                Reseed(0);
                return;
            }
            else if (SeedBytes.Length > 2048)
                throw new ArgumentException("Cannot seed ISAAC64 with more than 2048 bytes!");

            clear_state();
            for (int i = 0; i < SeedBytes.Length; i++)
            {
                if (i % 8 == 0)
                    ctx.rng_buf[i / 8] = 0;

                ctx.rng_buf[i / 8] |= ((ulong)SeedBytes[i] << ((i % 8) * 8));
            }
            init();
        }

        public void Reseed(ulong[] SeedULongs)
        {
            if (SeedULongs.Length == 0)
            {
                Reseed(0);
                return;
            }
            else if (SeedULongs.Length > 256)
                throw new ArgumentException("Cannot seed ISAAC64 with more than 256 ulongs!");

            clear_state();
            for (int i = 0; i < SeedULongs.Length; i++)
                ctx.rng_buf[i] = SeedULongs[i];

            init();
        }

        public void Reseed(ulong NumericSeed)
        {
            clear_state();
            if (NumericSeed == 0)
                init(true);
            else
            {
                ctx.rng_buf[0] = NumericSeed;
                init();
            }
        }

        public ulong Rand64(ulong Max = 0)
        {
            if (Max == 1) { return 0; }
            if (ctx.rng_count-- == 0) { isaac64(); ctx.rng_count = ISAAC64_SZ - 1; }

            return (Max == 0) ? ctx.rng_buf[ctx.rng_count] : ctx.rng_buf[ctx.rng_count] % Max;
        }

        public ulong RangedRand64(ulong Min, ulong Max)
        {
            var range_count = Math.Max(Min, Max) - Math.Min(Min, Max) + 1;
            var rnum = Rand64(range_count);
            return rnum + Math.Min(Min, Max);
        }

        public uint Rand32(uint Max = 0)
        {
            if (Max == 1) { return 0; }
            if (ctx.rng_count-- == 0) { isaac64(); ctx.rng_count = ISAAC64_SZ - 1; }

            uint r;

            if (ctx.banked32 > 0) { r = ctx.banked32; ctx.banked32 = 0; ctx.rng_count++;  return r; }

            r = Convert.ToUInt32(ctx.rng_buf[ctx.rng_count] & LOW32);
            ctx.banked32 = Convert.ToUInt32((ctx.rng_buf[ctx.rng_count] & HIGH32) >> 32);
            
            return (Max == 0) ? r : r % Max;
        }

        public uint RangedRand32(uint Min, uint Max)
        {
            var range_count = Math.Max(Min, Max) - Math.Min(Min, Max) + 1;
            var rnum = Rand32(range_count);
            return rnum + Math.Min(Min, Max);
        }

        public ushort Rand16(ushort Max = 0)
        {
            if (Max == 1) { return 0; }
            if (ctx.rng_count-- == 0) { isaac64(); ctx.rng_count = ISAAC64_SZ - 1; }

            ushort r;

            if (ctx.banked16.Count > 0) { ctx.rng_count++; return ctx.banked16.Pop(); }

            r = Convert.ToUInt16(ctx.rng_buf[ctx.rng_count] & 0x0000_0000_0000_FFFF);

            for (int i = 0; i < 3; i++)
                ctx.banked16.Push(Convert.ToByte((ctx.rng_buf[ctx.rng_count] & (ulong)(0xFFFF << ((i + 1) * 16)) >> ((i + 1) * 16))));

            return (Max == 0) ? r : (ushort)((uint)r % (uint)Max);
        }

        public ushort RangedRand16(ushort Min, ushort Max)
        {
            ushort range_count = (ushort)(Math.Max(Min, Max) - Math.Min(Min, Max) + 1);
            ushort rnum = Rand16(range_count);
            return (ushort)((uint)rnum + Math.Min(Min, Max));
        }

        public Byte Rand8(Byte Max = 0)
        {
            if (Max == 1) { return 0; }
            if (ctx.rng_count-- == 0) { isaac64(); ctx.rng_count = ISAAC64_SZ - 1; }

            Byte r;

            if (ctx.banked8.Count > 0) { ctx.rng_count++; return ctx.banked8.Pop(); }

            r = Convert.ToByte(ctx.rng_buf[ctx.rng_count] & 0x0000_0000_0000_00FF);
            
            for (int i = 0; i < 7; i++)
                ctx.banked8.Push(Convert.ToByte((ctx.rng_buf[ctx.rng_count] & (ulong)(0xFF << ((i + 1) * 8)) >> ((i + 1) * 8))));

            return (Max == 0) ? r : (Byte)((uint)r % (uint)Max);
        }

        public Byte RangedRand8(Byte Min, Byte Max)
        {
            Byte range_count = (Byte)(Math.Max(Min, Max) - Math.Min(Min, Max) + 1);
            Byte rnum = Rand8(range_count);
            return (Byte)((uint)rnum + Math.Min(Min, Max));
        }
    }
}

/* Reference implementation from Bob Jenkins: https://burtleburtle.net/bob/rand/isaacafa.html
 * 
 * Expected outputs from Rust core lib: https://docs.rs/rand_isaac/latest/src/rand_isaac/isaac64.rs.html
 *   - This version matches the expected outputs with the keys provided by the Rust team for 64-bit and 
 *     32-bit unsigned ints, both seeded and unseeded (see below).  Also matches Rust's 10,000k ignore
 *     test and verify on 10,001st 64-bit rng pull with seeded input.
 *     
 * Expected outputs from Zig std library: https://github.com/ziglang/zig/blob/master/lib/std/rand/Isaac64.zig
 *   - Zig std code provides the same output as below
 *   
 * Output from this project, with an unitialized seed (matches isaac64.c from Bob Jenkins, Rust core & zig std):
 * 
 * Rng #0: 0xf67dfba498e4937c
 * Rng #1: 0x84a5066a9204f380
 * Rng #2: 0xfee34bd5f5514dbb
 * Rng #3: 0x4d1664739b8f80d6
 * Rng #4: 0x8607459ab52a14aa
 * Rng #5: 0xe78bc5a98529e49
 * Rng #6: 0xfe5332822ad13777
 * Rng #7: 0x556c27525e33d01a
 * Rng #8: 0x8643ca615f3149f
 * Rng #9: 0xd0771faf3cb04714
 * Rng #10: 0x30e86f68a37b008d
 * Rng #11: 0x3074ebc0488a3adf
 * Rng #12: 0x270645ea7a2790bc
 * Rng #13: 0x5601a0a8d3763c6a
 * Rng #14: 0x2f83071f53f325dd
 * Rng #15: 0xb9090f3d42d2d2ea
 * 
 * Output from this project, seeded, 32-bit (see Rust core lib):
 * 
 * byte[] seed = { 1,0,0,0,   0,0,0,0, 23,0,0,0,   0,0,0,0,
 *                 200,1,0,0, 0,0,0,0, 210,30,0,0, 0,0,0,0 };
 * 
 * Rng #0: 3477963620
 * Rng #1: 3509106075
 * Rng #2: 687845478
 * Rng #3: 1797495790
 * Rng #4: 227048253
 * Rng #5: 2523132918
 * Rng #6: 4044335064
 * Rng #7: 1260557630
 * Rng #8: 4079741768
 * Rng #9: 3001306521
 * Rng #10: 69157722
 * Rng #11: 3958365844
 */
