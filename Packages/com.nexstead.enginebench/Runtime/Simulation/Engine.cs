// Port of engine-sim include/engine.h and src/engine.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>An engine: crankshafts, cylinder banks with their heads, pistons, rods and chambers, intakes, exhaust systems, the ignition module, the fuel and the throttle.</summary>
    public sealed class Engine : Part
    {
        public sealed class Parameters
        {
            public int cylinderBanks;
            public int cylinderCount;
            public int crankshaftCount;
            public int exhaustSystemCount;
            public int intakeCount;
            public string name;
            public double starterTorque = Units.Torque(90.0, Units.ft_lb);
            public double starterSpeed = Units.Rpm(200);
            public double redline = Units.Rpm(6500);
            public double dynoMinSpeed = Units.Rpm(1000);
            public double dynoMaxSpeed = Units.Rpm(6500);
            public double dynoHoldStep = Units.Rpm(100);
            public Throttle throttle;
            public double initialSimulationFrequency;
            public double initialHighFrequencyGain;
            public double initialNoise;
            public double initialJitter;
        }

        string name = "";

        Crankshaft[] crankshafts;
        int crankshaftCount;

        CylinderBank[] cylinderBanks;
        CylinderHead[] heads;
        int cylinderBankCount;

        Piston[] pistons;
        ConnectingRod[] connectingRods;
        CombustionChamber[] combustionChambers;
        int cylinderCount;

        double starterTorque;
        double starterSpeed;
        double redline;

        double dynoMinSpeed;
        double dynoMaxSpeed;
        double dynoHoldStep;

        double initialSimulationFrequency = 10000.0;
        double initialHighFrequencyGain = 0.01;
        double initialNoise = 1.0;
        double initialJitter = 0.5;

        ExhaustSystem[] exhaustSystems;
        int exhaustSystemCount;

        Intake[] intakes;
        int intakeCount;

        ForcedInduction forcedInduction;

        readonly IgnitionModule ignitionModule = new IgnitionModule();
        readonly Fuel fuel = new Fuel();

        Throttle throttle;
        double throttleValue = 0.0;

        double displacement;

        public void Initialize(Parameters @params)
        {
            crankshaftCount = @params.crankshaftCount;
            cylinderCount = @params.cylinderCount;
            cylinderBankCount = @params.cylinderBanks;
            exhaustSystemCount = @params.exhaustSystemCount;
            intakeCount = @params.intakeCount;
            starterTorque = @params.starterTorque;
            starterSpeed = @params.starterSpeed;
            dynoMinSpeed = @params.dynoMinSpeed;
            dynoMaxSpeed = @params.dynoMaxSpeed;
            dynoHoldStep = @params.dynoHoldStep;
            redline = @params.redline;
            name = @params.name;
            throttle = @params.throttle;
            initialHighFrequencyGain = @params.initialHighFrequencyGain;
            initialSimulationFrequency = @params.initialSimulationFrequency;
            initialJitter = @params.initialJitter;
            initialNoise = @params.initialNoise;

            crankshafts = Construct<Crankshaft>(crankshaftCount);
            cylinderBanks = Construct<CylinderBank>(cylinderBankCount);
            heads = Construct<CylinderHead>(cylinderBankCount);
            pistons = Construct<Piston>(cylinderCount);
            connectingRods = Construct<ConnectingRod>(cylinderCount);
            exhaustSystems = Construct<ExhaustSystem>(exhaustSystemCount);
            intakes = Construct<Intake>(intakeCount);
            combustionChambers = Construct<CombustionChamber>(cylinderCount);

            for (int i = 0; i < exhaustSystemCount; ++i)
            {
                exhaustSystems[i].index = i;
            }

            for (int i = 0; i < cylinderCount; ++i)
            {
                combustionChambers[i].SetEngine(this);
            }
        }

        /// <summary>Upstream's <c>new T[n]</c>: n default-constructed objects.</summary>
        static T[] Construct<T>(int count) where T : new()
        {
            var array = new T[count];
            for (int i = 0; i < count; ++i)
            {
                array[i] = new T();
            }

            return array;
        }

        public void SeedRandom(ulong seed)
        {
            for (int i = 0; i < cylinderCount; ++i)
            {
                combustionChambers[i].SeedRandom(seed, i);
            }
        }

        /// <summary>
        /// Gives the engine a turbocharger or supercharger (community fork, forced induction), or none: it connects it to the
        /// output crankshaft and every intake, and a turbocharger to every exhaust system. Call it once the intakes, exhaust
        /// systems and crankshafts are initialized, as the script does.
        /// </summary>
        public void SetForcedInduction(ForcedInduction forcedInduction)
        {
            this.forcedInduction = forcedInduction;
            if (forcedInduction == null) return;

            forcedInduction.SetCrankshaft(GetOutputCrankshaft());
            forcedInduction.SetExhaustSystemCount(exhaustSystemCount);
            for (int i = 0; i < intakeCount; ++i)
            {
                intakes[i].SetForcedInduction(forcedInduction);
                forcedInduction.AddIntake(intakes[i]);
            }

            if (forcedInduction.IsTurbocharger())
            {
                for (int i = 0; i < exhaustSystemCount; ++i)
                {
                    exhaustSystems[i].SetForcedInduction(forcedInduction);
                }
            }
        }

        /// <summary>The engine's turbocharger or supercharger; null if it has none.</summary>
        public ForcedInduction GetForcedInduction() => forcedInduction;

        public string GetName() => name;

        public Crankshaft GetOutputCrankshaft()
        {
            return crankshafts[0];
        }

        public void SetSpeedControl(double s)
        {
            throttle.SetSpeedControl(s);
        }

        public double GetSpeedControl()
        {
            return throttle.GetSpeedControl();
        }

        public void SetThrottle(double throttle)
        {
            for (int i = 0; i < intakeCount; ++i)
            {
                intakes[i].throttle = throttle;
            }

            throttleValue = throttle;
        }

        public double GetThrottle()
        {
            return throttleValue;
        }

        public double GetThrottlePlateAngle()
        {
            return (1 - intakes[0].GetThrottlePlatePosition()) * (Constants.pi / 2);
        }

        /// <summary>Where the rod puts its piston at <paramref name="crankshaftAngle"/>: the distance <paramref name="s"/> along its bank; false where it can't reach.</summary>
        static bool PlaceRod(ConnectingRod rod, CylinderBank bank, double crankshaftAngle, out double s)
        {
            s = 0;

            rod.GetCrankshaft().GetRodJournalPositionLocal(rod.GetJournal(), out double l_x, out double l_y);
            double dx = Math.Cos(crankshaftAngle);
            double dy = Math.Sin(crankshaftAngle);

            double p_x = rod.GetCrankshaft().GetPosX() + (dx * l_x - dy * l_y);
            double p_y = rod.GetCrankshaft().GetPosY() + (dy * l_x + dx * l_y);

            // (bank->m_x + bank->m_dx * s - p_x)^2 + (bank->m_y + bank->m_dy * s - p_y)^2 = (rod->m_length)^2
            double a = bank.GetDx() * bank.GetDx() + bank.GetDy() * bank.GetDy();
            double b = -2 * bank.GetDx() * (p_x - bank.GetX()) - 2 * bank.GetDy() * (p_y - bank.GetY());
            double c =
                (p_x - bank.GetX()) * (p_x - bank.GetX())
                + (p_y - bank.GetY()) * (p_y - bank.GetY())
                - rod.GetLength() * rod.GetLength();

            double det = b * b - 4 * a * c;
            if (det < 0) return false;

            double sqrt_det = Math.Sqrt(det);
            double s0 = (-b + sqrt_det) / (2 * a);
            double s1 = (-b - sqrt_det) / (2 * a);

            s = Utilities.StdMax(s0, s1);
            return !(s < 0);
        }

        public void CalculateDisplacement()
        {
            // There is a closed-form/correct way to do this which I really
            // don't feel like deriving right now, so I'm just going with this
            // numerical approximation.
            const int Resolution = 1000;

            var min_s = new double[cylinderCount];
            var max_s = new double[cylinderCount];

            for (int i = 0; i < cylinderCount; ++i)
            {
                min_s[i] = double.MaxValue;
                max_s[i] = -double.MaxValue;
            }

            for (int j = 0; j < Resolution; ++j)
            {
                double crankshaftAngle = 2 * (j / (double)Resolution) * Constants.pi;

                for (int i = 0; i < cylinderCount; ++i)
                {
                    Piston piston = pistons[i];
                    CylinderBank bank = piston.GetCylinderBank();
                    ConnectingRod rod = piston.GetRod();

                    if (!PlaceRod(rod, bank, crankshaftAngle, out double s)) continue;

                    min_s[i] = Utilities.StdMin(min_s[i], s);
                    max_s[i] = Utilities.StdMax(max_s[i], s);
                }
            }

            double displacement = 0;
            for (int i = 0; i < cylinderCount; ++i)
            {
                Piston piston = pistons[i];
                CylinderBank bank = piston.GetCylinderBank();

                if (min_s[i] < max_s[i])
                {
                    double r = bank.GetBore() / 2.0;
                    displacement += Constants.pi * r * r * (max_s[i] - min_s[i]);
                }
            }

            this.displacement = displacement;
        }

        public double GetDisplacement() => displacement;

        public double GetIntakeFlowRate()
        {
            double airIntake = 0;
            for (int i = 0; i < intakeCount; ++i)
            {
                airIntake += intakes[i].flowRate;
            }

            return airIntake;
        }

        public void Update(double dt)
        {
            throttle.Update(dt, this);
        }

        public double GetManifoldPressure()
        {
            double pressureSum = 0.0;
            for (int i = 0; i < intakeCount; ++i)
            {
                pressureSum += intakes[i].system.Pressure();
            }

            return pressureSum / intakeCount;
        }

        public double GetIntakeAfr()
        {
            double totalOxygen = 0.0;
            double totalFuel = 0.0;
            for (int i = 0; i < intakeCount; ++i)
            {
                totalOxygen += intakes[i].system.N_o2();
                totalFuel += intakes[i].system.N_fuel();
            }

            double octaneMolarMass = Units.Mass(114.23, Units.g);
            double oxygenMolarMass = Units.Mass(31.9988, Units.g);

            if (totalFuel == 0) return 0;
            else
            {
                return
                    (oxygenMolarMass * totalOxygen / 0.21)
                    / (totalFuel * octaneMolarMass);
            }
        }

        public double GetExhaustO2()
        {
            double totalInert = 0.0;
            double totalOxygen = 0.0;
            double totalFuel = 0.0;
            for (int i = 0; i < exhaustSystemCount; ++i)
            {
                totalInert += exhaustSystems[i].GetSystem().N_inert();
                totalOxygen += exhaustSystems[i].GetSystem().N_o2();
                totalFuel += exhaustSystems[i].GetSystem().N_fuel();
            }

            double octaneMolarMass = Units.Mass(114.23, Units.g);
            double oxygenMolarMass = Units.Mass(31.9988, Units.g);
            double nitrogenMolarMass = Units.Mass(28.014, Units.g);

            if (totalFuel == 0) return 0;
            else
            {
                return
                    (oxygenMolarMass * totalOxygen)
                    / (
                        totalFuel * octaneMolarMass
                        + nitrogenMolarMass * totalInert
                        + oxygenMolarMass * totalOxygen);
            }
        }

        public double GetRpm()
        {
            if (crankshaftCount == 0) return 0;
            else return Math.Abs(Units.ToRpm(GetCrankshaft(0).body.v_theta));
        }

        public double GetSpeed()
        {
            if (crankshaftCount == 0) return 0;
            else return Math.Abs(GetCrankshaft(0).body.v_theta);
        }

        public bool IsSpinningCw()
        {
            return GetOutputCrankshaft().body.v_theta <= 0;
        }

        public void ResetFuelConsumption()
        {
            for (int i = 0; i < intakeCount; ++i)
            {
                intakes[i].totalFuelInjected = 0;
            }
        }

        public double GetTotalFuelMassConsumed()
        {
            double n_fuelConsumed = 0;
            for (int i = 0; i < intakeCount; ++i)
            {
                n_fuelConsumed += intakes[i].totalFuelInjected;
            }

            return n_fuelConsumed * fuel.GetMolecularMass();
        }

        public double GetTotalVolumeFuelConsumed()
        {
            return GetTotalFuelMassConsumed() / fuel.GetDensity();
        }

        public double GetStarterTorque() => starterTorque;
        public double GetStarterSpeed() => starterSpeed;
        public double GetRedline() => redline;

        public double GetDynoMinSpeed() => dynoMinSpeed;
        public double GetDynoMaxSpeed() => dynoMaxSpeed;
        public double GetDynoHoldStep() => dynoHoldStep;

        public int GetCylinderBankCount() => cylinderBankCount;
        public int GetCylinderCount() => cylinderCount;
        public int GetCrankshaftCount() => crankshaftCount;
        public int GetExhaustSystemCount() => exhaustSystemCount;
        public int GetIntakeCount() => intakeCount;

        public int GetMaxDepth()
        {
            int maxDepth = 0;
            for (int i = 0; i < crankshaftCount; ++i)
            {
                maxDepth = Utilities.StdMax(crankshafts[i].GetRodJournalCount(), maxDepth);
            }

            return maxDepth;
        }

        public Crankshaft GetCrankshaft(int i) => crankshafts[i];
        public CylinderBank GetCylinderBank(int i) => cylinderBanks[i];
        public CylinderHead GetHead(int i) => heads[i];
        public Piston GetPiston(int i) => pistons[i];
        public ConnectingRod GetConnectingRod(int i) => connectingRods[i];
        public IgnitionModule GetIgnitionModule() => ignitionModule;
        public ExhaustSystem GetExhaustSystem(int i) => exhaustSystems[i];
        public Intake GetIntake(int i) => intakes[i];
        public CombustionChamber GetChamber(int i) => combustionChambers[i];
        public Fuel GetFuel() => fuel;

        /// <summary>Read access for the dense core's snapshot.</summary>
        internal Throttle GetThrottleController() => throttle;

        public double GetSimulationFrequency() => initialSimulationFrequency;
        public double GetInitialHighFrequencyGain() => initialHighFrequencyGain;
        public double GetInitialNoise() => initialNoise;
        public double GetInitialJitter() => initialJitter;

        public Simulator CreateSimulator(Vehicle vehicle, Transmission transmission)
        {
            var simulator = new PistonEngineSimulator();
            var simulatorParams = new Simulator.Parameters();
            simulatorParams.systemType = Simulator.SystemType.NsvOptimized;
            simulator.Initialize(simulatorParams);

            simulator.LoadSimulation(this, vehicle, transmission);
            simulator.SetFluidSimulationSteps(8);

            return simulator;
        }
    }
}
