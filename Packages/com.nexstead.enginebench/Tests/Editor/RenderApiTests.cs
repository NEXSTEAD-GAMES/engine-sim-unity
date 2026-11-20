// Checks of the render API (EngineRenderSource, EngineRenderer, EngineControls): what tools built on the Bench rely on.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public sealed class RenderApiTests
    {
        static readonly EngineControls Crank = new EngineControls { ignition = true, starter = true, throttle = 0.15 };

        /// <summary>The captured figures, in rpm, of the small-block V8: redline 6500, rev limit 6800, 8 cylinders.</summary>
        [Test]
        public void SourceHasTheEnginesFigures()
        {
            EngineRenderSource source = EngineRenderSource.Capture(TestEngines.Preset(TestEngines.V8));
            Assert.AreEqual(TestEngines.V8, source.Name);
            Assert.AreEqual(6500, source.RedlineRpm, 1e-6);
            Assert.AreEqual(6800, source.RevLimitRpm, 1e-6);
            Assert.AreEqual(8, source.Cylinders);
            Assert.Greater(source.StarterRpm, 0);
        }

        /// <summary>
        /// The renderer is the generator the Bench plays (tuned, at full volume, Burst-compiled when Burst is on):
        /// the same samples, bit for bit, under the same controls.
        /// </summary>
        [Test]
        public void RendererMatchesTheGenerator()
        {
            EngineAsset engine = TestEngines.Preset(TestEngines.V8);
            using var renderer = new EngineRenderer(EngineRenderSource.Capture(engine), 3);
            using var generator = new EngineSoundGenerator(engine.Build(), engine.ImpulseResponseSamples, 3,
                denseRenderer: BurstBlockRenderer.Renderer, volume: 1f, mix: engine.TunedMix());

            var a = new float[renderer.MaxBlockSamples];
            var b = new float[generator.MaxBlockSamples];
            for (int block = 0; block < 1200; ++block)
            {
                bool cranking = block < 400;
                double throttle = cranking ? 0.15 : 0.6;
                int n = renderer.RenderBlock(new EngineControls { ignition = true, starter = cranking, throttle = throttle }, a);
                var direct = new EngineSoundGenerator.Controls { ignition = true, starter = cranking, speedControl = throttle };
                Assert.AreEqual(n, generator.RenderBlock(direct, b, 0), $"block {block} sample count");
                for (int i = 0; i < n; ++i)
                {
                    if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) Assert.Fail($"block {block}, sample {i} differs");
                }
            }

            Assert.AreEqual(generator.Rpm, renderer.Rpm);
        }

        /// <summary>
        /// The dyno, holding, keeps the crank at its rpm exactly: a bank's held takes depend on it. Crank, ramp the dyno to
        /// 3000 rpm at full throttle, settle, then every block of a run reads 3000.
        /// </summary>
        [Test]
        public void DynoHoldsItsRpm()
        {
            using var renderer = new EngineRenderer(EngineRenderSource.Capture(TestEngines.Preset(TestEngines.V8)), 1);
            renderer.Run(Crank, renderer.BlocksFor(1.0), null, 0, out RpmSummary cranked);

            var held = new EngineControls { ignition = true, throttle = 1, dyno = true, dynoHold = true };
            int ramp = renderer.BlocksFor(1.0);
            for (int i = 1; i <= ramp; ++i)
            {
                held.dynoRpm = cranked.mean + (3000 - cranked.mean) * i / ramp;
                renderer.RenderBlock(held, new float[renderer.MaxBlockSamples]);
            }

            held.dynoRpm = 3000;
            renderer.Run(held, renderer.BlocksFor(0.5), null, 0, out _);
            int blocks = renderer.BlocksFor(0.5);
            var output = new float[blocks * renderer.MaxBlockSamples];
            int count = renderer.Run(held, blocks, output, 0, out RpmSummary rpm);

            Assert.Greater(count, 0);
            Assert.AreEqual(3000, rpm.mean, 0.01);
            Assert.AreEqual(3000, rpm.min, 0.01);
            Assert.AreEqual(3000, rpm.max, 0.01);
            Assert.IsFalse(rpm.limiterCut);
        }

        /// <summary>A source captured on the main thread renders on a worker thread exactly as on the main thread.</summary>
        [Test]
        public void RendersOnAWorkerThread()
        {
            EngineRenderSource source = EngineRenderSource.Capture(TestEngines.Preset(TestEngines.V8));
            float[] Render()
            {
                using var renderer = new EngineRenderer(source, 7);
                int blocks = renderer.BlocksFor(2.0);
                var output = new float[blocks * renderer.MaxBlockSamples];
                int count = renderer.Run(Crank, blocks, output, 0, out _);
                Array.Resize(ref output, count);
                return output;
            }

            float[] main = Render();
            float[] worker = Task.Run(Render).GetAwaiter().GetResult();
            Assert.AreEqual(main.Length, worker.Length);
            for (int i = 0; i < main.Length; ++i)
            {
                if (BitConverter.SingleToInt32Bits(main[i]) != BitConverter.SingleToInt32Bits(worker[i])) Assert.Fail($"sample {i} differs");
            }
        }

        /// <summary>Full throttle in neutral runs into the rev limiter, and the run reports the cut.</summary>
        [Test]
        public void RunReportsTheLimiterCut()
        {
            EngineRenderSource source = EngineRenderSource.Capture(TestEngines.Preset(TestEngines.V8));
            using var renderer = new EngineRenderer(source, 1);
            renderer.Run(Crank, renderer.BlocksFor(1.5), null, 0, out _);
            var full = new EngineControls { ignition = true, throttle = 1 };
            renderer.Run(full, renderer.BlocksFor(2.5), null, 0, out _);
            renderer.Run(full, renderer.BlocksFor(1.0), null, 0, out RpmSummary rpm);

            Assert.IsTrue(rpm.limiterCut, $"no cut; peaked at {rpm.max:0} rpm");
            Assert.Greater(rpm.max, 0.95 * source.RevLimitRpm);
        }
    }
}
