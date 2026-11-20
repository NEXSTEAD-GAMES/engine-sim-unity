// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense core's step: Simulator.SimulateStep, PistonEngineSimulator.SimulateStep_ and
// WriteToSynthesizer, Vehicle.Update and Transmission.Update, line for line. See DenseEngine.cs.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public unsafe struct DenseSimulator
    {
        public int simulationFrequency;
        public int fluidSimulationSteps;
        public double filteredEngineSpeed;

        /// <summary><see cref="Simulator.DynoTorqueSamples"/> values over the engine cycle.</summary>
        public double* dynoTorqueSamples;
        public int lastDynoTorqueSample;

        /// <summary>One per cylinder: the exhaust's delay to the synthesizer.</summary>
        public DelayLine* delayFilters;

        /// <summary>One per exhaust system, then the forced induction's control channels if it has one: this step's synthesizer input.</summary>
        public double* exhaustFlowStagingBuffer;

        // Vehicle
        public int vehicleMassBody;
        public double vehicleMass;
        public double travelledDistance;

        // Transmission
        public int gear;
        public double clutchPressure;
        public double maxClutchTorque;

        /// <summary>The Bench's capture, recorded after each step; null when off (the default).</summary>
        public DenseCapture* capture;
    }

    /// <summary>Mirror of <see cref="DelayFilter"/> and its <c>RingBuffer&lt;double&gt;</c>.</summary>
    public unsafe struct DelayLine
    {
        public double* buffer;
        public int capacity;
        public int writeIndex;
        public int start;
        public int latencySamples;
    }

    public static unsafe class SimulatorOps
    {
        const int DynoTorqueSamples = Simulator.DynoTorqueSamples;

        public static void Snapshot(PistonEngineSimulator simulator, DenseMemory memory, DenseSimulator* s)
        {
            Engine engine = simulator.GetEngine();

            s->simulationFrequency = simulator.GetSimulationFrequency();
            s->fluidSimulationSteps = simulator.GetFluidSimulationSteps();
            s->filteredEngineSpeed = simulator.FilteredEngineSpeed();
            s->dynoTorqueSamples = memory.Copy(simulator.DynoTorqueSampleArray);
            s->lastDynoTorqueSample = simulator.LastDynoTorqueSample;

            DelayFilter[] delayFilters = simulator.DelayFilters;
            s->delayFilters = memory.Alloc<DelayLine>(delayFilters.Length);
            for (int i = 0; i < delayFilters.Length; ++i)
            {
                RingBuffer<double> history = delayFilters[i].History;
                DelayLine* d = &s->delayFilters[i];
                d->buffer = memory.Copy(history.Buffer);
                d->capacity = history.Capacity;
                d->writeIndex = history.WriteIndex();
                d->start = history.Start();
                d->latencySamples = delayFilters[i].LatencySamples;
            }

            s->exhaustFlowStagingBuffer = memory.Copy(simulator.ExhaustFlowStagingBuffer);

            Vehicle vehicle = simulator.GetVehicle();
            s->vehicleMassBody = simulator.VehicleMass.index;
            s->vehicleMass = vehicle.GetMass();
            s->travelledDistance = vehicle.GetTravelledDistance();

            Transmission transmission = simulator.GetTransmission();
            s->gear = transmission.GetGear();
            s->clutchPressure = transmission.GetClutchPressure();
            s->maxClutchTorque = transmission.MaxClutchTorque;

            if (engine.GetExhaustSystemCount() + (engine.GetForcedInduction() != null ? Synthesizer.ForcedInductionChannelCount : 0)
                != simulator.ExhaustFlowStagingBuffer.Length)
            {
                throw new InvalidOperationException("Staging buffer and exhaust systems disagree.");
            }
        }

        public static double GetTimestep(DenseEngine* e) => 1.0 / e->simulator.simulationFrequency;

        /// <summary>The controls of <see cref="EngineSoundGenerator.RenderBlock"/>, applied the same way.</summary>
        public static void ApplyControls(DenseEngine* e, EngineSoundGenerator.Controls* controls)
        {
            PartsOps.SetSpeedControl(e, controls->speedControl);
            PartsOps.SetIgnitionEnabled(e, controls->ignition);
            SolverOps.SetStarter(e, controls->starter);
            SolverOps.SetDyno(e, controls->dynoEnabled, controls->dynoHold, controls->dynoSpeed);
        }

        /// <summary>
        /// <see cref="EngineSoundGenerator.RenderBlock"/>'s work after its controls: <paramref name="steps"/> steps, the block's end,
        /// one render, and the rendered samples into <paramref name="output"/> (the engine, 16-bit),
        /// <paramref name="forcedInductionOutput"/> (the forced induction, unclamped floats on the 16-bit scale) and
        /// <paramref name="mixOutput"/> (the two leveled together, 16-bit); a null pointer drops its output. Returns how many.
        /// </summary>
        public static int RenderBlock(DenseEngine* e, EngineSoundGenerator.Controls* controls, int steps, short* output, float* forcedInductionOutput,
            short* mixOutput)
        {
            ApplyControls(e, controls);

            DenseSynthesizer* synthesizer = &e->synthesizer;
            SynthOps.SetInputSampleRate(synthesizer, e->simulator.simulationFrequency);
            for (int step = 0; step < steps; ++step)
            {
                SimulateStep(e);
            }

            SynthOps.EndInputBlock(synthesizer);
            if (SynthOps.GetInputSize(synthesizer) > 0)
            {
                SynthOps.RenderAudio(synthesizer);
            }

            int count = SynthOps.GetOutputSize(synthesizer);
            SynthOps.ReadAudioOutput(synthesizer, count, output, forcedInductionOutput, mixOutput);
            return count;
        }

        /// <summary><see cref="Simulator.SimulateStep"/>.</summary>
        public static void SimulateStep(DenseEngine* e)
        {
            DenseSimulator* s = &e->simulator;
            RigidBodyData* bodies = e->solver.bodies;

            double timestep = GetTimestep(e);
            SolverOps.Process(e, timestep, 1);

            PartsOps.EngineUpdate(e, timestep);
            VehicleUpdate(e, timestep);
            TransmissionUpdate(e, timestep);

            UpdateFilteredEngineSpeed(e, timestep);

            // The output crankshaft is crankshaft 0.
            PartsOps.ResetCrankAngle(e, 0);

            int crankshaftCount = PartsOps.CrankshaftCount(e);
            int outputBody = PartsOps.CrankBody(e, 0);
            for (int i = 0; i < crankshaftCount; ++i)
            {
                // Correct drift (temporary hack)
                bodies[PartsOps.CrankBody(e, i)].theta = bodies[outputBody].theta;
            }

            int index =
                (int)Math.Floor((DynoTorqueSamples - 1) * PartsOps.CrankCycleAngle(e, 0) / (4 * Constants.pi));
            int step = PartsOps.IsSpinningCw(e) ? 1 : -1;
            s->dynoTorqueSamples[index] = SolverOps.DynoTorque(e);

            if (s->lastDynoTorqueSample != index)
            {
                for (int i = s->lastDynoTorqueSample + step; i != index; i += step)
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

                    s->dynoTorqueSamples[i] = SolverOps.DynoTorque(e);
                }

                s->lastDynoTorqueSample = index;
            }

            SimulateStep_(e);

            WriteToSynthesizer(e);

            if (s->capture != null) CaptureOps.Record(e, s->capture);
        }

        /// <summary><see cref="PistonEngineSimulator"/>'s <c>SimulateStep_</c>. The intake flow-rate display sum is not kept: nothing reads it.</summary>
        static void SimulateStep_(DenseEngine* e)
        {
            double timestep = GetTimestep(e);
            PartsOps.IgnitionUpdate(e, timestep);

            int cylinderCount = PartsOps.CylinderCount(e);
            for (int i = 0; i < cylinderCount; ++i)
            {
                if (PartsOps.IgnitionEvent(e, i))
                {
                    PartsOps.ChamberIgnite(e, i);
                }

                PartsOps.ChamberUpdate(e, i, timestep);
            }

            for (int i = 0; i < cylinderCount; ++i)
            {
                PartsOps.ChamberResetTimestepFlows(e, i);
            }

            int exhaustSystemCount = PartsOps.ExhaustCount(e);
            int intakeCount = PartsOps.IntakeCount(e);
            double fluidTimestep = timestep / e->simulator.fluidSimulationSteps;
            for (int i = 0; i < e->simulator.fluidSimulationSteps; ++i)
            {
                for (int j = 0; j < exhaustSystemCount; ++j)
                {
                    PartsOps.ExhaustProcess(e, j, fluidTimestep);
                }

                if (e->parts.forcedInduction != null)
                {
                    ForcedInductionOps.Process(e, fluidTimestep);
                }

                for (int j = 0; j < intakeCount; ++j)
                {
                    PartsOps.IntakeProcess(e, j, fluidTimestep);
                }

                for (int j = 0; j < cylinderCount; ++j)
                {
                    PartsOps.ChamberFlow(e, j, fluidTimestep);
                }
            }

            PartsOps.ResetIgnitionEvents(e);
        }

        /// <summary><see cref="PistonEngineSimulator"/>'s <c>WriteToSynthesizer</c>.</summary>
        static void WriteToSynthesizer(DenseEngine* e)
        {
            DenseSimulator* s = &e->simulator;
            double* staging = s->exhaustFlowStagingBuffer;

            int exhaustSystemCount = PartsOps.ExhaustCount(e);
            for (int i = 0; i < exhaustSystemCount; ++i)
            {
                staging[i] = 0;
            }

            double attenuation = Utilities.StdMin(Math.Abs(s->filteredEngineSpeed), 40.0) / 40.0;
            double attenuation_3 = attenuation * attenuation * attenuation;

            int cylinderCount = PartsOps.CylinderCount(e);
            for (int i = 0; i < cylinderCount; ++i)
            {
                int exhaust = PartsOps.CylinderExhaust(e, i);
                double exhaustLength =
                    PartsOps.HeaderPrimaryLength(e, i)
                    + PartsOps.ExhaustLength(e, exhaust);

                double exhaustFlow =
                    attenuation_3 * 1600 * (
                        1.0 * (PartsOps.ExhaustRunnerPressure(e, i) - Units.Pressure(1.0, Units.atm))
                        + 0.1 * PartsOps.ExhaustRunnerDynamicPressure(e, i, 1.0, 0.0)
                        + 0.1 * PartsOps.ExhaustRunnerDynamicPressure(e, i, -1.0, 0.0));

                double delayedExhaustPulse =
                    DelayFastF(&s->delayFilters[i], exhaustFlow);

                staging[exhaust] +=
                    PartsOps.SoundAttenuation(e, i)
                    * (PartsOps.ExhaustAudioVolume(e, exhaust) * delayedExhaustPulse / cylinderCount)
                    * (1 / (exhaustLength * exhaustLength));
            }

            ForcedInductionData* f = e->parts.forcedInduction;
            if (f != null)
            {
                if (ForcedInductionOps.IsTurbocharger(f))
                {
                    double muffling = 1.0 - 0.35 * f->turbineFraction;
                    for (int i = 0; i < exhaustSystemCount; ++i)
                    {
                        staging[i] *= muffling;
                    }
                }

                int aux = exhaustSystemCount;
                staging[aux + 0] = ForcedInductionOps.GetWhineFrequency(f) * f->whistlePitch;
                staging[aux + 1] = ForcedInductionOps.GetWhineLevel(e, f);
                staging[aux + 2] = ForcedInductionOps.GetBlowoffLevel(e, f);
                staging[aux + 3] = ForcedInductionOps.GetFlowLevel(f);
                staging[aux + 4] = f->surge;
                staging[aux + 5] = ForcedInductionOps.GetWastegateNoise(f);
                staging[aux + 6] = ForcedInductionOps.GetSoundMode(f);
            }

            SynthOps.WriteInput(&e->synthesizer, staging);
        }

        /// <summary><see cref="DelayFilter.FastF"/>.</summary>
        static double DelayFastF(DelayLine* d, double sample)
        {
            d->buffer[d->writeIndex] = sample;
            if (++d->writeIndex >= d->capacity)
            {
                d->writeIndex = 0;
            }

            int size = (d->writeIndex < d->start)
                ? d->writeIndex + (d->capacity - d->start)
                : d->writeIndex - d->start;
            if (size <= d->latencySamples)
            {
                return 0;
            }

            double v = d->buffer[d->start];
            d->start += 1;
            if (d->start >= d->capacity)
            {
                d->start -= d->capacity;
            }

            return v;
        }

        /// <summary><see cref="Vehicle.Update"/>.</summary>
        static void VehicleUpdate(DenseEngine* e, double dt)
        {
            DenseSimulator* s = &e->simulator;
            RigidBodyData* rotatingMass = &e->solver.bodies[s->vehicleMassBody];

            double E_r = 0.5 * rotatingMass->I * rotatingMass->v_theta * rotatingMass->v_theta;
            double vehicleSpeed = Math.Sqrt(2 * E_r / s->vehicleMass);
            s->travelledDistance += vehicleSpeed * dt;
        }

        /// <summary><see cref="Transmission.Update"/>.</summary>
        static void TransmissionUpdate(DenseEngine* e, double dt)
        {
            DenseSimulator* s = &e->simulator;
            if (s->gear == -1)
            {
                SolverOps.SetClutchTorqueLimits(e, 0, 0);
            }
            else
            {
                SolverOps.SetClutchTorqueLimits(e, -s->maxClutchTorque * s->clutchPressure, s->maxClutchTorque * s->clutchPressure);
            }
        }

        static void UpdateFilteredEngineSpeed(DenseEngine* e, double dt)
        {
            DenseSimulator* s = &e->simulator;
            double alpha = dt / (100 + dt);
            s->filteredEngineSpeed = alpha * s->filteredEngineSpeed + (1 - alpha) * PartsOps.GetRpm(e);
        }
    }

    /// <summary>
    /// A dense snapshot of a loaded <see cref="PistonEngineSimulator"/> and the memory it lives in. Take it after the simulator is
    /// loaded, its synthesizer has its impulse responses and its streams are seeded, and before the first step; from then on step
    /// the dense engine instead of the managed one.
    /// </summary>
    public sealed unsafe class DenseCore : IDisposable
    {
        readonly DenseMemory memory = new DenseMemory();
        DenseEngine* engine;

        public DenseCore(PistonEngineSimulator simulator)
        {
            if (simulator == null) throw new ArgumentNullException(nameof(simulator));

            try
            {
                engine = memory.Alloc<DenseEngine>(1);
                PartsOps.Snapshot(simulator.GetEngine(), memory, &engine->parts);
                SolverOps.Snapshot(simulator, memory, &engine->solver);
                SimulatorOps.Snapshot(simulator, memory, &engine->simulator);
                SynthOps.Snapshot(simulator.GetSynthesizer(), memory, &engine->synthesizer);
            }
            catch
            {
                memory.Dispose();
                throw;
            }
        }

        public DenseEngine* Engine => engine;

        public void Dispose()
        {
            memory.Dispose();
            engine = null;
        }
    }
}
