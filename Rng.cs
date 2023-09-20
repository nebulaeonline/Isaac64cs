using System.Runtime.InteropServices;

namespace Isaac64
{
    public class Rng
    {
        // size constants
        private const int ISAAC64_WORD_SZ = 8;
        private const int ISAAC64_SZ_64 = (int)(1 << ISAAC64_WORD_SZ);
        private const int ISAAC64_SZ_8  = (int)(ISAAC64_SZ_64 << 2);
        private const ulong IND_MASK = (ulong)(((ISAAC64_SZ_64) - 1) << 3);
        private const ulong HIGH32 = 0xFFFF_FFFF_0000_0000;
        private const ulong LOW32 = 0x0000_0000_FFFF_FFFF;

        // banked randoms
        private Stack<uint> banked32 = new Stack<uint>();
        private Stack<ushort> banked16 = new Stack<ushort>();
        private Stack<byte> banked8 = new Stack<byte>();

        // for mix
        private static readonly int[] MIX_SHIFT = { 9, 9, 23, 15, 14, 20, 17, 14 };

        // state & random data class
        private class Context
        {
            // randrsl
            internal ulong[] rng_buf = new ulong[ISAAC64_SZ_64];

            // mm
            internal ulong[] rng_state = new ulong[ISAAC64_SZ_64];

            // aa, bb, cc
            internal ulong aa, bb, cc;

            // randcnt
            internal int rngbuf_curptr;            
        }

        // create a context
        private Context ctx = new Context();

        /// <summary>
        /// Rng() constructs the rng object and seeds the rng
        /// This variant of the constructor uses the System.Security.Cryptography.RandomNumberGenerator
        /// component to get 2048 bytes of random data to seed the RNG.
        /// </summary>
        /// <returns>the constructed & seeded rng</returns>
        public Rng()
        {
            var seed = new byte[ISAAC64_SZ_8];
            seed = System.Security.Cryptography.RandomNumberGenerator.GetBytes(ISAAC64_SZ_8);
            Reseed(seed);
        }

        /// <summary>
        /// Rng() constructs the rng object and seeds the rng
        /// This variant of the constructor is for testing
        /// </summary>
        /// <param name="Testing">bool Testing - for testing purposes; if false, this will throw</param>
        /// <returns>the constructed & seeded rng</returns>
        public Rng(bool Testing)
        {
            Reseed(0, true);
        }

        /// <summary>
        /// Rng() constructs the rng object and seeds the rng
        /// </summary>
        /// <param name="Seedbytes">byte[] SeedBytes - the seed, as an array of bytes, to use to seed the rng</param>
        /// <param name="IgnoreZeroAndOverSZ8Bytes">bool IgnoreZeroAndOverSZ8Bytes - don't throw an exception if a zero sized or an oversized array is passed</param>
        /// <returns>the constructed & seeded rng</returns>
        public Rng(byte[] Seedbytes, bool IgnoreZeroAndOverSZ8Bytes = false)
        {
            Reseed(Seedbytes, IgnoreZeroAndOverSZ8Bytes);
        }

        /// <summary>
        /// Rng() constructs the rng object and seeds the rng
        /// </summary>
        /// <param name="SeedULongs">ulong[] SeedULongs - the seed, as an array of 64-bit unsigned numbers, to use to seed the rng</param>
        /// <param name="IgnoreZeroAndOverSZ64Longs">bool IgnoreZeroAndOverSZ64Longs - don't throw an exception if a zero size or an oversized array is passed</param>
        /// <returns>the constructed & seeded rng</returns>
        public Rng(ulong[] SeedULongs, bool IgnoreZeroAndOverSZ64Longs = false)
        {
            Reseed(SeedULongs, IgnoreZeroAndOverSZ64Longs);
        }

        /// <summary>
        /// Rng() constructs the rng object and seeds the rng
        /// </summary>
        /// <param name="NumericSeed">ulong NumericSeed - the seed to use to seed the rng</param>
        /// <param name="IgnoreZeroSeed">bool IgnoreZeroSeed - don't throw an exception if seeding with 0</param>
        /// <returns>the constructed & seeded rng</returns>
        public Rng(ulong NumericSeed, bool IgnoreZeroSeed = false)
        {
            Reseed(NumericSeed, IgnoreZeroSeed);
        }

        /// <summary>
        /// Shuffle() mixes and re-shuffles the seed data and re-populates the rng array; it does not re-seed anything
        /// </summary>
        /// <returns>none</returns>
        public void Shuffle()
        {
            isaac64();
            reset_curptr();
        }

        /// <summary>
        /// Reseed() reseeds the rng
        /// </summary>
        /// <param name="Seedbytes">byte[] SeedBytes - the seed, as an array of bytes, to use to seed the rng</param>
        /// <param name="IgnoreZeroAndOverSZ8Bytes">bool IgnoreZeroAndOverSZ8Bytes - don't throw an exception if a zero or oversized array is passed</param>
        /// <returns>none</returns>
        public void Reseed(byte[] Seedbytes, bool IgnoreZeroAndOverSZ8Bytes = false)
        {
            if (!IgnoreZeroAndOverSZ8Bytes && (Seedbytes.Length > ISAAC64_SZ_8 || Seedbytes.Length == 0))
                throw new ArgumentException($"Cannot seed ISAAC64 with zero or more than {ISAAC64_SZ_8} bytes! To pass a zero array size or an array size > {ISAAC64_SZ_8}, set IgnoreZeroAndOverSZ8Bytes to true.");

            if (Seedbytes.Length == 0)
            {
                Reseed(0, IgnoreZeroAndOverSZ8Bytes);
                return;
            }

            clear_state();

            for (int i = 0; i < Seedbytes.Length; i++)
            {
                if (i % 8 == 0)
                    ctx.rng_buf[i / 8] = 0;

                ctx.rng_buf[i / 8] |= ((ulong)Seedbytes[i] << ((i % 8) * 8));
            }
            init();
        }

        /// <summary>
        /// Reseed() reseeds the rng
        /// </summary>
        /// <param name="SeedULongs">ulong[] SeedULongs - the seed, as an array of 64-bit unsigned numbers, to use to seed the rng</param>
        /// <param name="IgnoreZeroAndOverSZ64Longs">bool IgnoreZeroAndOverSZ64Longs - don't throw an exception if an oversized array is passed</param>
        /// <returns>none</returns>
        public void Reseed(ulong[] SeedULongs, bool IgnoreZeroAndOverSZ64Longs = false)
        {
            if (!IgnoreZeroAndOverSZ64Longs && (SeedULongs.Length > ISAAC64_SZ_64 || SeedULongs.Length == 0))
                throw new ArgumentException($"Cannot seed ISAAC64 with zero or more than {ISAAC64_SZ_64} ulongs! To pass a zero array size or an array size > {ISAAC64_SZ_64}, set IgnoreZeroAndOverSZ64Longs to true.");


            clear_state();

            int sl = (SeedULongs.Length > ISAAC64_SZ_64) ? ISAAC64_SZ_64 : SeedULongs.Length;
            for (int i = 0; i < sl; i++)
                ctx.rng_buf[i] = SeedULongs[i];

            init();
        }

        /// <summary>
        /// Reseed() reseeds the rng
        /// </summary>
        /// <param name="NumericSeed">ulong NumericSeed - the seed to use to seed the rng</param>
        /// <returns>none</returns>
        public void Reseed(ulong NumericSeed, bool IgnoreZeroSeed = false)
        {
            clear_state();
            if (IgnoreZeroSeed && NumericSeed == 0)
                init(true);
            else
            {
                ctx.rng_buf[0] = NumericSeed;
                init();
            }
        }

        // clear the rng state
        private void clear_state()
        {
            for (int i = 0; i < ISAAC64_SZ_64; i++) ctx.rng_state[i] = (ulong)0;
        }

        // sets the curptr in the rng_buf back to max
        private void reset_curptr()
        {
            ctx.rngbuf_curptr = ISAAC64_SZ_64;
        }

        // decrements the curptr if possible, if not, shuffles
        private void dec_curptr()
        {
            ctx.rngbuf_curptr--;
            if (ctx.rngbuf_curptr <= 0)
            {
                Shuffle();
                ctx.rngbuf_curptr--;
            }
        }

        // Helper method for the isaac64() method
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
            ctx.rng_buf[rng_idx++] = b = ind(y >> ISAAC64_WORD_SZ) + x;
        }

        // Helper method for the isaac64() method
        private ulong ind(ulong x)
        {
            int index = (int)(x & IND_MASK) / 8;
            return ctx.rng_state[index];
        }

        // Helper method for the isaac64() & init() methods
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

        // internal shuffle
        private void isaac64()
        {
            ulong a, b, x, y;
            x = y = 0;

            int state_idx1, state_idx2, rng_idx, end_idx;
            rng_idx = 0;

            a = ctx.aa;
            b = ctx.bb + (++ctx.cc);

            for (state_idx1 = 0, end_idx = state_idx2 = (ISAAC64_SZ_64 / 2); state_idx1 < end_idx;)
                for (int i = 0; i < 4; i++)
                    rng_step(ref state_idx1, ref state_idx2, ref rng_idx, ref a, ref b, ref x, ref y);

            for (state_idx2 = 0; state_idx2 < end_idx;)
                for (int i = 0; i < 4; i++)
                    rng_step(ref state_idx1, ref state_idx2, ref rng_idx, ref a, ref b, ref x, ref y);

            ctx.bb = b;
            ctx.aa = a;
        }

        // internal rng init
        private void init(bool Zero = false)
        {
            int i;

            /* No need to waste the time on every update
             * 
             * const ulong MAGIC = 0x9E3779B97F4A7C13;
             * 
             * 
             *      ulong[] x = { MAGIC, MAGIC,
             *                    MAGIC, MAGIC,
             *                    MAGIC, MAGIC,
             *                    MAGIC, MAGIC };
             *
             *      ctx.aa = ctx.bb = ctx.cc = 0;
             *
             *      for (i = 0; i < 4; i++)
             *          mix(ref x);
            */

            // Save the 4 rounds of mix'ing MAGIC
            ulong[] x = { 0x647c4677a2884b7c, 0xb9f8b322c73ac862,
                          0x8c0ea5053d4712a0, 0xb29b2e824a595524,
                          0x82f053db8355e0ce, 0x48fe4a0fa5a09315,
                          0xae985bf2cbfc89ed, 0x98f5704f6c44c0ab };

            for (i = 0; i < ISAAC64_SZ_64; i += 8)
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
                for (i = 0; i < ISAAC64_SZ_64; i += 8)
                { 
                    for (int j = 0; j < 8; j++)
                        x[j] += ctx.rng_state[i + j];

                    mix(ref x);

                    for (int j = 0; j < 8; j++)
                        ctx.rng_state[i + j] = x[j];
                }
            }

            isaac64();
            reset_curptr();
        }

        /// <summary>
        /// Rand64() returns an unsigned 64-bit integer in the range [0, Max] (inclusive)
        /// </summary>
        /// <param name="Max">ulong Max - the maximum random number to return</param>
        /// <returns>ulong</returns>
        public ulong Rand64(ulong Max = 0)
        {
            if (Max == ulong.MaxValue) Max = 0;

            // enough bytes?
            dec_curptr();

            ulong ul = ctx.rng_buf[ctx.rngbuf_curptr];
            return (Max == 0) ? ul : ul % ++Max;
        }

        /// <summary>
        /// RangedRand64() returns an unsigned 64-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">ulong Min - the minimum random number to return</param>
        /// <param name="Max">ulong Max - the maximum random number to return</param>
        /// <returns>ulong</returns>
        public ulong RangedRand64(ulong Min, ulong Max)
        {
            if (Min == Max) { return Min; }

            var rmax = Math.Max(Min, Max) - Math.Min(Min, Max);
            var r = Rand64(rmax);
            return r + Math.Min(Min, Max);
        }

        /// <summary>
        /// RangedRand64S() returns a signed 64-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">ulong Min - the minimum random number to return</param>
        /// <param name="Max">ulong Max - the maximum random number to return</param>
        /// <returns>long</returns>
        public long RangedRand64S(long Min, long Max)
        {
            if (Min == Max) { return Min; }

            ulong u1, u2;
            u1 = (ulong)Min;
            u2 = (ulong)Max;

            ulong r = RangedRand64(Math.Min(u1, u2), Math.Max(u1, u2));
            return (long)r;            
        }

        /// <summary>
        /// Rand32() returns an unsigned 32-bit integer in the range [0, Max] (inclusive)
        /// </summary>
        /// <param name="Max">uint Max - the maximum random number to return</param>
        /// <returns>uint</returns>
        public uint Rand32(uint Max = 0)
        {
            if (Max == uint.MaxValue) Max = 0;

            if (banked32.Count > 0)
            {
                return (Max == 0) ? banked32.Pop() : banked32.Pop() % ++Max;
            }
            dec_curptr();

            uint ui = (uint)(ctx.rng_buf[ctx.rngbuf_curptr] & LOW32);
            banked32.Push((uint)((ctx.rng_buf[ctx.rngbuf_curptr] & HIGH32) >> 32));

            return (Max == 0) ? ui : ui % ++Max;
        }

        /// <summary>
        /// RangedRand32() returns an unsigned 32-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">uint Min - the minimum random number to return</param>
        /// <param name="Max">uint Max - the maximum random number to return</param>
        /// <returns>uint</returns>
        public uint RangedRand32(uint Min, uint Max)
        {
            if (Min == Max) { return Min; }

            uint rmax = Math.Max(Min, Max) - Math.Min(Min, Max);
            uint r = Rand32(rmax);
            return r + Math.Min(Min, Max);
        }

        /// <summary>
        /// RangedRand32S() returns a signed 32-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">int Min - the minimum random number to return</param>
        /// <param name="Max">int Max - the maximum random number to return</param>
        /// <returns>int</returns>
        public int RangedRand32S(int Min, int Max)
        {
            if (Min == Max) { return Min; }

            uint u1, u2;
            u1 = (uint)Min;
            u2 = (uint)Max;

            uint r = RangedRand32(Math.Min(u1, u2), Math.Max(u1, u2));
            return (int)r;
        }

        /// <summary>
        /// Rand16() returns an unsigned 16-bit integer in the range [0, Max] (inclusive)
        /// </summary>
        /// <param name="Max">ushort Max - the maximum random number to return</param>
        /// <returns>ushort</returns>
        public ushort Rand16(ushort Max = 0)
        {
            if (Max == ushort.MaxValue) Max = 0;

            if (banked16.Count > 0)
            {
                return (Max == 0) ? banked16.Pop() : (ushort)(banked16.Pop() % ++Max);
            }
            dec_curptr();

            ushort us = Convert.ToUInt16(ctx.rng_buf[ctx.rngbuf_curptr] & 0x0000_0000_0000_FFFF);

            for (int i = 0; i < 3; i++)
                banked16.Push(Convert.ToByte((ctx.rng_buf[ctx.rngbuf_curptr] & (ulong)(0xFFFF << ((i + 1) * 16)) >> ((i + 1) * 16))));

            return (Max == 0) ? us : (ushort)((uint)us % ++Max);
        }

        /// <summary>
        /// RangedRand16() returns an unsigned 16-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">ushort Min - the minimum random number to return</param>
        /// <param name="Max">ushort Max - the maximum random number to return</param>
        /// <returns>ushort</returns>
        public ushort RangedRand16(ushort Min, ushort Max)
        {
            if (Min == Max) { return Min; }

            ushort rmax = (ushort)(Math.Max(Min, Max) - Math.Min(Min, Max));
            ushort r = Rand16((ushort)rmax);
            return (ushort)(r + Math.Min(Min, Max));
        }

        /// <summary>
        /// RangedRand16S() returns a signed 16-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">short Min - the minimum random number to return</param>
        /// <param name="Max">short Max - the maximum random number to return</param>
        /// <returns>short</returns>
        public short RangedRand16S(short Min, short Max)
        {
            if (Min == Max) { return Min; }

            ushort u1, u2;
            u1 = (ushort)Min;
            u2 = (ushort)Max;

            ushort r = RangedRand16(Math.Min(u1, u2), Math.Max(u1, u2));
            return (short)r;
        }

        /// <summary>
        /// Rand8() returns an unsigned 8-bit integer in the range [0, Max] (inclusive)
        /// </summary>
        /// <param name="Max">byte Max - the maximum random number to return</param>
        /// <returns>byte</returns>
        public byte Rand8(byte Max = 0)
        {
            if (Max == byte.MaxValue) Max = 0;

            if (banked8.Count > 0)
            {
                return (Max == 0) ? banked8.Pop() : (byte)(banked8.Pop() % ++Max);
            }
            dec_curptr();

            byte ub = Convert.ToByte(ctx.rng_buf[ctx.rngbuf_curptr] & 0x0000_0000_0000_00FF);

            for (int i = 0; i < 7; i++)
                banked8.Push(Convert.ToByte((ctx.rng_buf[ctx.rngbuf_curptr] & (ulong)(0xFF << ((i + 1) * 8)) >> ((i + 1) * 8))));

            return (Max == 0) ? ub : (byte)((uint)ub % ++Max);
        }

        /// <summary>
        /// RangedRand8() returns an unsigned 8-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">byte Min - the minimum random number to return</param>
        /// <param name="Max">byte Max - the maximum random number to return</param>
        /// <returns>byte</returns>
        public byte RangedRand8(byte Min, byte Max)
        {
            if (Min == Max) { return Min; }

            byte rmax = (byte)(Math.Max(Min, Max) - Math.Min(Min, Max));
            byte r = Rand8(rmax);
            return (byte)(r + Math.Min(Min, Max));
        }

        /// <summary>
        /// RangedRand8S() returns a signed 8-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">sbyte Min - the minimum random number to return</param>
        /// <param name="Max">sbyte Max - the maximum random number to return</param>
        /// <returns>sbyte</returns>
        public sbyte RangedRand8S(sbyte Min, sbyte Max)
        {
            if (Min == Max) { return Min; }

            sbyte u1, u2;
            u1 = (sbyte)Min;
            u2 = (sbyte)Max;

            byte r = RangedRand8((byte)Math.Min(u1, u2), (byte)Math.Max(u1, u2));
            return (sbyte)r;
        }

        /// <summary>
        /// RandAlphaNum() returns a char conforming to the specified options
        /// </summary>
        /// <param name="AlphaUpper">bool AlphaUpper - include upper case alphas?</param>
        /// <param name="AlphaLower">bool AlphaLower - include lower case alphas?</param>
        /// <param name="Numeric">bool Numeric -    include numeric characters?</param>
        /// <returns>char</returns>
        public char RandAlphaNum(bool AlphaUpper = true, bool AlphaLower = true, bool Numeric = true)
        {
            if (!AlphaUpper && !AlphaLower && !Numeric) { throw new ArgumentException("You must select at least one character class in RandChar()"); }
            
            const byte NUMERIC = 0x30;
            const byte UALPHA = 0x41;
            const byte LALPHA = 0x61;

            byte rcnt = (Numeric) ? (byte)10 : (byte)0;

            if (AlphaUpper) { rcnt += (byte)26; }
            if (AlphaLower) { rcnt += (byte)26; }

            byte rnd = Rand8(--rcnt);

            if (Numeric && rnd < 10)
                return Convert.ToChar(rnd + NUMERIC);
            else if (Numeric)
                rnd -= 10;

            if (AlphaUpper && rnd < 26)
                return Convert.ToChar(rnd + UALPHA);
            else if (AlphaUpper)
                rnd -= 26;

            return Convert.ToChar(rnd + LALPHA);
        }

        /// <summary>
        /// RandDouble() returns a double between (0.0, 1.0) (non-inclusive)
        /// </summary>
        /// <returns>double</returns>
        public double RandDouble()
        {
            return RandDoubleRaw(1.0, 2.0, 1.0e-3) - 1;
        }

        // RandDoubleRaw() handles subnormals, but Min & Max must both
        // be subnormal or not subnormal.  You cannot generate randoms
        // between a subnormal and a regular double, we will throw.
        //
        // It is highly recommended to experiment with these ranges,
        // since ieee754 double precision floating point has some 
        // idiosyncracies.  Too small a zero, and the random range
        // becomes too large, and the results are poorly distributed,
        // tending to cluster around 0.
        //
        // Random subnormals only work well when their exponents
        // are the same or only 1 order of magnitude apart.
        //
        // For fp64 encoding info see:
        // https://en.wikipedia.org/wiki/Double-precision_floating-point_format
        //
        // Ranges (negative or positive):
        //
        //      2.2250738585072014 × 10^−308 (Min normal double) to
        //      1.7976931348623157 × 10^308  (Max normal double)
        //
        // and
        //
        //      4.9406564584124654 × 10^−324 (Min subnormal double) to
        //      2.2250738585072009 × 10^−308 (Max subnormal double)
        //
        // Other relevant info about ieee-754 double precision numbers:
        //
        //      18,446,744,073,709,551,615 UInt64.Max
        //      -9,223,372,036,854,775,808 Int64.Min
        //       9,223,372,036,854,775,807 Int64.Max
        //
        //      −9,007,199,254,740,992  Double Min Integer Exactly Representable
        //       9,007,199,254,740,992  Double Max Integer Exactly Representable
        //      18,014,398,509,481,984  Double Max Integer Representable By 2x
        //      36,028,797,018,963,968  Double Max Integer Representable By 4x
        //      Integers 2^n to 2^(n+1)                    Representable By 2n^(-52)x
        //
        // MinZero is the practical minimum when zero is part of the range
        // MinZero is +0 for subnormals
        public double RandDoubleRaw(double Min, double Max, double MinZero = 1e-3)
        {
            // No NaNs or INF
            if (double.IsNaN(Min) || double.IsNaN(Max) ||
                double.IsInfinity(Min) || double.IsInfinity(Max))
                throw new Exception("You cannot use infinities or NaNs for Min or Max!");

            // Both normal or both subnormal required
            if (!(double.IsSubnormal(Min) == double.IsSubnormal(Max)))
                throw new Exception("You cannot mix subnormal and normal doubles for Min & Max!");

            // Swap Min, Max if necessary
            // Easier to reason about if we know
            // d1 <= d2 ALWAYS
            if (Min > Max) (Min, Max) = (Max, Min);

            // Adjust MinZero if dealing with subnormals
            bool sn = double.IsSubnormal(Min);
            if (sn) MinZero = +0;
            else
            {
                // No subnormal, so see if the Abs(Min) is less
                // than MinZero or if Min == 0.0
                double absmin = Math.Abs(Min);

                if (absmin != 0.0 && absmin < MinZero)
                    MinZero = absmin;
                else if (Min == 0.0)
                    Min = MinZero;
            }
                        
            Dub mz = new Dub(MinZero);
            Dub d1 = new Dub(Min);
            Dub d2 = new Dub(Max);

            // First, figure out what our sign is going to be
            // ->both ++ then +, both -- then -, one of each then random
            bool rnd_neg = (d1.IsNeg == d2.IsNeg) ? d1.IsNeg : (RangedRand16S(short.MinValue, short.MaxValue) < 0);
            bool same_sign = (d1.IsNeg == d2.IsNeg);

            // Default to 0s for subnormals (exp is always 0)
            uint exp_min = 0;
            uint exp_max = 0;
            uint new_exp = 0;

            // Generate a new exponent if necessary
            if (!sn)
            {
                // New Exponent Range
                exp_min = Dub.EXP_MIN;
                exp_max = Dub.EXP_MAX;

                if (same_sign)
                {
                    // if they are the same sign, we don't really care that
                    // min has a greater exp than max if they're negative,
                    // because the random functions adjust on the fly
                    exp_min = d1.Exp;
                    exp_max = d2.Exp;
                }
                else
                {
                    // they are not the same sign; the min exp will
                    // always be MinZero, however because Min and
                    // Max straddle the zero line we have to check
                    // which one we're randomizing toward
                    exp_min = mz.Exp;
                    exp_max = (rnd_neg) ? d1.Exp : d2.Exp;
                }

                new_exp = RangedRand32(exp_min, exp_max);
            }
            
            // Work on the fractional part
            ulong frac_min = (ulong)0;
            ulong frac_max = Dub.FRAC_BITS;

            // We only need to mess with the fraction ranges
            // if the exponents are equal
            if (new_exp == exp_min || new_exp == exp_max)
            {
                if (same_sign)
                {
                    if (exp_min == exp_max)
                    {
                        frac_min = d1.Frac;
                        frac_max = d2.Frac;
                    }
                    else
                    {
                        if (new_exp == exp_min)
                            frac_min = d1.Frac;
                        else
                            frac_max = d2.Frac;
                    }
                }
                else
                {
                    if (rnd_neg)
                        frac_max = d1.Frac;
                    else
                        frac_max = d2.Frac;
                }

                // For integer powers of 2^n
                if (frac_max == 0) { new_exp--; frac_min = (ulong)0; frac_max = Dub.FRAC_BITS; }
            }

            // Build the new double
            ulong new_frac = RangedRand64(frac_min, frac_max);
            ulong d = new_frac;
            if (rnd_neg) d |= Dub.SIGN_BIT;
            d |= ((ulong)new_exp << 52);

            return BitConverter.UInt64BitsToDouble(d);            
        }

        // Dub - work with doubles
        public class Dub
        {
            public static readonly int EXP_BIAS = 0x3ff;
            public static readonly uint EXP_MIN = 1;
            public static readonly uint EXP_MAX = 0x7fe;
            public static readonly ulong SIGN_BIT = ((ulong)1 << 63);
            public static readonly ulong FRAC_BITS = (ulong)0xF_FFFF_FFFF_FFFF;

            private bool _neg;
            private uint _exp;
            private ulong _frac;

            public bool IsNeg { get { return _neg; } }
            public uint Exp { get { return _exp; } }
            public bool HasNegExp { get { return (_exp < EXP_BIAS); } }
            public int UnbiasedExp { get { return (int)_exp - EXP_BIAS; } }
            public ulong Frac { get { return _frac; } }
            
            public Dub(double InDub)
            {
                ulong db = System.BitConverter.DoubleToUInt64Bits(InDub);
                
                _neg = (db & SIGN_BIT) != 0;
                _exp = (uint)(((db & ~SIGN_BIT) & ~FRAC_BITS) >> 52);
                _frac = (db & FRAC_BITS);
            }            
        }
    }
}

/* ISAAC64 Reference implementation from Bob Jenkins: https://burtleburtle.net/bob/rand/isaacafa.html
 * 
 * Expected outputs from Rust core lib: https://docs.rs/rand_isaac/latest/src/rand_isaac/isaac64.rs.html
 *   - This version matches the expected outputs with the keys provided by the Rust team for 64-bit and 
 *     32-bit unsigned ints, both seeded and unseeded (see below).  Also matches Rust's 10k ignore
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
