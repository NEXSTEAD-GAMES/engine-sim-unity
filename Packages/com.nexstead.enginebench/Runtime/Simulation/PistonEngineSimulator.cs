// Port of engine-sim include/piston_engine_simulator.h and src/piston_engine_simulator.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
//
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class PistonEngineSimulator : Simulator
    {
        DelayFilter[] delayFilters;

        FixedPositionConstraint[] crankConstraints;
        ClutchConstraint[] crankshaftLinks;
        RotationFrictionConstraint[] crankshaftFrictionConstraints;
        LineConstraint[] cylinderWallConstraints;
        LinkConstraint[] linkConstraints;
        readonly RigidBody vehicleMass = new RigidBody();
        readonly VehicleDragConstraint vehicleDrag = new VehicleDragConstraint();

        double[] exhaustFlowStagingBuffer;

        int fluidSimulationSteps = 8;

        public override void LoadSimulation(Engine engine, Vehicle vehicle, Transmission transmission)
        {
            base.LoadSimulation(engine, vehicle, transmission);

            int crankCount = this.engine.GetCrankshaftCount();
            int cylinderCount = this.engine.GetCylinderCount();
            int linkCount = cylinderCount * 2;

            if (crankCount <= 0) return;

            crankConstraints = Construct<FixedPositionConstraint>(crankCount);
            cylinderWallConstraints = Construct<LineConstraint>(cylinderCount);
            linkConstraints = Construct<LinkConstraint>(linkCount);
            crankshaftFrictionConstraints = Construct<RotationFrictionConstraint>(crankCount);
            crankshaftLinks = Construct<ClutchConstraint>(crankCount - 1);
            delayFilters = Construct<DelayFilter>(cylinderCount);

            const double ks = 5000;
            const double kd = 10;

            for (int i = 0; i < crankCount; ++i)
            {
                Crankshaft outputShaft = this.engine.GetCrankshaft(0);
                Crankshaft crankshaft = this.engine.GetCrankshaft(i);

                crankConstraints[i].SetBody(crankshaft.body);
                crankConstraints[i].SetWorldPosition(
                    crankshaft.GetPosX(),
                    crankshaft.GetPosY());
                crankConstraints[i].SetLocalPosition(0.0, 0.0);
                crankConstraints[i].kd = kd;
                crankConstraints[i].ks = ks;

                crankshaft.body.p_x = crankshaft.GetPosX();
                crankshaft.body.p_y = crankshaft.GetPosY();
                crankshaft.body.theta = 0;
                crankshaft.body.m =
                    crankshaft.GetMass() + crankshaft.GetFlywheelMass();
                crankshaft.body.I = crankshaft.GetMomentOfInertia();

                crankshaftFrictionConstraints[i].minTorque = -crankshaft.GetFrictionTorque();
                crankshaftFrictionConstraints[i].maxTorque = crankshaft.GetFrictionTorque();
                crankshaftFrictionConstraints[i].SetBody(this.engine.GetCrankshaft(i).body);

                system.AddRigidBody(this.engine.GetCrankshaft(i).body);
                system.AddConstraint(crankConstraints[i]);
                system.AddConstraint(crankshaftFrictionConstraints[i]);

                if (crankshaft != outputShaft)
                {
                    ClutchConstraint crankLink = crankshaftLinks[i - 1];
                    crankLink.SetBody1(outputShaft.body);
                    crankLink.SetBody2(crankshaft.body);

                    system.AddConstraint(crankLink);
                }
            }

            this.transmission.AddToSystem(system, vehicleMass, this.vehicle, this.engine);
            this.vehicle.AddToSystem(system, vehicleMass);

            vehicleDrag.Initialize(vehicleMass, this.vehicle);
            system.AddConstraint(vehicleDrag);

            vehicleMass.Reset();
            vehicleMass.m = 1.0;
            vehicleMass.I = 1.0;
            system.AddRigidBody(vehicleMass);

            for (int i = 0; i < cylinderCount; ++i)
            {
                Piston piston = this.engine.GetPiston(i);
                ConnectingRod connectingRod = piston.GetRod();

                CylinderBank bank = piston.GetCylinderBank();
                double dx = Math.Cos(bank.GetAngle() + Constants.pi / 2);
                double dy = Math.Sin(bank.GetAngle() + Constants.pi / 2);

                cylinderWallConstraints[i].SetBody(piston.body);
                cylinderWallConstraints[i].dx = dx;
                cylinderWallConstraints[i].dy = dy;
                cylinderWallConstraints[i].local_x = 0.0;
                cylinderWallConstraints[i].local_y = piston.GetWristPinLocation();
                cylinderWallConstraints[i].p0_x = bank.GetX();
                cylinderWallConstraints[i].p0_y = bank.GetY();
                cylinderWallConstraints[i].ks = ks;
                cylinderWallConstraints[i].kd = kd;

                piston.SetCylinderConstraint(cylinderWallConstraints[i]);

                linkConstraints[i * 2 + 0].SetBody1(connectingRod.body);
                linkConstraints[i * 2 + 0].SetBody2(piston.body);
                linkConstraints[i * 2 + 0]
                    .SetLocalPosition1(0.0, connectingRod.GetLittleEndLocal());
                linkConstraints[i * 2 + 0].SetLocalPosition2(0.0, piston.GetWristPinLocation());
                linkConstraints[i * 2 + 0].ks = ks;
                linkConstraints[i * 2 + 0].kd = kd;

                Crankshaft crankshaft = connectingRod.GetCrankshaft();
                crankshaft.GetRodJournalPositionLocal(
                    connectingRod.GetJournal(),
                    out double journal_x,
                    out double journal_y);
                linkConstraints[i * 2 + 1].SetBody2(crankshaft.body);

                linkConstraints[i * 2 + 1].SetBody1(connectingRod.body);
                linkConstraints[i * 2 + 1]
                    .SetLocalPosition1(0.0, connectingRod.GetBigEndLocal());
                linkConstraints[i * 2 + 1]
                    .SetLocalPosition2(journal_x, journal_y);
                linkConstraints[i * 2 + 1].ks = ks;
                // The community fork's fix (Samuel, 77a5b95): upstream set the first link's damping again here.
                linkConstraints[i * 2 + 1].kd = kd;

                piston.body.m = piston.GetMass();
                piston.body.I = 1.0;

                connectingRod.body.m = connectingRod.GetMass();
                connectingRod.body.I = connectingRod.GetMomentOfInertia();

                system.AddRigidBody(piston.body);
                system.AddRigidBody(connectingRod.body);
                system.AddConstraint(linkConstraints[i * 2 + 0]);
                system.AddConstraint(linkConstraints[i * 2 + 1]);
                system.AddConstraint(cylinderWallConstraints[i]);
                system.AddForceGenerator(this.engine.GetChamber(i));
            }

            dyno.ConnectCrankshaft(this.engine.GetOutputCrankshaft());
            system.AddConstraint(dyno);

            starterMotor.ConnectCrankshaft(this.engine.GetOutputCrankshaft());
            starterMotor.maxTorque = this.engine.GetStarterTorque();
            starterMotor.rotationSpeed = -this.engine.GetStarterSpeed();
            system.AddConstraint(starterMotor);

            // Superchargers load the crankshaft with their drive torque (community fork, forced induction)
            if (this.engine.GetForcedInduction() != null)
            {
                system.AddForceGenerator(this.engine.GetForcedInduction());
            }

            PlaceAndInitialize();
            InitializeSynthesizer();

            // The reference's determinism patch seeds every stream with 0 on load.
            SeedRandom(0);
        }

        static T[] Construct<T>(int count) where T : new()
        {
            var array = new T[count];
            for (int i = 0; i < count; ++i)
            {
                array[i] = new T();
            }

            return array;
        }

        public override double GetAverageOutputSignal()
        {
            double sum = 0.0;
            for (int i = 0; i < engine.GetExhaustSystemCount(); ++i)
            {
                sum += engine.GetExhaustSystem(i).GetSystem().Pressure();
            }

            return sum / engine.GetExhaustSystemCount();
        }

        void PlaceAndInitialize()
        {
            int cylinderCount = engine.GetCylinderCount();
            for (int i = 0; i < cylinderCount; ++i)
            {
                PlaceCylinder(i);
            }

            for (int i = 0; i < cylinderCount; ++i)
            {
                engine.GetChamber(i).system.Initialize(
                    Units.Pressure(1.0, Units.atm),
                    engine.GetChamber(i).GetVolume(),
                    Units.Celcius(25.0));

                Piston piston = engine.GetChamber(i).GetPiston();
                CylinderHead head = engine.GetChamber(i).GetCylinderHead();
                ExhaustSystem exhaust = head.GetExhaustSystem(piston.GetCylinderIndex());
                double exhaustLength =
                    head.GetHeaderPrimaryLength(piston.GetCylinderIndex())
                    + exhaust.GetLength();
                double speedOfSound = 343.0 * Units.m / Units.sec;
                double delay = exhaustLength / speedOfSound;
                delayFilters[i].Initialize(delay, 10000.0);
            }

            engine.GetIgnitionModule().Reset();

            // Exhaust channels followed by the forced induction's control channels, if it has one
            exhaustFlowStagingBuffer = new double[engine.GetExhaustSystemCount()
                + (engine.GetForcedInduction() != null ? Synthesizer.ForcedInductionChannelCount : 0)];
        }

        void PlaceCylinder(int i)
        {
            ConnectingRod rod = engine.GetConnectingRod(i);
            Piston piston = engine.GetPiston(i);
            CylinderBank bank = piston.GetCylinderBank();

            rod.GetCrankshaft().GetRodJournalPositionGlobal(rod.GetJournal(), out double p_x, out double p_y);

            // (bank->m_x + bank->m_dx * s - p_x)^2 + (bank->m_y + bank->m_dy * s - p_y)^2 = (rod->m_length)^2
            double a = bank.GetDx() * bank.GetDx() + bank.GetDy() * bank.GetDy();
            double b = -2 * bank.GetDx() * (p_x - bank.GetX()) - 2 * bank.GetDy() * (p_y - bank.GetY());
            double c =
                (p_x - bank.GetX()) * (p_x - bank.GetX())
                + (p_y - bank.GetY()) * (p_y - bank.GetY())
                - rod.GetLength() * rod.GetLength();

            double det = b * b - 4 * a * c;
            if (det < 0) return;

            double sqrt_det = Math.Sqrt(det);
            double s0 = (-b + sqrt_det) / (2 * a);
            double s1 = (-b - sqrt_det) / (2 * a);

            double s = Utilities.StdMax(s0, s1);
            if (s < 0) return;

            double e_x = s * bank.GetDx() + bank.GetX();
            double e_y = s * bank.GetDy() + bank.GetY();

            double theta = ((e_y - p_y) > 0)
                ? Math.Acos((e_x - p_x) / rod.GetLength())
                : 2 * Constants.pi - Math.Acos((e_x - p_x) / rod.GetLength());
            rod.body.theta = theta - Constants.pi / 2;

            rod.body.LocalToWorld(0, rod.GetBigEndLocal(), out double cl_x, out double cl_y);
            rod.body.p_x += p_x - cl_x;
            rod.body.p_y += p_y - cl_y;

            piston.body.p_x = e_x;
            piston.body.p_y = e_y;
            piston.body.theta = bank.GetAngle() + Constants.pi;
        }

        protected override void SimulateStep_()
        {
            double timestep = GetTimestep();
            IgnitionModule im = engine.GetIgnitionModule();
            im.Update(timestep);

            int cylinderCount = engine.GetCylinderCount();
            for (int i = 0; i < cylinderCount; ++i)
            {
                if (im.GetIgnitionEvent(i))
                {
                    engine.GetChamber(i).Ignite();
                }

                engine.GetChamber(i).Update(timestep);
            }

            for (int i = 0; i < cylinderCount; ++i)
            {
                engine.GetChamber(i).ResetLastTimestepExhaustFlow();
                engine.GetChamber(i).ResetLastTimestepIntakeFlow();
            }

            int exhaustSystemCount = engine.GetExhaustSystemCount();
            int intakeCount = engine.GetIntakeCount();
            ForcedInduction forcedInduction = engine.GetForcedInduction();
            double fluidTimestep = timestep / fluidSimulationSteps;
            for (int i = 0; i < fluidSimulationSteps; ++i)
            {
                for (int j = 0; j < exhaustSystemCount; ++j)
                {
                    engine.GetExhaustSystem(j).Process(fluidTimestep);
                }

                forcedInduction?.Process(fluidTimestep);

                for (int j = 0; j < intakeCount; ++j)
                {
                    engine.GetIntake(j).Process(fluidTimestep);
                    // Frame bookkeeping for the intake flow display; upstream's startFrame/endFrame
                    // reset and normalise it. Nothing here paces frames, so it only accumulates: display
                    // data, read by nothing.
                    engine.GetIntake(j).flowRate += engine.GetIntake(j).flow;
                }

                for (int j = 0; j < cylinderCount; ++j)
                {
                    engine.GetChamber(j).Flow(fluidTimestep);
                }
            }

            im.ResetIgnitionEvents();
        }

        public override double GetTotalExhaustFlow()
        {
            double totalFlow = 0.0;
            for (int i = 0; i < engine.GetCylinderCount(); ++i)
            {
                totalFlow += engine.GetChamber(i).GetLastTimestepExhaustFlow();
            }

            return totalFlow;
        }

        public void SetFluidSimulationSteps(int steps) => fluidSimulationSteps = steps;
        public int GetFluidSimulationSteps() => fluidSimulationSteps;
        public int GetFluidSimulationFrequency() => fluidSimulationSteps * GetSimulationFrequency();

        /// <summary>The value written for an exhaust system's synthesizer channel in the last step.</summary>
        public double GetSynthesizerInput(int exhaustIndex) => exhaustFlowStagingBuffer[exhaustIndex];

        /// <summary>A forced induction control channel's value in the last step (<see cref="Synthesizer.ForcedInductionChannelCount"/>); the engine must have one.</summary>
        public double GetForcedInductionControl(int channel) => exhaustFlowStagingBuffer[engine.GetExhaustSystemCount() + channel];

        // Read access for the dense core's snapshot.
        internal DelayFilter[] DelayFilters => delayFilters;
        internal double[] ExhaustFlowStagingBuffer => exhaustFlowStagingBuffer;
        internal RigidBody VehicleMass => vehicleMass;

        protected override void WriteToSynthesizer()
        {
            int exhaustSystemCount = engine.GetExhaustSystemCount();
            for (int i = 0; i < exhaustSystemCount; ++i)
            {
                exhaustFlowStagingBuffer[i] = 0;
            }

            double attenuation = Utilities.StdMin(Math.Abs(FilteredEngineSpeed()), 40.0) / 40.0;
            double attenuation_3 = attenuation * attenuation * attenuation;

            int cylinderCount = engine.GetCylinderCount();
            for (int i = 0; i < cylinderCount; ++i)
            {
                Piston piston = engine.GetPiston(i);
                CylinderBank bank = piston.GetCylinderBank();
                CylinderHead head = engine.GetHead(bank.GetIndex());
                ExhaustSystem exhaust = head.GetExhaustSystem(piston.GetCylinderIndex());
                CombustionChamber chamber = engine.GetChamber(i);

                double exhaustLength =
                    head.GetHeaderPrimaryLength(piston.GetCylinderIndex())
                    + exhaust.GetLength();

                double exhaustFlow =
                    attenuation_3 * 1600 * (
                        1.0 * (chamber.exhaustRunnerAndPrimary.Pressure() - Units.Pressure(1.0, Units.atm))
                        + 0.1 * chamber.exhaustRunnerAndPrimary.DynamicPressure(1.0, 0.0)
                        + 0.1 * chamber.exhaustRunnerAndPrimary.DynamicPressure(-1.0, 0.0));

                double delayedExhaustPulse =
                    delayFilters[i].FastF(exhaustFlow);

                ExhaustSystem exhaustSystem = head.GetExhaustSystem(piston.GetCylinderIndex());
                exhaustFlowStagingBuffer[exhaustSystem.GetIndex()] +=
                    head.GetSoundAttenuation(piston.GetCylinderIndex())
                    * (exhaustSystem.GetAudioVolume() * delayedExhaustPulse / cylinderCount)
                    * (1 / (exhaustLength * exhaustLength));
            }

            ForcedInduction forcedInduction = engine.GetForcedInduction();
            if (forcedInduction != null)
            {
                // A turbine absorbs part of the exhaust pulses
                if (forcedInduction.IsTurbocharger())
                {
                    double muffling = 1.0 - 0.35 * forcedInduction.GetTurbineFlowFraction();
                    for (int i = 0; i < exhaustSystemCount; ++i)
                    {
                        exhaustFlowStagingBuffer[i] *= muffling;
                    }
                }

                // Control channels for the whine and blow-off sounds, which the synthesizer makes at the audio rate
                int aux = exhaustSystemCount;
                exhaustFlowStagingBuffer[aux + 0] = forcedInduction.GetWhineFrequency() * forcedInduction.GetWhistlePitch();
                exhaustFlowStagingBuffer[aux + 1] = forcedInduction.GetWhineLevel();
                exhaustFlowStagingBuffer[aux + 2] = forcedInduction.GetBlowoffLevel();
                exhaustFlowStagingBuffer[aux + 3] = forcedInduction.GetFlowLevel();
                exhaustFlowStagingBuffer[aux + 4] = forcedInduction.GetSurgeLevel();
                exhaustFlowStagingBuffer[aux + 5] = forcedInduction.GetWastegateNoise();
                exhaustFlowStagingBuffer[aux + 6] = forcedInduction.GetSoundMode();
            }

            GetSynthesizer().WriteInput(exhaustFlowStagingBuffer);
        }
    }
}
