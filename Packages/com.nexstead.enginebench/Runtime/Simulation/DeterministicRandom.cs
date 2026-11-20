// Port of include/deterministic_random.h, which the C++ reference the port is checked against
// adds to engine-sim.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>
    /// SplitMix64. Every consumer (each combustion chamber, the synthesizer, each jitter filter) owns a stream, so its values do
    /// not depend on how calls from different consumers interleave. Bit-identical to the reference.
    /// </summary>
    public sealed class DeterministicRandom
    {
        // A stream id is (domain << 32) | index.
        public const ulong SynthesizerNoiseStream = 0;
        public static ulong ChamberStream(int chamberIndex) => (1ul << 32) | (uint)chamberIndex;
        public static ulong JitterStream(int channelIndex) => (2ul << 32) | (uint)channelIndex;
        /// <summary>Seeds the forced induction sounds' own noise (the reference's forced induction patch).</summary>
        public const ulong ForcedInductionNoiseStream = 3ul << 32;

        ulong state;

        public void Seed(ulong seed, ulong stream)
        {
            unchecked
            {
                state = Mix(seed ^ Mix(stream + 1));
            }
        }

        public ulong Next()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15ul;
                return Mix(state);
            }
        }

        /// <summary>Uniform in [0, 1): the top 53 bits as a double.</summary>
        public double NextDouble() => (double)(Next() >> 11) * (1.0 / 9007199254740992.0);

        public ulong State => state;

        static ulong Mix(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }
    }
}
