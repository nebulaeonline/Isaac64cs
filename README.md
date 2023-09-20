# Isaac64cs

ISAAC64 Implementation in C#

---
NOTE: I intend to remove the use of spans & unsafe very soon.  I know some don't want
to build with unsafe code. I had intended spans to provide a more granular approach
when dealing with different data sizes, and some easier-to-follow code when
banking data types < 64-bit.  It's not ugly, but it just doesn't justify the
use of unsafe.
---

I am a big fan of using Isaac64 here and there in my projects.

This one is made to be super simple:

Create an instance of the Isaac64.Rng() class, it has 4 constructors: (1) is the default
and constructs a new RNG using the system's crypto RNG to generate a seed; (2) constructs
a new RNG from a single UInt64 number; (3) uses a UInt64 array containing up to 256
numbers; and (4) uses a byte array containing up to 2048 bytes.  These constructors
will throw exceptions if used in a non-optimal (or unseeded) manner.  All except the
default have a boolean override value in case you wish to deviate or test for
standards conformance.

There are RandN() & RangedRandN() methods for 64/32/16/8 bit unsigned integers.
The RandN() methods can take a Max argument (of the same type), while the 
RangedRandN() methods take a Min and a Max and will return a number between
those specified. The RangedRandNS() methods work the same, but they accept
signed arguments for Min & Max, and likewise return signed integers.

There are a few new methods (1) RandAlphaNum() which can return uppercase &
lowercase characters as well as numbers; (2) RandDouble() which returns a
double in the range (0, 1); and (3) RandDoubleRaw(), which provides a great
deal of flexibility in generating doubles within a given range.

The Shuffle() method mixes & rotates the data and fill back up the RNG buffer.  
It does it after every 256 64-bit numbers (that's the capacity), but if you have 
a need to do it more frequently the option is there.

You can also Reseed() the RNG from ground zero at any time, and the Reseed()
methods come in the same varieties as the non-default constructors.

If you pull a smaller data type than 64-bits (8 bytes), the remaining pieces 
of the 8-byte chunk are banked until you request that data size again.

As for conformance, I have checked this against Bob Jenkins' original C reference
implementation here: https://burtleburtle.net/bob/rand/isaacafa.html

I have also verified both the seeded and unseeded tests (32/64 bit) with the 
code in the Rust core library here: https://docs.rs/rand_isaac/latest/src/rand_isaac/isaac64.rs.html

The Zig std library's Isaac64 crypto provider also matches my unseeded output,
and it can be found here: https://github.com/ziglang/zig/blob/master/lib/std/rand/Isaac64.zig

This builds on the newest .NET 8 Preview 7 (September 2023) in the AOT build configuration.

If there are any bugs or diversions from the spec, please reach out.

N
