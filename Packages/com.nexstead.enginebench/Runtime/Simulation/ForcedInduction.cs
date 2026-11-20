// Port of the community fork's include/forced_induction.h and src/forced_induction.cpp
// (engine-sim-community-ultimate cf96419: kadaxo's port of kyan0s-git's model, commits 27ed482d and c1440107).
// MIT licence; the notices: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>
    /// A turbocharger or supercharger. The compressor fills a charge-air volume (pipes and intercooler) that the intakes'
    /// throttles draw from instead of the atmosphere.
    /// <list type="bullet">
    /// <item>Centrifugal compressors (turbochargers and centrifugal superchargers) are a pressure source behind a flow
    /// restriction. The source pressure follows from the Euler work of the impeller,
    /// PR = (1 + eta × sigma × U² / (c_p × T1))^(gamma / (gamma − 1)), so the boost depends on the wheel's tip speed U and drops
    /// as the flow nears the compressor's capacity. The impeller absorbs sigma × U² of work per kilogram.</item>
    /// <item>Positive displacement superchargers (Roots and twin-screw) deliver their displacement every revolution; some of it
    /// leaks back to the inlet through the clearances.</item>
    /// </list>
    /// A turbocharger's turbine is the restriction at the exhaust outlet. It extracts
    /// eta_t × c_p × T × (1 − (p_out / p_in)^((gamma − 1) / gamma)) per unit of flow; the shaft's kinetic energy integrates the
    /// turbine power minus the compressor and bearing power. A wastegate bypasses the turbine once the boost reaches its set
    /// point. Superchargers are driven by the crankshaft through their drive ratio and load it with their drive power. The
    /// compressor draws from the intakes' atmosphere, and boost is measured against it.
    /// </summary>
    public sealed class ForcedInduction : ForceGenerator
    {
        public enum Type
        {
            Turbocharger,
            Roots,
            TwinScrew,
            Centrifugal,
        }

        /// <summary>Turbochargers only.</summary>
        public enum Arrangement
        {
            Single,
            /// <summary>Two turbochargers in parallel.</summary>
            Twin,
            /// <summary>The second turbocharger joins at high speed.</summary>
            Sequential,
        }

        /// <summary>Where the blow-off valve vents, or no valve (the charge stays trapped when the throttle closes).</summary>
        public enum BlowoffType
        {
            Atmospheric,
            Recirculating,
            None,
        }

        /// <summary>Spring: the actuator opens on charge pressure alone and lets the boost creep up at high flow; electronic: closed loop on the target.</summary>
        public enum BoostControl
        {
            Spring,
            Electronic,
        }

        public sealed class Parameters
        {
            public Type type = Type.Turbocharger;
            public Arrangement arrangement = Arrangement.Single;

            // Compressor (centrifugal: per unit)
            /// <summary>Impeller tip diameter (m).</summary>
            public double compressorDiameter = 0.060;
            /// <summary>Maximum flow (kg/s).</summary>
            public double compressorFlow = 0.30;
            public double compressorEfficiency = 0.75;
            /// <summary>Shaft limit (rad/s); 0: from the tip speed limit.</summary>
            public double maxSpeed = 0;

            // Turbine (turbochargers)
            /// <summary>Housing A/R.</summary>
            public double turbineAr = 0.64;
            public double turbineEfficiency = 0.70;
            /// <summary>Rotating assembly (kg m²).</summary>
            public double inertia = 3.0e-5;
            public bool ballBearings = false;

            // Boost control
            /// <summary>Wastegate set point, gauge (Pa).</summary>
            public double boost = 1.0e5;
            public bool blowoffValve = true;
            public BlowoffType blowoffType = BlowoffType.Atmospheric;
            public BoostControl boostControl = BoostControl.Electronic;
            /// <summary>Larger, vents through its own pipe.</summary>
            public bool externalWastegate = false;
            /// <summary>Divided turbine housing: better spool.</summary>
            public bool twinScroll = false;
            /// <summary>Sound only.</summary>
            public double whistlePitch = 1.0;

            // Superchargers
            /// <summary>Compressor speed / crank speed.</summary>
            public double driveRatio = 1.0;
            /// <summary>Positive displacement, per rev (m³).</summary>
            public double displacement = 1.9e-3;
            /// <summary>Vents the charge at part throttle.</summary>
            public bool bypassValve = true;

            // Charge air
            public double intercoolerEffectiveness = 0.7;
            /// <summary>m³.</summary>
            public double chargeVolume = 6.0e-3;

            /// <summary>Sequential turbochargers: engine speed at which the second unit comes in (rad/s).</summary>
            public double sequentialSpeed = 2 * 3.14159265358979 * 4000 / 60;
        }

        public struct Unit
        {
            /// <summary>Shaft speed (rad/s).</summary>
            public double omega;
            /// <summary>Shaft kinetic energy (J).</summary>
            public double energy;
            /// <summary>mol/s.</summary>
            public double compressorFlow;
            /// <summary>W.</summary>
            public double turbinePower;
            public double compressorPower;
            public bool active;

            internal static Unit Default => new Unit { active = true };
        }

        // Reference conditions the flow constants are defined at, and the pressure the exhaust outlets vent to
        static readonly double Reference = Units.Pressure(1.0, Units.atm);
        static readonly double ReferenceTemperature = Units.Celcius(25.0);
        static readonly double ExhaustAmbient = Units.Pressure(1.0, Units.atm);

        // Air properties for the compressor
        const double Gamma = 1.4;
        /// <summary>J/(kg K).</summary>
        const double Cp_mass = 1005.0;
        const double Cp_molar = Constants.R * Gamma / (Gamma - 1);
        const double Exponent = (Gamma - 1) / Gamma;

        /// <summary>Tip speed limit of aluminium compressor wheels (m/s).</summary>
        const double MaxTipSpeed = 520.0;

        /// <summary>The most intakes a compressor feeds (upstream's fixed array).</summary>
        public const int MaxIntakes = 16;

        static double Approach(double value, double target, double dt, double timeConstant)
        {
            return value + (target - value) * Utilities.StdMin(1.0, dt / timeConstant);
        }

        static GasSystem.Mix AirMix()
        {
            GasSystem.Mix mix = GasSystem.Mix.Default;
            mix.p_fuel = 0;
            mix.p_o2 = 0.25;
            mix.p_inert = 0.75;
            return mix;
        }

        Type type = Type.Turbocharger;
        Arrangement arrangement = Arrangement.Single;

        readonly Unit[] units = { Unit.Default, Unit.Default };
        int unitCount = 1;

        readonly GasSystem charge = new GasSystem();

        Crankshaft crankshaft;
        readonly Intake[] intakes = new Intake[MaxIntakes];
        int intakeCount;
        int exhaustSystemCount = 1;

        // Air the compressor draws from
        double ambientPressure = Reference;
        double ambientTemperature = ReferenceTemperature;

        // Compressor
        double tipRadius;
        double compressorK;
        double compressorEfficiency = 0.75;
        double maxSpeed;
        readonly double slip = 0.9;

        // Positive displacement
        double displacement;
        double leakK;
        double bypassK;
        bool bypassValve;
        double bypass;
        double driveRatio = 1;
        double driveTorque;
        double drivePower;

        // Turbine
        double turbineK;
        double wastegateK;
        double turbineEfficiency = 0.7;
        double inertia = 3e-5;
        double bearingLoss;
        double turbineFraction;
        double sequentialSpeed;
        /// <summary>Turbine work since the last <see cref="Process"/> (J).</summary>
        double turbineEnergy;

        // Control
        double boostTarget;
        double wastegate;
        bool blowoffEnabled = true;
        double blowoff;
        double blowoffK;
        double blowoffFlowRate;

        double intercoolerEffectiveness = 0.7;
        BlowoffType blowoffType = BlowoffType.Atmospheric;
        BoostControl boostControl = BoostControl.Electronic;
        bool externalWastegate;
        bool twinScroll;
        double whistlePitch = 1.0;
        double surge;
        double wastegateIntegral;
        double lastBoost;
        double boostRate;
        /// <summary>kg/s.</summary>
        double maxCompressorFlow = 0.3;
        double compressorOutletTemperature = ReferenceTemperature;

        Parameters parameters;

        public void Initialize(Parameters @params)
        {
            parameters = @params;
            type = @params.type;
            arrangement = IsTurbocharger() ? @params.arrangement : Arrangement.Single;
            unitCount = (arrangement == Arrangement.Single) ? 1 : 2;

            double d = Utilities.Clamp(@params.compressorDiameter, 0.02, 0.2);
            tipRadius = d / 2;
            compressorEfficiency = Utilities.Clamp(@params.compressorEfficiency, 0.4, 0.85);
            maxSpeed = (@params.maxSpeed > 0)
                ? @params.maxSpeed
                : MaxTipSpeed / tipRadius;

            // The compressor delivers its maximum flow when the source pressure (about 3 bar at the design speed) drops by a
            // quarter across it
            double compressorFlow = Utilities.Clamp(@params.compressorFlow, 0.01, 3.0) / Units.AirMolecularMass;
            compressorK = GasSystem.FlowConstant(
                compressorFlow, 3.0 * Reference, 0.75 * Reference, Units.Celcius(50.0), Gamma);

            displacement = Utilities.StdMax(@params.displacement, 0.0);
            driveRatio = Utilities.StdMax(@params.driveRatio, 0.0);
            bypassValve = @params.bypassValve;

            // Rotor clearances leak back about 40% (Roots) or 20% (twin screw) of the delivery at 1 bar of boost and 6000 rpm
            // rotor speed
            double leakFraction = (type == Type.Roots) ? 0.4 : 0.2;
            double pdFlow = Reference * displacement * 100.0 / (Constants.R * ReferenceTemperature);
            leakK = GasSystem.FlowConstant(
                Utilities.StdMax(leakFraction * pdFlow, 1e-6), 2.0 * Reference, 1.0 * Reference, Units.Celcius(60.0), Gamma);
            bypassK = GasSystem.FlowConstant(
                Utilities.StdMax(pdFlow, 1e-6), Reference, Units.Pressure(1.5, Units.inHg), ReferenceTemperature, Gamma);

            double ar = Utilities.Clamp(@params.turbineAr, 0.3, 1.6);
            // Turbine flow capacity scales with the housing's A/R. Turbines are sized to spool well below the engine's maximum
            // flow (about half the compressor's maximum flow at an expansion ratio of 2.2 for a 0.64 A/R housing); the
            // wastegate bypasses the excess.
            double turbineFlow = 0.5 * compressorFlow * Math.Pow(ar / 0.64, 0.9);
            turbineK = GasSystem.FlowConstant(
                turbineFlow, 2.2 * Reference, 1.2 * Reference, 1000.0, 1.33);
            externalWastegate = @params.externalWastegate;
            wastegateK = (externalWastegate ? 6.0 : 4.0) * turbineK;
            twinScroll = @params.twinScroll;
            blowoffType = @params.blowoffType;
            boostControl = @params.boostControl;
            whistlePitch = Utilities.Clamp(@params.whistlePitch, 0.25, 4.0);
            maxCompressorFlow = Utilities.Clamp(@params.compressorFlow, 0.01, 3.0);
            surge = 0;
            wastegateIntegral = 0;
            lastBoost = 0;
            boostRate = 0;
            turbineEfficiency = Utilities.Clamp(@params.turbineEfficiency, 0.3, 0.85);
            inertia = Utilities.StdMax(@params.inertia, 1e-6);

            // Bearing and windage losses (P = c × omega²) scale with the wheel size; ball bearings lose a little over half as
            // much as journal bearings
            bearingLoss = (@params.ballBearings ? 1.1e-6 : 2.0e-6) * Math.Pow(d / 0.06, 3.0);
            sequentialSpeed = @params.sequentialSpeed;

            boostTarget = Utilities.StdMax(@params.boost, 0.0);
            blowoffEnabled = @params.blowoffValve && @params.blowoffType != BlowoffType.None;
            blowoffK = GasSystem.FlowConstant(
                0.5 * compressorFlow, 2.0 * Reference, 1.0 * Reference, Units.Celcius(50.0), Gamma);
            if (IsPositiveDisplacement())
            {
                blowoffK = bypassK;
            }

            intercoolerEffectiveness = Utilities.Clamp(@params.intercoolerEffectiveness, 0.0, 1.0);

            for (int i = 0; i < units.Length; ++i) units[i] = Unit.Default;
            units[1].active = (arrangement == Arrangement.Twin);

            // Charge pipes of about 2.5" diameter
            double area = Units.Area(32.0, Units.cm2);
            double volume = Utilities.StdMax(@params.chargeVolume, 0.5e-3);
            charge.Initialize(ambientPressure, volume, ambientTemperature, AirMix());
            charge.SetGeometry(volume / area, Math.Sqrt(area), 1.0, 0.0);
        }

        /// <summary>The parameters it was initialized with.</summary>
        public Parameters GetParameters() => parameters;

        // Connections (made by the engine)
        internal void SetCrankshaft(Crankshaft crankshaft) => this.crankshaft = crankshaft;
        internal void SetExhaustSystemCount(int count) => exhaustSystemCount = count;

        internal void AddIntake(Intake intake)
        {
            if (intakeCount >= MaxIntakes) return;
            intakes[intakeCount++] = intake;

            if (intakeCount == 1)
            {
                // The compressor draws from the intake's atmosphere, and the charge air carries the intake's mixture (the model
                // meters the fuel at the compressor inlet)
                ambientPressure = intake.GetAtmospherePressure();
                ambientTemperature = intake.GetAtmosphereTemperature();
                compressorOutletTemperature = ambientTemperature;
                charge.Reset(ambientPressure, ambientTemperature, intake.GetFuelAirMix());
            }
        }

        public string GetDescription()
        {
            switch (type)
            {
                case Type.Roots: return "Roots supercharger";
                case Type.TwinScrew: return "Twin-screw supercharger";
                case Type.Centrifugal: return "Centrifugal supercharger";
                default:
                    switch (arrangement)
                    {
                        case Arrangement.Twin: return "Twin turbochargers";
                        case Arrangement.Sequential: return "Sequential turbochargers";
                        default: return "Turbocharger";
                    }
            }
        }

        /// <summary>An exhaust system's outlet restriction: the turbines' share and the muffler in series.</summary>
        internal double GetExhaustOutletFlowK(double mufflerK)
        {
            if (!IsTurbocharger()) return mufflerK;

            double k = wastegateK * wastegate;
            for (int i = 0; i < unitCount; ++i)
            {
                if (units[i].active) k += turbineK;
            }

            // Each exhaust system feeds its share of the turbines; the turbine and the muffler are restrictions in series
            k /= Utilities.StdMax(exhaustSystemCount, 1);
            if (k <= 0) return 0;
            if (mufflerK <= 0) return k;

            return 1.0 / Math.Sqrt(1.0 / (k * k) + 1.0 / (mufflerK * mufflerK));
        }

        /// <summary>Reported by each exhaust system: the moles that left through its outlet, and the gas's state before.</summary>
        internal void AddExhaustFlow(double n, double T, double P, double hcr, double mufflerK)
        {
            if (!IsTurbocharger() || n <= 0 || P <= ExhaustAmbient) return;

            double turbineK = 0;
            for (int i = 0; i < unitCount; ++i)
            {
                if (units[i].active) turbineK += this.turbineK;
            }

            double totalK = turbineK + wastegateK * wastegate;
            if (totalK <= 0) return;

            double share = totalK / Utilities.StdMax(exhaustSystemCount, 1);
            turbineFraction = turbineK / totalK;

            // Pressure drop across the turbine (restrictions in series share the drop in proportion to 1 / k²)
            double turbineDropFraction = 1.0;
            if (mufflerK > 0)
            {
                double rt = 1.0 / (share * share);
                double rm = 1.0 / (mufflerK * mufflerK);
                turbineDropFraction = rt / (rt + rm);
            }

            double P_out = P - (P - ExhaustAmbient) * turbineDropFraction;
            double g = Utilities.StdMax(hcr, 1.05);
            double cp = Constants.R * g / (g - 1);
            double isentropicWork = cp * T * (1.0 - Math.Pow(P_out / P, (g - 1) / g));

            // A twin-scroll housing keeps the exhaust pulses of the cylinder groups apart, which helps most at low flow
            double efficiency = turbineEfficiency;
            if (twinScroll) efficiency *= 1.0 + 0.12 * Utilities.Clamp(1.0 - turbineFraction * 0.5, 0.0, 1.0);

            turbineEnergy += efficiency * turbineFraction * n * isentropicWork;
        }

        double CentrifugalSourcePressureRatio(double omega)
        {
            double U = omega * tipRadius;
            double pr = Math.Pow(
                1.0 + compressorEfficiency * slip * U * U / (Cp_mass * ambientTemperature),
                1.0 / Exponent);

            return Utilities.StdMin(pr, 6.0);
        }

        double PlenumPressure()
        {
            if (intakeCount == 0) return ambientPressure;

            double sum = 0;
            for (int i = 0; i < intakeCount; ++i)
            {
                sum += intakes[i].system.Pressure();
            }

            return sum / intakeCount;
        }

        double CrankSpeed() => (crankshaft != null) ? Math.Abs(crankshaft.body.v_theta) : 0.0;

        GasSystem.Mix ChargeMix() => (intakeCount > 0) ? intakes[0].GetFuelAirMix() : AirMix();

        void ProcessCentrifugal(ref Unit unit, double dt, double scale, GasSystem.Mix mix)
        {
            double pr0 = CentrifugalSourcePressureRatio(unit.omega);
            double pr = Utilities.StdMax(charge.Pressure() / ambientPressure, 1.0);

            // Compressor outlet temperature, then the intercooler
            double T2 = ambientTemperature
                * (1.0 + (Math.Pow(pr, Exponent) - 1.0) / compressorEfficiency);
            compressorOutletTemperature = T2;
            double T_in = T2 - intercoolerEffectiveness * (T2 - ambientTemperature);

            double flow = charge.Flow(scale * compressorK, dt, pr0 * ambientPressure, T_in, mix);
            double dn = -flow; // moles into the charge air

            unit.compressorFlow = dn / dt;

            // Reverse flow through the compressor: surge (the flutter heard without a blow-off valve)
            double reverse = Utilities.StdMax(-dn / dt * Units.AirMolecularMass / (0.15 * maxCompressorFlow), 0.0);
            surge = surge + (Utilities.StdMin(reverse, 1.0) - surge) * Utilities.StdMin(1.0, dt / 0.05);

            // Euler work of the impeller; reverse flow (surge) brakes the wheel too
            double U = unit.omega * tipRadius;
            double massFlow = Math.Abs(dn) / dt * Units.AirMolecularMass;
            unit.compressorPower = massFlow * slip * U * U;
        }

        void ProcessPositiveDisplacement(double dt, GasSystem.Mix mix)
        {
            double omega = CrankSpeed() * driveRatio;
            units[0].omega = omega;

            double pr = Utilities.StdMax(charge.Pressure() / ambientPressure, 1.0);
            double efficiency = (type == Type.Roots) ? 0.55 : 0.72;
            double T2 = ambientTemperature
                * (1.0 + (Math.Pow(pr, Exponent) - 1.0) / efficiency);
            compressorOutletTemperature = T2;
            double T_in = T2 - intercoolerEffectiveness * (T2 - ambientTemperature);

            // Displacement delivered this step
            double volumeFlow = displacement * omega / (2 * Constants.pi);
            double n = 0.95 * ambientPressure * volumeFlow * dt / (Constants.R * ambientTemperature);
            if (n > 0)
            {
                charge.Inject(n, T_in, mix);
            }

            // Leakage back to the inlet
            double leak = charge.Flow(leakK, dt, ambientPressure, ambientTemperature, mix);

            double netFlow = (n - leak) / dt;
            units[0].compressorFlow = netFlow;

            // Drive power: the work to heat the delivered air plus 10% mechanical losses
            units[0].compressorPower = (n / dt) * Cp_molar * (T2 - ambientTemperature) / 0.9;
        }

        /// <summary>One fluid step: after the exhausts, before the intakes.</summary>
        public void Process(double dt)
        {
            if (dt <= 0) return;

            GasSystem.Mix mix = ChargeMix();

            double p_plenum = PlenumPressure();
            double boost = charge.Pressure() - ambientPressure;

            if (IsTurbocharger())
            {
                // Sequential turbochargers: the second unit joins above the transition speed (with some hysteresis)
                if (arrangement == Arrangement.Sequential)
                {
                    double speed = CrankSpeed();
                    if (!units[1].active && speed > sequentialSpeed) units[1].active = true;
                    else if (units[1].active && speed < 0.9 * sequentialSpeed) units[1].active = false;
                }

                int activeTurbines = 0;
                for (int i = 0; i < unitCount; ++i)
                {
                    if (units[i].active) ++activeTurbines;
                }

                boostRate += ((boost - lastBoost) / dt - boostRate) * Utilities.StdMin(1.0, dt / 0.02);
                lastBoost = boost;

                // Wastegate. Electronic control: proportional-integral on the boost error, holding the target. It sets the
                // share of the exhaust that bypasses the turbine, which the boost follows evenly, and turns it into a valve
                // opening: near closed, a small opening bypasses a lot, and controlling the opening directly made the boost
                // hunt there. The proportional band is half the target, the integral only acts within it (so it doesn't wind
                // up while the turbo spools), and the valve starts to open as the boost rises quickly towards the target, which
                // keeps the boost from overshooting after a snap. Spring actuator: opens from 75% of the set point and only
                // fully at 120%, slowly, so boost overshoots and creeps up with the flow.
                double wastegateTarget = 1.0;
                if (boostTarget > 0)
                {
                    if (boostControl == BoostControl.Electronic)
                    {
                        double band = Utilities.StdMax(0.5 * boostTarget, Units.Pressure(0.5, Units.psi));
                        double error = (boost - boostTarget) / band;
                        double bypass = 1.0 + error;
                        if (Math.Abs(error) < 1.0)
                        {
                            wastegateIntegral = Utilities.Clamp(wastegateIntegral + 2.0 * error * dt, -1.0, 1.0);
                            if (boostRate > 0) bypass += 0.05 * boostRate / band;
                        }
                        bypass = Utilities.Clamp(bypass + wastegateIntegral, 0.0, 1.0);

                        // Opening w bypasses ratio × w / (1 + ratio × w) of the flow
                        double ratio = wastegateK / Utilities.StdMax(activeTurbines * turbineK, 1e-12);
                        double share = bypass * ratio / (1.0 + ratio);
                        wastegateTarget = Utilities.Clamp(share / ((1.0 - share) * ratio), 0.0, 1.0);
                    }
                    else
                    {
                        wastegateTarget = Utilities.Clamp((boost - 0.75 * boostTarget) / (0.45 * boostTarget), 0.0, 1.0);
                    }
                }
                wastegate = Approach(wastegate, wastegateTarget, dt,
                    (boostControl == BoostControl.Electronic) ? 0.03 : 0.12);

                for (int i = 0; i < unitCount; ++i)
                {
                    ref Unit unit = ref units[i];
                    unit.turbinePower = 0;
                    unit.compressorFlow = 0;
                    unit.compressorPower = 0;

                    if (unit.active)
                    {
                        ProcessCentrifugal(ref unit, dt, 1.0, mix);
                        double unitTurbineEnergy = turbineEnergy / Utilities.StdMax(activeTurbines, 1);
                        unit.turbinePower = unitTurbineEnergy / dt;
                        unit.energy += unitTurbineEnergy;
                    }

                    unit.energy -= (unit.compressorPower + bearingLoss * unit.omega * unit.omega) * dt;

                    // Overspeed: the wastegate can't stop a badly mismatched turbo but the wheel is limited to its maximum
                    // speed
                    double maxEnergy = 0.5 * inertia * maxSpeed * maxSpeed;
                    unit.energy = Utilities.Clamp(unit.energy, 0.0, maxEnergy);
                    unit.omega = Math.Sqrt(2 * unit.energy / inertia);
                }

                turbineEnergy = 0;
                drivePower = 0;
                driveTorque = 0;
            }
            else
            {
                if (type == Type.Centrifugal)
                {
                    ref Unit unit = ref units[0];
                    unit.omega = Utilities.StdMin(CrankSpeed() * driveRatio, maxSpeed);
                    ProcessCentrifugal(ref unit, dt, 1.0, mix);
                    unit.compressorPower += bearingLoss * unit.omega * unit.omega;
                }
                else
                {
                    ProcessPositiveDisplacement(dt, mix);

                    // Bypass valve: recirculates the delivery at part throttle
                    if (bypassValve)
                    {
                        double target =
                            Utilities.Clamp((0.9 * ambientPressure - p_plenum) / (0.2 * ambientPressure), 0.0, 1.0);
                        bypass = Approach(bypass, target, dt, 0.05);
                        if (bypass > 0)
                        {
                            charge.Flow(bypassK * bypass, dt, ambientPressure, ambientTemperature, mix);
                        }
                    }
                }

                drivePower = units[0].compressorPower;
                driveTorque = drivePower / Utilities.StdMax(CrankSpeed(), 20.0);
            }

            // Blow-off valve: vents the charge when the throttle snaps shut
            double blowoffTarget = 0;
            if (blowoffEnabled && !IsPositiveDisplacement())
            {
                double dp = charge.Pressure() - p_plenum;
                blowoffTarget = Utilities.Clamp((dp - Units.Pressure(4.0, Units.psi)) / Units.Pressure(3.0, Units.psi), 0.0, 1.0);
            }

            blowoff = Approach(blowoff, blowoffTarget, dt, 0.01);
            double vented = 0;
            if (blowoff > 0)
            {
                vented = charge.Flow(blowoffK * blowoff, dt, ambientPressure, ambientTemperature, mix);
            }

            blowoffFlowRate = Approach(blowoffFlowRate, Utilities.StdMax(vented, 0.0) / dt, dt, 0.02);

            charge.DissipateExcessVelocity();
            charge.UpdateVelocity(dt, 0.5);
        }

        /// <summary>Applies the supercharger's drive torque to the crankshaft.</summary>
        public override void Apply(SystemState system)
        {
            if (crankshaft == null || driveTorque == 0) return;

            int index = crankshaft.body.index;
            double v = system.v_theta[index];
            if (v > 0) system.t[index] -= driveTorque;
            else if (v < 0) system.t[index] += driveTorque;
        }

        public GasSystem GetChargeSystem() => charge;

        public Type GetForcedInductionType() => type;
        public Arrangement GetArrangement() => arrangement;
        public bool IsTurbocharger() => type == Type.Turbocharger;
        public bool IsPositiveDisplacement() => type == Type.Roots || type == Type.TwinScrew;

        public int GetUnitCount() => unitCount;
        public Unit GetUnit(int i) => units[i];

        /// <summary>Gauge pressure of the charge air against the intake atmosphere (Pa).</summary>
        public double GetBoost() => charge.Pressure() - ambientPressure;
        public double GetChargePressure() => charge.Pressure();
        public double GetChargeTemperature() => charge.Temperature();
        public double GetCompressorOutletTemperature() => compressorOutletTemperature;
        public double GetAmbientPressure() => ambientPressure;
        public double GetAmbientTemperature() => ambientTemperature;

        /// <summary>Shaft speed of the (first) compressor (rad/s).</summary>
        public double GetShaftSpeed() => units[0].omega;
        public double GetMaxSpeed() => maxSpeed;

        public double GetWastegateOpening() => wastegate;
        /// <summary>Superchargers.</summary>
        public double GetBypassOpening() => bypass;
        public double GetBlowoffOpening() => blowoff;
        /// <summary>mol/s.</summary>
        public double GetBlowoffFlow() => blowoffFlowRate;
        /// <summary>Superchargers (W).</summary>
        public double GetDrivePower() => drivePower;
        /// <summary>Superchargers (N m).</summary>
        public double GetDriveTorque() => driveTorque;

        /// <summary>mol/s.</summary>
        public double GetCompressorFlow()
        {
            double flow = 0;
            for (int i = 0; i < unitCount; ++i) flow += units[i].compressorFlow;
            return flow;
        }

        public double GetTurbinePower()
        {
            double power = 0;
            for (int i = 0; i < unitCount; ++i) power += units[i].turbinePower;
            return power;
        }

        /// <summary>Share of the exhaust flow going through the turbine(s), 0 to 1.</summary>
        public double GetTurbineFlowFraction() => turbineFraction;

        public double GetBoostTarget() => boostTarget;
        public bool HasBlowoffValve() => blowoffEnabled;
        public BlowoffType GetBlowoffType() => blowoffEnabled ? blowoffType : BlowoffType.None;
        public BoostControl GetBoostControl() => boostControl;
        public bool HasExternalWastegate() => externalWastegate;
        public bool IsTwinScroll() => twinScroll;
        public double GetWhistlePitch() => whistlePitch;
        public double GetIntercoolerEffectiveness() => intercoolerEffectiveness;
        public double GetDriveRatio() => driveRatio;

        /// <summary>Shaft order of the compressor (Hz); the synthesizer turns it into the whistle or the rotor lobe frequency.</summary>
        public double GetWhineFrequency() => units[0].omega / (2 * Constants.pi);

        /// <summary>0 to 1.</summary>
        public double GetWhineLevel()
        {
            if (IsPositiveDisplacement())
            {
                // Supercharger whine follows rotor speed and load
                double speed = Utilities.Clamp(units[0].omega / (2 * Constants.pi * 250.0), 0.0, 1.0);
                double load = Utilities.Clamp(GetBoost() / Units.Pressure(10.0, Units.psi), 0.0, 1.0);
                return speed * (0.3 + 0.7 * load);
            }

            double s = Utilities.Clamp(units[0].omega / maxSpeed, 0.0, 1.0);
            double flow = Utilities.Clamp(
                Math.Abs(GetCompressorFlow()) * Units.AirMolecularMass / 0.3, 0.0, 1.0);
            return s * s * (0.25 + 0.75 * flow);
        }

        /// <summary>Compressor flow, 0 to 1.</summary>
        public double GetFlowLevel() =>
            Utilities.Clamp(Math.Abs(GetCompressorFlow()) * Units.AirMolecularMass / Utilities.StdMax(maxCompressorFlow, 0.01), 0.0, 1.0);

        /// <summary>0 to 1.</summary>
        public double GetSurgeLevel() => surge;

        /// <summary>External wastegate venting, 0 to 1.</summary>
        public double GetWastegateNoise()
        {
            if (!externalWastegate || !IsTurbocharger()) return 0.0;
            return Utilities.Clamp(wastegate * (1.0 - turbineFraction) * 2.0, 0.0, 1.0);
        }

        /// <summary>
        /// 0 to 1. The hiss is the jet of charge air leaving the valve, so it follows the pressure driving it as well as the flow:
        /// a lift from full boost gives a sharp hiss, while the little air the compressor pushes through the open valve when
        /// cruising (with the charge at about ambient pressure) is nearly silent.
        /// </summary>
        public double GetBlowoffLevel()
        {
            double flow = Utilities.Clamp(blowoffFlowRate * Units.AirMolecularMass / 0.15, 0.0, 1.0);
            double drive = Utilities.Clamp(GetBoost() / Units.Pressure(5.0, Units.psi), 0.0, 1.0);
            return flow * drive;
        }

        /// <summary>
        /// Sound type for the synthesizer: 10 × type (1 turbocharger, 2 Roots, 3 twin-screw, 4 centrifugal) + blow-off type
        /// (0 vented to the atmosphere, 1 recirculating, 2 none).
        /// </summary>
        public int GetSoundMode()
        {
            int t = 1;
            switch (type)
            {
                case Type.Roots: t = 2; break;
                case Type.TwinScrew: t = 3; break;
                case Type.Centrifugal: t = 4; break;
            }

            return 10 * t + (int)GetBlowoffType();
        }

        // Read access for the dense core's snapshot.
        internal Crankshaft Crankshaft => crankshaft;
        internal Intake GetIntake(int i) => intakes[i];
        internal int IntakeCount => intakeCount;
        internal int ExhaustSystemCount => exhaustSystemCount;
        internal double TipRadius => tipRadius;
        internal double CompressorK => compressorK;
        internal double CompressorEfficiency => compressorEfficiency;
        internal double Slip => slip;
        internal double Displacement => displacement;
        internal double LeakK => leakK;
        internal double BypassK => bypassK;
        internal bool BypassValve => bypassValve;
        internal double TurbineK => turbineK;
        internal double WastegateK => wastegateK;
        internal double TurbineEfficiency => turbineEfficiency;
        internal double Inertia => inertia;
        internal double BearingLoss => bearingLoss;
        internal double SequentialSpeed => sequentialSpeed;
        internal double TurbineEnergy => turbineEnergy;
        internal double BlowoffK => blowoffK;
        internal double WastegateIntegral => wastegateIntegral;
        internal double LastBoost => lastBoost;
        internal double BoostRate => boostRate;
        internal double MaxCompressorFlow => maxCompressorFlow;
        internal BlowoffType ConfiguredBlowoffType => blowoffType;
    }
}
