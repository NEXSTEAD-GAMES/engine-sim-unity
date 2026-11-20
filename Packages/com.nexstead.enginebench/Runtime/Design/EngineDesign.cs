// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Design
{
    /// <summary>How the cylinders stand: in one row, in two banks at an angle, or in two opposed banks.</summary>
    public enum EngineLayout
    {
        Inline,
        V,
        Flat,
    }

    /// <summary>
    /// An engine as a designer describes it: layout, cylinder count and firing order, the rotating assembly, the head,
    /// camshafts, intake, exhausts, ignition and fuel. <see cref="EngineGenerator"/> derives the rest (bank angles, top
    /// dead centre, crank journals and which cylinders share them, cam lobe centrelines, spark plug angles, deck height)
    /// and builds the engine with the script nodes. Values are kept in the units engine scripts write them in: angles in
    /// degrees, engine speeds in rpm, flows in cfm (as measured on a flow bench: heads and blow-by at 28 inH2O, intakes
    /// and exhausts as carburettors are rated); everything else in SI units. Cylinders are numbered bank by bank from 1.
    /// </summary>
    [Serializable]
    public sealed class EngineDesign
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;

        // ---- The engine ----

        public string name = "";
        /// <summary>Starter motor torque (N·m) and speed (rpm).</summary>
        public double starterTorque = 200 * ScriptUnits.lb_ft;
        public double starterSpeed = 200;
        public double redline = 6000;
        /// <summary>The dyno's speed range and hold step (rpm).</summary>
        public double dynoMinSpeed = 1000;
        public double dynoMaxSpeed = 6000;
        public double dynoHoldStep = 100;
        /// <summary>Simulation steps per second: higher resolves faster engines, at more cost.</summary>
        public double simulationFrequency = 10000;
        public double highFrequencyGain = 0.01;
        public double noise = 1.0;
        public double jitter = 0.5;
        public ThrottleDesign throttle = new ThrottleDesign();

        // ---- Layout ----

        public EngineLayout layout = EngineLayout.Inline;
        public int cylinders = 4;
        /// <summary>The angle between a V engine's banks (°).</summary>
        public double bankAngle = 90;
        /// <summary>The cylinders in firing order, an even interval apart over the engine cycle (unless <see cref="firingAngles"/> sets them).</summary>
        public int[] firingOrder = { 1, 3, 4, 2 };
        /// <summary>
        /// Odd firing: each cylinder's firing angle in the engine cycle (°, 0 to 720), by cylinder. Empty: even firing, by
        /// <see cref="firingOrder"/>.
        /// </summary>
        public double[] firingAngles = Array.Empty<double>();
        /// <summary>The banks the other way round: a V's first bank to the left, a flat engine's to the right.</summary>
        public bool banksMirrored;
        /// <summary>A V with a crank pin for each cylinder, not one for each pair across the banks.</summary>
        public bool splitPins;
        /// <summary>Per bank: draw its valves on the other side of its bore (display only).</summary>
        public bool[] flipBankDisplay = { false, false };
        /// <summary>How far back the banks' cylinders are drawn one behind another (display only).</summary>
        public double displayDepth = 0.5;

        // ---- Rotating assembly (m, kg, kg·m², N·m) ----

        public double bore = 86 * ScriptUnits.mm;
        public double stroke = 86 * ScriptUnits.mm;
        public double rodLength = 145 * ScriptUnits.mm;
        /// <summary>From the wrist pin to the crown.</summary>
        public double compressionHeight = 1.0 * ScriptUnits.inch;
        /// <summary>The deck above the piston crown at top dead centre: the deck height is half the stroke, the rod, the compression height and this.</summary>
        public double deckClearance;
        public double pistonMass = 400 * ScriptUnits.g;
        public double wristPinPosition;
        public double rodMass = 500 * ScriptUnits.g;
        public double rodMomentOfInertia = ScriptFunctions.RodMomentOfInertia(500 * ScriptUnits.g, 145 * ScriptUnits.mm);
        public double rodCenterOfMass;
        public double crankMass = 15 * ScriptUnits.kg;
        public double flywheelMass = 8 * ScriptUnits.kg;
        /// <summary>Crank, flywheel and everything turning with them.</summary>
        public double crankMomentOfInertia = 0.2;
        public double crankFrictionTorque = 10 * ScriptUnits.lb_ft;

        // ---- Per cylinder ----

        /// <summary>One per cylinder, in cylinder order.</summary>
        public CylinderDesign[] cylinderSettings = Array.Empty<CylinderDesign>();

        // ---- Breathing ----

        /// <summary>The cylinder heads: one for every bank, or one a bank.</summary>
        public HeadDesign[] heads = { new HeadDesign() };
        public CamshaftDesign camshaft = new CamshaftDesign();
        public IntakeDesign[] intakes = { new IntakeDesign() };
        public ExhaustDesign[] exhausts = { new ExhaustDesign() };
        /// <summary>A turbocharger or supercharger feeding every intake; none by default.</summary>
        public ForcedInductionDesign forcedInduction = new ForcedInductionDesign();

        // ---- Ignition and fuel ----

        public IgnitionDesign ignition = new IgnitionDesign();
        public FuelDesign fuel = new FuelDesign();

        // ---- What the engine drives (the Bench uses the dyno; the descriptions carry these) ----

        public VehicleDesign vehicle = new VehicleDesign();
        public TransmissionDesign transmission = new TransmissionDesign();

        /// <summary>Banks: one for an inline engine, two otherwise.</summary>
        public int Banks => layout == EngineLayout.Inline ? 1 : 2;

        /// <summary>Cylinders per bank.</summary>
        public int CylindersPerBank => cylinders / Banks;

        /// <summary>The head of bank <paramref name="bank"/>: its own, or the one every bank shares.</summary>
        public HeadDesign HeadOf(int bank) => heads[Math.Min(bank, heads.Length - 1)];

        /// <summary>
        /// Takes another layout or cylinder count (rounded up to an even count on two banks): the per-cylinder settings
        /// follow (new cylinders copy the last one's, each bank breathing out through its own exhaust where there is one),
        /// one bank keeps the first head, and the engine fires evenly, in the first common order that fits (<see cref="FiringOrders"/>), or 1, 2, 3… if none.
        /// A V keeps its bank angle if it has one between 0° and 180°, else takes 90°.
        /// </summary>
        public void Reshape(EngineLayout newLayout, int newCylinders)
        {
            layout = newLayout;
            cylinders = layout == EngineLayout.Inline ? Math.Max(1, newCylinders) : Math.Max(2, newCylinders + newCylinders % 2);
            if (layout == EngineLayout.V && (bankAngle <= 0 || bankAngle >= 180)) bankAngle = 90;

            CylinderDesign[] old = cylinderSettings ?? Array.Empty<CylinderDesign>();
            int exhaustCount = Math.Max(1, exhausts?.Length ?? 0);
            var settings = new CylinderDesign[cylinders];
            for (int c = 0; c < settings.Length; ++c)
            {
                CylinderDesign from = c < old.Length ? old[c] : old.Length > 0 ? old[old.Length - 1] : new CylinderDesign();
                settings[c] = new CylinderDesign
                {
                    blowby = from.blowby, soundAttenuation = from.soundAttenuation, headerPrimaryLength = from.headerPrimaryLength, intake = 0,
                    exhaust = Math.Min(c / CylindersPerBank, exhaustCount - 1),
                };
            }

            cylinderSettings = settings;
            if (heads.Length > Banks) heads = new[] { heads[0] };
            var flips = new bool[Banks];
            for (int b = 0; b < flips.Length; ++b) flips[b] = flipBankDisplay != null && b < flipBankDisplay.Length ? flipBankDisplay[b] : b > 0;
            flipBankDisplay = flips;

            firingAngles = Array.Empty<double>();
            System.Collections.Generic.List<int[]> common = FiringOrders.For(this);
            if (common.Count > 0)
            {
                firingOrder = common[0];
                return;
            }

            firingOrder = new int[cylinders];
            for (int c = 0; c < cylinders; ++c) firingOrder[c] = c + 1;
        }
    }

    /// <summary>How the pedal reaches the throttle plate.</summary>
    [Serializable]
    public sealed class ThrottleDesign
    {
        /// <summary>Direct: the plate follows the pedal, shaped by <see cref="gamma"/>. Governed: the pedal sets a speed the governor holds.</summary>
        public bool governed;
        public double gamma = 1.0;
        /// <summary>The governor's speed range (rpm) and response.</summary>
        public double governorMinSpeed = 1000;
        public double governorMaxSpeed = 3600;
        public double governorMinVelocity = -2.0;
        public double governorMaxVelocity = 2.0;
        public double governorK_s = 1.0;
        public double governorK_d = 300.0;
        public double governorGamma = 0.1;
    }

    /// <summary>What differs from cylinder to cylinder.</summary>
    [Serializable]
    public sealed class CylinderDesign
    {
        /// <summary>Leakage past the rings (cfm at 28 inH2O).</summary>
        public double blowby;
        /// <summary>How loud this cylinder's exhaust is in its system, relative to the others.</summary>
        public double soundAttenuation = 1.0;
        /// <summary>This cylinder's header pipe, to the collector (m).</summary>
        public double headerPrimaryLength;
        /// <summary>Which intake and which exhaust system it uses.</summary>
        public int intake;
        public int exhaust;
    }

    /// <summary>The cylinder head (every bank's): chamber, runners and port flow measured against valve lift.</summary>
    [Serializable]
    public sealed class HeadDesign
    {
        public double chamberVolume = 118.0 * ScriptUnits.cc;
        public double intakeRunnerVolume = 300.0 * ScriptUnits.cc;
        public double intakeRunnerCrossSectionArea = ScriptFunctions.CircleArea(0.75 * ScriptUnits.inch);
        public double exhaustRunnerVolume = 300.0 * ScriptUnits.cc;
        public double exhaustRunnerCrossSectionArea = ScriptFunctions.CircleArea(0.85 * ScriptUnits.inch);
        /// <summary>Port flow (cfm at 28 inH2O) against valve lift (thou), smoothed over <see cref="flowSmoothing"/> thou.</summary>
        public FlowSample[] intakeFlow = Array.Empty<FlowSample>();
        public FlowSample[] exhaustFlow = Array.Empty<FlowSample>();
        public double flowSmoothing = 50;
    }

    /// <summary>A flow bench reading: valve lift (thou) and flow (cfm at 28 inH2O).</summary>
    [Serializable]
    public struct FlowSample
    {
        public double lift;
        public double flow;

        public FlowSample(double lift, double flow)
        {
            this.lift = lift;
            this.flow = flow;
        }
    }

    /// <summary>The camshafts: an intake and an exhaust camshaft a bank, one lobe a cylinder, timed by the firing order.</summary>
    [Serializable]
    public sealed class CamshaftDesign
    {
        public LobeDesign intakeLobe = new LobeDesign();
        public LobeDesign exhaustLobe = new LobeDesign();
        /// <summary>Lobe centres: the intake's after, the exhaust's before overlap top dead centre (°).</summary>
        public double intakeLobeCenter = 114;
        public double exhaustLobeCenter = 114;
        /// <summary>Cam timing advance (°).</summary>
        public double advance;
        public double baseRadius = 0.75 * ScriptUnits.inch;
        /// <summary>A second, high-lift pair of profiles the valvetrain switches to (variable valve lift).</summary>
        public VariableLiftDesign variableLift = new VariableLiftDesign();
    }

    /// <summary>
    /// Variable valve lift: high-lift camshafts the valvetrain switches to above an engine speed, a road speed and a throttle
    /// position, with enough manifold pressure.
    /// </summary>
    [Serializable]
    public sealed class VariableLiftDesign
    {
        public bool enabled;
        public LobeDesign intakeLobe = new LobeDesign();
        public LobeDesign exhaustLobe = new LobeDesign();
        /// <summary>The high-lift lobes' centres (°), timing advance (°) and base circle (m).</summary>
        public double intakeLobeCenter = 114;
        public double exhaustLobeCenter = 114;
        public double advance;
        public double baseRadius = 0.75 * ScriptUnits.inch;
        /// <summary>Switches above this engine speed (rpm).</summary>
        public double minRpm = 5800;
        /// <summary>…and this road speed (mph).</summary>
        public double minSpeed = 10;
        /// <summary>…while the manifold vacuum is below this (Pa below atmosphere).</summary>
        public double manifoldVacuum = 1.0 * ScriptUnits.atm - 5.0 * ScriptUnits.inHg;
        /// <summary>…and the throttle is open past this.</summary>
        public double minThrottlePosition = 0.3;
    }

    /// <summary>A harmonic cam lobe: its duration at 50 thou of lift (°), shape, peak lift (m) and samples a side.</summary>
    [Serializable]
    public sealed class LobeDesign
    {
        public double durationAt50Thou = 220;
        public double gamma = 1.0;
        public double lift = 400 * ScriptUnits.thou;
        public int steps = 100;
    }

    /// <summary>An intake: plenum, throttle body and runners. Flows in cfm, as carburettors are rated.</summary>
    [Serializable]
    public sealed class IntakeDesign
    {
        public double plenumVolume = 2.0 * ScriptUnits.L;
        public double plenumCrossSectionArea = 100.0 * ScriptUnits.cm2;
        public double intakeFlowRate = 600;
        public double idleFlowRate;
        public double runnerFlowRate = 200;
        public double runnerLength = 4.0 * ScriptUnits.inch;
        public double idleThrottlePlatePosition = 0.975;
        public double velocityDecay = 0.25;
        public double molecularAfr = 25.0 / 2.0;
        /// <summary>The air the intake draws from (Pa, K): altitude. A forced induction draws from it too.</summary>
        public double atmospherePressure = 1.0 * ScriptUnits.atm;
        public double atmosphereTemperature = 25.0 + ScriptUnits.K0;
    }

    /// <summary>
    /// A turbocharger or supercharger (the community fork's model, from kyan0s-git): its compressor fills a charge-air volume that
    /// every intake's throttle draws from. A turbocharger's turbine sits at the outlet of every exhaust system and spools with the
    /// exhaust flow; a supercharger is driven by the crank. Shaft and engine speeds in rpm, boost in Pa above the intake's
    /// atmosphere, the compressor's flow in kg/s, volumes in m³. A new one has the script's turbocharger defaults;
    /// <see cref="Defaults"/> gives each kind its own.
    /// </summary>
    [Serializable]
    public sealed class ForcedInductionDesign
    {
        public ForcedInductionKind type = ForcedInductionKind.None;

        // Turbochargers
        public ForcedInduction.Arrangement arrangement = ForcedInduction.Arrangement.Single;
        public double turbineAr = 0.64;
        public double turbineEfficiency = 0.7;
        /// <summary>The rotating assembly (kg·m²): more lags more.</summary>
        public double inertia = 0.00003;
        public bool ballBearings;
        /// <summary>The wastegate's set point.</summary>
        public double boost = 100.0 * ScriptUnits.kPa;
        public ForcedInduction.BoostControl boostControl = ForcedInduction.BoostControl.Electronic;
        public bool externalWastegate;
        public bool twinScroll;
        /// <summary>Where a sequential pair's second turbo joins (rpm).</summary>
        public double sequentialSpeed = 4000;

        // Superchargers
        /// <summary>Compressor speed / crank speed, a centrifugal's gearbox step-up included.</summary>
        public double driveRatio = 2.0;
        /// <summary>Air a Roots or twin-screw blower moves per rotor turn.</summary>
        public double displacement = 1.9 * ScriptUnits.L;
        /// <summary>Vents a positive displacement blower's delivery at part throttle.</summary>
        public bool bypassValve = true;

        // Compressor (turbochargers and centrifugal superchargers)
        public double compressorDiameter = 60 * ScriptUnits.mm;
        /// <summary>The compressor's maximum flow (kg/s).</summary>
        public double compressorFlow = 0.3;
        public double compressorEfficiency = 0.75;
        /// <summary>The shaft's speed limit (rpm); 0: from the compressor wheel's tip speed limit.</summary>
        public double maxSpeed;

        // All
        public bool blowoffValve = true;
        public ForcedInduction.BlowoffType blowoffType = ForcedInduction.BlowoffType.Atmospheric;
        public double intercoolerEffectiveness = 0.7;
        /// <summary>The pipes and intercooler between the compressor and the throttles.</summary>
        public double chargeVolume = 6.0 * ScriptUnits.L;
        /// <summary>Scales the whistle's or whine's pitch (sound only).</summary>
        public double whistlePitch = 1.0;

        /// <summary>A forced induction of <paramref name="type"/> with the script's defaults for it (turbocharger() or supercharger()).</summary>
        public static ForcedInductionDesign Defaults(ForcedInductionKind type)
        {
            var design = new ForcedInductionDesign { type = type };
            if (type == ForcedInductionKind.None || type == ForcedInductionKind.Turbocharger) return design;

            design.compressorDiameter = 90 * ScriptUnits.mm;
            design.compressorFlow = 0.5;
            design.intercoolerEffectiveness = 0.0;
            design.chargeVolume = 4.0 * ScriptUnits.L;
            design.ballBearings = true;
            design.boost = 0.0;
            design.sequentialSpeed = 0.0;
            return design;
        }

        public ForcedInductionDesign Copy() => (ForcedInductionDesign)MemberwiseClone();
    }

    /// <summary>An exhaust system: primaries into a collector and out through the outlet, and the impulse response it sounds through.</summary>
    [Serializable]
    public sealed class ExhaustDesign
    {
        public double length = 100.0 * ScriptUnits.L / ScriptFunctions.CircleArea(2.0 * ScriptUnits.inch);
        public double collectorCrossSectionArea = ScriptFunctions.CircleArea(2.0 * ScriptUnits.inch);
        /// <summary>Flows in cfm, as carburettors are rated.</summary>
        public double outletFlowRate = 1000;
        public double primaryFlowRate = 100;
        public double primaryTubeLength = 10.0 * ScriptUnits.inch;
        public double velocityDecay = 1.0;
        public double audioVolume = 1.0;
        /// <summary>The impulse response, by the file engine descriptions name it with (an impulse response asset's source file).</summary>
        public string impulseResponse = ExhaustFilters.Stock.response.file;
        public double impulseResponseVolume = ExhaustFilters.Stock.response.volume;
    }

    /// <summary>Spark timing against engine speed, and the rev limiter.</summary>
    [Serializable]
    public sealed class IgnitionDesign
    {
        /// <summary>Advance (°) against engine speed (rpm), smoothed over <see cref="timingSmoothing"/> rpm.</summary>
        public TimingSample[] timingCurve = { new TimingSample(0, 12), new TimingSample(1000, 12), new TimingSample(3000, 30), new TimingSample(5000, 36) };
        public double timingSmoothing = 1000;
        public double revLimit = 6500;
        /// <summary>How long the limiter cuts the spark for (s).</summary>
        public double limiterDuration = 0.5;
    }

    /// <summary>A point of the timing curve: engine speed (rpm) and advance (°).</summary>
    [Serializable]
    public struct TimingSample
    {
        public double speed;
        public double advance;

        public TimingSample(double speed, double advance)
        {
            this.speed = speed;
            this.advance = advance;
        }
    }

    /// <summary>The fuel and how it burns; gasoline by default.</summary>
    [Serializable]
    public sealed class FuelDesign
    {
        public double molecularMass = 100 * ScriptUnits.g;
        public double energyDensity = 48.1 * ScriptUnits.kJ / ScriptUnits.g;
        public double density = 0.755 * ScriptUnits.kg / ScriptUnits.L;
        public double molecularAfr = 25 / 2.0;
        public double maxBurningEfficiency = 0.8;
        public double burningEfficiencyRandomness = 0.5;
        public double lowEfficiencyAttenuation = 0.6;
        public double maxTurbulenceEffect = 2.0;
        public double maxDilutionEffect = 10.0;
        /// <summary>Flame speed against turbulence, smoothed over <see cref="turbulenceSmoothing"/>; empty for gasoline's default.</summary>
        public CurveSample[] turbulenceToFlameSpeedRatio = Array.Empty<CurveSample>();
        public double turbulenceSmoothing = 5.0;
    }

    /// <summary>A point of a curve.</summary>
    [Serializable]
    public struct CurveSample
    {
        public double x;
        public double y;

        public CurveSample(double x, double y)
        {
            this.x = x;
            this.y = y;
        }
    }

    /// <summary>The vehicle the engine drives (SI units).</summary>
    [Serializable]
    public sealed class VehicleDesign
    {
        public double mass = 1597;
        public double dragCoefficient = 0.25;
        public double crossSectionArea = (6.0 * ScriptUnits.foot) * (6.0 * ScriptUnits.foot);
        public double diffRatio = 3.42;
        public double tireRadius = 10 * ScriptUnits.inch;
        public double rollingResistance = 2000.0;
    }

    /// <summary>The gearbox: its ratios and clutch.</summary>
    [Serializable]
    public sealed class TransmissionDesign
    {
        public double[] gearRatios = { 2.97, 2.07, 1.43, 1.00, 0.84, 0.56 };
        public double maxClutchTorque = 1000.0 * ScriptUnits.lb_ft;
    }
}
