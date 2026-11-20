// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// Plays the live engine's captured cycles slowed down <see cref="Slowdown"/> times: it takes the last whole engine cycle
    /// in the capture (from one top dead centre of the cycle to the next), replays it step by step, then takes the latest
    /// cycle again. Everything shown is captured: the pistons, rods, valve lifts and the ignitions, which glow for a quarter
    /// of the cycle after they happen.
    /// </summary>
    sealed class CycleReplay
    {
        public const int Slowdown = 40;
        /// <summary>A firing glows for this share of the engine cycle (a quarter: 180° of crank).</summary>
        const double GlowShare = 0.25;
        /// <summary>Below this the engine counts as stopped (rpm).</summary>
        const double StoppedRpm = 5;

        readonly double[] littleEnd, bigEnd;
        EngineCapture capture;
        // The steps shown, and the buffer the next read goes into (swapped in only when the read succeeds).
        CaptureSteps shown, spare;
        int start, end;
        double position;
        double rpm;
        bool whole;

        public CycleReplay(EngineGeometry geometry)
        {
            littleEnd = new double[geometry.cylinders.Length];
            bigEnd = new double[geometry.cylinders.Length];
            for (int c = 0; c < littleEnd.Length; ++c)
            {
                EngineGeometry.Cylinder cylinder = geometry.cylinders[c];
                littleEnd[c] = cylinder.rodLength / 2 - cylinder.rodCentreOfMass;
                bigEnd[c] = -cylinder.rodLength / 2 + cylinder.rodCentreOfMass;
            }
        }

        /// <summary>
        /// Moves the replay on by <paramref name="seconds"/> of real time and poses <paramref name="pose"/>; false while there's
        /// nothing to show (no capture of this engine, or less than the capture's window published yet).
        /// </summary>
        public bool Advance(EngineCapture from, double seconds, EnginePose pose, out string note)
        {
            note = null;
            if (from == null || from.Cylinders != pose.Cylinders) return false;
            if (from != capture)
            {
                capture = from;
                shown = spare = null;
            }

            position += seconds * capture.SimulationFrequency / Slowdown;
            bool stopped = rpm < StoppedRpm;
            bool due = shown == null || (stopped ? capture.Rpm >= StoppedRpm : position >= end - start);
            if (due) Take();
            if (shown == null) return false;

            Pose(pose);
            note = rpm < StoppedRpm
                ? "Stopped"
                : $"Slowed 1:{Slowdown} · {(whole ? "one engine cycle" : "part of a cycle")} captured at {rpm:#,0} rpm";
            return true;
        }

        /// <summary>Takes the latest whole cycle from the capture (or the latest steps, if the capture holds less than a cycle).</summary>
        void Take()
        {
            spare ??= new CaptureSteps(capture.ReadableSteps, capture.Cylinders, capture.Exhausts);
            long published = capture.PublishedSteps;
            int count = spare.Count;
            double revs = capture.Rpm;
            if (published < count || !capture.ReadSteps(published - count, spare)) return;

            (shown, spare) = (spare, shown);
            rpm = revs;
            position = 0;
            if (rpm < StoppedRpm)
            {
                start = count - 1;
                end = count;
                whole = false;
                return;
            }

            // The cycle angle wraps (0 ↔ 4π) at top dead centre of the cycle: the last two wraps bound the last whole cycle.
            int last = -1, previous = -1;
            for (int s = count - 1; s > 0 && previous < 0; --s)
            {
                if (Math.Abs(shown.crankAngle[s] - shown.crankAngle[s - 1]) <= Math.PI) continue;
                if (last < 0) last = s;
                else previous = s;
            }

            whole = previous >= 0;
            end = whole ? last : count;
            start = whole ? previous : Math.Max(0, count - StepsPerCycle());
        }

        int StepsPerCycle() => (int)Math.Round(capture.SimulationFrequency * 120 / Math.Max(rpm, 1));

        void Pose(EnginePose pose)
        {
            int s = Math.Min(end - 1, start + (int)position);
            int cylinders = pose.Cylinders;
            int glowSteps = Math.Max(1, (int)(StepsPerCycle() * GlowShare));
            pose.crankAngle = shown.crankAngle[s];
            for (int c = 0; c < cylinders; ++c)
            {
                int k = s * cylinders + c;
                double angle = shown.rodAngle[k], sin = Math.Sin(angle), cos = Math.Cos(angle);
                double x = shown.rodX[k], y = shown.rodY[k];

                double glow = 0;
                if (rpm >= StoppedRpm)
                {
                    for (int f = s; f >= 0 && s - f < glowSteps; --f)
                    {
                        if (shown.fired[f * cylinders + c] == 0) continue;
                        glow = 1 - (s - f) / (double)glowSteps;
                        break;
                    }
                }

                pose.Set(c, shown.pistonTravel[k], x - sin * bigEnd[c], y + cos * bigEnd[c], x - sin * littleEnd[c], y + cos * littleEnd[c],
                    shown.intakeLift[k], shown.exhaustLift[k], glow);
            }
        }
    }
}
