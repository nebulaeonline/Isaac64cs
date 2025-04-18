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
    }
}
