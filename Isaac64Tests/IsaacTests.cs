using Isaac64;
using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Isaac64.Tests
{
    public class RngTests
    {
        [Fact]
        public void Rng_WithSameSeed_ProducesSameSequence()
        {
            var seed = 0xDEADBEEFCAFEBABE;
            var rng1 = new Rng(seed);
            var rng2 = new Rng(seed);

            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal(rng1.Rand64(ulong.MaxValue), rng2.Rand64(ulong.MaxValue));
            }
        }

        [Fact]
        public void Rng_WithDifferentSeeds_ProducesDifferentSequences()
        {
            var rng1 = new Rng(123456789);
            var rng2 = new Rng(987654321);

            int same = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (rng1.Rand64(ulong.MaxValue) == rng2.Rand64(ulong.MaxValue))
                    same++;
            }

            Assert.True(same < 5, $"Too many matching outputs: {same}");
        }

        [Fact]
        public void Rng_CanGenerateMillionsOfValues_WithoutError()
        {
            var rng = new Rng(0x123456789ABCDEF);
            for (int i = 0; i < 1_000_000; i++)
            {
                var _ = rng.Rand64(ulong.MaxValue);
            }
        }

        [Fact]
        public void Rng_ProducesFullRange()
        {
            var rng = new Rng(42);
            bool sawLow = false, sawHigh = false;

            for (int i = 0; i < 10_000; i++)
            {
                ulong val = rng.Rand64(ulong.MaxValue);
                if (val < ulong.MaxValue / 10) sawLow = true;
                if (val > ulong.MaxValue - (ulong.MaxValue / 10)) sawHigh = true;
                if (sawLow && sawHigh) break;
            }

            Assert.True(sawLow, "Low-range values not seen.");
            Assert.True(sawHigh, "High-range values not seen.");
        }

        [Fact]
        public void Rng_RandDouble_StaysInRange()
        {
            var rng = new Rng(1337);
            for (int i = 0; i < 10000; i++)
            {
                var d = rng.RandDouble();
                Assert.True(d > 0.0 && d < 1.0, $"RandDouble out of bounds: {d}");
            }
        }

        [Fact]
        public void Rng_Throws_OnZeroSeed()
        {
            Assert.Throws<ArgumentException>(() => new Rng(0));
        }

        [Fact]
        public void Rng_Throws_OnOversizedByteArray()
        {
            byte[] bytes = new byte[3000]; // >2048
            Assert.Throws<ArgumentException>(() => new Rng(bytes));
        }

        [Fact]
        public void Rng_Throws_OnOversizedUlongArray()
        {
            ulong[] ulongs = Enumerable.Repeat(1UL, 300).ToArray(); // >256
            Assert.Throws<ArgumentException>(() => new Rng(ulongs));
        }

        [Fact]
        public void Rng_Reseed_ChangesOutput()
        {
            var rng = new Rng(999);
            ulong before = rng.Rand64(ulong.MaxValue);
            rng.Reseed(888);
            ulong after = rng.Rand64(ulong.MaxValue);
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void Rng_Shuffle_RandomizesStream()
        {
            var rng = new Rng(101010);
            ulong[] vals = new ulong[256];
            for (int i = 0; i < 256; i++)
                vals[i] = rng.Rand64(ulong.MaxValue);

            rng.Shuffle();

            ulong[] vals2 = new ulong[256];
            for (int i = 0; i < 256; i++)
                vals2[i] = rng.Rand64(ulong.MaxValue);

            Assert.NotEqual(vals, vals2);
        }

        [Fact]
        public void Rng_RandDoubleRaw_ProducesValuesWithinSpecifiedRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                double d = rng.RandDoubleRaw(1.5, 3.5);
                Assert.True(d > 1.5 && d < 3.5, $"Value out of range: {d}");
            }
        }

        [Fact]
        public void Rng_RandAlphaNum_ThrowsWhenNoCharacterClassSelected()
        {
            var rng = new Rng(123);
            Assert.Throws<ArgumentException>(() => rng.RandAlphaNum(false, false, false));
        }

        [Fact]
        public void Rng_Rand8_Rand16_Rand32_DoNotCrashWithBankedReuse()
        {
            var rng = new Rng(555);

            // Burn through enough calls to verify bank reuse
            for (int i = 0; i < 100; i++)
            {
                byte b = rng.Rand8();
                ushort s = rng.Rand16();
                uint u = rng.Rand32();
                Assert.InRange(b, byte.MinValue, byte.MaxValue);
                Assert.InRange(s, ushort.MinValue, ushort.MaxValue);
                Assert.InRange(u, uint.MinValue, uint.MaxValue);
            }
        }

        [Fact]
        public void Rng_RangedRand64S_RespectsSignedRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                long v = rng.RangedRand64S(-5000, 5000);
                Assert.InRange(v, -5000, 5000);
            }
        }

        [Fact]
        public void Rng_UnseededRng_ProducesExpectedFirstValues()
        {
            var rng = new Rng(true); // unseeded test mode
            var expected = new ulong[]
            {
                0xf67dfba498e4937c, 0x84a5066a9204f380,
                0xfee34bd5f5514dbb, 0x4d1664739b8f80d6,
                0x8607459ab52a14aa, 0x0e78bc5a98529e49,
                0xfe5332822ad13777, 0x556c27525e33d01a
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], rng.Rand64());
            }
        }

        [Fact]
        public void Rng_Unseeded_ProducesKnownOutputs()
        {
            var rng = new Rng(true);
            ulong[] expected = new ulong[]
            {
                0xf67dfba498e4937c, 0x84a5066a9204f380,
                0xfee34bd5f5514dbb, 0x4d1664739b8f80d6,
                0x8607459ab52a14aa, 0x0e78bc5a98529e49,
                0xfe5332822ad13777, 0x556c27525e33d01a,
                0x08643ca615f3149f, 0xd0771faf3cb04714,
                0x30e86f68a37b008d, 0x3074ebc0488a3adf,
                0x270645ea7a2790bc, 0x5601a0a8d3763c6a,
                0x2f83071f53f325dd, 0xb9090f3d42d2d2ea
            };

            for (int i = 0; i < expected.Length; i++)
            {
                ulong actual = rng.Rand64();
                Assert.Equal(expected[i], actual);
            }
        }

        [Fact]
        public void Rng_SeededByteArray_MatchesRustOutputs()
        {
            byte[] seed = new byte[]
            {
                1,0,0,0,   0,0,0,0, 23,0,0,0,   0,0,0,0,
                200,1,0,0, 0,0,0,0, 210,30,0,0, 0,0,0,0
            };

            var rng = new Rng(seed);

            uint[] expected = new uint[]
            {
                3477963620, 3509106075,  687845478, 1797495790,
                227048253, 2523132918,  4044335064, 1260557630,
                4079741768, 3001306521,  69157722, 3958365844
            };

            for (int i = 0; i < expected.Length; i++)
            {
                uint actual = rng.Rand32();
                Assert.Equal(expected[i], actual);
            }
        }

        [Fact]
        public void Benchmark_Issac64_Rand64_500Million()
        {
            var rng = new Rng(42);
            var sw = Stopwatch.StartNew();

            ulong sum = 0;
            const int total = 500_000_000;

            for (int i = 0; i < total; i++)
            {
                sum += rng.Rand64();
            }

            sw.Stop();

            Console.WriteLine($"Generated {total:N0} Rand64() values in {sw.Elapsed.TotalSeconds:N2} seconds");
            Console.WriteLine($"Checksum: {sum}"); // Prevents optimization away

            Assert.True(true); // always pass, just measure
        }

        [Fact]
        public void ClonedRngProducesIdenticalSequence()
        {
            // Arrange
            var original = new Rng(123456789UL); // any known seed
            var clone = original.Clone();

            // Act & Assert
            for (int i = 0; i < 1000; i++)
            {
                var oVal = original.Rand64();
                var cVal = clone.Rand64();

                Assert.Equal(oVal, cVal);
            }
        }

        [Fact]
        public void RangedRand8_HandlesReversedBoundsAndFullRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                byte value = rng.RangedRand8(255, 0);
                Assert.InRange(value, 0, 255);
            }
        }

        [Fact]
        public void RangedRand8S_HandlesReversedBoundsAndFullRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                sbyte value = rng.RangedRand8S(sbyte.MinValue, sbyte.MaxValue);
                Assert.InRange(value, -128, 127);
            }
        }

        [Fact]
        public void RangedRand16_HandlesReversedBoundsAndFullRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                ushort value = rng.RangedRand16(ushort.MaxValue, 0);
                Assert.InRange(value, 0, 65535);
            }
        }

        [Fact]
        public void RangedRand16S_HandlesReversedBoundsAndFullRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                short value = rng.RangedRand16S(short.MinValue, short.MaxValue);
                Assert.InRange(value, short.MinValue, short.MaxValue);
            }
        }

        [Fact]
        public void RangedRand32_HandlesReversedBoundsAndFullRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                uint value = rng.RangedRand32(uint.MaxValue, 0);
                Assert.True(value >= 0, $"Out of range: {value}");
            }
        }

        [Fact]
        public void RangedRand32S_HandlesReversedBoundsAndFullRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                int value = rng.RangedRand32S(int.MinValue, int.MaxValue);
                Assert.InRange(value, int.MinValue, int.MaxValue);
            }
        }

        [Fact]
        public void RangedRand64_HandlesReversedBoundsAndFullRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                ulong value = rng.RangedRand64(ulong.MaxValue, 0);
                Assert.True(value >= 0, $"Out of range: {value}");
            }
        }

        [Fact]
        public void RangedRand64S_HandlesReversedBoundsAndFullRange()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 1000; i++)
            {
                long value = rng.RangedRand64S(long.MinValue, long.MaxValue);
                Assert.InRange(value, long.MinValue, long.MaxValue);
            }
        }

        [Theory]
        [InlineData((byte)5, (byte)5)]
        [InlineData((byte)42, (byte)43)]
        public void RangedRand8_SmallRanges_ProduceValidResults(byte min, byte max)
        {
            var rng = new Rng(123);
            for (int i = 0; i < 100; i++)
            {
                byte v = rng.RangedRand8(min, max);
                Assert.InRange(v, Math.Min(min, max), Math.Max(min, max));
            }
        }

        [Theory]
        [InlineData((sbyte)-10, (sbyte)-10)]
        [InlineData((sbyte)12, (sbyte)13)]
        public void RangedRand8S_SmallRanges_ProduceValidResults(sbyte min, sbyte max)
        {
            var rng = new Rng(123);
            for (int i = 0; i < 100; i++)
            {
                sbyte v = rng.RangedRand8S(min, max);
                Assert.InRange(v, Math.Min(min, max), Math.Max(min, max));
            }
        }

        [Theory]
        [InlineData((ushort)1000, (ushort)1000)]
        [InlineData((ushort)2000, (ushort)2001)]
        public void RangedRand16_SmallRanges_ProduceValidResults(ushort min, ushort max)
        {
            var rng = new Rng(123);
            for (int i = 0; i < 100; i++)
            {
                ushort v = rng.RangedRand16(min, max);
                Assert.InRange(v, Math.Min(min, max), Math.Max(min, max));
            }
        }

        [Theory]
        [InlineData((short)-12345, (short)-12345)]
        [InlineData((short)1000, (short)1001)]
        public void RangedRand16S_SmallRanges_ProduceValidResults(short min, short max)
        {
            var rng = new Rng(123);
            for (int i = 0; i < 100; i++)
            {
                short v = rng.RangedRand16S(min, max);
                Assert.InRange(v, Math.Min(min, max), Math.Max(min, max));
            }
        }

        [Theory]
        [InlineData((uint)99999, (uint)99999)]
        [InlineData((uint)65535, (uint)65536)]
        public void RangedRand32_SmallRanges_ProduceValidResults(uint min, uint max)
        {
            var rng = new Rng(123);
            for (int i = 0; i < 100; i++)
            {
                uint v = rng.RangedRand32(min, max);
                Assert.InRange(v, Math.Min(min, max), Math.Max(min, max));
            }
        }

        [Theory]
        [InlineData((int)-50000, (int)-50000)]
        [InlineData((int)1337, (int)1338)]
        public void RangedRand32S_SmallRanges_ProduceValidResults(int min, int max)
        {
            var rng = new Rng(123);
            for (int i = 0; i < 100; i++)
            {
                int v = rng.RangedRand32S(min, max);
                Assert.InRange(v, Math.Min(min, max), Math.Max(min, max));
            }
        }

        [Theory]
        [InlineData((ulong)9876543210, (ulong)9876543210)]
        [InlineData((ulong)1, (ulong)2)]
        public void RangedRand64_SmallRanges_ProduceValidResults(ulong min, ulong max)
        {
            var rng = new Rng(123);
            for (int i = 0; i < 100; i++)
            {
                ulong v = rng.RangedRand64(min, max);
                Assert.InRange(v, Math.Min(min, max), Math.Max(min, max));
            }
        }

        [Theory]
        [InlineData((long)-9999999999, (long)-9999999999)]
        [InlineData((long)123456789, (long)123456790)]
        public void RangedRand64S_SmallRanges_ProduceValidResults(long min, long max)
        {
            var rng = new Rng(123);
            for (int i = 0; i < 100; i++)
            {
                long v = rng.RangedRand64S(min, max);
                Assert.InRange(v, Math.Min(min, max), Math.Max(min, max));
            }
        }
    }
}
