// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// The synthesizer settings upstream's application changes while an engine runs (its X, C, V and B keys), plus the cutoff
    /// of the noise V mixes in. The synthesizer reads them at every render, so a change takes effect from the next block.
    /// </summary>
    public struct EngineSoundMix
    {
        /// <summary>How much of the signal goes through the exhaust's impulse response, 0 to 1 (upstream X, "convolution level"); the rest is the raw exhaust pulses.</summary>
        public float convolution;
        /// <summary>The share of the pulses' rate of change in the signal, 0 to 1 (upstream C, "high freq. gain"): brighter, raspier.</summary>
        public float highFrequencyGain;
        /// <summary>How much low-passed noise modulates the pulses, 0 to 1 (upstream V, "low freq. noise").</summary>
        public float lowFrequencyNoise;
        /// <summary>The cutoff of that noise's low-pass filter (Hz).</summary>
        public float lowFrequencyNoiseCutoff;
        /// <summary>Random jitter in reading the simulation's samples, 0 to 1 (upstream B, "high freq. noise").</summary>
        public float highFrequencyNoise;

        /// <summary>
        /// The mix an engine starts with, as upstream's application loads it: the engine's initial high-frequency gain, noise
        /// and jitter, and the synthesizer's defaults for the rest.
        /// </summary>
        public static EngineSoundMix AsLoaded(double initialHighFrequencyGain, double initialNoise, double initialJitter)
        {
            Synthesizer.AudioParameters defaults = Synthesizer.AudioParameters.Default;
            return new EngineSoundMix
            {
                convolution = defaults.convolution,
                highFrequencyGain = (float)initialHighFrequencyGain,
                lowFrequencyNoise = (float)initialNoise,
                lowFrequencyNoiseCutoff = defaults.airNoiseFrequencyCutoff,
                highFrequencyNoise = (float)initialJitter,
            };
        }

        public void ApplyTo(ref Synthesizer.AudioParameters parameters)
        {
            parameters.convolution = convolution;
            parameters.dF_F_mix = highFrequencyGain;
            parameters.airNoise = lowFrequencyNoise;
            parameters.airNoiseFrequencyCutoff = lowFrequencyNoiseCutoff;
            parameters.inputSampleNoise = highFrequencyNoise;
        }
    }
}
