// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// What drives an engine for an <see cref="EngineRenderer"/>, applied at the start of each block. Part of the render API
    /// (stable within a major version).
    /// </summary>
    public struct EngineControls
    {
        /// <summary>
        /// 0 closed to 1 open, through the engine's throttle linkage or governor. A double, as the simulation reads it, so a
        /// value such as 0.15 reaches it exactly.
        /// </summary>
        public double throttle;
        public bool ignition;
        public bool starter;
        /// <summary>The dynamometer is on the crank: it brakes it to <see cref="dynoRpm"/>.</summary>
        public bool dyno;
        /// <summary>
        /// The dyno also drives the crank up to <see cref="dynoRpm"/>, so it holds that speed exactly. Engage it only on a
        /// turning engine: from rest it drives the crank backwards.
        /// </summary>
        public bool dynoHold;
        /// <summary>The crank speed the dyno holds, in rpm.</summary>
        public double dynoRpm;

        /// <summary>The generator's form: the same values, the dyno speed in rad/s.</summary>
        internal EngineSoundGenerator.Controls ToGenerator() => new EngineSoundGenerator.Controls
        {
            speedControl = throttle,
            ignition = ignition,
            starter = starter,
            dynoEnabled = dyno,
            dynoHold = dynoHold,
            dynoSpeed = Units.Rpm(dynoRpm),
        };
    }
}
