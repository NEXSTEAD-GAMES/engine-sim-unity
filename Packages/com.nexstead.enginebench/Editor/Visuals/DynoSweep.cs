// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// A dyno pull to the redline, run as engine-sim's application runs one: the throttle open, the dyno braking the engine
    /// (not driving it) to a speed that starts at the engine's lowest dyno speed and rises <see cref="RampRpmPerSecond"/> per
    /// second of simulated time while the engine makes torque against it, easing back while it doesn't, kept within the
    /// engine's dyno speeds. The pull ends past the redline, or at the engine's top dyno speed or its rev limiter if either is
    /// lower: an engine can't pass its limiter, and engine-sim would hold at its top dyno speed until stopped. It records the engine's torque every <see cref="PointSpacingRpm"/>. As in
    /// engine-sim, the torque read is the dyno's: what the engine makes less what accelerating its rotating parts takes.
    /// </summary>
    sealed class DynoSweep
    {
        public const double RampRpmPerSecond = 500;
        public const double PointSpacingRpm = 50;
        /// <summary>engine-sim's threshold: the speed rises while the dyno reads more than 1 ft·lb (in N·m).</summary>
        const double MinTorque = 1.3558179483314004;
        /// <summary>Simulated time the pull runs before recording: the dyno's torque is averaged over a cycle, and the first one is part-braked.</summary>
        const double SettleSeconds = 0.25;

        readonly List<(double rpm, double torque)> points = new List<(double, double)>();
        readonly List<(double rpm, double torque)> previous = new List<(double, double)>();
        EngineCapture capture;
        double minSpeed, maxSpeed, redline, revLimit;
        long lastSteps;
        double elapsed;
        double nextPoint;

        public bool Running { get; private set; }

        /// <summary>The speed the dyno brakes the engine to now (rpm).</summary>
        public double Speed { get; private set; }

        /// <summary>The current (or last) pull's points, by rising rpm.</summary>
        public IReadOnlyList<(double rpm, double torque)> Points => points;

        /// <summary>The pull before it, for comparison.</summary>
        public IReadOnlyList<(double rpm, double torque)> Previous => previous;

        /// <summary>
        /// Starts a pull on the running engine, whose parameters are <paramref name="engine"/>: within its dyno speeds, to its
        /// redline or rev limiter. Keeps the last pull as the previous one.
        /// </summary>
        public void Start(EngineCapture running, EngineParametersDescription engine)
        {
            if (points.Count > 0)
            {
                previous.Clear();
                previous.AddRange(points);
            }

            points.Clear();
            capture = running;
            minSpeed = Units.ToRpm(engine.dynoMinSpeed);
            maxSpeed = Units.ToRpm(engine.dynoMaxSpeed);
            redline = Units.ToRpm(engine.redline);
            revLimit = Units.ToRpm(engine.ignitionModule.revLimit);
            lastSteps = running.PublishedSteps;
            elapsed = 0;
            Speed = minSpeed;
            nextPoint = Math.Ceiling(Speed / PointSpacingRpm) * PointSpacingRpm;
            Running = true;
        }

        public void Stop()
        {
            Running = false;
            capture = null;
        }

        /// <summary>Forgets both pulls (another engine).</summary>
        public void Clear()
        {
            Stop();
            points.Clear();
            previous.Clear();
        }

        /// <summary>Moves the pull on by the steps <paramref name="from"/> published since the last update; stops it if the engine was replaced.</summary>
        public void Update(EngineCapture from)
        {
            if (!Running) return;
            if (from != capture)
            {
                Stop();
                return;
            }

            long steps = capture.PublishedSteps;
            double seconds = (steps - lastSteps) / (double)capture.SimulationFrequency;
            lastSteps = steps;
            if (seconds <= 0) return;

            double torque = capture.DynoTorque;
            Speed = torque > MinTorque ? Speed + RampRpmPerSecond * seconds : Speed / (1 + seconds);
            elapsed += seconds;

            // Only while the dyno holds the engine: not while it brakes a faster engine down to the pull's speed.
            double rpm = capture.Rpm;
            if (elapsed >= SettleSeconds && rpm >= nextPoint && rpm <= Speed + PointSpacingRpm)
            {
                points.Add((rpm, torque));
                nextPoint = (Math.Floor(rpm / PointSpacingRpm) + 1) * PointSpacingRpm;
            }

            if (Speed > redline || Speed >= Math.Min(maxSpeed, revLimit)) Stop();
            Speed = Math.Min(Math.Max(Speed, minSpeed), maxSpeed);
        }
    }
}
