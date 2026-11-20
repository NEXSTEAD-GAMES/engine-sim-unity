// Checks of the capture the Bench's visuals read: the same on both cores, silent, and true to the engine's firing.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public sealed class CaptureTests
    {
        static readonly EngineSoundGenerator.Controls Crank = new EngineSoundGenerator.Controls { ignition = true, starter = true, speedControl = 0.15 };
        static readonly EngineSoundGenerator.Controls Revving = new EngineSoundGenerator.Controls { ignition = true, speedControl = 0.6 };

        static EngineSoundGenerator Generator(EngineSoundGenerator.Core core, bool capture,
            EngineSoundGenerator.DenseBlockRenderer renderer = null)
        {
            EngineAsset engine = TestEngines.Preset(TestEngines.V8);
            return new EngineSoundGenerator(engine.Build(), engine.ImpulseResponseSamples, 5, Synthesizer.ConvolutionMode.Partitioned,
                0.0025, core, renderer, 1f, engine.TunedMix(), capture);
        }

        /// <summary>Renders <paramref name="blocks"/> blocks: cranking for the first 600 (1.5 s), then revving; returns the samples.</summary>
        static List<float> Run(EngineSoundGenerator generator, int blocks)
        {
            var output = new List<float>();
            var block = new float[generator.MaxBlockSamples];
            for (int b = 0; b < blocks; ++b)
            {
                int n = generator.RenderBlock(b < 600 ? Crank : Revving, block, 0);
                for (int i = 0; i < n; ++i) output.Add(block[i]);
            }

            return output;
        }

        static CaptureSteps Read(EngineCapture capture, long first, int count)
        {
            var steps = new CaptureSteps(count, capture.Cylinders, capture.Exhausts);
            Assert.IsTrue(capture.ReadSteps(first, steps), "steps not readable");
            return steps;
        }

        /// <summary>The longest window a reader may take: the ring less a block's margin.</summary>
        static int Window(EngineCapture capture) => capture.ReadableSteps;

        /// <summary>
        /// The managed port and the dense core (run as managed code) record the same values at every step, bit for bit:
        /// crank angle, pistons, valve lifts, ignitions and the exhaust signal.
        /// </summary>
        [Test]
        public void BothCoresCaptureAlike()
        {
            using EngineSoundGenerator managed = Generator(EngineSoundGenerator.Core.Managed, true);
            using EngineSoundGenerator dense = Generator(EngineSoundGenerator.Core.Dense, true);
            Run(managed, 800);
            Run(dense, 800);

            EngineCapture a = managed.Capture, b = dense.Capture;
            Assert.AreEqual(a.PublishedSteps, b.PublishedSteps);
            int count = Window(a);
            long first = a.PublishedSteps - count;
            CaptureSteps x = Read(a, first, count), y = Read(b, first, count);
            AssertSame(x.crankAngle, y.crankAngle, "crank angle");
            AssertSame(x.pistonTravel, y.pistonTravel, "piston travel");
            AssertSame(x.rodX, y.rodX, "rod x");
            AssertSame(x.rodY, y.rodY, "rod y");
            AssertSame(x.rodAngle, y.rodAngle, "rod angle");
            AssertSame(x.intakeLift, y.intakeLift, "intake lift");
            AssertSame(x.exhaustLift, y.exhaustLift, "exhaust lift");
            AssertSame(x.exhaustInput, y.exhaustInput, "exhaust signal");
            CollectionAssert.AreEqual(x.fired, y.fired, "ignitions");
            Assert.AreEqual(a.Rpm, b.Rpm);
            Assert.AreEqual(a.DynoTorque, b.DynoTorque);
        }

        /// <summary>
        /// The captured rod poses agree with the pistons: each rod's little end (along the rod's axis from its body, by the
        /// description's length and centre of mass) stays on its bank's axis, where the cylinder wall holds the wrist pin, and
        /// moves with the captured piston travel, a constant distance from it.
        /// </summary>
        [Test]
        public void RodsFollowTheirPistons()
        {
            EngineAsset engine = TestEngines.Preset(TestEngines.V8);
            using EngineSoundGenerator generator = Generator(EngineSoundGenerator.Core.Dense, true, BurstBlockRenderer.Renderer);
            Run(generator, 800);
            EngineCapture capture = generator.Capture;
            int count = Window(capture);
            CaptureSteps steps = Read(capture, capture.PublishedSteps - count, count);

            EngineParametersDescription d = engine.Description.engine;
            var bankOf = new int[capture.Cylinders];
            for (int b = 0, c = 0; b < d.cylinderBanks.Length; ++b)
            {
                for (int k = 0; k < d.cylinderBanks[b].cylinderCount; ++k) bankOf[c++] = b;
            }

            double worstOffAxis = 0, worstSpread = 0;
            for (int c = 0; c < capture.Cylinders; ++c)
            {
                ConnectingRodDescription rod = d.connectingRods[c];
                CylinderBankDescription bank = d.cylinderBanks[bankOf[c]];
                double littleEnd = rod.length / 2 - rod.centerOfMass;
                double dx = Math.Cos(bank.angle + Constants.pi / 2), dy = Math.Sin(bank.angle + Constants.pi / 2);
                double low = double.MaxValue, high = double.MinValue;
                for (int s = 0; s < count; ++s)
                {
                    int k = s * capture.Cylinders + c;
                    double angle = steps.rodAngle[k];
                    double ex = steps.rodX[k] - Math.Sin(angle) * littleEnd - bank.positionX;
                    double ey = steps.rodY[k] + Math.Cos(angle) * littleEnd - bank.positionY;
                    worstOffAxis = Math.Max(worstOffAxis, Math.Abs(ex * dy - ey * dx));
                    double offset = (ex * dx + ey * dy) - steps.pistonTravel[k];
                    low = Math.Min(low, offset);
                    high = Math.Max(high, offset);
                }

                worstSpread = Math.Max(worstSpread, high - low);
            }

            Assert.Less(worstOffAxis, 0.5e-3, $"a little end strayed {worstOffAxis * 1000:0.000} mm off its bank's axis");
            Assert.Less(worstSpread, 0.5e-3, $"a little end's distance from its piston varied by {worstSpread * 1000:0.000} mm");
        }

        /// <summary>Recording changes no sample: the same run with and without capture, Burst-compiled when Burst is on.</summary>
        [Test]
        public void CaptureLeavesTheSoundUnchanged()
        {
            using EngineSoundGenerator with = Generator(EngineSoundGenerator.Core.Dense, true, BurstBlockRenderer.Renderer);
            using EngineSoundGenerator without = Generator(EngineSoundGenerator.Core.Dense, false, BurstBlockRenderer.Renderer);
            List<float> a = Run(with, 800), b = Run(without, 800);
            AssertSame(a.ToArray(), b.ToArray(), "samples");
        }

        /// <summary>
        /// Held by the dyno at 3000 rpm (an engine cycle every 40 ms), every cylinder fires once per cycle: over the window a
        /// reader can take, each fires the window's whole cycles, or one more.
        /// </summary>
        [Test]
        public void EveryCylinderFiresOncePerCycle()
        {
            using EngineSoundGenerator generator = Generator(EngineSoundGenerator.Core.Dense, true, BurstBlockRenderer.Renderer);
            var block = new float[generator.MaxBlockSamples];
            for (int b = 0; b < 400; ++b) generator.RenderBlock(Crank, block, 0);
            var held = new EngineSoundGenerator.Controls { ignition = true, speedControl = 1, dynoEnabled = true, dynoHold = true };
            double from = generator.Rpm;
            for (int b = 1; b <= 400; ++b)
            {
                held.dynoSpeed = Units.Rpm(from + (3000 - from) * b / 400);
                generator.RenderBlock(held, block, 0);
            }

            for (int b = 0; b < 400; ++b) generator.RenderBlock(held, block, 0);

            EngineCapture capture = generator.Capture;
            int count = Window(capture);
            CaptureSteps steps = Read(capture, capture.PublishedSteps - count, count);
            double cycles = count / (double)capture.SimulationFrequency * 3000 / 120;
            for (int c = 0; c < capture.Cylinders; ++c)
            {
                int fires = 0;
                for (int s = 0; s < count; ++s) fires += steps.fired[s * capture.Cylinders + c];
                Assert.That(fires, Is.InRange((int)Math.Floor(cycles), (int)Math.Ceiling(cycles)), $"cylinder {c + 1} fired {fires} times in {cycles:0.00} cycles");
            }
        }

        /// <summary>While the rev limiter cuts the ignition, no cylinder fires: a block that starts and ends in a cut has no ignition.</summary>
        [Test]
        public void NothingFiresDuringALimiterCut()
        {
            using EngineSoundGenerator generator = Generator(EngineSoundGenerator.Core.Dense, true, BurstBlockRenderer.Renderer);
            var block = new float[generator.MaxBlockSamples];
            for (int b = 0; b < 600; ++b) generator.RenderBlock(Crank, block, 0);

            var full = new EngineSoundGenerator.Controls { ignition = true, speedControl = 1 };
            EngineCapture capture = generator.Capture;
            int inCut = 0;
            bool wasCutting = false;
            for (int b = 0; b < 1600; ++b)
            {
                long first = capture.PublishedSteps;
                generator.RenderBlock(full, block, 0);
                bool cutting = generator.RevLimiterCutting;
                if (wasCutting && cutting)
                {
                    CaptureSteps steps = Read(capture, first, generator.StepsPerBlock);
                    foreach (byte fired in steps.fired) Assert.AreEqual(0, fired, $"a cylinder fired in block {b}, inside a limiter cut");
                    ++inCut;
                }

                wasCutting = cutting;
            }

            Assert.Greater(inCut, 0, "the limiter never cut for two blocks running");
        }

        /// <summary>Steps not published yet, or overtaken by the writer, can't be read.</summary>
        [Test]
        public void ReaderRefusesStepsItCantHave()
        {
            using EngineSoundGenerator generator = Generator(EngineSoundGenerator.Core.Dense, true, BurstBlockRenderer.Renderer);
            Run(generator, 200);
            EngineCapture capture = generator.Capture;
            long published = capture.PublishedSteps;
            Assert.IsFalse(capture.ReadSteps(published - 10, new CaptureSteps(20, capture.Cylinders, capture.Exhausts)), "unpublished steps read");
            Assert.IsFalse(capture.ReadSteps(0, new CaptureSteps(10, capture.Cylinders, capture.Exhausts)), "overwritten steps read");
            Assert.IsTrue(capture.ReadSteps(published - 10, new CaptureSteps(10, capture.Cylinders, capture.Exhausts)));
        }

        /// <summary>The audio tap holds the last samples rendered, exactly.</summary>
        [Test]
        public void AudioTapHoldsTheLastSamples()
        {
            using EngineSoundGenerator generator = Generator(EngineSoundGenerator.Core.Dense, true, BurstBlockRenderer.Renderer);
            List<float> rendered = Run(generator, 700);
            var tap = new float[4096];
            Assert.AreEqual(tap.Length, generator.Capture.ReadAudio(tap));
            AssertSame(rendered.GetRange(rendered.Count - tap.Length, tap.Length).ToArray(), tap, "audio tap");
        }

        static void AssertSame(float[] expected, float[] actual, string what)
        {
            Assert.AreEqual(expected.Length, actual.Length, what + " length");
            for (int i = 0; i < expected.Length; ++i)
            {
                if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
                {
                    Assert.Fail($"{what}: value {i} differs ({expected[i]:R} vs {actual[i]:R})");
                }
            }
        }
    }
}
