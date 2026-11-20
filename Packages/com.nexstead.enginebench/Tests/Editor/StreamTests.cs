// Checks of the real-time stream: what reaches the audio thread is exactly what the generator
// and the resampler make, whatever the threads' timing.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public sealed class StreamTests
    {
        static EngineSoundGenerator NewGenerator() => TestEngines.Generator(TestEngines.Preset(TestEngines.V8), 9);

        static readonly EngineSoundGenerator.Controls Running = new EngineSoundGenerator.Controls
        {
            speedControl = 0.3, ignition = true, starter = true,
        };

        /// <summary>
        /// With fixed controls, the frames read through the threaded stream equal the generator's blocks converted by the
        /// resampler on one thread: the hand-over loses, repeats or reorders nothing.
        /// </summary>
        [TestCase(44100)]
        [TestCase(48000)]
        public void StreamDeliversWhatTheGeneratorMakes(int outputRate)
        {
            const int frames = 22050;

            // Expected: one thread, block by block.
            using EngineSoundGenerator direct = NewGenerator();
            var resampler = new Resampler(EngineSoundGenerator.AudioSampleRate, outputRate, direct.MaxBlockSamples);
            var block = new float[direct.MaxBlockSamples];
            var converted = new float[resampler.MaxOutputCount(direct.MaxBlockSamples)];
            var expected = new List<float>();
            while (expected.Count < frames)
            {
                int n = direct.RenderBlock(Running, block, 0);
                int m = resampler.Process(block, 0, n, converted, 0, converted.Length);
                for (int i = 0; i < m; ++i) expected.Add(converted[i]);
            }

            // Actual: the simulation thread fills the ring; this thread reads like the audio thread, in odd sizes.
            var actual = new List<float>();
            using (EngineSoundGenerator generator = NewGenerator())
            using (var stream = new EngineSoundStream(generator, outputRate, 0.02))
            {
                stream.SetControls(Running);
                stream.Start();
                var data = new float[333];
                Stopwatch watch = Stopwatch.StartNew();
                while (actual.Count < frames)
                {
                    Assert.IsNull(stream.Failure, "simulation thread failed");
                    Assert.Less(watch.Elapsed.TotalSeconds, 120, $"only {actual.Count} of {frames} frames arrived");
                    int got = stream.Read(data, 1, 1f);
                    for (int i = 0; i < got; ++i) actual.Add(data[i]);
                }

                Assert.AreEqual(0, stream.OverflowFrames, "frames dropped by the producer");
            }

            for (int i = 0; i < frames; ++i)
            {
                if (BitConverter.SingleToInt32Bits(actual[i]) != BitConverter.SingleToInt32Bits(expected[i]))
                {
                    Assert.Fail($"frame {i}: {actual[i]:R} vs {expected[i]:R}");
                }
            }
        }

        /// <summary>Every frame read is copied to each channel, scaled by the gain.</summary>
        [Test]
        public void ReadFillsEveryChannel()
        {
            using (EngineSoundGenerator generator = NewGenerator())
            using (var stream = new EngineSoundStream(generator, 44100, 0.02))
            {
                stream.SetControls(Running);
                for (int b = 0; b < 20; ++b) stream.ProduceBlock();

                var data = new float[2 * 100];
                Assert.AreEqual(100, stream.Read(data, 2, 0.5f));
                for (int f = 0; f < 100; ++f) Assert.AreEqual(data[2 * f], data[2 * f + 1], $"frame {f}");
            }
        }

        /// <summary>Frames that aren't ready come out as silence and count as underrun.</summary>
        [Test]
        public void UnderrunIsSilence()
        {
            using (EngineSoundGenerator generator = NewGenerator())
            using (var stream = new EngineSoundStream(generator, 44100, 0.02))
            {
                var data = new float[64];
                for (int i = 0; i < data.Length; ++i) data[i] = 1f;

                Assert.AreEqual(0, stream.Read(data, 1, 1f));
                foreach (float v in data) Assert.AreEqual(0f, v);
                Assert.AreEqual(64.0 / 44100, stream.UnderrunSeconds, 1e-12);
            }
        }
    }
}
