// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;
using Unity.Burst;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// The dense core's block rendering compiled by Burst. With Burst compilation off (Jobs > Burst > Enable Compilation),
    /// the same method runs as ordinary managed code.
    /// </summary>
    [BurstCompile]
    public static unsafe class BurstBlockRenderer
    {
        /// <summary>For <see cref="EngineSoundGenerator"/>'s dense core.</summary>
        public static readonly EngineSoundGenerator.DenseBlockRenderer Renderer = RenderBlock;

        // Strict: no reassociation, no fused multiply-add, so the arithmetic stays the managed port's. Compiled on first use,
        // synchronously, so no block runs managed while Burst is on.
        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
        public static int RenderBlock(DenseEngine* engine, EngineSoundGenerator.Controls* controls, int steps, short* output, float* forcedInductionOutput, short* mixOutput)
        {
            return SimulatorOps.RenderBlock(engine, controls, steps, output, forcedInductionOutput, mixOutput);
        }

        /// <summary>1 when this code runs Burst-compiled, 0 when it runs as managed code.</summary>
        [BurstCompile(CompileSynchronously = true)]
        public static int RunsBurstCompiled()
        {
            int managed = 0;
            MarkManaged(ref managed);
            return managed == 0 ? 1 : 0;
        }

        // Burst leaves calls to this out of its compiled code.
        [BurstDiscard]
        static void MarkManaged(ref int managed) => managed = 1;
    }
}
