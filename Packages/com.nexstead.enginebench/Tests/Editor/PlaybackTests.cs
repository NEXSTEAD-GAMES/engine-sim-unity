// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Nexstead.EngineBench.Simulation;
using UnityEngine.Profiling;

namespace Nexstead.EngineBench.Tests
{
    public sealed class PlaybackTests
    {
        // ---- AudioRingBuffer ----

        [Test]
        public void RingBufferKeepsOrderAcrossWrapAround()
        {
            var ring = new AudioRingBuffer(8);
            var write = new float[5];
            var read = new float[5];
            float next = 0, expected = 0;

            // 5 in, 5 out, repeatedly: the positions wrap round the 8-sample ring many times.
            for (int round = 0; round < 20; ++round)
            {
                for (int i = 0; i < 5; ++i) write[i] = next++;
                Assert.AreEqual(5, ring.Write(write, 0, 5));
                Assert.AreEqual(5, ring.Count);
                Assert.AreEqual(5, ring.Read(read, 0, 5));
                for (int i = 0; i < 5; ++i) Assert.AreEqual(expected++, read[i]);
                Assert.AreEqual(0, ring.Count);
            }
        }

        [Test]
        public void RingBufferWritesAndReadsWhatFits()
        {
            var ring = new AudioRingBuffer(8);
            var data = new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            Assert.AreEqual(8, ring.Write(data, 0, 10), "a full ring takes only its capacity");
            Assert.AreEqual(0, ring.Free);
            Assert.AreEqual(0, ring.Write(data, 8, 2), "nothing fits in a full ring");

            var target = new float[10];
            Assert.AreEqual(3, ring.Read(target, 0, 3));
            Assert.AreEqual(new float[] { 1, 2, 3 }, new[] { target[0], target[1], target[2] });
            Assert.AreEqual(3, ring.Free);

            Assert.AreEqual(2, ring.Write(data, 8, 2));
            Assert.AreEqual(7, ring.Read(target, 3, 10), "an empty-ing read returns only what is there");
            Assert.AreEqual(new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, target);
            Assert.AreEqual(0, ring.Read(target, 0, 1), "nothing to read from an empty ring");

            ring.Clear();
            Assert.AreEqual(0, ring.Count);
            Assert.AreEqual(8, ring.Free);
        }

        /// <summary>
        /// A producer thread writes 0 … N − 1 in odd-sized chunks while this thread reads in other odd sizes: every value
        /// arrives once, in order, whatever the timing. The timeout only bounds a hang.
        /// </summary>
        [Test]
        public void RingBufferHandsOverEverySampleBetweenThreads()
        {
            const int total = 200_000; // floats hold every integer below 2^24 exactly
            var ring = new AudioRingBuffer(64);
            Exception producerFailure = null;

            var producer = new Thread(() =>
            {
                try
                {
                    int[] chunkSizes = { 1, 7, 13, 31, 63 };
                    var chunk = new float[63];
                    int value = 0, c = 0;
                    while (value < total)
                    {
                        int size = Math.Min(chunkSizes[c++ % chunkSizes.Length], total - value);
                        for (int i = 0; i < size; ++i) chunk[i] = value + i;

                        int done = 0;
                        while (done < size)
                        {
                            int n = ring.Write(chunk, done, size - done);
                            if (n == 0) Thread.Yield();
                            done += n;
                        }

                        value += size;
                    }
                }
                catch (Exception e)
                {
                    producerFailure = e;
                }
            }) { IsBackground = true };
            producer.Start();

            int[] readSizes = { 5, 11, 29, 3, 64 };
            var target = new float[64];
            int expected = 0, r = 0;
            var watch = Stopwatch.StartNew();
            while (expected < total && producerFailure == null)
            {
                Assert.Less(watch.Elapsed.TotalSeconds, 60.0, $"hung after {expected} samples");

                int n = ring.Read(target, 0, readSizes[r++ % readSizes.Length]);
                if (n == 0)
                {
                    Thread.Yield();
                    continue;
                }

                for (int i = 0; i < n; ++i)
                {
                    if (target[i] != expected) Assert.Fail($"sample {expected} read as {target[i]}");
                    ++expected;
                }
            }

            Assert.IsTrue(producer.Join(TimeSpan.FromSeconds(60)), "producer did not finish");
            Assert.IsNull(producerFailure, producerFailure?.ToString());
            Assert.AreEqual(total, expected);
            Assert.AreEqual(0, ring.Count);
        }

        // ---- Resampler ----

        // Measured on 2026-11-07 (.NET 8): amplitude within 0.0001 dB and error −100.3 dB or better for every rate and
        // frequency below; rejection −94.4 dB at 12.5 kHz, −100.8 dB at 14 kHz into 22.05 kHz. The bounds leave margin.
        const double AmplitudeBoundDb = 0.01;
        const double ErrorBoundDb = -90.0;
        const double RejectionBoundDb = -90.0;

        const int SynthesizerRate = 44100;

        [Test]
        public void EqualRatesPassThroughExactly()
        {
            var resampler = new Resampler(SynthesizerRate, SynthesizerRate);
            Assert.IsTrue(resampler.IsPassThrough);
            Assert.AreEqual(0, resampler.Latency);

            float[] input = Noise(10_000, 5);
            float[] output = Run(resampler, input, 110);

            Assert.AreEqual(input.Length, output.Length, "no latency");
            for (int i = 0; i < input.Length; ++i)
            {
                Assert.AreEqual(BitConverter.SingleToInt32Bits(input[i]), BitConverter.SingleToInt32Bits(output[i]), $"sample {i}");
            }
        }

        static IEnumerable<TestCaseData> PassbandCases()
        {
            foreach (int outputRate in new[] { 48000, 96000, 22050 })
            {
                foreach (double frequency in new[] { 100.0, 1000.0, 8000.0, 15000.0 })
                {
                    // Inside the passband: below 0.45 of the lower rate.
                    if (frequency > 0.45 * Math.Min(SynthesizerRate, outputRate)) continue;
                    yield return new TestCaseData(outputRate, frequency).SetName($"SineSurvivesConversion({outputRate},{frequency})");
                }
            }
        }

        /// <summary>
        /// A sine inside the passband comes out at the same amplitude, matching the exact sine at the output rate (output m is
        /// the signal at input time m × 44100 ÷ outputRate) to within <see cref="ErrorBoundDb"/>.
        /// </summary>
        [TestCaseSource(nameof(PassbandCases))]
        public void SineSurvivesConversion(int outputRate, double frequency)
        {
            const double amplitude = 0.5;
            var resampler = new Resampler(SynthesizerRate, outputRate);
            float[] input = Sine(SynthesizerRate, frequency, amplitude);
            float[] output = Run(resampler, input, 110);

            double error = 0, reference = 0, power = 0;
            for (int m = 0; m < output.Length; ++m)
            {
                double t = (double)m * SynthesizerRate / outputRate;
                // Skip both ends, where the window reaches past the signal.
                if (t < resampler.Latency + 1 || t > input.Length - resampler.Latency - 2) continue;

                double exact = amplitude * Math.Sin(2 * Math.PI * frequency * t / SynthesizerRate);
                error += (output[m] - exact) * (output[m] - exact);
                reference += exact * exact;
                power += (double)output[m] * output[m];
            }

            double amplitudeDb = 10 * Math.Log10(power / reference);
            double errorDb = 10 * Math.Log10(error / reference);
            Assert.LessOrEqual(Math.Abs(amplitudeDb), AmplitudeBoundDb, "amplitude change (dB)");
            Assert.LessOrEqual(errorDb, ErrorBoundDb, "error relative to the exact sine (dB)");
        }

        /// <summary>Downsampling to 22.05 kHz rejects a 14 kHz sine (above the output's Nyquist) instead of aliasing it to 8.05 kHz.</summary>
        [Test]
        public void DownsamplingRejectsAboveNyquist()
        {
            const double amplitude = 0.5;
            var resampler = new Resampler(SynthesizerRate, 22050);
            float[] input = Sine(SynthesizerRate, 14000.0, amplitude);
            float[] output = Run(resampler, input, 110);

            double power = 0;
            int count = 0;
            for (int m = 0; m < output.Length; ++m)
            {
                double t = (double)m * SynthesizerRate / 22050;
                if (t < resampler.Latency + 1 || t > input.Length - resampler.Latency - 2) continue;
                power += (double)output[m] * output[m];
                ++count;
            }

            double inputRms = amplitude / Math.Sqrt(2);
            double levelDb = 20 * Math.Log10(Math.Sqrt(power / count) / inputRms);
            Assert.LessOrEqual(levelDb, RejectionBoundDb, "level left of a 14 kHz sine (dB)");
        }

        /// <summary>The same input in any chunking gives the same output, bit for bit.</summary>
        [TestCase(48000)]
        [TestCase(96000)]
        [TestCase(22050)]
        public void ChunkingDoesNotChangeTheOutput(int outputRate)
        {
            float[] input = Noise(SynthesizerRate / 2, 3);
            float[] reference = Run(new Resampler(SynthesizerRate, outputRate), input, 4096);

            foreach (int chunk in new[] { 1, 7, 110, 1000 })
            {
                float[] output = Run(new Resampler(SynthesizerRate, outputRate), input, chunk);
                Assert.AreEqual(reference.Length, output.Length, $"chunk {chunk}: output count");
                for (int i = 0; i < output.Length; ++i)
                {
                    if (BitConverter.SingleToInt32Bits(reference[i]) != BitConverter.SingleToInt32Bits(output[i]))
                    {
                        Assert.Fail($"chunk {chunk}: sample {i} differs");
                    }
                }
            }
        }

        /// <summary>Converting and handing over audio allocates nothing once running.</summary>
        [Test]
        public void ConversionAndHandOverDoNotAllocate()
        {
            var resampler = new Resampler(SynthesizerRate, 48000);
            var ring = new AudioRingBuffer(4096);
            float[] input = Noise(110, 9);
            var converted = new float[resampler.MaxOutputCount(input.Length)];
            var drained = new float[converted.Length];

            void Block()
            {
                int n = resampler.Process(input, 0, input.Length, converted, 0, converted.Length);
                ring.Write(converted, 0, n);
                ring.Read(drained, 0, n);
            }

            for (int i = 0; i < 50; ++i) Block();

            // Positive control: the recorder sees one allocation.
            Assert.AreEqual(1, CountAllocations(() => GC.KeepAlive(new float[4])), "GC.Alloc recorder control");
            Assert.AreEqual(0, CountAllocations(() =>
            {
                for (int i = 0; i < 200; ++i) Block();
            }), "allocations in 200 blocks");
        }

        // ---- helpers ----

        static float[] Run(Resampler resampler, float[] input, int chunk)
        {
            var output = new List<float>();
            var buffer = new float[resampler.MaxOutputCount(chunk)];
            for (int offset = 0; offset < input.Length; offset += chunk)
            {
                int n = Math.Min(chunk, input.Length - offset);
                int got = resampler.Process(input, offset, n, buffer, 0, buffer.Length);
                for (int i = 0; i < got; ++i) output.Add(buffer[i]);
            }

            return output.ToArray();
        }

        static float[] Sine(int length, double frequency, double amplitude)
        {
            var samples = new float[length];
            for (int n = 0; n < length; ++n) samples[n] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * n / SynthesizerRate));
            return samples;
        }

        static float[] Noise(int length, ulong seed)
        {
            var random = new DeterministicRandom();
            random.Seed(seed, 0);
            var samples = new float[length];
            for (int n = 0; n < length; ++n) samples[n] = (float)(random.NextDouble() - 0.5);
            return samples;
        }

        static int CountAllocations(Action action)
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            action();

            recorder.enabled = false;
            recorder.CollectFromAllThreads();
            return recorder.sampleBlockCount;
        }
    }
}
