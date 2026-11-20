// Port of engine-sim include/filter.h, src/filter.cpp, include/derivative_filter.h,
// src/derivative_filter.cpp, include/low_pass_filter.h, src/low_pass_filter.cpp,
// include/leveling_filter.h, src/leveling_filter.cpp (with the package's ceiling),
// include/jitter_filter.h and src/jitter_filter.cpp (with the reference's seeded random patch).
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
//
// Single precision where upstream uses float. Mixed float/double expressions keep upstream's
// literal types, so they promote and narrow at the same points.
using System;

namespace Nexstead.EngineBench.Simulation
{
    public class Filter
    {
        public virtual float F(float sample)
        {
            return sample;
        }
    }

    public sealed class DerivativeFilter : Filter
    {
        public float dt = 0;
        float previous = 0;

        public override float F(float sample)
        {
            float temp = previous;
            previous = sample;

            return (sample - temp) / dt;
        }

        // Read access for the dense core's snapshot.
        internal float Previous => previous;
    }

    public sealed class LowPassFilter : Filter
    {
        public float dt = 1 / 44100.0f;
        float y = 0;
        float rc = 0;

        public override float F(float sample) => FastF(sample);

        public float FastF(float sample)
        {
            float alpha = dt / (rc + dt);
            y = alpha * sample + (1 - alpha) * y;
            return y;
        }

        public void SetCutoffFrequency(float f)
        {
            rc = 1.0f / (f * 2.0f * (float)Constants.pi);
        }

        // Read access for the dense core's snapshot.
        internal float Y => y;
        internal float Rc => rc;
    }

    /// <summary>
    /// Upstream's leveler: scales the signal so its peaks sit at <see cref="p_target"/>, the peak it tracks rising at once
    /// and falling back by 0.999 a sample, the gain moving a tenth of the way to target / peak each sample, within
    /// <see cref="p_minLevel"/> and <see cref="p_maxLevel"/>. Its gain rises between firing pulses faster than it falls on
    /// the next one, so pulses overshoot; the package's addition is a ceiling: where a sample would pass
    /// <see cref="p_ceiling"/>, the gain is cut at once to what fits, and moves on from there as upstream's does.
    /// </summary>
    public sealed class LevelingFilter : Filter
    {
        float peak = 30000.0f;
        float attenuation = 1.0f;

        public float p_maxLevel = 1.0f;
        public float p_minLevel = 0.0f;
        public float p_target = 30000.0f;
        /// <summary>No leveled sample's magnitude passes it (the package's addition); none by default.</summary>
        public float p_ceiling = float.PositiveInfinity;

        public override float F(float sample)
        {
            peak = 0.999f * peak;
            if (Math.Abs(sample) > peak)
            {
                peak = Math.Abs(sample);
            }

            if (peak == 0) return 0;

            float raw_attenuation = p_target / peak;

            float attenuation = raw_attenuation;
            if (attenuation < p_minLevel) attenuation = p_minLevel;
            else if (attenuation > p_maxLevel) attenuation = p_maxLevel;

            // Upstream's 0.9 and 0.1 are double literals: the blend runs in double.
            this.attenuation = (float)(0.9 * this.attenuation + 0.1 * attenuation);
            if (Math.Abs(sample) * this.attenuation > p_ceiling)
            {
                this.attenuation = p_ceiling / Math.Abs(sample);
            }

            return sample * this.attenuation;
        }

        public float GetAttenuation() => attenuation;

        // Read access for the dense core's snapshot.
        internal float Peak => peak;
    }

    /// <summary>Reads its input back with a random, low-pass-filtered delay of up to <c>maxJitter - 1</c> samples.</summary>
    public sealed class JitterFilter : Filter
    {
        readonly ButterworthLowPassFilterFloat noiseFilter = new ButterworthLowPassFilterFloat();
        readonly DeterministicRandom random = new DeterministicRandom();

        float jitterScale = 0.0f;
        int maxJitter = 0;
        int offset = 0;
        float[] history;

        public void Initialize(int maxJitter, float cutoffFrequency, float audioFrequency)
        {
            this.maxJitter = maxJitter;
            history = new float[maxJitter];
            offset = 0;

            noiseFilter.SetCutoffFrequency(cutoffFrequency, audioFrequency);
        }

        public override float F(float sample) => FastF(sample);

        public float FastF(float sample, float jitterScale = 1.0f)
        {
            history[offset] = sample;
            ++offset;

            if (offset >= maxJitter)
            {
                offset = 0;
            }

            float jitter = (float)(random.NextDouble() * (maxJitter - 1));
            float s = noiseFilter.FastF(jitter * this.jitterScale * jitterScale);
            float s_i_0 = Utilities.Clamp(MathF.Floor(s), 0.0f, (float)(maxJitter - 1));
            float s_i_1 = Utilities.Clamp(MathF.Ceiling(s), 0.0f, (float)(maxJitter - 1));

            float s_frac = (s - s_i_0);

            int i_0 = (int)s_i_0 + offset;
            int i_1 = (int)s_i_1 + offset;

            float v0 = history[i_0 >= maxJitter ? i_0 - maxJitter : i_0];
            float v1 = history[i_1 >= maxJitter ? i_1 - maxJitter : i_1];

            return v1 * s_frac + v0 * (1 - s_frac);
        }

        public void SeedRandom(ulong seed, int channelIndex)
        {
            random.Seed(seed, DeterministicRandom.JitterStream(channelIndex));
        }

        public void SetJitterScale(float jitterScale) => this.jitterScale = jitterScale;
        public float GetJitterScale() => jitterScale;

        // Read access for the dense core's snapshot.
        internal ButterworthLowPassFilterFloat NoiseFilter => noiseFilter;
        internal DeterministicRandom Random => random;
        internal int MaxJitter => maxJitter;
        internal int Offset => offset;
        internal float[] History => history;
    }
}
