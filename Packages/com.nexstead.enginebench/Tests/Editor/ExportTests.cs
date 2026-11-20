// Checks of the Bench's own exports: a free rev, a start and a stop, rendered from a seed and written as WAV files.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.IO;
using Nexstead.EngineBench.Editor;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public sealed class ExportTests
    {
        /// <summary>Under the leveler's ceiling: no sample reaches 16-bit full scale.</summary>
        const float FullScale = 32767.0f / 32768.0f;

        static EngineRenderSource Source(string preset) => EngineRenderSource.Capture(TestEngines.Preset(preset));

        static void AssertSame(float[] a, float[] b, string what)
        {
            Assert.AreEqual(a.Length, b.Length, what + " length");
            for (int i = 0; i < a.Length; ++i)
            {
                if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) Assert.Fail($"{what}: sample {i} differs");
            }
        }

        static void AssertUnderFullScale(float[] samples, string what)
        {
            for (int i = 0; i < samples.Length; ++i) Assert.Less(Math.Abs(samples[i]), FullScale, $"{what}: sample {i}");
        }

        /// <summary>
        /// The V8's free rev to the limiter: the engine fires and reaches the limiter, the clip is 1 s of idle, the rev, the half
        /// second held at the limiter and 3 s of idle, under full scale, and the same seed renders it again sample for sample.
        /// </summary>
        [Test]
        public void FreeRevFiresAndRepeats()
        {
            EngineRenderSource source = Source(TestEngines.V8);
            EngineClip clip = EngineExports.FreeRev(source, 1);

            Assert.IsTrue(clip.fired, $"never above twice the starter's speed (max {clip.maxRpm:0} rpm)");
            Assert.IsTrue(clip.limiterCut, $"no limiter cut (max {clip.maxRpm:0} rpm)");
            Assert.Greater(clip.idleRpm, source.StarterRpm, "idle");
            Assert.Greater(clip.Seconds, 1.0 + 0.5 + 3.0);
            Assert.Less(clip.Seconds, 1.0 + 4.0 + 0.5 + 3.0 + 0.01);
            Assert.IsNull(clip.forcedInduction);
            AssertUnderFullScale(clip.mix, "free rev");

            AssertSame(clip.mix, EngineExports.FreeRev(source, 1).mix, "free rev again");
        }

        /// <summary>
        /// The V8's start catches and settles to an idle above the starter's speed (so it fires: the starter is off by then), its
        /// stop ends at rest, both under full scale, and the same seed renders both again sample for sample.
        /// </summary>
        [Test]
        public void StartAndStopFireAndRepeat()
        {
            EngineRenderSource source = Source(TestEngines.V8);
            (EngineClip start, EngineClip stop) = EngineExports.StartAndStop(source, 3);

            Assert.IsTrue(start.fired, $"the start didn't catch (idle {start.idleRpm:0} rpm, starter {source.StarterRpm:0})");
            Assert.Less(start.Seconds, 6.0 + 0.2 + 2.5 + 0.01);
            Assert.Greater(start.Seconds, 0.2 + 2.5);
            Assert.LessOrEqual(stop.endRpm, 30.0, "the stop doesn't reach rest");
            Assert.Less(stop.Seconds, 8.0 + 0.3 + 0.01);
            AssertUnderFullScale(start.mix, "start");
            AssertUnderFullScale(stop.mix, "stop");

            (EngineClip startAgain, EngineClip stopAgain) = EngineExports.StartAndStop(source, 3);
            AssertSame(start.mix, startAgain.mix, "start again");
            AssertSame(stop.mix, stopAgain.mix, "stop again");
        }

        /// <summary>
        /// A boosted engine's free rev writes three files: the mix as 16-bit PCM (the rendered samples exactly), and its engine and
        /// forced induction layers as 32-bit float, all 44.1 kHz mono and as long as the clip.
        /// </summary>
        [Test]
        public void BoostedEngineWritesItsLayers()
        {
            EngineClip clip = EngineExports.FreeRev(Source("boxer-4-turbo"), 1);
            Assert.IsNotNull(clip.forcedInduction);
            Assert.AreEqual(clip.mix.Length, clip.engine.Length);
            Assert.AreEqual(clip.mix.Length, clip.forcedInduction.Length);
            Assert.IsTrue(Array.Exists(clip.forcedInduction, v => v != 0), "the turbo is silent");

            string folder = Path.Combine(Path.GetTempPath(), "nx-engine-bench-export-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = EngineExports.Write(clip, folder, "Test/turbo");
                Assert.AreEqual(3, paths.Count);
                StringAssert.EndsWith("Test_turbo - free rev.wav", paths[0]);

                using (var reader = new BinaryReader(File.OpenRead(paths[0])))
                {
                    reader.ReadBytes(20);
                    Assert.AreEqual(1, reader.ReadInt16(), "PCM");
                    Assert.AreEqual(1, reader.ReadInt16(), "mono");
                    Assert.AreEqual(EngineRenderer.SampleRate, reader.ReadInt32());
                    reader.ReadBytes(6);
                    Assert.AreEqual(16, reader.ReadInt16());
                    reader.ReadBytes(4);
                    Assert.AreEqual(clip.mix.Length * 2, reader.ReadInt32(), "data size");
                    for (int i = 0; i < clip.mix.Length; ++i)
                    {
                        Assert.AreEqual(clip.mix[i] * 32768.0f, reader.ReadInt16(), $"sample {i}");
                    }
                }

                foreach (string layer in new[] { paths[1], paths[2] })
                {
                    using var reader = new BinaryReader(File.OpenRead(layer));
                    reader.ReadBytes(20);
                    Assert.AreEqual(3, reader.ReadInt16(), "float");
                    // RIFF size: WAVE, fmt (18), fact (4) and data chunks
                    Assert.AreEqual(4 + (8 + 18) + (8 + 4) + (8 + clip.mix.Length * 4), new FileInfo(layer).Length - 8, "file size");
                }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
