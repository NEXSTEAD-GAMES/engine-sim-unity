// Checks of the leveler: upstream's, with a ceiling that holds what it would overshoot.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public sealed class LevelerTests
    {
        /// <summary>
        /// The leveler with its ceiling is upstream's, sample for sample, until upstream's would overshoot past the ceiling,
        /// and never passes it: pulses of mixed heights 10 ms apart, between which upstream's gain rises faster than it
        /// falls on the next pulse.
        /// </summary>
        [Test]
        public void LevelerIsUpstreamsUntilItWouldOvershoot()
        {
            var ceiled = new LevelingFilter { p_target = 30000, p_maxLevel = 1.9f, p_minLevel = 0.00001f, p_ceiling = 32000 };
            var upstream = new LevelingFilter { p_target = 30000, p_maxLevel = 1.9f, p_minLevel = 0.00001f };
            const int period = 441;
            float[] heights = { 8000, 20000, 12000, 26000, 5000, 30000, 15000 };
            bool overshot = false;
            for (int p = 0; p < 70; ++p)
            {
                for (int i = 0; i < period; ++i)
                {
                    float sample = i < 20 ? heights[p % heights.Length] * (float)Math.Sin(Math.PI * i / 20) : 0;
                    float y = ceiled.F(sample), reference = upstream.F(sample);
                    Assert.LessOrEqual(Math.Abs(y), ceiled.p_ceiling * (1 + 1e-6f), $"pulse {p}, sample {i}");
                    overshot |= Math.Abs(reference) > ceiled.p_ceiling;
                    if (!overshot) Assert.AreEqual(BitConverter.SingleToInt32Bits(reference), BitConverter.SingleToInt32Bits(y), $"pulse {p}, sample {i}");
                }
            }

            Assert.IsTrue(overshot, "upstream's leveler never overshot, so the ceiling isn't tried");
        }

        /// <summary>
        /// A rise 100 times louder after 5 s of quiet pulses, faster than the leveler's gain follows: no leveled sample passes
        /// the ceiling, the ceiling acts, and after its cuts the gain doesn't jump back up: from one sample to the next it
        /// rises at most upstream's tenth of the way to its top.
        /// </summary>
        [Test]
        public void CeilingHoldsASuddenRise()
        {
            var leveler = new LevelingFilter { p_target = 30000, p_maxLevel = 1.9f, p_minLevel = 0.00001f, p_ceiling = 64000 };
            const int period = 441;
            int cutAt = -1;
            float last = 0;
            for (int p = 0; p < 1000; ++p)
            {
                float height = p < 500 ? 1000 : 100000;
                for (int i = 0; i < period; ++i)
                {
                    float y = leveler.F(i < 20 ? height * (float)Math.Sin(Math.PI * i / 20) : 0);
                    Assert.LessOrEqual(Math.Abs(y), leveler.p_ceiling * (1 + 1e-6f), $"pulse {p}, sample {i}");
                    float gain = leveler.GetAttenuation();
                    if (cutAt < 0 && Math.Abs(y) >= leveler.p_ceiling * (1 - 1e-6f)) cutAt = p * period + i;
                    else if (cutAt >= 0)
                    {
                        float slowest = last + 0.1f * (leveler.p_maxLevel - last);
                        Assert.LessOrEqual(gain, slowest * (1 + 1e-6f), $"the gain jumped after the cut, pulse {p}, sample {i}");
                    }

                    last = gain;
                }
            }

            Assert.GreaterOrEqual(cutAt, 500 * period, "the ceiling acted before the rise");
        }

        /// <summary>
        /// A start, idle and a blip on the superbike preset at the Bench's (full) volume, on both cores: the leveler's ceiling
        /// holds what it would overshoot, so no output sample passes it, and some sit at it.
        /// </summary>
        [TestCase(EngineSoundGenerator.Core.Managed)]
        [TestCase(EngineSoundGenerator.Core.Dense)]
        public void BlipFromIdleStaysUnderTheCeiling(EngineSoundGenerator.Core core)
        {
            EngineAsset preset = TestEngines.Preset("inline-4-superbike");
            using var generator = new EngineSoundGenerator(preset.Build(), preset.ImpulseResponseSamples, 1, core: core,
                volume: 1f);
            var block = new float[generator.MaxBlockSamples];
            int Run(EngineSoundGenerator.Controls controls, double seconds, ref int held)
            {
                int over = 0;
                int blocks = (int)Math.Round(seconds * generator.SimulationFrequency / generator.StepsPerBlock);
                for (int b = 0; b < blocks; ++b)
                {
                    int n = generator.RenderBlock(controls, block, 0);
                    for (int i = 0; i < n; ++i)
                    {
                        if (Math.Abs(block[i]) * 32768 > Synthesizer.OutputCeiling) ++over;
                        if (Math.Abs(block[i]) * 32768 == Synthesizer.OutputCeiling) ++held;
                    }
                }

                return over;
            }

            int ceiling = 0;
            var c = new EngineSoundGenerator.Controls { ignition = true, starter = true, speedControl = 0.15 };
            Assert.AreEqual(0, Run(c, 1, ref ceiling), "start");
            c.starter = false;
            c.speedControl = 0;
            Assert.AreEqual(0, Run(c, 1, ref ceiling), "idle");
            c.speedControl = 1;
            Assert.AreEqual(0, Run(c, 0.5, ref ceiling), "blip");
            Assert.Greater(ceiling, 0, "the ceiling never acted");
        }
    }
}
