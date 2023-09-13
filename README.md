# Isaac64cs

ISAAC64 Implementation in C#

I am a big fan of using Isaac64 here and there in my projects.

This one is made to be super simple:

Create an instance of the Isaac64.Rng() class, it has 3 constructors: (1) constructs
a new RNG from a single UInt64 number, (2) from a UInt64 array containing up to 256
numbers, and (3) from a byte array containing up to 2048 bytes.

There are RandN() & RangedRandN() methods for 64/32/16/8 bit unsigned integers.
The RandN() methods can take a Max argument (of the same type), while the 
RangedRandN() methods take a Min and a Max and will return a number between
those specified.

There is a Shuffle() method that will mix & rotate the data and fill back
up the RNG.  It does it after every 256 64-bit numbers (that's the
capacity), but if you have a need to do it more frequently the option is
there.

You can also Reseed() the RNG from ground zero at any time, and the Reseed()
methods come in the same varieties as the constructors.

I have not gone hog wild with managing the rng data.  If you pull a smaller
data type than 64-bits (8 bytes), the remaining pieces are banked and saved
until you call that data type again.  This is just a first go at getting a
conforming implementation working so I can move on to what I was really 
trying to do.

This code is not as optimized and featureful as it could be, but it is at least
correct (unless there are some edge cases).  So many repos have flat-out wrong
implementations, and I'm not sure if it's intentional, or just people getting
their feet wet.

As for conformance, I have checked this again Bob Jenkins' original C reference
implementation here: https://burtleburtle.net/bob/rand/isaacafa.html

I have also verified both the unseeded and seeded tests (32/64 bit) with the 
code in the Rust core library here: https://docs.rs/rand_isaac/latest/src/rand_isaac/isaac64.rs.html

The Zig std library's Isaac64 crypto provider also matches my unseeded output,
and it can be found here: https://github.com/ziglang/zig/blob/master/lib/std/rand/Isaac64.zig

I would like to add floats/doubles and maybe a random character provider as well, but
no time at the moment.

My C# is a little rusty, I'm not super up-to-date with the new goodies, but this does
build on the newest .NET 8 Preview 7 (September 2023) in the AOT build configuration,
so there's that.

If there are any bugs or diversions from the spec, please reach out.

N
