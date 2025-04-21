namespace Isaac64.Tests
{
    public class RngHistogramTests
    {
        private const int SampleCount = 10_000_000; // Large enough for good stats

        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(32)]
        public void RangedRandN_Histogram_IsUniform(int bits)
        {
            var rng = new Rng(12345);
            int buckets = 64;
            int[] histogram = new int[buckets];

            for (int i = 0; i < SampleCount; i++)
            {
                int val = bits switch
                {
                    8 => rng.RangedRand8(0, (byte)(buckets - 1)),
                    16 => rng.RangedRand16(0, (ushort)(buckets - 1)),
                    32 => (int)rng.RangedRand32(0, (uint)(buckets - 1)),
                    _ => throw new ArgumentOutOfRangeException(nameof(bits))
                };

                histogram[val]++;
            }

            double expected = SampleCount / (double)buckets;
            double tolerance = expected * 0.01; // 1% margin

            Console.WriteLine($"RangedRand{bits} Buckets:");

            for (int i = 0; i < buckets; i++)
            {
                Console.WriteLine($"[RangedRand{bits}] Bucket {i:D2} = {histogram[i]}");
                Assert.InRange(histogram[i], expected - tolerance, expected + tolerance);
            }
        }

        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(32)]
        public void RangedRandNS_Histogram_IsUniform(int bits)
        {
            var rng = new Rng(54321);
            int buckets = 64;
            int[] histogram = new int[buckets];

            for (int i = 0; i < SampleCount; i++)
            {
                int val = bits switch
                {
                    8 => rng.RangedRand8S(-32, 31) + 32,  // Normalize to index 0–63
                    16 => rng.RangedRand16S(-32, 31) + 32,
                    32 => rng.RangedRand32S(-32, 31) + 32,
                    _ => throw new ArgumentOutOfRangeException(nameof(bits))
                };

                histogram[val]++;
            }

            double expected = SampleCount / (double)buckets;
            double tolerance = expected * 0.01; // 1% margin

            Console.WriteLine($"RangedRand{bits}S() Bucket Distribution:");

            for (int i = 0; i < buckets; i++)
            {
                Assert.InRange(histogram[i], expected - tolerance, expected + tolerance);
                Console.WriteLine($"[RangedRand{bits}S] Bucket {i:D2} = {histogram[i]}");
            }
        }

        [Fact]
        public void RangedRand64_Histogram_IsUniform()
        {
            var rng = new Rng(2024);
            int buckets = 64;
            int[] histogram = new int[buckets];
            long sampleCount = 10_000_000;

            for (int i = 0; i < sampleCount; i++)
            {
                ulong val = rng.RangedRand64(0, (ulong)(buckets - 1));
                histogram[val]++;
            }

            double expected = sampleCount / (double)buckets;
            double tolerance = expected * 0.01;

            Console.WriteLine("RangedRand64() Bucket Distriubtion:");
            for (int i = 0; i < buckets; i++)
            {
                Assert.InRange(histogram[i], expected - tolerance, expected + tolerance);
                Console.WriteLine($"[RangedRand64] Bucket {i:D2}: {histogram[i]}");
            }
        }

        [Fact]
        public void RangedRand64S_Histogram_IsUniform()
        {
            var rng = new Rng(2025);
            int buckets = 64;
            int[] histogram = new int[buckets];
            long sampleCount = 10_000_000;

            for (int i = 0; i < sampleCount; i++)
            {
                long val = rng.RangedRand64S(-32, 31);
                histogram[val + 32]++;
            }

            double expected = sampleCount / (double)buckets;
            double tolerance = expected * 0.01;

            Console.WriteLine("RangedRand64S Bucket Distriubtion:");
            for (int i = 0; i < buckets; i++)
            {
                Assert.InRange(histogram[i], expected - tolerance, expected + tolerance);
                Console.WriteLine($"[RangedRand64S] Bucket {i:D2}: {histogram[i]}");
            }
        }


        [Fact]
        public void RandDouble_Distribution_IsRoughlyUniform()
        {
            var rng = new Rng(999);
            const int buckets = 100;
            const int samples = 10_000_000;
            int[] histogram = new int[buckets];

            for (int i = 0; i < samples; i++)
            {
                double val = rng.RandDouble(); // returns in (0.0, 1.0)
                int index = (int)(val * buckets);
                if (index >= buckets) index = buckets - 1;
                histogram[index]++;
            }

            double expected = samples / (double)buckets;
            double tolerance = expected * 0.015; // 1.5% wiggle room

            Console.WriteLine("Buckets for RandDouble() Distribution:");
            for (int i = 0; i < buckets; i++)
            {
                Assert.InRange(histogram[i], expected - tolerance, expected + tolerance);
                Console.WriteLine($"[RandDouble] Bucket {i:D2}: {histogram[i]}");
            }
        }

        [Fact]
        public void RandDoubleRaw_Distribution_IsRoughlyUniform()
        {
            var rng = new Rng(4242);
            const int buckets = 100;
            const int samples = 10_000_000;
            int[] histogram = new int[buckets];

            for (int i = 0; i < samples; i++)
            {
                double val = rng.RandDoubleRaw(1.0, 2.0, 1e-3) - 1.0; // we do this to get rid of the range compression caused by too many "close to zero" numbers
                int index = (int)(val * buckets);
                if (index >= buckets) index = buckets - 1;
                histogram[index]++;
            }

            double expected = samples / (double)buckets;
            double tolerance = expected * 0.015;

            Console.WriteLine("RandDoubleRaw Bucket Distribution:");
            for (int i = 0; i < buckets; i++)
            {
                Assert.InRange(histogram[i], expected - tolerance, expected + tolerance);
                Console.WriteLine($"[RandDoubleRaw] Bucket {i:D2}: {histogram[i]}");
            }
        }

    }
}
