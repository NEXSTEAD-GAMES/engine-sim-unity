// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// The Bench window's live views and what feeds them: the front view and the cylinder lights play the slowed replay
    /// (<see cref="CycleReplay"/>), the rev counter, scope and spectrum follow the engine in real time, and the dyno graph
    /// shows the pulls (<see cref="DynoSweep"/>) with the live engine on it. The window places the elements;
    /// <see cref="Animate"/> runs every frame while the window is open.
    /// </summary>
    sealed class BenchVisuals
    {
        public readonly EngineView EngineView = new EngineView();
        public readonly Tachometer Tachometer = new Tachometer();
        public readonly CylinderLights Lights = new CylinderLights();
        public readonly ScopeView Scope = new ScopeView();
        public readonly SpectrumView Spectrum = new SpectrumView();
        public readonly DynoGraph Dyno = new DynoGraph();

        EngineGeometry geometry;
        EnginePose pose;
        CycleReplay replay;
        string stillNote;
        int frame;

        /// <summary>The engine on the bench; null for none.</summary>
        public void SetEngine(EngineAsset engine)
        {
            EngineDescription description = engine != null ? engine.Description : null;
            Lights.SetEngine(description);
            stillNote = null;
            if (description == null)
            {
                geometry = null;
                pose = null;
                replay = null;
                EngineView.SetEngine(null);
                Tachometer.Show(-1);
                return;
            }

            EngineParametersDescription e = description.engine;
            geometry = new EngineGeometry(description);
            pose = new EnginePose(geometry.cylinders.Length);
            replay = new CycleReplay(geometry);
            EngineView.SetEngine(geometry);
            Tachometer.SetRange(Units.ToRpm(e.redline), Units.ToRpm(e.ignitionModule.revLimit));
            Dyno.SetRange(Units.ToRpm(e.redline), Units.ToRpm(e.ignitionModule.revLimit));
            ShowStill("Not running · Live runs it");
        }

        /// <summary>
        /// Moves the views on by <paramref name="seconds"/> of real time, from <paramref name="live"/> (null when not running)
        /// and the dyno pulls of <paramref name="sweep"/>; <paramref name="dynoOn"/> while the dyno is on the crank.
        /// </summary>
        public void Animate(EngineAudioSource live, double seconds, DynoSweep sweep, bool dynoOn)
        {
            Dyno.SetPulls(sweep.Points, sweep.Previous);
            if (geometry == null) return;
            EngineCapture capture = live != null && live.Playing ? live.Capture : null;
            if (capture == null)
            {
                ShowStill("Not running · Live runs it");
                Tachometer.Show(-1);
                Dyno.Show(-1, double.NaN);
                Scope.Show(null);
                Spectrum.Show(null, 0);
                return;
            }

            double rpm = capture.Rpm;
            Tachometer.Show(rpm);
            Dyno.Show(rpm, dynoOn ? capture.DynoTorque : double.NaN);
            if (replay.Advance(capture, seconds, pose, out string note))
            {
                stillNote = null;
                EngineView.Show(pose, note);
                Lights.Show(pose.flash);
            }
            else
            {
                ShowStill("Starting…");
            }

            // The traces at half the frame rate: plenty for the eye.
            if ((++frame & 1) != 0) return;
            Scope.Show(capture);
            Spectrum.Show(capture, rpm * capture.Cylinders / 120);
        }

        /// <summary>The engine at rest, the first cylinder at top dead centre, once per change of note.</summary>
        void ShowStill(string note)
        {
            if (stillNote == note) return;
            stillNote = note;
            geometry.StillPose(geometry.tdc, pose);
            EngineView.Show(pose, note);
            Lights.Show(null);
        }
    }
}
