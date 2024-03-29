# Isaac64cs
#### ISAAC64 Library Implementation in C# 

I'm a big fan of using Isaac64 here and there in my projects, and this one is designed to be super easy to use.

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
3. `RandAlphaNum(bool Upper, bool Lower, bool Numeric)` generates a char using the range(s) specified
4. `RandDouble()` returns a 64-bit double-precision float in the range (0.0, 1.0)
5. `RandDoubleRaw(double Min, double Max, double MinZero = 1e-3)` generates a double in the range (Min, Max) using the MinZero parameter as the defacto smallest number (see source for info)
6. `Shuffle()` mixes & rotates the data and refills the RNG buffer (occurs automatically at mod 256 runs)
7. `Reseed()` reseeds the RNG from ground zero at any time; has variants mirroring the class constructors

When pulling a data type smaller than 64-bits, the remaining bytes of the 8-byte chunk are banked until you request that same type size again.

### Notes on Random Doubles:

All doubles pull a 64-bit integer for the mantissa/fraction. Regular doubles may also pull up to two 16-bit integers, one for the sign and one for the exponent. If the specified Min & Max are the same sign, no integer is pulled.  Likewise, if Min & Max share a common exponent, no integer will be pulled. Subnormal doubles (extremely small < 10^-308) will never pull an integer for their exponent, and may or may not pull an integer for their sign, exactly the same as regular doubles.

### Standard Conformance:

1. Verified against Bob Jenkins' original C reference implementation: [ISAAC64](https://burtleburtle.net/bob/rand/isaacafa.html)
2. Verified both seeded and unseeded tests (32 & 64-bit) with the Rust core library: [Rust Core Lib](https://docs.rs/rand_isaac/latest/src/rand_isaac/isaac64.rs.html)
3. Verified with the Zig std library's Isaac64 crypto provider: [Zig Std](https://github.com/ziglang/zig/blob/master/lib/std/rand/Isaac64.zig)

### Building:

This builds on the newest .NET 8 RC2 (October 2023) in the AOT build configuration, and nothing is too fancy.
It should build going pretty far back in the C# lineage if required.

### Etc:

If there are any bugs or diversions from the spec, please reach out.

## N