using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Isaac64
{
    public class Rng
    {
        // size constants
        private const int ISAAC64_WORD_SZ = 8;
        private const int ISAAC64_SZ_64 = (int)(1 << ISAAC64_WORD_SZ);
        private const int ISAAC64_SZ_8 = (int)(ISAAC64_SZ_64 << 2);
        private const ulong IND_MASK = (ulong)(((ISAAC64_SZ_64) - 1) << 3);
        private const ulong HIGH32 = 0xFFFF_FFFF_0000_0000;
        private const ulong LOW32 = 0x0000_0000_FFFF_FFFF;
        private const ulong LOW16 = 0x0000_0000_0000_FFFF;
        private const ulong LOW8 = 0x0000_0000_0000_00FF;

        // banked randoms
        private ConcurrentStack<uint> banked32 = new ConcurrentStack<uint>();
        private ConcurrentStack<ushort> banked16 = new ConcurrentStack<ushort>();
        private ConcurrentStack<byte> banked8 = new ConcurrentStack<byte>();

        // concurrency lock
        private readonly object _lock = new object();

        // for mix
        private static readonly int[] MIX_SHIFT = { 9, 9, 23, 15, 14, 20, 17, 14 };

        // state & random data class
        public class Context
        {
            // randrsl
            internal ulong[] rng_buf = new ulong[ISAAC64_SZ_64];

            // mm
            internal ulong[] rng_state = new ulong[ISAAC64_SZ_64];

            // aa, bb, cc
            internal ulong aa, bb, cc;

            // randcnt
            internal int rngbuf_curptr;

            // allows to clone the context for simulation
            // and testing purposes
            internal Context Clone()
            {
                return new Context
                {
                    aa = this.aa,
                    bb = this.bb,
                    cc = this.cc,
                    rngbuf_curptr = this.rngbuf_curptr,
                    rng_buf = (ulong[])this.rng_buf.Clone(),
                    rng_state = (ulong[])this.rng_state.Clone()
                };
            }
        }

        // create a context
        private Context ctx = new Context();

        /// <summary>
        /// Clone() clones the internal context of the rng object and returns a new rng object
        /// This is useful to split the same rng object into multiple rng objects to take
        /// two different paths, but maintain the same repeatability if using known
        /// seeds. This is useful for testing & simulation purposes.
        /// </summary>
        /// <returns>A newly constructed rng with the same internal state as the instance
        /// Clone() was called on</returns>
        public Rng Clone()
        {
            Rng copy;

            lock (_lock)
            {
                copy = new Rng(true); // testing constructor; does not reseed
                copy.ctx = this.ctx.Clone(); // manually assign copied context
                copy.banked8 = new ConcurrentStack<byte>(this.banked8);
                copy.banked16 = new ConcurrentStack<ushort>(this.banked16);
                copy.banked32 = new ConcurrentStack<uint>(this.banked32);
            }
            return copy;
        }

        /// <summary>
        /// Rng() constructs the rng object and seeds the rng
        /// This variant of the constructor uses the System.Security.Cryptography.RandomNumberGenerator
        /// component to get 2048 bytes of random data to seed the RNG.
        /// </summary>
        /// <returns>the constructed & seeded rng</returns>
        public Rng()
        {
            var seed = new byte[ISAAC64_SZ_8];
#if NET6_0_OR_GREATER
            seed = System.Security.Cryptography.RandomNumberGenerator.GetBytes(ISAAC64_SZ_8);
#else
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(seed);
            }
#endif
                Reseed(seed);
        }

        /// <summary>
        /// Rng() constructs the rng object and seeds the rng
        /// This variant of the constructor is for testing
        /// </summary>
        /// <param name="Testing">bool Testing - for testing purposes; if false, this will throw</param>
        /// <returns>the constructed & seeded rng</returns>
        public Rng(bool Testing = false)
        {
            Reseed(0, Testing);
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
            lock (_lock)
            {
                isaac64();
                reset_curptr();
            }
        }

        /// <summary>
        /// Reseed() reseeds the rng
        /// </summary>
        /// <param name="Seedbytes">byte[] SeedBytes - the seed, as an array of bytes, to use to seed the rng</param>
        /// <param name="IgnoreZeroAndOverSZ8Bytes">bool IgnoreZeroAndOverSZ8Bytes - don't throw an exception if a zero or oversized array is passed</param>
        /// <returns>none</returns>
        public void Reseed(byte[] Seedbytes, bool IgnoreZeroAndOverSZ8Bytes = false)
        {
            lock (_lock)
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
        }

        /// <summary>
        /// Reseed() reseeds the rng
        /// </summary>
        /// <param name="SeedULongs">ulong[] SeedULongs - the seed, as an array of 64-bit unsigned numbers, to use to seed the rng</param>
        /// <param name="IgnoreZeroAndOverSZ64Longs">bool IgnoreZeroAndOverSZ64Longs - don't throw an exception if an oversized array is passed</param>
        /// <returns>none</returns>
        public void Reseed(ulong[] SeedULongs, bool IgnoreZeroAndOverSZ64Longs = false)
        {
            lock (_lock)
            {
                if (!IgnoreZeroAndOverSZ64Longs && (SeedULongs.Length > ISAAC64_SZ_64 || SeedULongs.Length == 0))
                    throw new ArgumentException($"Cannot seed ISAAC64 with zero or more than {ISAAC64_SZ_64} ulongs! To pass a zero array size or an array size > {ISAAC64_SZ_64}, set IgnoreZeroAndOverSZ64Longs to true.");


                clear_state();

                int sl = (SeedULongs.Length > ISAAC64_SZ_64) ? ISAAC64_SZ_64 : SeedULongs.Length;
                for (int i = 0; i < sl; i++)
                    ctx.rng_buf[i] = SeedULongs[i];

                init();
            }
        }

        /// <summary>
        /// Reseed() reseeds the rng
        /// </summary>
        /// <param name="NumericSeed">ulong NumericSeed - the seed to use to seed the rng</param>
        /// <returns>none</returns>
        public void Reseed(ulong NumericSeed, bool IgnoreZeroSeed = false)
        {
            lock (_lock)
            {
                clear_state();
                if (IgnoreZeroSeed && NumericSeed == 0)
                    init(true);
                else if (NumericSeed == 0)
                    throw new ArgumentException("Rng seeded with 0 value. Set the IgnoreZeroSeed parameter if this behavior is desired.");
                else
                {
                    ctx.rng_buf[0] = NumericSeed;
                    init();
                }
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
            if (--ctx.rngbuf_curptr < 0)
            {
                Shuffle();
                ctx.rngbuf_curptr = ISAAC64_SZ_64 - 1; // explicit
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

            //No need to waste the time on every update

            /*const ulong MAGIC = 0x9E3779B97F4A7C13;


           ulong[] x = { MAGIC, MAGIC,
                         MAGIC, MAGIC,
                         MAGIC, MAGIC,
                         MAGIC, MAGIC };



           for (i = 0; i < 4; i++)
               mix(ref x);*/

            // Save the 4 rounds of mix'ing MAGIC
            ulong[] x = { 0x647c4677a2884b7c, 0xb9f8b322c73ac862,
                          0x8c0ea5053d4712a0, 0xb29b2e824a595524,
                          0x82f053db8355e0ce, 0x48fe4a0fa5a09315,
                          0xae985bf2cbfc89ed, 0x98f5704f6c44c0ab };

            ctx.aa = ctx.bb = ctx.cc = 0;

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
        /// Interface that mimics System.Random
        /// Returns a non-negative random integer (32-bit).
        /// </summary>
        public int Next()
        {
            return (int)(Rand32() & 0x7FFFFFFF); // Strip sign bit to mimic System.Random
        }

        /// <summary>
        /// Interface that mimics System.Random
        /// Returns a non-negative random integer less than max.
        /// </summary>
        public int Next(int max)
        {
            if (max <= 0)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be positive");

            return (int)(Rand32((uint)(max - 1)) & 0x7FFFFFFF);
        }

        /// <summary>
        /// Interface that mimics System.Random
        /// Returns a random integer between min (inclusive) and max (exclusive).
        /// </summary>
        public int Next(int min, int max)
        {
            if (min > max)
                (max, min) = (min, max);

            if (min == max)
                return min;

            return RangedRand32S(min, max - 1);
        }

        /// <summary>
        /// Rand64() returns an unsigned 64-bit integer in the range [0, Max] (inclusive)
        /// </summary>
        /// <param name="Max">ulong Max - the maximum random number to return</param>
        /// <returns>ulong</returns>
        public ulong Rand64(ulong Max = 0)
        {
            lock (_lock)
            {
                if (Max == ulong.MaxValue) Max = 0;

                // enough bytes?
                dec_curptr();

                ulong ul = ctx.rng_buf[ctx.rngbuf_curptr];
                return (Max == 0) ? ul : ul % ++Max;
            }
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
            if (Max < Min) { (Min, Max) = (Max, Min); }

            var rmax = Max - Min;
            var r = Rand64(rmax);
            return r + Min;
        }

        /// <summary>
        /// RangedRand64S() returns a signed 64-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">long Min - the minimum random number to return</param>
        /// <param name="Max">long Max - the maximum random number to return</param>
        /// <returns>long</returns>
        public long RangedRand64S(long Min, long Max)
        {
            if (Min == Max) return Min;
            if (Min > Max) (Min, Max) = (Max, Min);

            ulong range = (ulong)(Max - Min);
            ulong rand = Rand64(range);
            return Min + (long)rand;
        }

        /// <summary>
        /// Rand32() returns an unsigned 32-bit integer in the range [0, Max] (inclusive)
        /// </summary>
        /// <param name="Max">uint Max - the maximum random number to return</param>
        /// <returns>uint</returns>
        public uint Rand32(uint Max = 0)
        {
            lock (_lock)
            {
                if (Max == uint.MaxValue) Max = 0;

                if (banked32.TryPop(out uint ui))
                    return (Max == 0) ? ui : ui % ++Max;

                dec_curptr();

                uint ui2 = (uint)(ctx.rng_buf[ctx.rngbuf_curptr] & LOW32);
                banked32.Push((uint)((ctx.rng_buf[ctx.rngbuf_curptr] & HIGH32) >> 32));

                return (Max == 0) ? ui2 : ui2 % ++Max;
            }
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
            if (Max < Min) { (Min, Max) = (Max, Min); }

            uint rmax = Max - Min;
            uint r = Rand32(rmax);
            return r + Min;
        }

        /// <summary>
        /// RangedRand32S() returns a signed 32-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">int Min - the minimum random number to return</param>
        /// <param name="Max">int Max - the maximum random number to return</param>
        /// <returns>int</returns>
        public int RangedRand32S(int Min, int Max)
        {
            if (Min == Max) return Min;
            if (Min > Max) (Min, Max) = (Max, Min);

            uint range = (uint)(Max - Min);
            uint rand = Rand32(range);
            return Min + (int)rand;
        }

        /// <summary>
        /// Rand16() returns an unsigned 16-bit integer in the range [0, Max] (inclusive)
        /// </summary>
        /// <param name="Max">ushort Max - the maximum random number to return</param>
        /// <returns>ushort</returns>
        public ushort Rand16(ushort Max = 0)
        {
            lock (_lock)
            {
                if (Max == ushort.MaxValue) Max = 0;

                if (banked16.TryPop(out ushort us))
                    return (Max == 0) ? us : (ushort)(us % ++Max);

                dec_curptr();

                ushort us2 = Convert.ToUInt16(ctx.rng_buf[ctx.rngbuf_curptr] & LOW16);

                for (int i = 0; i < 3; i++)
                    banked16.Push(Convert.ToUInt16((ctx.rng_buf[ctx.rngbuf_curptr] & (ulong)(LOW16 << ((i + 1) * 16))) >> ((i + 1) * 16)));

                return (Max == 0) ? us2 : (ushort)((uint)us2 % ++Max);
            }
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
            if (Max < Min) { (Min, Max) = (Max, Min); }

            ushort rmax = (ushort)(Max - Min);
            ushort r = Rand16((ushort)rmax);
            return (ushort)(r + Min);
        }

        /// <summary>
        /// RangedRand16S() returns a signed 16-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">short Min - the minimum random number to return</param>
        /// <param name="Max">short Max - the maximum random number to return</param>
        /// <returns>short</returns>
        public short RangedRand16S(short Min, short Max)
        {
            if (Min == Max) return Min;
            if (Min > Max) (Min, Max) = (Max, Min);

            ushort range = (ushort)(Max - Min);
            ushort rand = Rand16(range);
            return (short)(Min + rand);
        }

        /// <summary>
        /// Rand8() returns an unsigned 8-bit integer in the range [0, Max] (inclusive)
        /// </summary>
        /// <param name="Max">byte Max - the maximum random number to return</param>
        /// <returns>byte</returns>
        public byte Rand8(byte Max = 0)
        {
            lock (_lock)
            {
                if (Max == byte.MaxValue) Max = 0;

                if (banked8.TryPop(out byte ub))
                    return (Max == 0) ? ub : (byte)(ub % ++Max);

                dec_curptr();

                byte ub2 = Convert.ToByte(ctx.rng_buf[ctx.rngbuf_curptr] & LOW8);

                for (int i = 0; i < 7; i++)
                    banked8.Push(Convert.ToByte((ctx.rng_buf[ctx.rngbuf_curptr] & (ulong)(LOW8 << ((i + 1) * 8))) >> ((i + 1) * 8)));

                return (Max == 0) ? ub2 : (byte)((uint)ub2 % ++Max);
            }
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
            if (Max < Min) { (Min, Max) = (Max, Min); }

            byte rmax = (byte)(Max - Min);
            byte r = Rand8(rmax);
            return (byte)(r + Min);
        }

        /// <summary>
        /// RangedRand8S() returns a signed 8-bit integer in the range [Min, Max] (inclusive)
        /// </summary>
        /// <param name="Min">sbyte Min - the minimum random number to return</param>
        /// <param name="Max">sbyte Max - the maximum random number to return</param>
        /// <returns>sbyte</returns>
        public sbyte RangedRand8S(sbyte Min, sbyte Max)
        {
            if (Min == Max) return Min;
            if (Min > Max) (Min, Max) = (Max, Min);

            byte range = (byte)(Max - Min);
            byte rand = Rand8(range);
            return (sbyte)(Min + rand);
        }


        /// <summary>
        /// RandAlphaNum() returns a char conforming to the specified options
        /// </summary>
        /// <param name="Upper">bool AlphaUpper - include upper case alphas?</param>
        /// <param name="Lower">bool AlphaLower - include lower case alphas?</param>
        /// <param name="Numeric">bool Numeric -  include numeric characters?</param>
        /// <param name="ExtraSymbols">char[]? ExtraSymbols - any additional symbols? (pass as char array)</param>
        /// <returns>char</returns>
        public char RandAlphaNum(bool Upper = true, bool Lower = true, bool Numeric = true, char[] ExtraSymbols = null)
        {
            List<char> charset = new List<char>();

            if (Numeric)
                charset.AddRange("0123456789");
            if (Upper)
                charset.AddRange("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
            if (Lower)
                charset.AddRange("abcdefghijklmnopqrstuvwxyz");
            if (ExtraSymbols != null)
                charset.AddRange(ExtraSymbols);

            if (charset.Count == 0)
                throw new ArgumentException("You must enable at least one character group or pass custom symbols.");

            // Rejection sampling to remove bias
            byte rnd;
            int count = charset.Count;
            int max = 256 - (256 % count);

            do
            {
                rnd = Rand8();
            } while (rnd >= max);

            return charset[rnd % count];
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
        //  +/- 2.2250738585072014 × 10^−308 (Min normal double) to
        //  +/- 1.7976931348623157 × 10^308  (Max normal double)
        //
        // and
        //
        //  +/- 4.9406564584124654 × 10^−324 (Min subnormal double) to
        //  +/- 2.2250738585072009 × 10^−308 (Max subnormal double)
        //
        // Other relevant info about ieee-754 double precision numbers:
        //
        //     For reference:
        //      18,446,744,073,709,551,615 UInt64.Max
        //      -9,223,372,036,854,775,808 Int64.Min
        //       9,223,372,036,854,775,807 Int64.Max
        //
        //     Doubles:
        //      −9,007,199,254,740,992  Double Min Integer Exactly Representable
        //       9,007,199,254,740,992  Double Max Integer Exactly Representable
        //  +/- 18,014,398,509,481,984  Double Max Integer Representable By 2x (i.e. n mod 2 == 0)
        //  +/- 36,028,797,018,963,968  Double Max Integer Representable By 4x (i.e. n mod 4 == 0)
        //  +/- Integers 2^n to 2^(n+1)                    Representable By 2n^(-52)x
        //
        // MinZero is the practical minimum when zero is part of the range
        // MinZero is +0 for subnormals
        public double RandDoubleRaw(double Min, double Max, double MinZero = 1e-3)
        {
            // No NaNs or INF
            if (double.IsNaN(Min) || double.IsNaN(Max) ||
                double.IsInfinity(Min) || double.IsInfinity(Max))
                throw new ArgumentException("You cannot use infinities or NaNs for Min or Max! Use actual numeric double values instead.");

            // Both normal or both subnormal required
#if !NET5_0_OR_GREATER
            if (!(Dub.IsSubnormal(Min) == Dub.IsSubnormal(Max)))
#else
            if (!(double.IsSubnormal(Min) == double.IsSubnormal(Max)))
#endif
            throw new ArgumentException("You cannot mix subnormal and normal doubles for Min & Max! Choose both subnormals or both normals.");

            // Swap Min, Max if necessary
            // Easier to reason about if we know
            // d1 <= d2 ALWAYS
            if (Min > Max) (Min, Max) = (Max, Min);

            // Adjust MinZero if dealing with subnormals
#if !NET5_0_OR_GREATER
            bool sn = Dub.IsSubnormal(Min);
#else
            bool sn = double.IsSubnormal(Min);
#endif
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
#if NET6_0_OR_GREATER
            return BitConverter.UInt64BitsToDouble(d);
#else
            return BitConverter.ToDouble(BitConverter.GetBytes(d), 0);
#endif
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

#if !NET5_0_OR_GREATER
            public static bool IsSubnormal(double value)
            {
                if (value == 0.0) return false;

                ulong bits = BitConverter.ToUInt64(BitConverter.GetBytes(value), 0);
                uint exponent = (uint)((bits >> 52) & 0x7FF);   // Extract exponent bits
                ulong mantissa = bits & 0xFFFFFFFFFFFFF;        // Extract mantissa bits

                return exponent == 0 && mantissa != 0;
            }
#endif
            public Dub(double InDub)
            {
#if NET6_0_OR_GREATER
                ulong db = BitConverter.DoubleToUInt64Bits(InDub);
#else
                ulong db = BitConverter.ToUInt64(BitConverter.GetBytes(InDub), 0);
#endif

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
 * Expected outputs from Zig std library: https://ratfactor.com/zig/stdlib-browseable2/rand/Isaac64.zig.html
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
