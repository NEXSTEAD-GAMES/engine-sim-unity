// Checks of the simulation and the block generator on the presets: every engine runs, and nothing allocates once running.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Editor;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace Nexstead.EngineBench.Tests
{
    public sealed class GeneratorTests
    {
        /// <summary>Every preset engine builds from its description and runs without producing NaN.</summary>
        [Test]
        public void EveryPresetBuildsAndSteps()
        {
            var failures = new List<string>();
            foreach (string name in EnginePresets.EngineNames())
            {
                EngineDescription description = TestEngines.Preset(name).Description;
                EngineAssembly assembly = EngineBuilder.Build(description);
                Engine engine = assembly.engine;

                if (engine.GetCylinderCount() != description.engine.pistons.Length
                    || engine.GetExhaustSystemCount() != description.engine.exhaustSystems.Length
                    || engine.GetIntakeCount() != description.engine.intakes.Length
                    || engine.GetCrankshaftCount() != description.engine.crankshafts.Length)
                {
                    failures.Add(name + ": part counts differ from the description");
                    continue;
                }

                var simulator = (PistonEngineSimulator)engine.CreateSimulator(assembly.vehicle, assembly.transmission);
                simulator.SetSimulationFrequency((int)engine.GetSimulationFrequency());
                engine.GetIgnitionModule().enabled = true;
                simulator.starterMotor.enabled = true;
                for (int step = 0; step < 1000; ++step) simulator.SimulateStep();

                bool finite = !double.IsNaN(engine.GetOutputCrankshaft().body.v_theta);
                for (int i = 0; i < engine.GetCylinderCount(); ++i) finite &= !double.IsNaN(engine.GetChamber(i).system.Pressure());
                if (!finite) failures.Add(name + ": NaN after 1000 steps");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// Once running, stepping and rendering allocate nothing (the simulation is meant for the audio path): 20 blocks of
        /// 10 ms of a running V8, each stepped, ended, rendered and read.
        /// </summary>
        [Test]
        public void SteppingAndRenderingDoNotAllocate()
        {
            EngineAsset v8 = TestEngines.Preset(TestEngines.V8);
            EngineAssembly assembly = v8.Build();
            Engine engine = assembly.engine;
            var simulator = (PistonEngineSimulator)engine.CreateSimulator(assembly.vehicle, assembly.transmission);
            simulator.SetSimulationFrequency((int)engine.GetSimulationFrequency());
            Synthesizer synthesizer = simulator.GetSynthesizer();
            for (int i = 0; i < engine.GetExhaustSystemCount(); ++i)
            {
                ImpulseResponse response = engine.GetExhaustSystem(i).GetImpulseResponse();
                short[] samples = v8.ImpulseResponseSamples(response.GetFilename());
                synthesizer.InitializeImpulseResponse(samples, samples.Length, (float)response.GetVolume(), i);
            }

            engine.GetIgnitionModule().enabled = true;
            simulator.starterMotor.enabled = true;
            engine.SetSpeedControl(0.5);

            var output = new short[Synthesizer.RenderLimit];
            int blockSteps = simulator.GetSimulationFrequency() / 100;
            void Block()
            {
                synthesizer.SetInputSampleRate(simulator.GetSimulationFrequency());
                for (int step = 0; step < blockSteps; ++step) simulator.SimulateStep();
                synthesizer.EndInputBlock();
                synthesizer.RenderAudio();
                synthesizer.ReadAudioOutput(synthesizer.GetOutputSize(), output);
            }

            for (int block = 0; block < 20; ++block) Block();

            // Positive control: the recorder sees one allocation.
            Assert.AreEqual(1, CountAllocations(() => GC.KeepAlive(new double[4])), "GC.Alloc recorder control");

            int allocations = CountAllocations(() =>
            {
                for (int block = 0; block < 20; ++block) Block();
            });
            Assert.AreEqual(0, allocations, "allocations in 20 blocks");
        }

        /// <summary>A generator's blocks allocate nothing once running, on either core.</summary>
        [TestCase(EngineSoundGenerator.Core.Managed)]
        [TestCase(EngineSoundGenerator.Core.Dense)]
        public void GeneratorBlocksDoNotAllocate(EngineSoundGenerator.Core core)
        {
            using EngineSoundGenerator generator = TestEngines.Generator(TestEngines.Preset(TestEngines.V8), 1, core);
            var controls = new EngineSoundGenerator.Controls { speedControl = 0.5, ignition = true, starter = true };
            var block = new float[generator.MaxBlockSamples];
            for (int b = 0; b < 20; ++b) generator.RenderBlock(controls, block, 0);

            Assert.AreEqual(1, CountAllocations(() => GC.KeepAlive(new double[4])), "GC.Alloc recorder control");
            int allocations = CountAllocations(() =>
            {
                for (int b = 0; b < 20; ++b) generator.RenderBlock(controls, block, 0);
            });
            Assert.AreEqual(0, allocations, "allocations in 20 blocks");
        }

        static int CountAllocations(Action action)
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            action();

            recorder.enabled = false;
            recorder.CollectFromAllThreads();
            return recorder.sampleBlockCount;
        }
    }
}
