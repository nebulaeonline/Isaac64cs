# Isaac64 C-Sharp
#### ISAAC64 Library Implementation in C# 

I'm a big fan of using Isaac64 here and there in my projects, and this one is designed to be super easy to use.

This is a pure C# implementation of the ISAAC64 algorithm, which is a fast, high-quality, and non-cryptographic pseudo-random number generator (PRNG) designed by Bob Jenkins. It is known for its speed and statistical quality, making it suitable for various applications, including simulations, games, and other scenarios where random numbers are needed.

This library is battle-tested for 2+ years in production in gaming, and has been used in a variety of other projects including simulations due to its robust random double support.

No dependencies, no fluff, no nonsense. In fact, you can just drop Rng.cs into your project or cut & paste and it will *just work*.

Cyptographic Note: While it is true that certain constructors *do* seed with a crypographically secure 2048-byte seed from the system RNG, ISAAC operations are not guaranteed to be constant time in this implementation, and ISAAC is not itself advertised as a cryptographically secure RNG. ISAAC should however be more than secure for most any other use, unless someone can run timing attacks on the same system to dump the rng state. All bets are off with unrestricted physical access.

---

**Latest Update 2025-04-21**

1. The RangedRandN() and RangedRandNS() function groups now eliminate RNG bias through modulo sampling. Note this may mean that more than one random number is burned per pull when using these functions.
2. Added tests for issues in the original bias elimination code which caused infinite loops on ranges of 1 and 2 numbers (now fixed).
3. Added histogram tests with output, 10M Samples (64 buckets for the ints, 100 buckets for the doubles):

| Function        | Max Deviation | Min Deviation | Spread             |
| :-------------- | :----:        | :----:        | :----:             |
| RangedRand8()   |+0.719%        |-0.606%        | 1.325% (+/- 0.72%) |
| RangedRand16()  |+0.603%        |-0.541%        | 1.144% (+/- 0.60%) |
| RangedRand32()  |+0.800%        |-0.552%        | 1.352% (+/- 0.80%) |
| RangedRand64()  |+0.569%        |-0.689%        | 1.258% (+/- 0.69%) |
| RangedRand8S()  |+0.544%        |-0.466%        | 1.010% (+/- 0.55%) |
| RangedRand16S() |+0.705%        |-0.577%        | 1.282% (+/- 0.71%) |
| RangedRand32S() |+0.615%        |-0.681%        | 1.294% (+/- 0.68%) |
| RangedRand64S() |+0.544%        |-0.796%        | 1.340% (+/- 0.80%) |
| RandDouble()    |+0.823%        |-0.749%        | 1.572% (+/- 0.82%) |
| RandDoubleRaw() |+0.769%        |-0.626%        | 1.395% (+/- 0.77%) |

**Excellent distributions all around (Thank you Bob!)**

**Update 2025-04-20**

Beating it to death. Went with concurrent stacks and added locks. Thread-safe now but a tad bit slower.

**Update 2025-04-19**

1. Relicense to MIT
2. Backported for Nuget and users of old versions of .NET so they can use the library. Conditional compilation directives are included in the source, so if you rebuild you'll take advantage of the latest Microsoft library functions.

**Update 2025-04-17**

Added a .Clone() method to the Rng class to allow for easy cloning of the RNG state into a new instance. Use this if you need to create a copy of the RNG state for parallel processing or other purposes.

**Update 2025-04-14**

1. Fixed a subtle signed-to-unsigned cast wraparound in the RangedRandNS() set of functions
2. Fixed error with a 0-seeded Rng  not throwing
3. Added unit tests so users can feel confident in the library
4. Added interface that mimics System.Random with 32-bit Next() functions

Speed is approx 23.91 seconds in Debug mode for 500M random numbers (Ryzen 3950x). Should run significantly faster in Release or AOT configurations.

---

[![NuGet](https://img.shields.io/nuget/v/Isaac64.svg)](https://www.nuget.org/packages/Isaac64)

Install with:

$ dotnet add package Isaac64

---

### Isaac64.Rng()

###  Constructors:

1. `Rng()`: RNG seeded with 2048 bytes from the system's crypto RNG
2. `Rng(bool)`: unseeded RNG suitable for verifying against the spec
3. `Rng(byte[], bool = false)`: RNG seeded with a byte array containing up to 2048 bytes
4. `Rng(ulong[], bool = false)`: RNG seeded with an array of up to 256 ulongs
5. `Rng(ulong, bool = false)`: RNG seeded with a single UInt64 number > 0
 
These constructors will throw exceptions if used unseeded (0 or empty arrays), or if the passed arrays exceed the prescribed size limits (no silent ignore). The flags are provided to allow overriding this behavior should your use case require it, or if you wish to test for standards conformance.

### Methods:

1. `RandN(_size_ Max)`, where N is 64/32/16/8.  These methods return unsigned integers of the corresponding size in the range [0, Max]
2. `RangedRandN(_size_ Min, _size_ Max)` methods return unsigned integers in the range [Min, Max]; the `RangedRandNS(_size_ Min, _size_ Max)` variants return signed integers instead
3. `RandAlphaNum(bool Upper, bool Lower, bool Numeric, char[]? symbols)` generates a char using the range(s) specified, optionally also using the symbols array if provided (bias is eliminated in all cases)
4. `RandDouble()` returns a 64-bit double-precision float in the range (0.0, 1.0)
5. `RandDoubleRaw(double Min, double Max, double MinZero = 1e-3)` generates a double in the range (Min, Max) using the MinZero parameter as the defacto smallest number (see source for info)
6. `Shuffle()` mixes & rotates the data and refills the RNG buffer (occurs automatically at mod 256 runs)
7. `Reseed()` reseeds the RNG from ground zero at any time; has variants mirroring the class constructors
8. `Clone()` returns a new instance of the Rng with a complete clone of the current RNG's state; this allows you to "fork" the RNG and run multiple independent RNGs, all of which will start with identical state from the point of Clone(). Useful for using the same RNG state in multiple functions or threads.

### Mimic of System.Random for 32-bit Ints:

1. `Next()`: returns a 32-bit unsigned integer in the range [0, 2^32)
2. `Next(int Max)`: returns a 32-bit unsigned integer in the range [0, Max)
3. `Next(int Min, int Max)`: returns a 32-bit unsigned integer in the range [Min, Max)

When pulling a data type smaller than 64-bits, the remaining bytes of the 8-byte chunk are banked until you request that same type size again.

### Notes on Random Doubles:

All doubles pull a 64-bit integer for the mantissa/fraction. Regular doubles may also pull up to two 16-bit integers, one for the sign and one for the exponent. If the specified Min & Max are the same sign, no integer is pulled.  Likewise, if Min & Max share a common exponent, no integer will be pulled. Subnormal doubles (extremely small < 10^-308) will never pull an integer for their exponent, and may or may not pull an integer for their sign, exactly the same as regular doubles.

### Standard Conformance:

1. Verified against Bob Jenkins' original C reference implementation: [ISAAC64](https://burtleburtle.net/bob/rand/isaacafa.html)
2. Verified both seeded and unseeded tests (32 & 64-bit) with the Rust core library: [Rust Core Lib](https://docs.rs/rand_isaac/latest/src/rand_isaac/isaac64.rs.html)
3. Verified with the Zig std library's Isaac64 crypto provider: [Zig Std](https://ratfactor.com/zig/stdlib-browseable2/rand/Isaac64.zig.html)

### Building:

This builds on the newest .NET 9 (April 2025) in both JITed and AOT build configurations.
It should build going pretty far back in the C# lineage if required.

### Etc:

If there are any bugs or diversions from the spec, please reach out.

## N