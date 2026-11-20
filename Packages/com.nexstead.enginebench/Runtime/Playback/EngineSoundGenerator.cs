// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Runtime.InteropServices;
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// An engine's simulation and synthesizer, set up as upstream's application loads an engine, and stepped one block at a time
    /// under a set of controls. Produces the synthesizer's 44.1 kHz output as floats: the engine and its forced induction (a
    /// turbocharger's or supercharger's sounds) apart, or summed. Single-threaded: one owner calls <see cref="RenderBlock(in Controls, float[], int)"/>.
    /// Steps the dense core by default (see <see cref="DenseCore"/>); dispose it to free that memory.
    /// </summary>
    public sealed unsafe class EngineSoundGenerator : IDisposable
    {
        public enum Core
        {
            /// <summary>The managed port: the oracle, class by class.</summary>
            Managed,
            /// <summary>A dense snapshot of it, stepped by <see cref="SimulatorOps"/>: the same results, and Burst can compile it.</summary>
            Dense,
        }

        /// <summary>
        /// Renders one dense block: <see cref="SimulatorOps.RenderBlock"/>, or a Burst-compiled call of it. Cdecl, so that Burst
        /// can also compile it as a function pointer.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int DenseBlockRenderer(DenseEngine* engine, Controls* controls, int steps, short* output, float* forcedInductionOutput,
            short* mixOutput);

        /// <summary>What drives the engine. Applied at the start of each block, as upstream's application applies its input once per frame.</summary>
        public struct Controls
        {
            /// <summary>Upstream's speed control: the throttle (0 closed, 1 open) through the engine's throttle linkage or governor.</summary>
            public double speedControl;
            public bool ignition;
            public bool starter;
            public bool dynoEnabled;
            /// <summary>The dyno also drives the crank up to <see cref="dynoSpeed"/>, not only brakes it. Engage only on a turning engine: from rest it drives the crank backwards.</summary>
            public bool dynoHold;
            /// <summary>Crank speed the dyno holds (rad/s, positive).</summary>
            public double dynoSpeed;
        }

        public const int AudioSampleRate = 44100;

        readonly Engine engine;
        readonly PistonEngineSimulator simulator;
        readonly Synthesizer synthesizer;
        readonly int simulationFrequency;
        readonly int stepsPerBlock;
        readonly short[] rendered = new short[Synthesizer.RenderLimit];
        readonly float[] renderedForcedInduction = new float[Synthesizer.RenderLimit];
        readonly short[] renderedMix = new short[Synthesizer.RenderLimit];
        readonly Core core;
        readonly DenseCore dense;
        readonly DenseBlockRenderer denseRenderer;
        readonly EngineCapture capture;

        /// <param name="assembly">A freshly built engine (it can be loaded into one simulator only).</param>
        /// <param name="impulseResponses">The 44.1 kHz samples of an impulse response file named in the engine's description.</param>
        /// <param name="seed">Seeds every random stream (combustion, noise, jitter).</param>
        /// <param name="blockDuration">Seconds simulated per block: the controls' time resolution.</param>
        /// <param name="core">Which core steps the engine.</param>
        /// <param name="denseRenderer">For the dense core: renders a block; null renders it with <see cref="SimulatorOps.RenderBlock"/>.</param>
        /// <param name="volume">
        /// The synthesizer's output volume, applied after its leveler and before the 16-bit output. At upstream's 1, as the
        /// Bench and <c>EngineAudioSource</c> play, the leveled peaks sit at 30000 and a pulse the leveler overshoots is held
        /// at its ceiling (<see cref="Synthesizer.OutputCeiling"/>). The simulation doesn't depend on it; the ceiling is in
        /// output units, so it scales with it.
        /// </param>
        /// <param name="mix">The synthesizer's mix to start with; null: as upstream's application loads the engine (<see cref="EngineSoundMix.AsLoaded"/>).</param>
        public EngineSoundGenerator(EngineAssembly assembly, Func<string, short[]> impulseResponses, ulong seed,
            Synthesizer.ConvolutionMode convolution = Synthesizer.ConvolutionMode.Partitioned, double blockDuration = 0.0025,
            Core core = Core.Dense, DenseBlockRenderer denseRenderer = null, float volume = 1.0f, EngineSoundMix? mix = null)
            : this(assembly, impulseResponses, seed, convolution, blockDuration, core, denseRenderer, volume, mix, capture: false)
        {
        }

        /// <param name="capture">Records the engine's state at every step for the Bench's visuals (<see cref="Capture"/>); it changes no sample.</param>
        internal EngineSoundGenerator(EngineAssembly assembly, Func<string, short[]> impulseResponses, ulong seed,
            Synthesizer.ConvolutionMode convolution, double blockDuration, Core core, DenseBlockRenderer denseRenderer, float volume,
            EngineSoundMix? mix, bool capture)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            if (impulseResponses == null) throw new ArgumentNullException(nameof(impulseResponses));

            engine = assembly.engine;

            // As upstream's EngineSimApplication::loadEngine.
            simulator = (PistonEngineSimulator)engine.CreateSimulator(assembly.vehicle, assembly.transmission);
            engine.CalculateDisplacement();
            simulator.SetSimulationFrequency((int)engine.GetSimulationFrequency());

            synthesizer = simulator.GetSynthesizer();
            Synthesizer.AudioParameters audio = synthesizer.GetAudioParameters();
            (mix ?? EngineSoundMix.AsLoaded(engine.GetInitialHighFrequencyGain(), engine.GetInitialNoise(), engine.GetInitialJitter()))
                .ApplyTo(ref audio);
            audio.volume = volume;
            synthesizer.SetAudioParameters(audio);

            synthesizer.convolutionMode = convolution;
            for (int i = 0; i < engine.GetExhaustSystemCount(); ++i)
            {
                ImpulseResponse response = engine.GetExhaustSystem(i).GetImpulseResponse();
                short[] samples = impulseResponses(response.GetFilename())
                    ?? throw new ArgumentException($"No samples for impulse response '{response.GetFilename()}'.", nameof(impulseResponses));
                synthesizer.InitializeImpulseResponse(samples, samples.Length, (float)response.GetVolume(), i);
            }

            simulator.SeedRandom(seed);

            simulationFrequency = simulator.GetSimulationFrequency();
            stepsPerBlock = Math.Max(1, (int)Utilities.Round(blockDuration * simulationFrequency));
            if (Math.Ceiling(stepsPerBlock * (double)AudioSampleRate / simulationFrequency) + 1 > Synthesizer.RenderLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(blockDuration), "Block too long for one synthesizer render.");
            }

            // Upstream's application sets the speed control every frame, from 0.
            engine.SetSpeedControl(0.0);

            this.core = core;
            if (core == Core.Dense)
            {
                dense = new DenseCore(simulator);
                this.denseRenderer = denseRenderer ?? SimulatorOps.RenderBlock;
            }

            if (capture)
            {
                this.capture = new EngineCapture(engine.GetCylinderCount(), engine.GetExhaustSystemCount(), simulationFrequency, stepsPerBlock,
                    MaxBlockSamples);
                if (dense != null) dense.Engine->simulator.capture = this.capture.Data;
            }
        }

        /// <summary>The engine's turbocharger or supercharger, by how it sounds.</summary>
        public ForcedInductionKind ForcedInductionKind => ForcedInductionSound.KindOf(engine.GetForcedInduction());

        /// <summary>Its blow-off valve.</summary>
        public BlowoffValveKind BlowoffValveKind => ForcedInductionSound.BlowoffOf(engine.GetForcedInduction());

        /// <summary>Its whistle pitch factor (1: as modelled).</summary>
        public double WhistlePitch => engine.GetForcedInduction()?.GetWhistlePitch() ?? 1.0;

        /// <summary>The forced induction's state after the last block; all zero without one.</summary>
        public ForcedInductionTelemetry ForcedInduction
        {
            get
            {
                ForcedInduction managed = engine.GetForcedInduction();
                if (managed == null) return default;

                double shaftHz, boost;
                double* controls = stackalloc double[Synthesizer.ForcedInductionChannelCount];
                if (core == Core.Dense)
                {
                    ForcedInductionData* f = dense.Engine->parts.forcedInduction;
                    shaftHz = ForcedInductionOps.GetWhineFrequency(f);
                    boost = ForcedInductionOps.GetBoost(dense.Engine, f);
                    double* staging = dense.Engine->simulator.exhaustFlowStagingBuffer + engine.GetExhaustSystemCount();
                    for (int k = 0; k < Synthesizer.ForcedInductionChannelCount; ++k) controls[k] = staging[k];
                }
                else
                {
                    shaftHz = managed.GetWhineFrequency();
                    boost = managed.GetBoost();
                    for (int k = 0; k < Synthesizer.ForcedInductionChannelCount; ++k) controls[k] = simulator.GetForcedInductionControl(k);
                }

                return new ForcedInductionTelemetry(shaftHz,
                    ForcedInductionSound.ToneHz(ForcedInductionKind, shaftHz, managed.GetWhistlePitch()),
                    boost / 1000.0, controls[1], controls[2], controls[3], controls[4], controls[5]);
            }
        }

        /// <summary>The engine's recorded state, for the Bench's visuals; null unless built with capture on.</summary>
        internal EngineCapture Capture => capture;

        public Core CoreKind => core;

        /// <summary>Engine speed (rpm, unsigned) after the last block.</summary>
        public double Rpm => core == Core.Dense ? PartsOps.GetRpm(dense.Engine) : engine.GetRpm();

        /// <summary>
        /// The rev limiter is cutting the ignition after the last block. A cut lasts the ignition module's limiter duration
        /// (50–500 ms in the library), far longer than a block, so no cut falls between two readings.
        /// </summary>
        public bool RevLimiterCutting => core == Core.Dense
            ? dense.Engine->parts.ignition.revLimitTimer > 0
            : engine.GetIgnitionModule().GetRevLimitTimer() > 0;

        public void Dispose()
        {
            dense?.Dispose();
            capture?.Dispose();
        }

        /// <summary>The managed engine. With the dense core it stays at its loaded state: read the generator's own readouts instead.</summary>
        public Engine Engine => engine;
        public PistonEngineSimulator Simulator => simulator;
        public int SimulationFrequency => simulationFrequency;
        public int StepsPerBlock => stepsPerBlock;

        /// <summary>The most samples one block can produce.</summary>
        public int MaxBlockSamples => (int)Math.Ceiling(stepsPerBlock * (double)AudioSampleRate / simulationFrequency) + 1;

        /// <summary>Changes the synthesizer's mix from the next block on. Call it between blocks, from the thread that renders them.</summary>
        public void SetMix(in EngineSoundMix mix)
        {
            if (core == Core.Dense)
            {
                mix.ApplyTo(ref dense.Engine->synthesizer.audioParameters);
                return;
            }

            Synthesizer.AudioParameters audio = synthesizer.GetAudioParameters();
            mix.ApplyTo(ref audio);
            synthesizer.SetAudioParameters(audio);
        }

        /// <summary>
        /// Applies the controls, steps one block, renders it and writes the samples (16-bit scale mapped to ±1) into
        /// <paramref name="output"/> from <paramref name="offset"/>: the engine and its forced induction leveled together, as
        /// engine-sim's community fork plays them, under the leveler's ceiling (without forced induction, the engine's own output).
        /// Returns how many were written (at most <see cref="MaxBlockSamples"/>).
        /// </summary>
        public int RenderBlock(in Controls controls, float[] output, int offset)
        {
            int count = Render(controls);
            for (int i = 0; i < count; ++i)
            {
                output[offset + i] = renderedMix[i] * (1.0f / 32768.0f);
            }

            capture?.PublishBlock(output, offset, count, Rpm, MeanDynoTorque(), RevLimiterCutting);
            return count;
        }

        /// <summary>
        /// As <see cref="RenderBlock(in Controls, float[], int)"/>, with the two outputs apart: the engine into
        /// <paramref name="engineOutput"/> and its forced induction's sounds into <paramref name="forcedInductionOutput"/> (silence
        /// without one), both from <paramref name="offset"/>. Both go through the gain the leveler takes from the engine's sound
        /// alone, so the engine output doesn't depend on the forced induction's sounds (the two add up to that gain's mix, not
        /// to the one-buffer output's, which levels them together); the forced induction output is neither
        /// rounded to 16 bits nor clamped, so it can pass ±1 (a blow-off on a quiet overrun, where that gain is high).
        /// </summary>
        public int RenderBlock(in Controls controls, float[] engineOutput, float[] forcedInductionOutput, int offset)
        {
            int count = Render(controls);
            for (int i = 0; i < count; ++i)
            {
                engineOutput[offset + i] = rendered[i] * (1.0f / 32768.0f);
                forcedInductionOutput[offset + i] = renderedForcedInduction[i] * (1.0f / 32768.0f);
            }

            capture?.PublishBlock(engineOutput, offset, count, Rpm, MeanDynoTorque(), RevLimiterCutting);
            return count;
        }

        /// <summary>
        /// One block's three outputs at once, each from <paramref name="offset"/>: <paramref name="mix"/> as
        /// <see cref="RenderBlock(in Controls, float[], int)"/> writes it, <paramref name="engineOutput"/> and
        /// <paramref name="forcedInductionOutput"/> as <see cref="RenderBlock(in Controls, float[], float[], int)"/> does. For
        /// the Bench's exports, which write the mix and a boosted engine's layers from one run.
        /// </summary>
        internal int RenderBlockOutputs(in Controls controls, float[] mix, float[] engineOutput, float[] forcedInductionOutput, int offset)
        {
            int count = Render(controls);
            for (int i = 0; i < count; ++i)
            {
                mix[offset + i] = renderedMix[i] * (1.0f / 32768.0f);
                engineOutput[offset + i] = rendered[i] * (1.0f / 32768.0f);
                forcedInductionOutput[offset + i] = renderedForcedInduction[i] * (1.0f / 32768.0f);
            }

            capture?.PublishBlock(mix, offset, count, Rpm, MeanDynoTorque(), RevLimiterCutting);
            return count;
        }

        /// <summary>Steps and renders one block into <see cref="rendered"/>, <see cref="renderedForcedInduction"/> and <see cref="renderedMix"/>.</summary>
        int Render(in Controls controls)
        {
            if (core == Core.Dense)
            {
                Controls current = controls;
                fixed (short* samples = rendered)
                fixed (float* forcedInductionSamples = renderedForcedInduction)
                fixed (short* mixSamples = renderedMix)
                {
                    return denseRenderer(dense.Engine, &current, stepsPerBlock, samples, forcedInductionSamples, mixSamples);
                }
            }

            engine.SetSpeedControl(controls.speedControl);
            engine.GetIgnitionModule().enabled = controls.ignition;
            simulator.starterMotor.enabled = controls.starter;
            simulator.dyno.enabled = controls.dynoEnabled;
            simulator.dyno.hold = controls.dynoHold;
            simulator.dyno.rotationSpeed = controls.dynoSpeed;

            // The part of upstream's startFrame that reaches the synthesizer.
            synthesizer.SetInputSampleRate(simulationFrequency);
            for (int step = 0; step < stepsPerBlock; ++step)
            {
                simulator.SimulateStep();
                if (capture != null) CaptureOps.Record(simulator, capture.Data);
            }

            // Upstream's endFrame, then its rendering thread's turn.
            synthesizer.EndInputBlock();
            if (synthesizer.GetInputSize() > 0)
            {
                synthesizer.RenderAudio();
            }

            int count = synthesizer.GetOutputSize();
            synthesizer.ReadAudioOutput(count, rendered, renderedForcedInduction, renderedMix);
            return count;
        }

        /// <summary>The dyno's torque samples over the engine cycle, averaged: its torque on the crank over the last cycle.</summary>
        double MeanDynoTorque()
        {
            double sum = 0;
            if (core == Core.Dense)
            {
                double* samples = dense.Engine->simulator.dynoTorqueSamples;
                for (int i = 0; i < Nexstead.EngineBench.Simulation.Simulator.DynoTorqueSamples; ++i) sum += samples[i];
            }
            else
            {
                double[] samples = simulator.DynoTorqueSampleArray;
                for (int i = 0; i < samples.Length; ++i) sum += samples[i];
            }

            return sum / Nexstead.EngineBench.Simulation.Simulator.DynoTorqueSamples;
        }
    }
}
