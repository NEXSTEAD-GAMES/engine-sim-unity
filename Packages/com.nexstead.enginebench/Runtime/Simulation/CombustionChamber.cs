// Port of engine-sim include/combustion_chamber.h and src/combustion_chamber.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>
    /// A cylinder's gas with its intake runner and exhaust primary: volume from the piston, flows through the valves, the flame,
    /// and the gas force (plus ring friction) on the piston.
    /// </summary>
    public sealed class CombustionChamber : ForceGenerator
    {
        public sealed class Parameters
        {
            public Piston Piston;
            public CylinderHead Head;
            public Fuel Fuel;
            public Function MeanPistonSpeedToTurbulence;
            public double StartingPressure;
            public double StartingTemperature;
            public double CrankcasePressure;
        }

        /// <summary>Upstream holds this by value and never copies it, so a class serves.</summary>
        public sealed class FlameEvent
        {
            public double lit_n = 0;
            public double total_n = 0;
            public double percentageLit = 0;
            public double efficiency = 1.0;
            public double flameSpeed = 0.0;
            public double lastVolume = 0.0;
            public double travel_x = 0.0;
            public double travel_y = 0.0;
            public GasSystem.Mix globalMix = GasSystem.Mix.Default;
        }

        public sealed class FrictionModelParams
        {
            public double frictionCoeff = 0.06;
            public double breakawayFriction = Units.Force(50, Units.N);
            public double breakawayFrictionVelocity = Units.Distance(0.1, Units.m);
            public double viscousFrictionCoefficient = Units.Force(20, Units.N);
        }

        const int StateSamples = 256;

        public Function meanPistonSpeedToTurbulence = null;
        public readonly GasSystem system = new GasSystem();
        public readonly GasSystem intakeRunnerAndManifold = new GasSystem();
        public readonly GasSystem exhaustRunnerAndPrimary = new GasSystem();
        public readonly FlameEvent flameEvent = new FlameEvent();
        public bool lit = false;
        public readonly FrictionModelParams frictionModel = new FrictionModelParams();
        public double peakTemperature = 0;
        public double nBurntFuel = 0;

        double intakeFlowRate = 0;
        double exhaustFlowRate = 0;
        double manifoldToRunnerFlowRate = 0;
        double primaryToCollectorFlowRate = 0;
        double cylinderCrossSectionSurfaceArea = 0;
        double cylinderWidthApproximation = 0;
        double lastTimestepTotalExhaustFlow = 0;
        double lastTimestepTotalIntakeFlow = 0;
        double exhaustFlow = 0;
        double crankcasePressure = 0.0;
        double[] pressure;
        double[] pistonSpeed;

        readonly DeterministicRandom random = new DeterministicRandom();

        bool litLastFrame = false;
        Piston piston;
        CylinderHead head;
        Engine engine;
        Fuel fuel;

        public void Initialize(Parameters @params)
        {
            piston = @params.Piston;
            head = @params.Head;
            fuel = @params.Fuel;
            crankcasePressure = @params.CrankcasePressure;
            meanPistonSpeedToTurbulence = @params.MeanPistonSpeedToTurbulence;

            pistonSpeed = new double[StateSamples];
            pressure = new double[StateSamples];

            Intake intake = head.GetIntake(piston.GetCylinderIndex());
            ExhaustSystem exhaust = head.GetExhaustSystem(piston.GetCylinderIndex());

            manifoldToRunnerFlowRate = intake.GetRunnerFlowRate();
            primaryToCollectorFlowRate = exhaust.GetPrimaryFlowRate();

            double bore_r = head.GetCylinderBank().GetBore() / 2.0;
            cylinderCrossSectionSurfaceArea = Constants.pi * bore_r * bore_r;
            cylinderWidthApproximation = Math.Sqrt(cylinderCrossSectionSurfaceArea);

            double height = GetVolume() / cylinderCrossSectionSurfaceArea;
            system.SetGeometry(
                cylinderWidthApproximation,
                height,
                1.0,
                0.0);

            double intakeRunnerCrossSection = head.GetIntakeRunnerCrossSectionArea();
            double intakeRunnerWidth = Math.Sqrt(intakeRunnerCrossSection);
            double manifoldRunnerLength = intake.GetRunnerLength();
            double manifoldRunnerVolume = intakeRunnerCrossSection * manifoldRunnerLength;
            double totalIntakeRunnerVolume = head.GetIntakeRunnerVolume() + manifoldRunnerVolume;
            double overallIntakeRunnerLength = totalIntakeRunnerVolume / intakeRunnerCrossSection;
            intakeRunnerAndManifold.Initialize(
                Units.Pressure(1.0, Units.atm),
                totalIntakeRunnerVolume,
                Units.Celcius(25.0));
            intakeRunnerAndManifold.SetGeometry(
                overallIntakeRunnerLength,
                intakeRunnerWidth,
                1.0,
                0.0);

            double exhaustRunnerCrossSection = head.GetExhaustRunnerCrossSectionArea();
            double exhaustRunnerWidth = Math.Sqrt(exhaustRunnerCrossSection);
            double exhaustTubeLength =
                exhaust.GetPrimaryTubeLength() + head.GetHeaderPrimaryLength(piston.GetCylinderIndex());
            double exhaustTubeVolume = exhaustRunnerCrossSection * exhaustTubeLength;
            double totalExhaustRunnerVolume = head.GetExhaustRunnerVolume() + exhaustTubeVolume;
            double overallExhaustRunnerLength = totalExhaustRunnerVolume / exhaustRunnerCrossSection;
            exhaustRunnerAndPrimary.Initialize(
                Units.Pressure(1.0, Units.atm),
                totalExhaustRunnerVolume,
                Units.Celcius(25.0));
            exhaustRunnerAndPrimary.SetGeometry(
                overallExhaustRunnerLength,
                exhaustRunnerWidth,
                1.0,
                0.0);
        }

        public void SeedRandom(ulong seed, int chamberIndex)
        {
            random.Seed(seed, DeterministicRandom.ChamberStream(chamberIndex));
        }

        public void SetEngine(Engine engine) { this.engine = engine; }

        public CylinderHead GetCylinderHead() => head;
        public Piston GetPiston() => piston;

        public double GetVolume()
        {
            double combustionPortVolume = head.GetCombustionChamberVolume();
            CylinderBank bank = head.GetCylinderBank();

            double area = bank.BoreSurfaceArea();
            double s =
                piston.RelativeX() * bank.GetDx()
                + piston.RelativeY() * bank.GetDy();
            double sweep =
                area * (bank.GetDeckHeight() - s - piston.GetCompressionHeight());

            return sweep + combustionPortVolume - piston.GetDisplacement();
        }

        public double PistonSpeed()
        {
            CylinderBank bank = head.GetCylinderBank();
            return
                piston.body.v_x * bank.GetDx()
                + piston.body.v_y * bank.GetDy();
        }

        public double CalculateMeanPistonSpeed()
        {
            double avg = 0;
            for (int i = 0; i < StateSamples; ++i)
            {
                avg += pistonSpeed[i];
            }

            avg /= StateSamples;
            return avg;
        }

        public double CalculateFiringPressure()
        {
            double firingPressure = 0;
            for (int i = 0; i < StateSamples; ++i)
            {
                if (pressure[i] > firingPressure)
                {
                    firingPressure = pressure[i];
                }
            }

            return firingPressure;
        }

        public bool IsLit() => lit;

        /// <summary>How many times a spark has lit this chamber: display bookkeeping (the Bench's capture), read by no step.</summary>
        internal int ignitions;

        internal CylinderHead GetHead() => head;

        public bool PopLitLastFrame()
        {
            bool lit = litLastFrame;
            litLastFrame = false;

            return lit;
        }

        public void Ignite()
        {
            if (!lit)
            {
                if (system.GetMix().p_fuel == 0) return;

                double afr = system.GetMix().p_o2 / system.GetMix().p_fuel;
                double equivalenceRatio = afr / fuel.GetMolecularAfr();
                if (equivalenceRatio < 0.5) return;
                else if (equivalenceRatio > 1.9) return;

                double idealInert = system.GetMix().p_o2 / 0.7;
                double dilution = (system.GetMix().p_inert / idealInert) - 1;

                flameEvent.lastVolume = GetVolume();
                flameEvent.travel_x = 0;
                flameEvent.travel_y = 0;
                flameEvent.lit_n = 0;
                flameEvent.total_n = system.N();
                flameEvent.percentageLit = 0;
                flameEvent.globalMix = system.GetMix();
                lit = true;
                litLastFrame = true;
                ++ignitions;

                double randomness =
                    fuel.GetBurningEfficiencyRandomness();
                double lowEfficiencyAttenuation =
                    fuel.GetLowEfficiencyAttenuation();
                double maxBurningEfficiency =
                    fuel.GetMaxBurningEfficiency();
                double maxTurbulenceEffect =
                    fuel.GetMaxTurbulenceEffect();
                double maxDilutionEffect =
                    fuel.GetMaxDilutionEffect();

                double turbulence =
                    meanPistonSpeedToTurbulence.SampleTriangle(
                        CalculateMeanPistonSpeed());
                double mixingFactor =
                    1.0 - (
                        Utilities.Clamp(turbulence / maxTurbulenceEffect)
                        * Utilities.Clamp(1 - dilution / maxDilutionEffect));
                double rand_s =
                    lowEfficiencyAttenuation
                    * ((1 - randomness) + randomness * random.NextDouble());
                double efficiencyAttenuation =
                    (mixingFactor * rand_s + (1 - mixingFactor));
                flameEvent.efficiency =
                    efficiencyAttenuation * maxBurningEfficiency;
                flameEvent.flameSpeed = fuel.FlameSpeed(
                    turbulence,
                    afr,
                    system.Temperature(),
                    system.Pressure(),
                    CalculateFiringPressure(),
                    Units.Pressure(160, Units.psi));
            }
        }

        public void Update(double dt)
        {
            system.SetVolume(GetVolume());

            UpdateCycleStates();

            intakeFlowRate = head.IntakeFlowRate(piston.GetCylinderIndex());
            exhaustFlowRate = head.ExhaustFlowRate(piston.GetCylinderIndex());
        }

        public void Flow(double dt)
        {
            if (system.Temperature() > peakTemperature)
            {
                peakTemperature = system.Temperature();
            }

            double volume = GetVolume();
            double cylinderHeight = volume / cylinderCrossSectionSurfaceArea;
            double cylinderSurfaceArea =
                cylinderHeight * Constants.pi * head.GetCylinderBank().GetBore()
                + cylinderCrossSectionSurfaceArea * 2;

            double dT = Units.Celcius(90.0) - system.Temperature();

            system.ChangeEnergy(dT * cylinderSurfaceArea * 100 * dt);
            system.Flow(piston.GetBlowbyK(), dt, crankcasePressure, Units.Celcius(25.0));

            Intake intake = head.GetIntake(piston.GetCylinderIndex());
            ExhaustSystem exhaust = head.GetExhaustSystem(piston.GetCylinderIndex());

            GasSystem.FlowParameters flowParams = default;
            flowParams.dt = dt;

            flowParams.k_flow = manifoldToRunnerFlowRate;
            flowParams.crossSectionArea_0 = intake.GetPlenumCrossSectionArea();
            flowParams.crossSectionArea_1 = head.GetIntakeRunnerCrossSectionArea();
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.system_0 = intake.system;
            flowParams.system_1 = intakeRunnerAndManifold;
            GasSystem.Flow(flowParams);

            intakeRunnerAndManifold.DissipateExcessVelocity();

            flowParams.k_flow = intakeFlowRate;
            flowParams.crossSectionArea_0 = head.GetIntakeRunnerCrossSectionArea();
            flowParams.crossSectionArea_1 = volume / cylinderHeight;
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.system_0 = intakeRunnerAndManifold;
            flowParams.system_1 = system;
            double intakeFlow = GasSystem.Flow(flowParams);

            intakeRunnerAndManifold.DissipateExcessVelocity();
            system.DissipateExcessVelocity();

            flowParams.k_flow = exhaustFlowRate;
            flowParams.crossSectionArea_0 = volume / cylinderHeight;
            flowParams.crossSectionArea_1 = head.GetExhaustRunnerCrossSectionArea();
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.system_0 = system;
            flowParams.system_1 = exhaustRunnerAndPrimary;
            double exhaustFlow = GasSystem.Flow(flowParams);

            system.DissipateExcessVelocity();
            exhaustRunnerAndPrimary.DissipateExcessVelocity();

            flowParams.k_flow = primaryToCollectorFlowRate;
            flowParams.crossSectionArea_0 = head.GetExhaustRunnerCrossSectionArea();
            flowParams.crossSectionArea_1 = exhaust.GetCollectorCrossSectionArea();
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.system_0 = exhaustRunnerAndPrimary;
            flowParams.system_1 = exhaust.GetSystem();
            GasSystem.Flow(flowParams);

            intakeRunnerAndManifold.UpdateVelocity(dt, intake.GetVelocityDecay());
            system.UpdateVelocity(dt, 0.5);
            exhaustRunnerAndPrimary.UpdateVelocity(dt, exhaust.GetVelocityDecay());

            if (Math.Abs(intakeFlow) > 1E-9 && lit)
            {
                lit = false;
            }

            this.exhaustFlow = exhaustFlow;
            lastTimestepTotalExhaustFlow += exhaustFlow;
            lastTimestepTotalIntakeFlow += intakeFlow;

            if (lit)
            {
                CylinderBank bank = head.GetCylinderBank();
                double totalTravel_x = bank.GetBore() / 2;
                double totalTravel_y = volume / bank.BoreSurfaceArea();
                double expansion = volume / flameEvent.lastVolume;
                double lastTravel_x = flameEvent.travel_x;
                double lastTravel_y = flameEvent.travel_y * expansion;
                double flameSpeed = flameEvent.flameSpeed;

                flameEvent.travel_x =
                    Utilities.Fmin(lastTravel_x + dt * flameSpeed, totalTravel_x);
                flameEvent.travel_y =
                    Utilities.Fmin(lastTravel_y + dt * flameSpeed, totalTravel_y);

                if (lastTravel_x < flameEvent.travel_x || lastTravel_y < flameEvent.travel_y)
                {
                    double burnedVolume =
                        flameEvent.travel_x * flameEvent.travel_x
                        * Constants.pi * flameEvent.travel_y;
                    double prevBurnedVolume =
                        lastTravel_x * lastTravel_x * Constants.pi * lastTravel_y;
                    double litVolume = burnedVolume - prevBurnedVolume;
                    double n = (litVolume / volume) * system.N();

                    double fuelBurned =
                        system.React(n * flameEvent.efficiency, flameEvent.globalMix);
                    double massFuelBurned = fuelBurned * fuel.GetMolecularMass();
                    system.ChangeEnergy(
                        massFuelBurned * fuel.GetEnergyDensity());

                    flameEvent.lit_n += n;
                    flameEvent.percentageLit += litVolume / volume;

                    nBurntFuel += massFuelBurned;
                }
                else
                {
                    lit = false;
                }

                flameEvent.lastVolume = volume;
            }
        }

        public double LastEventAfr()
        {
            double totalFuel = flameEvent.globalMix.p_fuel * flameEvent.total_n;
            double totalOxygen = flameEvent.globalMix.p_o2 * flameEvent.total_n;
            double totalInert = flameEvent.globalMix.p_inert * flameEvent.total_n;

            double octaneMolarMass = Units.Mass(114.23, Units.g);
            double oxygenMolarMass = Units.Mass(31.9988, Units.g);
            double nitrogenMolarMass = Units.Mass(28.014, Units.g);

            if (totalFuel == 0) return 0;
            else
            {
                return
                    (oxygenMolarMass * totalOxygen + totalInert * nitrogenMolarMass)
                    / (totalFuel * octaneMolarMass);
            }
        }

        public double GetLastIterationExhaustFlow() => exhaustFlow;
        public void ResetLastTimestepExhaustFlow() { lastTimestepTotalExhaustFlow = 0; }
        public double GetLastTimestepExhaustFlow() => lastTimestepTotalExhaustFlow;
        public void ResetLastTimestepIntakeFlow() { lastTimestepTotalIntakeFlow = 0; }
        public double GetLastTimestepIntakeFlow() => lastTimestepTotalIntakeFlow;

        double CalculateFrictionForce(double v_s)
        {
            double cylinderWallForce = piston.CalculateCylinderWallForce();

            double F_coul = frictionModel.frictionCoeff * cylinderWallForce;
            double v_st = frictionModel.breakawayFrictionVelocity * Constants.root_2;
            double v_coul = frictionModel.breakawayFrictionVelocity / 10;
            double F_brk = frictionModel.breakawayFriction;
            double v = Math.Abs(v_s);

            double F_0 = Constants.root_2 * Constants.e * (F_brk - F_coul);
            double F_1 = v / v_st;
            double F_2 = Math.Exp(-F_1 * F_1) * F_1;
            double F_3 = F_coul * Math.Tanh(v / v_coul);
            double F_4 = frictionModel.viscousFrictionCoefficient * v;

            return F_0 * F_2 + F_3 + F_4;
        }

        void UpdateCycleStates()
        {
            double crankAngle = engine.GetOutputCrankshaft().GetCycleAngle();
            if (double.IsNaN(crankAngle) || double.IsInfinity(crankAngle))
            {
                crankAngle = 0.0;
            }

            int i = (int)Utilities.Round((crankAngle / (4 * Constants.pi)) * (StateSamples - 1.0));

            pistonSpeed[i] = Math.Abs(PistonSpeed());
            pressure[i] = system.Pressure();
        }

        public override void Apply(SystemState system)
        {
            CylinderBank bank = head.GetCylinderBank();
            double area = (bank.GetBore() * bank.GetBore() / 4.0) * Constants.pi;
            double v_x = system.v_x[piston.body.index];
            double v_y = system.v_y[piston.body.index];

            double v_s =
                v_x * bank.GetDx() + v_y * bank.GetDy();

            double pressureDifferential = this.system.Pressure() - crankcasePressure;
            double force = -area * pressureDifferential;

            const double limit = 1E-3;
            double abs_v_s = Utilities.Fmin(Math.Abs(v_s), limit);
            double attenuation = abs_v_s / limit;

            double F = CalculateFrictionForce(v_s) * attenuation;
            double F_fric = (v_s > 0)
                ? -F
                : F;

            system.ApplyForce(
                0.0,
                0.0,
                (force + F_fric) * bank.GetDx(),
                (force + F_fric) * bank.GetDy(),
                piston.body.index);
        }

        public double GetFrictionForce()
        {
            CylinderBank bank = head.GetCylinderBank();
            double v_x = piston.body.v_x;
            double v_y = piston.body.v_y;

            double v_s =
                v_x * bank.GetDx() + v_y * bank.GetDy();

            return CalculateFrictionForce(v_s);
        }

        // Read access for the dense core's snapshot.
        internal double GetIntakeFlowRate() => intakeFlowRate;
        internal double GetExhaustFlowRate() => exhaustFlowRate;
        internal double GetManifoldToRunnerFlowRate() => manifoldToRunnerFlowRate;
        internal double GetPrimaryToCollectorFlowRate() => primaryToCollectorFlowRate;
        internal double GetCylinderCrossSectionSurfaceArea() => cylinderCrossSectionSurfaceArea;
        internal double GetCrankcasePressure() => crankcasePressure;
        internal double GetPressureSample(int i) => pressure[i];
        internal double GetPistonSpeedSample(int i) => pistonSpeed[i];
        internal ulong GetRandomState() => random.State;
    }
}
