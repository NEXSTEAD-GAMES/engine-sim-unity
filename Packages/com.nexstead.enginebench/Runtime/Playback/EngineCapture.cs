// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Threading;
using Nexstead.EngineBench.Dense;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// What the Bench's visuals read from a running engine: its state at every step (crank angle, pistons, valve lifts,
    /// ignitions, the exhaust signal the synthesizer is fed), the last output audio, and per block the revs, the dyno torque
    /// and the rev limiter. The thread that renders the blocks writes it (<see cref="EngineSoundGenerator"/>) and publishes
    /// each block; any other thread may read what is published. A read that the writer overtook while copying fails rather
    /// than returning mixed steps.
    /// </summary>
    internal sealed unsafe class EngineCapture : IDisposable
    {
        /// <summary>Steps kept: a quarter of a second, an engine cycle down to 480 rpm and more than the scope shows.</summary>
        const double KeptSeconds = 0.25;
        /// <summary>Output samples kept: 16384, about 0.37 s at 44.1 kHz.</summary>
        const int AudioCapacity = 16384;

        readonly DenseMemory memory = new DenseMemory();
        readonly DenseCapture* data;
        readonly float[] audio = new float[AudioCapacity];
        long publishedSteps;
        long audioWritten;
        long publishedAudio;
        double rpm;
        double torque;
        int limiter;
        // The writer fills the next block's steps and samples before publishing them: a read stays this far from being overtaken.
        readonly int stepMargin;
        readonly int audioMargin;

        public EngineCapture(int cylinders, int exhausts, int simulationFrequency, int stepsPerBlock, int maxBlockSamples)
        {
            stepMargin = stepsPerBlock;
            audioMargin = maxBlockSamples;
            Cylinders = cylinders;
            Exhausts = exhausts;
            SimulationFrequency = simulationFrequency;
            Capacity = (int)Math.Ceiling(KeptSeconds * simulationFrequency);

            data = memory.Alloc<DenseCapture>(1);
            data->capacity = Capacity;
            data->cylinders = cylinders;
            data->exhausts = exhausts;
            data->crankAngle = memory.Alloc<float>(Capacity);
            data->pistonTravel = memory.Alloc<float>(Capacity * cylinders);
            data->rodX = memory.Alloc<float>(Capacity * cylinders);
            data->rodY = memory.Alloc<float>(Capacity * cylinders);
            data->rodAngle = memory.Alloc<float>(Capacity * cylinders);
            data->intakeLift = memory.Alloc<float>(Capacity * cylinders);
            data->exhaustLift = memory.Alloc<float>(Capacity * cylinders);
            data->fired = memory.Alloc<byte>(Capacity * cylinders);
            data->exhaustInput = memory.Alloc<float>(Capacity * exhausts);
            data->lastIgnitions = memory.Alloc<int>(cylinders);
        }

        public int Capacity { get; }
        public int Cylinders { get; }
        public int Exhausts { get; }
        public int SimulationFrequency { get; }

        /// <summary>The rings, for the recording code (<see cref="CaptureOps"/>).</summary>
        public DenseCapture* Data => data;

        /// <summary>Steps published so far: the next readable step is this one less one.</summary>
        public long PublishedSteps => Interlocked.Read(ref publishedSteps);

        /// <summary>Output samples published so far.</summary>
        public long PublishedAudio => Interlocked.Read(ref publishedAudio);

        /// <summary>Revs (rpm, unsigned) after the last published block.</summary>
        public double Rpm => Volatile.Read(ref rpm);

        /// <summary>The dyno's torque on the crank (N·m), averaged over the last engine cycle: the engine's torque while the dyno holds it.</summary>
        public double DynoTorque => Volatile.Read(ref torque);

        /// <summary>The rev limiter was cutting the ignition after the last published block.</summary>
        public bool LimiterCutting => Volatile.Read(ref limiter) != 0;

        /// <summary>Writer thread, after each block: publishes its steps, its output samples and the block's readouts.</summary>
        public void PublishBlock(float[] samples, int offset, int count, double rpm, double dynoTorque, bool limiterCutting)
        {
            for (int i = 0; i < count; ++i) audio[(int)((audioWritten + i) % AudioCapacity)] = samples[offset + i];
            audioWritten += count;

            Volatile.Write(ref this.rpm, rpm);
            Volatile.Write(ref torque, dynoTorque);
            Volatile.Write(ref limiter, limiterCutting ? 1 : 0);
            Interlocked.Exchange(ref publishedAudio, audioWritten);
            Interlocked.Exchange(ref publishedSteps, data->written);
        }

        /// <summary>
        /// Copies <see cref="CaptureSteps.Count"/> steps from step <paramref name="first"/> on into <paramref name="into"/>.
        /// False, with nothing reliable copied, when those steps aren't published yet or the writer overtook them.
        /// </summary>
        public bool ReadSteps(long first, CaptureSteps into)
        {
            int count = into.Count;
            long published = PublishedSteps;
            if (into.Cylinders != Cylinders || into.Exhausts != Exhausts) throw new ArgumentException("Buffers of another engine.", nameof(into));
            if (first < 0 || first + count > published || published - first > Capacity - stepMargin) return false;

            for (int s = 0; s < count; ++s)
            {
                int index = (int)((first + s) % Capacity);
                into.crankAngle[s] = data->crankAngle[index];
                for (int c = 0; c < Cylinders; ++c)
                {
                    int from = index * Cylinders + c, to = s * Cylinders + c;
                    into.pistonTravel[to] = data->pistonTravel[from];
                    into.rodX[to] = data->rodX[from];
                    into.rodY[to] = data->rodY[from];
                    into.rodAngle[to] = data->rodAngle[from];
                    into.intakeLift[to] = data->intakeLift[from];
                    into.exhaustLift[to] = data->exhaustLift[from];
                    into.fired[to] = data->fired[from];
                }

                for (int x = 0; x < Exhausts; ++x) into.exhaustInput[s * Exhausts + x] = data->exhaustInput[index * Exhausts + x];
            }

            // Overtaken while copying: the oldest step copied may be rewritten already.
            return PublishedSteps - first <= Capacity - stepMargin;
        }

        /// <summary>The most steps a read can take: the ring less the block the writer may be filling.</summary>
        public int ReadableSteps => Capacity - stepMargin;

        /// <summary>
        /// Copies the latest <paramref name="destination"/>.Length output samples, oldest first. Returns how many (fewer while
        /// fewer are published), or 0 when the writer overtook them while copying.
        /// </summary>
        public int ReadAudio(float[] destination)
        {
            long published = PublishedAudio;
            int count = (int)Math.Min(Math.Min(destination.Length, published), AudioCapacity - audioMargin);
            long first = published - count;
            for (int i = 0; i < count; ++i) destination[i] = audio[(int)((first + i) % AudioCapacity)];
            return PublishedAudio - first <= AudioCapacity - audioMargin ? count : 0;
        }

        public void Dispose() => memory.Dispose();
    }

    /// <summary>A run of captured steps (<see cref="EngineCapture.ReadSteps"/>); per-cylinder and per-exhaust values step by step.</summary>
    internal sealed class CaptureSteps
    {
        public CaptureSteps(int count, int cylinders, int exhausts)
        {
            Count = count;
            Cylinders = cylinders;
            Exhausts = exhausts;
            crankAngle = new float[count];
            pistonTravel = new float[count * cylinders];
            rodX = new float[count * cylinders];
            rodY = new float[count * cylinders];
            rodAngle = new float[count * cylinders];
            intakeLift = new float[count * cylinders];
            exhaustLift = new float[count * cylinders];
            fired = new byte[count * cylinders];
            exhaustInput = new float[count * exhausts];
        }

        public int Count { get; }
        public int Cylinders { get; }
        public int Exhausts { get; }
        public readonly float[] crankAngle;
        public readonly float[] pistonTravel;
        public readonly float[] rodX;
        public readonly float[] rodY;
        public readonly float[] rodAngle;
        public readonly float[] intakeLift;
        public readonly float[] exhaustLift;
        public readonly byte[] fired;
        public readonly float[] exhaustInput;
    }
}
