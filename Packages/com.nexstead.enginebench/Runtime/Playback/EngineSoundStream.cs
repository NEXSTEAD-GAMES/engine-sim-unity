// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Diagnostics;
using System.Threading;
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// Plays an <see cref="EngineSoundGenerator"/> in real time. A simulation thread renders blocks under the latest controls,
    /// converts them to the output rate and keeps a ring buffer filled to the target latency; the audio thread copies samples out
    /// with <see cref="Read"/>, which never blocks, signals or allocates. If the simulation can't keep up, the reader gets silence for
    /// the missing frames and they are counted as underrun.
    /// </summary>
    /// <remarks>
    /// Threads: <see cref="SetControls"/>, <see cref="SetMix"/>, <see cref="Start"/>, <see cref="Stop"/> and <see cref="Dispose"/> from one owner
    /// thread; <see cref="Read"/> from one audio thread; the telemetry properties from anywhere.
    /// </remarks>
    public sealed class EngineSoundStream : IDisposable
    {
        readonly EngineSoundGenerator generator;
        readonly Resampler resampler;
        readonly AudioRingBuffer ring;
        readonly int outputSampleRate;
        readonly int targetSamples;

        readonly float[] block;
        readonly float[] converted;
        readonly float[] readScratch = new float[4096];

        readonly object controlsLock = new object();
        EngineSoundGenerator.Controls controls;
        EngineSoundMix pendingMix;
        bool mixPending;

        Thread thread;
        volatile bool running;
        volatile Exception failure;

        long producedFrames;
        long underrunFrames;
        long overflowFrames;
        long renderTicks;
        long rpmBits;

        /// <param name="targetLatency">Seconds of audio kept ready ahead of the reader.</param>
        public EngineSoundStream(EngineSoundGenerator generator, int outputSampleRate, double targetLatency = 0.04)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            if (outputSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputSampleRate));
            if (targetLatency <= 0) throw new ArgumentOutOfRangeException(nameof(targetLatency));

            this.outputSampleRate = outputSampleRate;
            int maxBlock = generator.MaxBlockSamples;
            resampler = new Resampler(EngineSoundGenerator.AudioSampleRate, outputSampleRate, maxBlock);
            block = new float[maxBlock];
            converted = new float[resampler.MaxOutputCount(maxBlock)];

            targetSamples = Math.Max(1, (int)Math.Ceiling(targetLatency * outputSampleRate));
            // Room for the target plus one converted block, rounded up to a power of two.
            int capacity = 1;
            while (capacity < targetSamples + converted.Length) capacity <<= 1;
            ring = new AudioRingBuffer(capacity);
        }

        public EngineSoundGenerator Generator => generator;
        public int OutputSampleRate => outputSampleRate;
        public bool Running => running;

        /// <summary>The exception that stopped the simulation thread, if one did.</summary>
        public Exception Failure => failure;

        /// <summary>Engine speed after the last rendered block (rpm, unsigned).</summary>
        public double Rpm => BitConverter.Int64BitsToDouble(Volatile.Read(ref rpmBits));

        /// <summary>Seconds of audio ready ahead of the reader.</summary>
        public double BufferedSeconds => (double)ring.Count / outputSampleRate;

        public double ProducedSeconds => (double)Volatile.Read(ref producedFrames) / outputSampleRate;
        public double UnderrunSeconds => (double)Volatile.Read(ref underrunFrames) / outputSampleRate;


        /// <summary>Converted frames that did not fit in the ring (should stay 0: the producer waits for room).</summary>
        public long OverflowFrames => Volatile.Read(ref overflowFrames);

        /// <summary>Wall-clock seconds spent rendering per second of audio produced: below 1 keeps up with real time.</summary>
        public double RenderCost
        {
            get
            {
                double produced = ProducedSeconds;
                return produced > 0 ? Volatile.Read(ref renderTicks) / (double)Stopwatch.Frequency / produced : 0;
            }
        }

        public void SetControls(in EngineSoundGenerator.Controls value)
        {
            lock (controlsLock)
            {
                controls = value;
            }
        }

        /// <summary>Changes the synthesizer's mix from the next block on.</summary>
        public void SetMix(in EngineSoundMix mix)
        {
            lock (controlsLock)
            {
                pendingMix = mix;
                mixPending = true;
            }
        }

        public void Start()
        {
            if (thread != null) throw new InvalidOperationException("Already started.");

            running = true;
            thread = new Thread(Run)
            {
                Name = "Nx Engine Bench",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            thread.Start();
        }

        public void Stop()
        {
            if (thread == null) return;

            running = false;
            thread.Join();
            thread = null;
        }

        public void Dispose() => Stop();

        void Run()
        {
            try
            {
                while (running)
                {
                    if (ring.Count >= targetSamples)
                    {
                        // Ahead of the reader. The reader never signals (it must not block or make system calls on the
                        // audio thread), so check again after a short sleep; the target latency covers it many times.
                        Thread.Sleep(1);
                        continue;
                    }

                    ProduceBlock();
                }
            }
            catch (Exception e)
            {
                failure = e;
                running = false;
            }
        }

        /// <summary>
        /// Renders one block under the current controls and mix, converts it and adds it to the ring. The simulation thread calls
        /// this; without <see cref="Start"/>, an owner can call it directly (deterministic use).
        /// </summary>
        public void ProduceBlock()
        {
            EngineSoundGenerator.Controls current;
            EngineSoundMix mix = default;
            bool newMix;
            lock (controlsLock)
            {
                current = controls;
                newMix = mixPending;
                if (newMix) mix = pendingMix;
                mixPending = false;
            }

            if (newMix) generator.SetMix(mix);

            long start = Stopwatch.GetTimestamp();
            int count = generator.RenderBlock(current, block, 0);
            int convertedCount = resampler.Process(block, 0, count, converted, 0, converted.Length);
            Volatile.Write(ref renderTicks, Volatile.Read(ref renderTicks) + (Stopwatch.GetTimestamp() - start));

            int written = ring.Write(converted, 0, convertedCount);
            Volatile.Write(ref producedFrames, Volatile.Read(ref producedFrames) + written);
            if (written < convertedCount)
            {
                Volatile.Write(ref overflowFrames, Volatile.Read(ref overflowFrames) + (convertedCount - written));
            }

            Volatile.Write(ref rpmBits, BitConverter.DoubleToInt64Bits(generator.Rpm));
        }

        /// <summary>
        /// Fills interleaved <paramref name="data"/> (<paramref name="channels"/> per frame) with the next frames, the mono
        /// signal times <paramref name="gain"/> on every channel. Frames not ready are silence. Returns how many frames came from
        /// the engine.
        /// </summary>
        public int Read(float[] data, int channels, float gain)
        {
            int frames = data.Length / channels;
            int fromEngine = 0;
            int done = 0;
            while (done < frames)
            {
                int chunk = Math.Min(frames - done, readScratch.Length);
                int got = ring.Read(readScratch, 0, chunk);

                for (int i = 0; i < chunk; ++i)
                {
                    float v = i < got ? readScratch[i] * gain : 0f;
                    int frame = (done + i) * channels;
                    for (int c = 0; c < channels; ++c) data[frame + c] = v;
                }

                fromEngine += got;
                done += chunk;
                if (got < chunk)
                {
                    Interlocked.Add(ref underrunFrames, chunk - got);
                }
            }

            return fromEngine;
        }
    }
}
