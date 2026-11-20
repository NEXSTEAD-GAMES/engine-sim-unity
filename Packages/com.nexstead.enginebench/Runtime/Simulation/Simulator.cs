// Port of engine-sim include/simulator.h and src/simulator.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
//
// Upstream's frame pacing (startFrame/endFrame, the step budget, simulation speed, latency), the
// audio thread and timing are left out: pacing belongs to the playback layer (EngineSoundGenerator). The
// synthesizer is created here and fed every step, as upstream.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public abstract class Simulator
    {
        /// <summary>Upstream also offers a generic rigid body system; only the optimized one is ported.</summary>
        public enum SystemType
        {
            NsvOptimized
        }

        public sealed class Parameters
        {
            public SystemType systemType = SystemType.NsvOptimized;
        }

        public const int DynoTorqueSamples = 512;

        public readonly Dynamometer dyno = new Dynamometer();
        public readonly StarterMotor starterMotor = new StarterMotor();

        readonly Synthesizer synthesizer = new Synthesizer();

        protected RigidBodySystem system;

        protected Engine engine;
        protected Transmission transmission;
        protected Vehicle vehicle;

        int simulationFrequency = 10000;

        double[] dynoTorqueSamples;
        int lastDynoTorqueSample = 0;

        double filteredEngineSpeed = 0.0;

        public virtual void Initialize(Parameters @params)
        {
            var optimized = new OptimizedNsvRigidBodySystem();
            optimized.Initialize(new GaussSeidelSleSolver());
            system = optimized;

            dynoTorqueSamples = new double[DynoTorqueSamples];
            for (int i = 0; i < DynoTorqueSamples; ++i)
            {
                dynoTorqueSamples[i] = 0.0;
            }
        }

        public virtual void LoadSimulation(Engine engine, Vehicle vehicle, Transmission transmission)
        {
            this.engine = engine;
            this.vehicle = vehicle;
            this.transmission = transmission;
        }

        /// <summary>Seeds every random stream: the combustion chambers' and the synthesizer's.</summary>
        public void SeedRandom(ulong seed)
        {
            engine.SeedRandom(seed);
            synthesizer.SeedRandom(seed);
        }

        /// <summary>One fixed step of upstream's <c>simulateStep</c>.</summary>
        public void SimulateStep()
        {
            double timestep = GetTimestep();
            system.Process(timestep, 1);

            engine.Update(timestep);
            vehicle.Update(timestep);
            transmission.Update(timestep);

            UpdateFilteredEngineSpeed(timestep);

            Crankshaft outputShaft = engine.GetOutputCrankshaft();
            outputShaft.ResetAngle();

            for (int i = 0; i < engine.GetCrankshaftCount(); ++i)
            {
                Crankshaft shaft = engine.GetCrankshaft(i);

                // Correct drift (temporary hack)
                shaft.body.theta = outputShaft.body.theta;
            }

            // DynoTorqueSamples - 1: a full cycle angle would index past the end (the community fork's fix, bobsayshilol, fbe369d).
            int index =
                (int)Math.Floor((DynoTorqueSamples - 1) * outputShaft.GetCycleAngle() / (4 * Constants.pi));
            int step = engine.IsSpinningCw() ? 1 : -1;
            dynoTorqueSamples[index] = dyno.GetTorque();

            if (lastDynoTorqueSample != index)
            {
                for (int i = lastDynoTorqueSample + step; i != index; i += step)
                {
                    if (i >= DynoTorqueSamples)
                    {
                        i = -1;
                        continue;
                    }
                    else if (i < 0)
                    {
                        i = DynoTorqueSamples;
                        continue;
                    }

                    dynoTorqueSamples[i] = dyno.GetTorque();
                }

                lastDynoTorqueSample = index;
            }

            SimulateStep_();

            WriteToSynthesizer();
        }

        public virtual double GetTotalExhaustFlow()
        {
            return 0.0;
        }

        public Synthesizer GetSynthesizer() => synthesizer;

        public Engine GetEngine() => engine;
        public Transmission GetTransmission() => transmission;
        public Vehicle GetVehicle() => vehicle;
        public RigidBodySystem GetSystem() => system;

        public void SetSimulationFrequency(int frequency) => simulationFrequency = frequency;
        public int GetSimulationFrequency() => simulationFrequency;

        public double GetTimestep() => 1.0 / simulationFrequency;

        public virtual double GetFilteredDynoTorque()
        {
            if (dynoTorqueSamples == null) return 0;

            double averageTorque = 0;
            for (int i = 0; i < DynoTorqueSamples; ++i)
            {
                averageTorque += dynoTorqueSamples[i];
            }

            return averageTorque / DynoTorqueSamples;
        }

        public virtual double GetDynoPower()
        {
            return (engine != null)
                ? GetFilteredDynoTorque() * engine.GetSpeed()
                : 0;
        }

        public virtual double GetAverageOutputSignal()
        {
            return 0.0;
        }

        public double FilteredEngineSpeed() => filteredEngineSpeed;

        // Read access for the dense core's snapshot.
        internal double[] DynoTorqueSampleArray => dynoTorqueSamples;
        internal int LastDynoTorqueSample => lastDynoTorqueSample;

        /// <summary>
        /// As upstream: 44.1 kHz output, one input channel per exhaust system, buffers of one second. The input rate starts at
        /// the simulation frequency current at load; the runtime keeps it equal to the stepping rate.
        /// </summary>
        protected void InitializeSynthesizer()
        {
            synthesizer.Initialize(new Synthesizer.Parameters
            {
                audioBufferSize = 44100,
                audioSampleRate = 44100,
                inputBufferSize = 44100,
                inputChannelCount = engine.GetExhaustSystemCount(),
                // The forced induction's control channels. The community fork adds them to every engine; without forced
                // induction they carry zeros and make no sound, so only an engine with it gets them.
                auxChannelCount = engine.GetForcedInduction() != null ? Synthesizer.ForcedInductionChannelCount : 0,
                inputSampleRate = (float)GetSimulationFrequency(),
            });
        }

        protected virtual void SimulateStep_()
        {
            /* void */
        }

        protected abstract void WriteToSynthesizer();

        void UpdateFilteredEngineSpeed(double dt)
        {
            double alpha = dt / (100 + dt);
            filteredEngineSpeed = alpha * filteredEngineSpeed + (1 - alpha) * engine.GetRpm();
        }
    }
}
