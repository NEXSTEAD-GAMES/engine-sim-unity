// The engine description format written by the C++ reference the port is checked against
// (engine-sim built headless; format "engine-sim-reference/engine" version 2): an engine, vehicle and transmission as
// the arguments of the calls that construct them. Field names are the JSON keys.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;

namespace Nexstead.EngineBench.Simulation
{
    [Serializable]
    public sealed class EngineDescription
    {
        /// <summary>A copy sharing every field's object: change a field of the copy, not of what it shares.</summary>
        public EngineDescription ShallowCopy() => (EngineDescription)MemberwiseClone();

        public const string FormatName = "engine-sim-reference/engine";
        public const int FormatVersion = 2;

        public string format;
        public int version;
        public FunctionDescription[] functions;
        public ImpulseResponseDescription[] impulseResponses;
        public EngineParametersDescription engine;
        public VehicleDescription vehicle;
        public TransmissionDescription transmission;
        public SourceDescription source;
    }

    [Serializable]
    public sealed class SourceDescription
    {
        public string script;
        public string engineNode;
        /// <summary>For a reference variant: the library description it was derived from.</summary>
        public string variantOf;
    }

    /// <summary>A <see cref="Function"/>: its filter radius, scales and samples (sorted by x).</summary>
    [Serializable]
    public sealed class FunctionDescription
    {
        public double filterRadius;
        public double inputScale;
        public double outputScale;
        public double[] x;
        public double[] y;
    }

    /// <summary>An impulse response file (relative to the upstream checkout) and its volume.</summary>
    [Serializable]
    public sealed class ImpulseResponseDescription
    {
        public string file;
        public double volume;
    }

    [Serializable]
    public sealed class EngineParametersDescription
    {
        /// <summary>A copy sharing every field's object: change a field of the copy, not of what it shares.</summary>
        public EngineParametersDescription ShallowCopy() => (EngineParametersDescription)MemberwiseClone();

        public string name;
        public double starterTorque;
        public double starterSpeed;
        public double redline;
        public double dynoMinSpeed;
        public double dynoMaxSpeed;
        public double dynoHoldStep;
        public double simulationFrequency;
        public double highFrequencyGain;
        public double noise;
        public double jitter;
        public ThrottleDescription throttle;
        public CrankshaftDescription[] crankshafts;
        public CylinderBankDescription[] cylinderBanks;
        public CylinderHeadDescription[] cylinderHeads;
        public PistonDescription[] pistons;
        public ConnectingRodDescription[] connectingRods;
        public IntakeDescription[] intakes;
        public ExhaustSystemDescription[] exhaustSystems;
        /// <summary>The turbocharger or supercharger; none when absent or its type is empty (<see cref="HasForcedInduction"/>).</summary>
        public ForcedInductionDescription forcedInduction;
        public IgnitionModuleDescription ignitionModule;
        public FuelDescription fuel;
        public CombustionChamberDescription combustionChamber;

        public bool HasForcedInduction => forcedInduction != null && !string.IsNullOrEmpty(forcedInduction.type);
    }

    [Serializable]
    public sealed class ThrottleDescription
    {
        public const string DirectLinkage = "directLinkage";
        public const string GovernorType = "governor";

        public string type;
        public double gamma;

        // Governor only
        public double minSpeed;
        public double maxSpeed;
        public double minVelocity;
        public double maxVelocity;
        public double k_s;
        public double k_d;
    }

    [Serializable]
    public sealed class CrankshaftDescription
    {
        public double mass;
        public double flywheelMass;
        public double momentOfInertia;
        public double @throw;
        public double positionX;
        public double positionY;
        public double tdc;
        public double frictionTorque;
        public double[] rodJournalAngles;
    }

    [Serializable]
    public sealed class CylinderBankDescription
    {
        public double angle;
        public double bore;
        public double deckHeight;
        public double positionX;
        public double positionY;
        public double displayDepth;
        public int cylinderCount;
    }

    [Serializable]
    public sealed class CylinderHeadDescription
    {
        /// <summary>A copy sharing every field's object: change a field of the copy, not of what it shares.</summary>
        public CylinderHeadDescription ShallowCopy() => (CylinderHeadDescription)MemberwiseClone();

        public double combustionChamberVolume;
        public double intakeRunnerVolume;
        public double intakeRunnerCrossSectionArea;
        public double exhaustRunnerVolume;
        public double exhaustRunnerCrossSectionArea;
        public bool flipDisplay;
        public int intakePortFlow;
        public int exhaustPortFlow;
        public ValvetrainDescription valvetrain;
        public HeadCylinderDescription[] cylinders;
    }

    [Serializable]
    public sealed class HeadCylinderDescription
    {
        /// <summary>A copy sharing every field's object: change a field of the copy, not of what it shares.</summary>
        public HeadCylinderDescription ShallowCopy() => (HeadCylinderDescription)MemberwiseClone();

        public int intake;
        public int exhaustSystem;
        public double soundAttenuation;
        public double headerPrimaryLength;
    }

    [Serializable]
    public sealed class ValvetrainDescription
    {
        public const string Standard = "standard";
        public const string VariableLift = "variableLift";

        public string type;
        public CamshaftDescription intakeCamshaft;
        public CamshaftDescription exhaustCamshaft;

        // Variable lift only
        public double minRpm;
        public double minSpeed;
        public double manifoldVacuum;
        public double minThrottlePosition;
        public CamshaftDescription highLiftIntakeCamshaft;
        public CamshaftDescription highLiftExhaustCamshaft;
    }

    [Serializable]
    public sealed class CamshaftDescription
    {
        public double advance;
        public double baseRadius;
        /// <summary>Camshaft turns per crankshaft turn: 0.5 for a four-stroke.</summary>
        public double reductionRatio;
        public int lobeProfile;
        /// <summary>The crank angles given to <c>SetLobeCenterline</c>.</summary>
        public double[] lobeCenterlines;
    }

    [Serializable]
    public sealed class PistonDescription
    {
        public double blowby;
        public double compressionHeight;
        public double wristPinPosition;
        public double displacement;
        public double mass;
    }

    [Serializable]
    public sealed class ConnectingRodDescription
    {
        public double mass;
        public double momentOfInertia;
        public double centerOfMass;
        public double length;
        /// <summary>The crankshaft, and the journal on it, the rod runs on.</summary>
        public int crankshaft;
        public int journal;
    }

    [Serializable]
    public sealed class IntakeDescription
    {
        public double plenumVolume;
        public double plenumCrossSectionArea;
        public double inputFlowK;
        public double idleFlowK;
        public double runnerFlowRate;
        public double molecularAfr;
        public double idleThrottlePlatePosition;
        public double runnerLength;
        public double velocityDecay;
        /// <summary>The air the intake draws from (Pa, K).</summary>
        public double atmospherePressure;
        public double atmosphereTemperature;
    }

    [Serializable]
    public sealed class ExhaustSystemDescription
    {
        /// <summary>A copy sharing every field's object: change a field of the copy, not of what it shares.</summary>
        public ExhaustSystemDescription ShallowCopy() => (ExhaustSystemDescription)MemberwiseClone();

        public double length;
        public double collectorCrossSectionArea;
        public double outletFlowRate;
        public double primaryTubeLength;
        public double primaryFlowRate;
        public double velocityDecay;
        public double audioVolume;
        public int impulseResponse;
    }

    /// <summary>
    /// A <see cref="ForcedInduction"/>: the parameters it is initialized with, as the script gives them (the community fork's
    /// <c>turbocharger()</c> and <c>supercharger()</c>). Values are SI: m, kg/s, rad/s, Pa (gauge), m³, kg m².
    /// </summary>
    [Serializable]
    public sealed class ForcedInductionDescription
    {
        /// <summary>A copy: every field is a value.</summary>
        public ForcedInductionDescription Copy() => (ForcedInductionDescription)MemberwiseClone();

        public const string Turbocharger = "turbocharger";
        public const string Roots = "roots";
        public const string TwinScrew = "twinScrew";
        public const string Centrifugal = "centrifugal";

        public const string Single = "single";
        public const string Twin = "twin";
        public const string Sequential = "sequential";

        public const string Atmospheric = "atmospheric";
        public const string Recirculating = "recirculating";
        public const string NoBlowoff = "none";

        public const string Electronic = "electronic";
        public const string Spring = "spring";

        public string type;
        public string arrangement;
        public double compressorDiameter;
        public double compressorFlow;
        public double compressorEfficiency;
        /// <summary>Shaft speed limit; 0: from the compressor wheel's tip speed limit.</summary>
        public double maxSpeed;
        public double turbineAr;
        public double turbineEfficiency;
        public double inertia;
        public bool ballBearings;
        /// <summary>The wastegate's set point (gauge).</summary>
        public double boost;
        public bool blowoffValve;
        public string blowoffType;
        public string boostControl;
        public bool externalWastegate;
        public bool twinScroll;
        public double whistlePitch;
        /// <summary>Compressor speed / crank speed (superchargers).</summary>
        public double driveRatio;
        /// <summary>Air moved per rotor revolution (Roots and twin-screw).</summary>
        public double displacement;
        public bool bypassValve;
        public double intercoolerEffectiveness;
        public double chargeVolume;
        /// <summary>Engine speed at which a sequential arrangement's second turbo joins.</summary>
        public double sequentialSpeed;
    }

    [Serializable]
    public sealed class IgnitionModuleDescription
    {
        public int timingCurve;
        public double revLimit;
        public double limiterDuration;
        /// <summary>Sparks per plug per 720° cycle: 1 for a four-stroke, 2 for a two-stroke.</summary>
        public int sparksPerCycle;
        public SparkPlugDescription[] plugs;
    }

    [Serializable]
    public sealed class SparkPlugDescription
    {
        public double angle;
        public bool enabled;
    }

    [Serializable]
    public sealed class FuelDescription
    {
        public double molecularMass;
        public double energyDensity;
        public double density;
        public double molecularAfr;
        public double burningEfficiencyRandomness;
        public double lowEfficiencyAttenuation;
        public double maxBurningEfficiency;
        public double maxTurbulenceEffect;
        public double maxDilutionEffect;
        public int turbulenceToFlameSpeedRatio;
    }

    [Serializable]
    public sealed class CombustionChamberDescription
    {
        public double crankcasePressure;
        public int meanPistonSpeedToTurbulence;
    }

    [Serializable]
    public sealed class VehicleDescription
    {
        public double mass;
        public double dragCoefficient;
        public double crossSectionArea;
        public double diffRatio;
        public double tireRadius;
        public double rollingResistance;
    }

    [Serializable]
    public sealed class TransmissionDescription
    {
        public double[] gearRatios;
        public double maxClutchTorque;
    }
}
