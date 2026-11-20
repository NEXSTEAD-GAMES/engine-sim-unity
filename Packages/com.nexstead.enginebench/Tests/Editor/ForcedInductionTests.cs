// Checks of the forced induction's render API: its separate and leveled outputs, its telemetry and the designer's round trip.
// MIT licence: LICENSE.md; engine-sim's notice and the community fork's: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Nexstead.EngineBench.Tests
{
    public sealed class ForcedInductionTests
    {
        const string Turbo = "boxer-4-turbo";
        const string Roots = "v8-roots-blown";

        static readonly EngineControls Crank = new EngineControls { ignition = true, starter = true, throttle = 0.15 };

        /// <summary>
        /// The one-buffer output levels the engine and its forced induction together, so it stays under the leveler's ceiling where
        /// the two outputs at the engine's gain add up past it: a turbo cranked, run to full throttle, and lifted (its blow-off),
        /// rendered both ways from the same seed.
        /// </summary>
        [Test]
        public void MixStaysUnderTheCeiling()
        {
            EngineRenderSource source = EngineRenderSource.Capture(TestEngines.Preset(Turbo));
            using var apart = new EngineRenderer(source, 5);
            using var mixed = new EngineRenderer(source, 5);
            var engine = new float[apart.MaxBlockSamples];
            var forcedInduction = new float[apart.MaxBlockSamples];
            var mix = new float[mixed.MaxBlockSamples];
            const float ceiling = Nexstead.EngineBench.Simulation.Synthesizer.OutputCeiling / 32768.0f;

            int cranking = apart.BlocksFor(1.5), pulling = apart.BlocksFor(1.5), lifted = apart.BlocksFor(0.5);
            bool sounded = false, sumPassed = false;
            for (int block = 0; block < cranking + pulling + lifted; ++block)
            {
                EngineControls controls = block < cranking ? Crank
                    : new EngineControls { ignition = true, throttle = block < cranking + pulling ? 1.0 : 0.0 };
                int n = apart.RenderBlock(controls, engine, forcedInduction);
                Assert.AreEqual(n, mixed.RenderBlock(controls, mix), $"block {block} sample count");
                for (int i = 0; i < n; ++i)
                {
                    Assert.LessOrEqual(Math.Abs(mix[i]), ceiling, $"block {block}, sample {i}");
                    Assert.LessOrEqual(Math.Abs(engine[i]), ceiling, $"block {block}, sample {i}");
                    sounded |= forcedInduction[i] != 0;
                    sumPassed |= Math.Abs(engine[i] + forcedInduction[i]) > ceiling;
                }
            }

            Assert.IsTrue(sounded, "the turbo made no sound");
            Assert.IsTrue(sumPassed, "the outputs at the engine's gain never passed the ceiling: the run doesn't show what the leveled mix is for");
        }

        /// <summary>
        /// The dense core renders a turbo's three outputs exactly as the managed core: engine, forced induction, and the two
        /// leveled together, all three from one run of each (as the Bench's exports render them).
        /// </summary>
        [Test]
        public void DenseCoreMatchesManaged()
        {
            EngineAsset engine = TestEngines.Preset(Turbo);
            using var managed = TestEngines.Generator(engine, 3, EngineSoundGenerator.Core.Managed);
            using var dense = TestEngines.Generator(engine, 3, EngineSoundGenerator.Core.Dense);
            int size = managed.MaxBlockSamples;
            float[] e1 = new float[size], f1 = new float[size], e2 = new float[size], f2 = new float[size], m1 = new float[size], m2 = new float[size];

            double blockSeconds = managed.StepsPerBlock / (double)managed.SimulationFrequency;
            int cranking = (int)Math.Round(1.5 / blockSeconds), pulling = (int)Math.Round(1.0 / blockSeconds), lifted = (int)Math.Round(0.5 / blockSeconds);
            for (int block = 0; block < cranking + pulling + lifted; ++block)
            {
                var controls = new EngineSoundGenerator.Controls
                {
                    ignition = true, starter = block < cranking,
                    speedControl = block < cranking ? 0.15 : block < cranking + pulling ? 1.0 : 0.0,
                };
                int n = managed.RenderBlockOutputs(controls, m1, e1, f1, 0);
                Assert.AreEqual(n, dense.RenderBlockOutputs(controls, m2, e2, f2, 0), $"block {block} sample count");
                for (int i = 0; i < n; ++i)
                {
                    if (BitConverter.SingleToInt32Bits(e1[i]) != BitConverter.SingleToInt32Bits(e2[i])) Assert.Fail($"engine: block {block}, sample {i} differs");
                    if (BitConverter.SingleToInt32Bits(f1[i]) != BitConverter.SingleToInt32Bits(f2[i])) Assert.Fail($"forced induction: block {block}, sample {i} differs");
                    if (BitConverter.SingleToInt32Bits(m1[i]) != BitConverter.SingleToInt32Bits(m2[i])) Assert.Fail($"mix: block {block}, sample {i} differs");
                }
            }
        }

        /// <summary>An engine without forced induction has a silent forced induction output and an engine output equal to the mix.</summary>
        [Test]
        public void NoForcedInductionIsSilent()
        {
            EngineRenderSource source = EngineRenderSource.Capture(TestEngines.Preset(TestEngines.V8));
            using var apart = new EngineRenderer(source, 2);
            using var mixed = new EngineRenderer(source, 2);
            Assert.AreEqual(ForcedInductionKind.None, apart.ForcedInductionKind);

            var engine = new float[apart.MaxBlockSamples];
            var forcedInduction = new float[apart.MaxBlockSamples];
            var mix = new float[mixed.MaxBlockSamples];
            for (int block = 0; block < apart.BlocksFor(1.0); ++block)
            {
                int n = apart.RenderBlock(Crank, engine, forcedInduction);
                mixed.RenderBlock(Crank, mix);
                for (int i = 0; i < n; ++i)
                {
                    Assert.AreEqual(0.0f, forcedInduction[i], $"block {block}, sample {i}");
                    if (BitConverter.SingleToInt32Bits(engine[i]) != BitConverter.SingleToInt32Bits(mix[i])) Assert.Fail($"block {block}, sample {i} differs");
                }
            }

            Assert.AreEqual(default(ForcedInductionTelemetry), apart.ForcedInduction);
        }

        /// <summary>
        /// The turbo preset spools at full throttle on the dyno to its 14 psi (96.5 kPa) wastegate set point, its whistle's tone
        /// is the tone of its shaft speed, and a lift blows off.
        /// </summary>
        [Test]
        public void TurboSpoolsAndBlowsOff()
        {
            using var renderer = new EngineRenderer(EngineRenderSource.Capture(TestEngines.Preset(Turbo)), 1);
            Assert.AreEqual(ForcedInductionKind.Turbocharger, renderer.ForcedInductionKind);
            Assert.AreEqual(BlowoffValveKind.Atmospheric, renderer.BlowoffValveKind);

            renderer.Run(Crank, renderer.BlocksFor(1.5), null, 0, out RpmSummary cranked);
            var held = new EngineControls { ignition = true, throttle = 1, dyno = true, dynoHold = true };
            int ramp = renderer.BlocksFor(1.0);
            for (int i = 1; i <= ramp; ++i)
            {
                held.dynoRpm = cranked.mean + (5000 - cranked.mean) * i / ramp;
                renderer.Run(held, 1, null, 0, out _);
            }

            renderer.Run(held, renderer.BlocksFor(2.0), null, 0, out _);
            ForcedInductionTelemetry spooled = renderer.ForcedInduction;
            Assert.AreEqual(96.5, spooled.boostKpa, 10.0, "boost at full throttle");
            Assert.Greater(spooled.whineLevel, 0.0);
            Assert.AreEqual(renderer.ToneHz(spooled.shaftHz), spooled.toneHz);

            double blowoff = 0;
            var lifted = new EngineControls { ignition = true, throttle = 0, dyno = true, dynoHold = true, dynoRpm = 5000 };
            for (int block = 0; block < renderer.BlocksFor(0.5); ++block)
            {
                renderer.Run(lifted, 1, null, 0, out _);
                blowoff = Math.Max(blowoff, renderer.ForcedInduction.blowoffLevel);
            }

            Assert.Greater(blowoff, 0.1, "no blow-off on the lift");
        }

        /// <summary>The Roots blower whines at its lobe frequency, locked to the revs, and has no blow-off sound.</summary>
        [Test]
        public void RootsBlowerWhinesAtItsLobeFrequency()
        {
            using var renderer = new EngineRenderer(EngineRenderSource.Capture(TestEngines.Preset(Roots)), 1);
            Assert.AreEqual(ForcedInductionKind.Roots, renderer.ForcedInductionKind);
            Assert.AreEqual(BlowoffValveKind.None, renderer.BlowoffValveKind);

            renderer.Run(Crank, renderer.BlocksFor(1.0), null, 0, out _);
            renderer.Run(new EngineControls { ignition = true, throttle = 1 }, renderer.BlocksFor(1.0), null, 0, out _);
            ForcedInductionTelemetry pulling = renderer.ForcedInduction;
            Assert.Greater(pulling.boostKpa, 0.0);
            Assert.Greater(pulling.whineLevel, 0.0);
            Assert.AreEqual(Math.Min(6.0 * pulling.shaftHz, 0.2 * EngineRenderer.SampleRate), pulling.toneHz, 1e-9);
        }

        /// <summary>
        /// A boosted engine's source without its forced induction renders as the same engine described with none, sample for
        /// sample, with a silent forced induction output; the engine asset keeps its turbo, and an engine without one is its own
        /// source.
        /// </summary>
        [Test]
        public void SourceWithoutForcedInductionRendersTheEngineWithNone()
        {
            EngineAsset turbo = TestEngines.Preset(Turbo);
            EngineRenderSource source = EngineRenderSource.Capture(turbo);
            Assert.AreEqual(ForcedInductionKind.Turbocharger, source.ForcedInductionKind);
            EngineRenderSource without = source.WithoutForcedInduction();
            Assert.AreEqual(ForcedInductionKind.None, without.ForcedInductionKind);
            Assert.AreSame(without, without.WithoutForcedInduction());
            Assert.IsTrue(turbo.Description.engine.HasForcedInduction, "the preset lost its turbo");

            EngineDescription none = turbo.Description.ShallowCopy();
            none.engine = turbo.Description.engine.ShallowCopy();
            none.engine.forcedInduction = null;
            var asset = ScriptableObject.CreateInstance<EngineAsset>();
            try
            {
                asset.Set(none, turbo.ImpulseResponses);
                using var expected = new EngineRenderer(EngineRenderSource.Capture(asset), 4);
                using var actual = new EngineRenderer(without, 4);
                Assert.AreEqual(ForcedInductionKind.None, actual.ForcedInductionKind);
                var e1 = new float[expected.MaxBlockSamples];
                var e2 = new float[actual.MaxBlockSamples];
                var f2 = new float[actual.MaxBlockSamples];
                int cranking = expected.BlocksFor(1.0), pulling = expected.BlocksFor(1.0);
                for (int block = 0; block < cranking + pulling; ++block)
                {
                    EngineControls controls = block < cranking ? Crank : new EngineControls { ignition = true, throttle = 1.0 };
                    int n = expected.RenderBlock(controls, e1);
                    Assert.AreEqual(n, actual.RenderBlock(controls, e2, f2), $"block {block} sample count");
                    for (int i = 0; i < n; ++i)
                    {
                        if (BitConverter.SingleToInt32Bits(e1[i]) != BitConverter.SingleToInt32Bits(e2[i])) Assert.Fail($"block {block}, sample {i} differs");
                        Assert.AreEqual(0.0f, f2[i], $"block {block}, sample {i}");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        /// <summary>Each forced induction preset's design builds its description again: the designer holds it whole.</summary>
        [TestCase(Turbo)]
        [TestCase(Roots)]
        public void DesignHoldsTheForcedInduction(string preset)
        {
            EngineAsset engine = TestEngines.Preset(preset);
            Assert.IsNotNull(engine.Design, preset + " has no design");
            ForcedInductionDescription made = EngineGenerator.Describe(engine.Design).engine.forcedInduction;
            ForcedInductionDescription asset = engine.Description.engine.forcedInduction;

            var differences = new System.Collections.Generic.List<string>();
            StructuralComparer.Collect(made, asset, "forcedInduction", differences);
            // Speeds go through rpm in the design: within a rounding step.
            differences.RemoveAll(d => d.StartsWith("forcedInduction.sequentialSpeed", StringComparison.Ordinal)
                                       || d.StartsWith("forcedInduction.maxSpeed", StringComparison.Ordinal));
            Assert.IsEmpty(differences, string.Join("\n", differences));
            Assert.AreEqual(asset.sequentialSpeed, made.sequentialSpeed, 1e-9 * Math.Max(1, asset.sequentialSpeed));
        }
    }
}
