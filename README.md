# Isaac64cs

ISAAC64 Implementation in C#

I am a big fan of using Isaac64 here and there in my projects.

This one is made to be super simple:

Create an instance of the Isaac64.Rng() class, it has 3 constructors: (1) constructs
a new RNG from a UInt64 number, (2) from a UInt64 array, and (3) from a byte array
containing up to 2048 bytes.

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
