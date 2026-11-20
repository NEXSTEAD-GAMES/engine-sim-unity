// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice and the community fork's: Third Party Notices.md.
//
// The dense core's forced induction: Runtime/Simulation/ForcedInduction.cs line for line over the
// structs here. See DenseEngine.cs for the rules.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    /// <summary>Mirror of <see cref="ForcedInduction.Unit"/>.</summary>
    public struct ForcedInductionUnitData
    {
        public double omega;
        public double energy;
        public double compressorFlow;
        public double turbinePower;
        public double compressorPower;
        public int active;
    }

    /// <summary>Mirror of <see cref="ForcedInduction"/>: enums as their ints, references as indices.</summary>
    public unsafe struct ForcedInductionData
    {
        public int type;
        public int arrangement;

        /// <summary>Two, of which <see cref="unitCount"/> are used.</summary>
        public ForcedInductionUnitData* units;
        public int unitCount;

        /// <summary>Its charge air: an index into <see cref="DenseParts.gases"/>.</summary>
        public int charge;

        /// <summary>The crankshaft it is connected to (an index into <see cref="DenseParts.crankshafts"/>).</summary>
        public int crankshaft;
        /// <summary>The intakes it feeds, in the order connected.</summary>
        public int* intakes;
        public int intakeCount;
        public int exhaustSystemCount;

        public double ambientPressure;
        public double ambientTemperature;

        public double tipRadius;
        public double compressorK;
        public double compressorEfficiency;
        public double maxSpeed;
        public double slip;

        public double displacement;
        public double leakK;
        public double bypassK;
        public int bypassValve;
        public double bypass;
        public double driveRatio;
        public double driveTorque;
        public double drivePower;

        public double turbineK;
        public double wastegateK;
        public double turbineEfficiency;
        public double inertia;
        public double bearingLoss;
        public double turbineFraction;
        public double sequentialSpeed;
        public double turbineEnergy;

        public double boostTarget;
        public double wastegate;
        public int blowoffEnabled;
        public double blowoff;
        public double blowoffK;
        public double blowoffFlowRate;

        public double intercoolerEffectiveness;
        public int blowoffType;
        public int boostControl;
        public int externalWastegate;
        public int twinScroll;
        public double whistlePitch;
        public double surge;
        public double wastegateIntegral;
        public double lastBoost;
        public double boostRate;
        public double maxCompressorFlow;
        public double compressorOutletTemperature;
    }

    /// <summary>Mirror of <see cref="ForcedInduction"/>'s methods.</summary>
    public static unsafe class ForcedInductionOps
    {
        const int Turbocharger = (int)ForcedInduction.Type.Turbocharger;
        const int Roots = (int)ForcedInduction.Type.Roots;
        const int TwinScrew = (int)ForcedInduction.Type.TwinScrew;
        const int Centrifugal = (int)ForcedInduction.Type.Centrifugal;
        const int Sequential = (int)ForcedInduction.Arrangement.Sequential;
        const int Electronic = (int)ForcedInduction.BoostControl.Electronic;
        const int NoBlowoff = (int)ForcedInduction.BlowoffType.None;

        const double Gamma = 1.4;
        const double Cp_mass = 1005.0;
        const double Cp_molar = Constants.R * Gamma / (Gamma - 1);
        const double Exponent = (Gamma - 1) / Gamma;

        static double Approach(double value, double target, double dt, double timeConstant)
        {
            return value + (target - value) * Utilities.StdMin(1.0, dt / timeConstant);
        }

        static MixData AirMix()
        {
            MixData mix = GasOps.DefaultMix();
            mix.p_fuel = 0;
            mix.p_o2 = 0.25;
            mix.p_inert = 0.75;
            return mix;
        }

        public static bool IsTurbocharger(ForcedInductionData* f) => f->type == Turbocharger;
        public static bool IsPositiveDisplacement(ForcedInductionData* f) => f->type == Roots || f->type == TwinScrew;

        /// <summary><see cref="ForcedInduction.GetExhaustOutletFlowK"/>.</summary>
        public static double GetExhaustOutletFlowK(ForcedInductionData* f, double mufflerK)
        {
            if (!IsTurbocharger(f)) return mufflerK;

            double k = f->wastegateK * f->wastegate;
            for (int i = 0; i < f->unitCount; ++i)
            {
                if (f->units[i].active != 0) k += f->turbineK;
            }

            k /= Utilities.StdMax(f->exhaustSystemCount, 1);
            if (k <= 0) return 0;
            if (mufflerK <= 0) return k;

            return 1.0 / Math.Sqrt(1.0 / (k * k) + 1.0 / (mufflerK * mufflerK));
        }

        /// <summary><see cref="ForcedInduction.AddExhaustFlow"/>.</summary>
        public static void AddExhaustFlow(ForcedInductionData* f, double n, double T, double P, double hcr, double mufflerK)
        {
            double exhaustAmbient = Units.Pressure(1.0, Units.atm);
            if (!IsTurbocharger(f) || n <= 0 || P <= exhaustAmbient) return;

            double turbineK = 0;
            for (int i = 0; i < f->unitCount; ++i)
            {
                if (f->units[i].active != 0) turbineK += f->turbineK;
            }

            double totalK = turbineK + f->wastegateK * f->wastegate;
            if (totalK <= 0) return;

            double share = totalK / Utilities.StdMax(f->exhaustSystemCount, 1);
            f->turbineFraction = turbineK / totalK;

            double turbineDropFraction = 1.0;
            if (mufflerK > 0)
            {
                double rt = 1.0 / (share * share);
                double rm = 1.0 / (mufflerK * mufflerK);
                turbineDropFraction = rt / (rt + rm);
            }

            double P_out = P - (P - exhaustAmbient) * turbineDropFraction;
            double g = Utilities.StdMax(hcr, 1.05);
            double cp = Constants.R * g / (g - 1);
            double isentropicWork = cp * T * (1.0 - Math.Pow(P_out / P, (g - 1) / g));

            double efficiency = f->turbineEfficiency;
            if (f->twinScroll != 0) efficiency *= 1.0 + 0.12 * Utilities.Clamp(1.0 - f->turbineFraction * 0.5, 0.0, 1.0);

            f->turbineEnergy += efficiency * f->turbineFraction * n * isentropicWork;
        }

        static double CentrifugalSourcePressureRatio(ForcedInductionData* f, double omega)
        {
            double U = omega * f->tipRadius;
            double pr = Math.Pow(
                1.0 + f->compressorEfficiency * f->slip * U * U / (Cp_mass * f->ambientTemperature),
                1.0 / Exponent);

            return Utilities.StdMin(pr, 6.0);
        }

        static double PlenumPressure(DenseEngine* e, ForcedInductionData* f)
        {
            if (f->intakeCount == 0) return f->ambientPressure;

            double sum = 0;
            for (int i = 0; i < f->intakeCount; ++i)
            {
                sum += GasOps.Pressure(&e->parts.gases[e->parts.intakes[f->intakes[i]].system]);
            }

            return sum / f->intakeCount;
        }

        static double CrankSpeed(DenseEngine* e, ForcedInductionData* f) =>
            (f->crankshaft >= 0) ? Math.Abs(e->solver.bodies[e->parts.crankshafts[f->crankshaft].body].v_theta) : 0.0;

        static MixData ChargeMix(DenseEngine* e, ForcedInductionData* f) =>
            (f->intakeCount > 0) ? PartsOps.IntakeFuelAirMix(&e->parts.intakes[f->intakes[0]]) : AirMix();

        static void ProcessCentrifugal(ForcedInductionData* f, GasData* charge, ForcedInductionUnitData* unit, double dt, double scale, MixData mix)
        {
            double pr0 = CentrifugalSourcePressureRatio(f, unit->omega);
            double pr = Utilities.StdMax(GasOps.Pressure(charge) / f->ambientPressure, 1.0);

            double T2 = f->ambientTemperature
                * (1.0 + (Math.Pow(pr, Exponent) - 1.0) / f->compressorEfficiency);
            f->compressorOutletTemperature = T2;
            double T_in = T2 - f->intercoolerEffectiveness * (T2 - f->ambientTemperature);

            double flow = GasOps.FlowToEnvironment(charge, scale * f->compressorK, dt, pr0 * f->ambientPressure, T_in, mix);
            double dn = -flow;

            unit->compressorFlow = dn / dt;

            double reverse = Utilities.StdMax(-dn / dt * Units.AirMolecularMass / (0.15 * f->maxCompressorFlow), 0.0);
            f->surge = f->surge + (Utilities.StdMin(reverse, 1.0) - f->surge) * Utilities.StdMin(1.0, dt / 0.05);

            double U = unit->omega * f->tipRadius;
            double massFlow = Math.Abs(dn) / dt * Units.AirMolecularMass;
            unit->compressorPower = massFlow * f->slip * U * U;
        }

        static void ProcessPositiveDisplacement(DenseEngine* e, ForcedInductionData* f, GasData* charge, double dt, MixData mix)
        {
            double omega = CrankSpeed(e, f) * f->driveRatio;
            f->units[0].omega = omega;

            double pr = Utilities.StdMax(GasOps.Pressure(charge) / f->ambientPressure, 1.0);
            double efficiency = (f->type == Roots) ? 0.55 : 0.72;
            double T2 = f->ambientTemperature
                * (1.0 + (Math.Pow(pr, Exponent) - 1.0) / efficiency);
            f->compressorOutletTemperature = T2;
            double T_in = T2 - f->intercoolerEffectiveness * (T2 - f->ambientTemperature);

            double volumeFlow = f->displacement * omega / (2 * Constants.pi);
            double n = 0.95 * f->ambientPressure * volumeFlow * dt / (Constants.R * f->ambientTemperature);
            if (n > 0)
            {
                GasOps.Inject(charge, n, T_in, mix);
            }

            double leak = GasOps.FlowToEnvironment(charge, f->leakK, dt, f->ambientPressure, f->ambientTemperature, mix);

            double netFlow = (n - leak) / dt;
            f->units[0].compressorFlow = netFlow;

            f->units[0].compressorPower = (n / dt) * Cp_molar * (T2 - f->ambientTemperature) / 0.9;
        }

        /// <summary><see cref="ForcedInduction.Process"/>.</summary>
        public static void Process(DenseEngine* e, double dt)
        {
            ForcedInductionData* f = e->parts.forcedInduction;
            if (dt <= 0) return;

            GasData* charge = &e->parts.gases[f->charge];
            MixData mix = ChargeMix(e, f);

            double p_plenum = PlenumPressure(e, f);
            double boost = GasOps.Pressure(charge) - f->ambientPressure;

            if (IsTurbocharger(f))
            {
                if (f->arrangement == Sequential)
                {
                    double speed = CrankSpeed(e, f);
                    if (f->units[1].active == 0 && speed > f->sequentialSpeed) f->units[1].active = 1;
                    else if (f->units[1].active != 0 && speed < 0.9 * f->sequentialSpeed) f->units[1].active = 0;
                }

                int activeTurbines = 0;
                for (int i = 0; i < f->unitCount; ++i)
                {
                    if (f->units[i].active != 0) ++activeTurbines;
                }

                f->boostRate += ((boost - f->lastBoost) / dt - f->boostRate) * Utilities.StdMin(1.0, dt / 0.02);
                f->lastBoost = boost;

                double wastegateTarget = 1.0;
                if (f->boostTarget > 0)
                {
                    if (f->boostControl == Electronic)
                    {
                        double band = Utilities.StdMax(0.5 * f->boostTarget, Units.Pressure(0.5, Units.psi));
                        double error = (boost - f->boostTarget) / band;
                        double bypass = 1.0 + error;
                        if (Math.Abs(error) < 1.0)
                        {
                            f->wastegateIntegral = Utilities.Clamp(f->wastegateIntegral + 2.0 * error * dt, -1.0, 1.0);
                            if (f->boostRate > 0) bypass += 0.05 * f->boostRate / band;
                        }
                        bypass = Utilities.Clamp(bypass + f->wastegateIntegral, 0.0, 1.0);

                        double ratio = f->wastegateK / Utilities.StdMax(activeTurbines * f->turbineK, 1e-12);
                        double share = bypass * ratio / (1.0 + ratio);
                        wastegateTarget = Utilities.Clamp(share / ((1.0 - share) * ratio), 0.0, 1.0);
                    }
                    else
                    {
                        wastegateTarget = Utilities.Clamp((boost - 0.75 * f->boostTarget) / (0.45 * f->boostTarget), 0.0, 1.0);
                    }
                }
                f->wastegate = Approach(f->wastegate, wastegateTarget, dt,
                    (f->boostControl == Electronic) ? 0.03 : 0.12);

                for (int i = 0; i < f->unitCount; ++i)
                {
                    ForcedInductionUnitData* unit = &f->units[i];
                    unit->turbinePower = 0;
                    unit->compressorFlow = 0;
                    unit->compressorPower = 0;

                    if (unit->active != 0)
                    {
                        ProcessCentrifugal(f, charge, unit, dt, 1.0, mix);
                        double unitTurbineEnergy = f->turbineEnergy / Utilities.StdMax(activeTurbines, 1);
                        unit->turbinePower = unitTurbineEnergy / dt;
                        unit->energy += unitTurbineEnergy;
                    }

                    unit->energy -= (unit->compressorPower + f->bearingLoss * unit->omega * unit->omega) * dt;

                    double maxEnergy = 0.5 * f->inertia * f->maxSpeed * f->maxSpeed;
                    unit->energy = Utilities.Clamp(unit->energy, 0.0, maxEnergy);
                    unit->omega = Math.Sqrt(2 * unit->energy / f->inertia);
                }

                f->turbineEnergy = 0;
                f->drivePower = 0;
                f->driveTorque = 0;
            }
            else
            {
                if (f->type == Centrifugal)
                {
                    ForcedInductionUnitData* unit = &f->units[0];
                    unit->omega = Utilities.StdMin(CrankSpeed(e, f) * f->driveRatio, f->maxSpeed);
                    ProcessCentrifugal(f, charge, unit, dt, 1.0, mix);
                    unit->compressorPower += f->bearingLoss * unit->omega * unit->omega;
                }
                else
                {
                    ProcessPositiveDisplacement(e, f, charge, dt, mix);

                    if (f->bypassValve != 0)
                    {
                        double target =
                            Utilities.Clamp((0.9 * f->ambientPressure - p_plenum) / (0.2 * f->ambientPressure), 0.0, 1.0);
                        f->bypass = Approach(f->bypass, target, dt, 0.05);
                        if (f->bypass > 0)
                        {
                            GasOps.FlowToEnvironment(charge, f->bypassK * f->bypass, dt, f->ambientPressure, f->ambientTemperature, mix);
                        }
                    }
                }

                f->drivePower = f->units[0].compressorPower;
                f->driveTorque = f->drivePower / Utilities.StdMax(CrankSpeed(e, f), 20.0);
            }

            double blowoffTarget = 0;
            if (f->blowoffEnabled != 0 && !IsPositiveDisplacement(f))
            {
                double dp = GasOps.Pressure(charge) - p_plenum;
                blowoffTarget = Utilities.Clamp((dp - Units.Pressure(4.0, Units.psi)) / Units.Pressure(3.0, Units.psi), 0.0, 1.0);
            }

            f->blowoff = Approach(f->blowoff, blowoffTarget, dt, 0.01);
            double vented = 0;
            if (f->blowoff > 0)
            {
                vented = GasOps.FlowToEnvironment(charge, f->blowoffK * f->blowoff, dt, f->ambientPressure, f->ambientTemperature, mix);
            }

            f->blowoffFlowRate = Approach(f->blowoffFlowRate, Utilities.StdMax(vented, 0.0) / dt, dt, 0.02);

            GasOps.DissipateExcessVelocity(charge);
            GasOps.UpdateVelocity(charge, dt, 0.5);
        }

        /// <summary><see cref="ForcedInduction.Apply"/>: the supercharger's drive torque on the crankshaft.</summary>
        public static void Apply(DenseEngine* e, SystemStateData* state)
        {
            ForcedInductionData* f = e->parts.forcedInduction;
            if (f->crankshaft < 0 || f->driveTorque == 0) return;

            int index = e->parts.crankshafts[f->crankshaft].body;
            double v = state->v_theta[index];
            if (v > 0) state->t[index] -= f->driveTorque;
            else if (v < 0) state->t[index] += f->driveTorque;
        }

        // ---- Readouts ----

        public static double GetBoost(DenseEngine* e, ForcedInductionData* f) => GasOps.Pressure(&e->parts.gases[f->charge]) - f->ambientPressure;

        public static double GetCompressorFlow(ForcedInductionData* f)
        {
            double flow = 0;
            for (int i = 0; i < f->unitCount; ++i) flow += f->units[i].compressorFlow;
            return flow;
        }

        public static double GetWhineFrequency(ForcedInductionData* f) => f->units[0].omega / (2 * Constants.pi);

        public static double GetWhineLevel(DenseEngine* e, ForcedInductionData* f)
        {
            if (IsPositiveDisplacement(f))
            {
                double speed = Utilities.Clamp(f->units[0].omega / (2 * Constants.pi * 250.0), 0.0, 1.0);
                double load = Utilities.Clamp(GetBoost(e, f) / Units.Pressure(10.0, Units.psi), 0.0, 1.0);
                return speed * (0.3 + 0.7 * load);
            }

            double s = Utilities.Clamp(f->units[0].omega / f->maxSpeed, 0.0, 1.0);
            double flow = Utilities.Clamp(
                Math.Abs(GetCompressorFlow(f)) * Units.AirMolecularMass / 0.3, 0.0, 1.0);
            return s * s * (0.25 + 0.75 * flow);
        }

        public static double GetFlowLevel(ForcedInductionData* f) =>
            Utilities.Clamp(Math.Abs(GetCompressorFlow(f)) * Units.AirMolecularMass / Utilities.StdMax(f->maxCompressorFlow, 0.01), 0.0, 1.0);

        public static double GetWastegateNoise(ForcedInductionData* f)
        {
            if (f->externalWastegate == 0 || !IsTurbocharger(f)) return 0.0;
            return Utilities.Clamp(f->wastegate * (1.0 - f->turbineFraction) * 2.0, 0.0, 1.0);
        }

        public static double GetBlowoffLevel(DenseEngine* e, ForcedInductionData* f)
        {
            double flow = Utilities.Clamp(f->blowoffFlowRate * Units.AirMolecularMass / 0.15, 0.0, 1.0);
            double drive = Utilities.Clamp(GetBoost(e, f) / Units.Pressure(5.0, Units.psi), 0.0, 1.0);
            return flow * drive;
        }

        public static int GetBlowoffType(ForcedInductionData* f) => f->blowoffEnabled != 0 ? f->blowoffType : NoBlowoff;

        public static int GetSoundMode(ForcedInductionData* f)
        {
            int t = 1;
            switch (f->type)
            {
                case Roots: t = 2; break;
                case TwinScrew: t = 3; break;
                case Centrifugal: t = 4; break;
            }

            return 10 * t + GetBlowoffType(f);
        }

        // ---- Snapshot (managed) ----

        /// <summary>
        /// Copies a managed forced induction's state; <paramref name="charge"/> is its charge air's gas index,
        /// <paramref name="crankshaft"/> and <paramref name="intakes"/> the parts' indices of what it is connected to.
        /// </summary>
        public static ForcedInductionData* Snapshot(ForcedInduction source, DenseMemory memory, DenseParts* parts, int charge, int crankshaft, int[] intakes)
        {
            ForcedInductionData* f = memory.Alloc<ForcedInductionData>(1);

            f->type = (int)source.GetForcedInductionType();
            f->arrangement = (int)source.GetArrangement();
            f->unitCount = source.GetUnitCount();
            f->units = memory.Alloc<ForcedInductionUnitData>(2);
            for (int i = 0; i < 2; ++i)
            {
                ForcedInduction.Unit u = source.GetUnit(i);
                f->units[i] = new ForcedInductionUnitData
                {
                    omega = u.omega,
                    energy = u.energy,
                    compressorFlow = u.compressorFlow,
                    turbinePower = u.turbinePower,
                    compressorPower = u.compressorPower,
                    active = u.active ? 1 : 0,
                };
            }

            f->charge = charge;
            f->crankshaft = crankshaft;
            f->intakes = memory.Copy(intakes);
            f->intakeCount = intakes.Length;
            f->exhaustSystemCount = source.ExhaustSystemCount;

            f->ambientPressure = source.GetAmbientPressure();
            f->ambientTemperature = source.GetAmbientTemperature();

            f->tipRadius = source.TipRadius;
            f->compressorK = source.CompressorK;
            f->compressorEfficiency = source.CompressorEfficiency;
            f->maxSpeed = source.GetMaxSpeed();
            f->slip = source.Slip;

            f->displacement = source.Displacement;
            f->leakK = source.LeakK;
            f->bypassK = source.BypassK;
            f->bypassValve = source.BypassValve ? 1 : 0;
            f->bypass = source.GetBypassOpening();
            f->driveRatio = source.GetDriveRatio();
            f->driveTorque = source.GetDriveTorque();
            f->drivePower = source.GetDrivePower();

            f->turbineK = source.TurbineK;
            f->wastegateK = source.WastegateK;
            f->turbineEfficiency = source.TurbineEfficiency;
            f->inertia = source.Inertia;
            f->bearingLoss = source.BearingLoss;
            f->turbineFraction = source.GetTurbineFlowFraction();
            f->sequentialSpeed = source.SequentialSpeed;
            f->turbineEnergy = source.TurbineEnergy;

            f->boostTarget = source.GetBoostTarget();
            f->wastegate = source.GetWastegateOpening();
            f->blowoffEnabled = source.HasBlowoffValve() ? 1 : 0;
            f->blowoff = source.GetBlowoffOpening();
            f->blowoffK = source.BlowoffK;
            f->blowoffFlowRate = source.GetBlowoffFlow();

            f->intercoolerEffectiveness = source.GetIntercoolerEffectiveness();
            f->blowoffType = (int)source.ConfiguredBlowoffType;
            f->boostControl = (int)source.GetBoostControl();
            f->externalWastegate = source.HasExternalWastegate() ? 1 : 0;
            f->twinScroll = source.IsTwinScroll() ? 1 : 0;
            f->whistlePitch = source.GetWhistlePitch();
            f->surge = source.GetSurgeLevel();
            f->wastegateIntegral = source.WastegateIntegral;
            f->lastBoost = source.LastBoost;
            f->boostRate = source.BoostRate;
            f->maxCompressorFlow = source.MaxCompressorFlow;
            f->compressorOutletTemperature = source.GetCompressorOutletTemperature();
            return f;
        }
    }
}
