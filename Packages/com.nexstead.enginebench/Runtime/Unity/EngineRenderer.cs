// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>The revs over a <see cref="EngineRenderer.Run"/>, read after each block.</summary>
    public readonly struct RpmSummary
    {
        public readonly double mean;
        public readonly double min;
        public readonly double max;
        /// <summary>The rev limiter cut the ignition at some point of the run.</summary>
        public readonly bool limiterCut;

        public RpmSummary(double mean, double min, double max, bool limiterCut)
        {
            this.mean = mean;
            this.min = min;
            this.max = max;
            this.limiterCut = limiterCut;
        }
    }

    /// <summary>
    /// Renders an engine's sound block by block under <see cref="EngineControls"/>: 44.1 kHz mono samples on the 16-bit
    /// output's scale (a 16-bit value / 32768), at a set volume, Burst-compiled whenever Burst compilation is on. Build it
    /// from an <see cref="EngineRenderSource"/>, on any thread, and use it from one thread at a time. Each renderer starts
    /// from rest; the same source, seed and controls render the same samples. Dispose it to free its memory. Part of the
    /// render API (stable within a major version).
    /// </summary>
    public sealed class EngineRenderer : IDisposable
    {
        public const int SampleRate = EngineSoundGenerator.AudioSampleRate;

        readonly EngineSoundGenerator generator;
        readonly float[] block;
        readonly float[] forcedInductionBlock;

        /// <param name="source">The engine.</param>
        /// <param name="seed">Seeds every random stream (combustion, noise, jitter).</param>
        /// <param name="volume">
        /// The synthesizer's output volume, after its leveler: 1, upstream's and the Bench's, plays the leveled peaks at
        /// 30000 under the leveler's ceiling (<see cref="Synthesizer.OutputCeiling"/>); lower renders the same sound, quieter.
        /// </param>
        public EngineRenderer(EngineRenderSource source, ulong seed, float volume = 1f)
            : this(source, seed, volume, capture: false)
        {
        }

        /// <param name="capture">Records the engine's state for the Bench's visuals (<see cref="EngineSoundGenerator.Capture"/>).</param>
        internal EngineRenderer(EngineRenderSource source, ulong seed, float volume, bool capture)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            generator = new EngineSoundGenerator(source.Build(), source.ImpulseResponse, seed, Synthesizer.ConvolutionMode.Partitioned,
                0.0025, EngineSoundGenerator.Core.Dense, BurstBlockRenderer.Renderer, volume, source.Mix, capture);
            block = new float[generator.MaxBlockSamples];
            forcedInductionBlock = new float[generator.MaxBlockSamples];
            BlockSeconds = (double)generator.StepsPerBlock / generator.SimulationFrequency;
        }

        /// <summary>Whether renderers run Burst-compiled now (Jobs > Burst > Enable Compilation); otherwise as managed code.</summary>
        public static bool BurstCompiled => BurstBlockRenderer.RunsBurstCompiled() == 1;

        /// <summary>Seconds of simulation per block: the controls' time resolution.</summary>
        public double BlockSeconds { get; }

        /// <summary>The most samples one block can produce.</summary>
        public int MaxBlockSamples => generator.MaxBlockSamples;

        /// <summary>Crank speed after the last block, in rpm (unsigned).</summary>
        public double Rpm => generator.Rpm;

        /// <summary>The rev limiter is cutting the ignition after the last block.</summary>
        public bool RevLimiterCutting => generator.RevLimiterCutting;

        /// <summary>The engine's turbocharger or supercharger, by how it sounds; <see cref="ForcedInductionKind.None"/> without one.</summary>
        public ForcedInductionKind ForcedInductionKind => generator.ForcedInductionKind;

        /// <summary>Its blow-off valve (<see cref="BlowoffValveKind.None"/> without one).</summary>
        public BlowoffValveKind BlowoffValveKind => generator.BlowoffValveKind;

        /// <summary>The forced induction's state after the last block (shaft speed, tone, boost, the levels its sounds follow); all zero without one.</summary>
        public ForcedInductionTelemetry ForcedInduction => generator.ForcedInduction;

        /// <summary>The tone this engine's forced induction makes at a shaft speed (<see cref="ForcedInductionSound.ToneHz"/>, with its whistle pitch).</summary>
        public double ToneHz(double shaftHz) => ForcedInductionSound.ToneHz(generator.ForcedInductionKind, shaftHz, generator.WhistlePitch);

        /// <summary>One block's three outputs at once: the mix, the engine and the forced induction (the Bench's exports).</summary>
        internal int RenderBlockOutputs(in EngineControls controls, float[] mix, float[] engine, float[] forcedInduction) =>
            generator.RenderBlockOutputs(controls.ToGenerator(), mix, engine, forcedInduction, 0);

        /// <summary>The generator underneath, for the real-time stream.</summary>
        internal EngineSoundGenerator Generator => generator;

        /// <summary>Blocks in <paramref name="seconds"/>, rounded.</summary>
        public int BlocksFor(double seconds) => (int)Math.Round(seconds / BlockSeconds);

        /// <summary>
        /// Renders one block into <paramref name="output"/> from <paramref name="offset"/>: the engine and its forced induction's sounds
        /// leveled together, as engine-sim's community fork plays them, under the leveler's ceiling (without forced induction, the
        /// engine alone). Returns the sample count.
        /// </summary>
        public int RenderBlock(in EngineControls controls, float[] output, int offset = 0) =>
            generator.RenderBlock(controls.ToGenerator(), output, offset);

        /// <summary>
        /// Renders one block with the outputs apart: the engine's sound into <paramref name="engine"/> and its turbocharger's or
        /// supercharger's sounds (whistle or whine, blow-off, surge, wastegate) into <paramref name="forcedInduction"/>, silence
        /// without one, both from <paramref name="offset"/>. Each has its own antialiasing; both go through the gain the leveler and
        /// its ceiling take from the engine's sound alone, so the engine output is the same whatever the forced induction sounds
        /// like. Their sum is that gain's mix, which can pass the ceiling; <see cref="RenderBlock(in EngineControls, float[], int)"/>
        /// levels the two together instead, so it never does. The engine output is the
        /// 16-bit output's; the forced induction output is unclamped floats on the same scale, which can pass ±1 (a blow-off on a
        /// quiet overrun, where that gain is high): scale it before writing it to a fixed-point format. Returns the sample count.
        /// </summary>
        public int RenderBlock(in EngineControls controls, float[] engine, float[] forcedInduction, int offset = 0) =>
            generator.RenderBlock(controls.ToGenerator(), engine, forcedInduction, offset);

        /// <summary>
        /// Renders <paramref name="blocks"/> blocks under fixed controls into <paramref name="output"/> from
        /// <paramref name="offset"/>, or discards them when it is null (a settling run). Returns the sample count; the revs
        /// over the run are in <paramref name="rpm"/>. A buffer of <c>blocks × MaxBlockSamples</c> always has room.
        /// </summary>
        public int Run(in EngineControls controls, int blocks, float[] output, int offset, out RpmSummary rpm) =>
            Run(controls, blocks, output, null, offset, out rpm, apart: false);

        /// <summary>
        /// As <see cref="Run(in EngineControls, int, float[], int, out RpmSummary)"/>, with the outputs apart as in
        /// <see cref="RenderBlock(in EngineControls, float[], float[], int)"/>: the engine into <paramref name="engine"/>, the forced
        /// induction into <paramref name="forcedInduction"/>; either may be null to discard it.
        /// </summary>
        public int Run(in EngineControls controls, int blocks, float[] engine, float[] forcedInduction, int offset, out RpmSummary rpm) =>
            Run(controls, blocks, engine, forcedInduction, offset, out rpm, apart: true);

        int Run(in EngineControls controls, int blocks, float[] output, float[] forcedInductionOutput, int offset, out RpmSummary rpm, bool apart)
        {
            EngineSoundGenerator.Controls c = controls.ToGenerator();
            int count = 0;
            double sum = 0, min = double.MaxValue, max = 0;
            bool cut = false;
            for (int b = 0; b < blocks; ++b)
            {
                int n = apart
                    ? generator.RenderBlock(c, block, forcedInductionBlock, 0)
                    : generator.RenderBlock(c, block, 0);
                if (output != null) Array.Copy(block, 0, output, offset + count, n);
                if (forcedInductionOutput != null) Array.Copy(forcedInductionBlock, 0, forcedInductionOutput, offset + count, n);
                count += n;

                double revs = generator.Rpm;
                sum += revs;
                min = Math.Min(min, revs);
                max = Math.Max(max, revs);
                cut |= generator.RevLimiterCutting;
            }

            rpm = blocks > 0 ? new RpmSummary(sum / blocks, min, max, cut) : new RpmSummary(generator.Rpm, generator.Rpm, generator.Rpm, false);
            return count;
        }

        public void Dispose() => generator.Dispose();
    }
}
