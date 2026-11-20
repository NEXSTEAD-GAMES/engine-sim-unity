// Checks of what the Bench's views compute: the firing order, the slowed replay and the dyno pull.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Editor;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public sealed class VisualsTests
    {
        /// <summary>The V8 cranked, then held by the dyno at 3000 rpm with the throttle open (an engine cycle every 40 ms).</summary>
        static EngineSoundGenerator HeldAt3000(EngineAsset engine)
        {
            var generator = new EngineSoundGenerator(engine.Build(), engine.ImpulseResponseSamples, 5, Synthesizer.ConvolutionMode.Partitioned,
                0.0025, EngineSoundGenerator.Core.Dense, BurstBlockRenderer.Renderer, 1f, engine.TunedMix(), true);
            var block = new float[generator.MaxBlockSamples];
            var crank = new EngineSoundGenerator.Controls { ignition = true, starter = true, speedControl = 0.15 };
            for (int b = 0; b < 400; ++b) generator.RenderBlock(crank, block, 0);

            var held = new EngineSoundGenerator.Controls { ignition = true, speedControl = 1, dynoEnabled = true, dynoHold = true };
            double from = generator.Rpm;
            for (int b = 1; b <= 400; ++b)
            {
                held.dynoSpeed = Units.Rpm(from + (3000 - from) * b / 400);
                generator.RenderBlock(held, block, 0);
            }

            for (int b = 0; b < 400; ++b) generator.RenderBlock(held, block, 0);
            return generator;
        }

        /// <summary>The lamps' order (by the spark plugs' angles) is the order the captured ignitions come in.</summary>
        [Test]
        public void LampsFollowTheCapturedFiringOrder()
        {
            EngineAsset engine = TestEngines.Preset(TestEngines.V8);
            using EngineSoundGenerator generator = HeldAt3000(engine);
            EngineCapture capture = generator.Capture;
            int count = capture.ReadableSteps;
            var steps = new CaptureSteps(count, capture.Cylinders, capture.Exhausts);
            Assert.IsTrue(capture.ReadSteps(capture.PublishedSteps - count, steps));

            int[] order = CylinderLights.FiringOrder(engine.Description.engine);
            var place = new int[order.Length];
            for (int i = 0; i < order.Length; ++i) place[order[i]] = i;

            var fired = new List<int>();
            for (int s = 0; s < count; ++s)
            {
                for (int c = 0; c < capture.Cylinders; ++c)
                {
                    if (steps.fired[s * capture.Cylinders + c] != 0) fired.Add(c);
                }
            }

            Assert.Greater(fired.Count, 2 * order.Length, "too few ignitions captured");
            for (int i = 1; i < fired.Count; ++i)
            {
                Assert.AreEqual((place[fired[i - 1]] + 1) % order.Length, place[fired[i]],
                    $"cylinder {fired[i] + 1} fired after cylinder {fired[i - 1] + 1}");
            }
        }

        /// <summary>
        /// The replay takes a whole engine cycle, and stepping through it a simulation step at a time, every cylinder lights up
        /// fully once (at the step its charge was lit).
        /// </summary>
        [Test]
        public void ReplayShowsAWholeCycleWithEveryCylinderFiring()
        {
            EngineAsset engine = TestEngines.Preset(TestEngines.V8);
            using EngineSoundGenerator generator = HeldAt3000(engine);
            EngineCapture capture = generator.Capture;
            var geometry = new EngineGeometry(engine.Description);
            var pose = new EnginePose(geometry.cylinders.Length);
            var replay = new CycleReplay(geometry);

            Assert.IsTrue(replay.Advance(capture, 0, pose, out string note));
            StringAssert.Contains("one engine cycle", note);

            double stepSeconds = CycleReplay.Slowdown / (double)capture.SimulationFrequency;
            int cycleSteps = capture.SimulationFrequency * 120 / 3000;
            var brightest = new double[pose.Cylinders];
            for (int s = 0; s <= cycleSteps; ++s)
            {
                for (int c = 0; c < pose.Cylinders; ++c) brightest[c] = Math.Max(brightest[c], pose.flash[c]);
                Assert.IsTrue(replay.Advance(capture, stepSeconds, pose, out _));
            }

            for (int c = 0; c < pose.Cylinders; ++c) Assert.Greater(brightest[c], 0.95, $"cylinder {c + 1} never lit");
        }

        /// <summary>
        /// A pull runs from the engine's lowest dyno speed to its redline and ends there, recording rising points; at 3000 rpm
        /// it reads the torque a steady hold at 3000 rpm reads, less what speeding the engine up takes (within a tenth).
        /// </summary>
        [Test]
        public void DynoPullRunsToTheRedlineAndReadsTheEnginesTorque()
        {
            EngineAsset engine = TestEngines.Preset(TestEngines.V8);
            EngineParametersDescription e = engine.Description.engine;
            double held;
            using (EngineSoundGenerator steady = HeldAt3000(engine)) held = steady.Capture.DynoTorque;

            IReadOnlyList<(double rpm, double torque)> points = Pull(engine);
            double redline = Units.ToRpm(e.redline);
            Assert.Less(points[0].rpm, Units.ToRpm(e.dynoMinSpeed) + 500, "the pull started late");
            Assert.Greater(points[points.Count - 1].rpm, redline - 2 * DynoSweep.PointSpacingRpm, "the pull stopped short of the redline");
            for (int i = 1; i < points.Count; ++i) Assert.Greater(points[i].rpm, points[i - 1].rpm, "points out of order");

            (double rpm, double torque) at3000 = points[0];
            foreach ((double rpm, double torque) point in points)
            {
                if (Math.Abs(point.rpm - 3000) < Math.Abs(at3000.rpm - 3000)) at3000 = point;
            }

            Assert.AreEqual(held, at3000.torque, held * 0.1,
                $"pull {at3000.torque:0.0} N·m at {at3000.rpm:0} rpm, steady {held:0.0} N·m at 3000 rpm");
        }

        /// <summary>
        /// An engine whose rev limiter is below its redline can't pass it, so its pull ends at the limiter, not the redline: a
        /// V8 limited 500 rpm short of its redline.
        /// </summary>
        [Test]
        public void DynoPullEndsAtARevLimiterBelowTheRedline()
        {
            EngineAsset engine = EnginePresets.TemporaryCopy(TestEngines.Preset(TestEngines.V8));
            try
            {
                EngineDesign design = engine.Design;
                design.ignition.revLimit = design.redline - 500;
                engine.SetDesign(design, EnginePresets.ImpulseResponseFromFile);
                IReadOnlyList<(double rpm, double torque)> points = Pull(engine);
                Assert.Greater(points[points.Count - 1].rpm, design.ignition.revLimit - 2 * DynoSweep.PointSpacingRpm, "the pull stopped short of the limiter");
                Assert.Less(points[points.Count - 1].rpm, design.ignition.revLimit + DynoSweep.PointSpacingRpm, "the pull passed the limiter");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(engine);
            }
        }

        /// <summary>A pull on the engine from a start, as the Bench runs one; fails if it hasn't ended within twice its ideal time.</summary>
        static IReadOnlyList<(double rpm, double torque)> Pull(EngineAsset engine)
        {
            EngineParametersDescription e = engine.Description.engine;
            using EngineSoundGenerator generator = new EngineSoundGenerator(engine.Build(), engine.ImpulseResponseSamples, 5,
                Synthesizer.ConvolutionMode.Partitioned, 0.0025, EngineSoundGenerator.Core.Dense, BurstBlockRenderer.Renderer,
                1f, engine.TunedMix(), true);
            var block = new float[generator.MaxBlockSamples];
            var crank = new EngineSoundGenerator.Controls { ignition = true, starter = true, speedControl = 0.15 };
            for (int b = 0; b < 400; ++b) generator.RenderBlock(crank, block, 0);

            var sweep = new DynoSweep();
            sweep.Start(generator.Capture, e);
            double budget = (Units.ToRpm(e.redline) - Units.ToRpm(e.dynoMinSpeed)) / DynoSweep.RampRpmPerSecond * 2;
            double blockSeconds = (double)generator.StepsPerBlock / generator.SimulationFrequency;
            int blocks = 0;
            while (sweep.Running && blocks * blockSeconds < budget)
            {
                var pull = new EngineSoundGenerator.Controls
                {
                    ignition = true, speedControl = 1, dynoEnabled = true, dynoHold = false, dynoSpeed = Units.Rpm(sweep.Speed),
                };
                generator.RenderBlock(pull, block, 0);
                sweep.Update(generator.Capture);
                ++blocks;
            }

            Assert.IsFalse(sweep.Running, $"the pull hadn't ended after {budget:0.0} s");
            return sweep.Points;
        }
    }
}
