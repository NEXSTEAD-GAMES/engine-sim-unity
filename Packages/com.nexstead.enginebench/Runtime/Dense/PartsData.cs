// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense core's engine parts: what the per-step code reads and writes of everything in the
// managed Engine, as unmanaged structs. Each struct mirrors its managed class's fields under the
// same names (references become indices); PartsOps (PartsOps.cs, PartsGas.cs) mirrors its methods.
// Display-only bookkeeping that nothing in a step reads (intake flowRate and totalFuelInjected,
// chamber peakTemperature, nBurntFuel, litLastFrame, the flame event's lit_n, total_n and
// percentageLit) is not kept. See DenseEngine.cs for the rules.
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public unsafe struct DenseParts
    {
        /// <summary>As <c>Engine.GetCrankshaft(i)</c>; 0 is the output crankshaft.</summary>
        public int crankshaftCount;
        public CrankshaftData* crankshafts;

        /// <summary>As <c>Engine.GetCylinderBank(i)</c>.</summary>
        public int bankCount;
        public CylinderBankData* banks;

        /// <summary>As <c>Engine.GetHead(i)</c>, one per bank.</summary>
        public CylinderHeadData* heads;

        /// <summary>As <c>Engine.GetPiston(i)</c> and <c>Engine.GetChamber(i)</c>: a cylinder's global index.</summary>
        public int cylinderCount;
        public PistonData* pistons;
        public ChamberData* chambers;

        /// <summary>As <c>Engine.GetIntake(i)</c>.</summary>
        public int intakeCount;
        public IntakeData* intakes;

        /// <summary>As <c>Engine.GetExhaustSystem(i)</c> (so position equals the managed <c>GetIndex()</c>).</summary>
        public int exhaustCount;
        public ExhaustData* exhausts;

        /// <summary>Every camshaft the valvetrains use, in the order first met (heads by index, intake before exhaust).</summary>
        public int camshaftCount;
        public CamshaftData* camshafts;

        /// <summary>Every valvetrain the heads use, in the order first met.</summary>
        public int valvetrainCount;
        public ValvetrainData* valvetrains;

        /// <summary>Every <see cref="Function"/> a part samples per step, each once, in the order first met.</summary>
        public int functionCount;
        public FunctionData* functions;

        /// <summary>
        /// Every gas system. Chamber c owns 3c (its cylinder), 3c + 1 (intake runner and manifold) and 3c + 2 (exhaust
        /// runner and primary); intake i owns G + 2i (its plenum) and G + 2i + 1 (its atmosphere), G = 3 × cylinderCount;
        /// exhaust x owns H + 2x (its collector) and H + 2x + 1 (its atmosphere), H = G + 2 × intakeCount; a forced induction
        /// owns the last, H + 2 × exhaustCount (its charge air). Each owner also stores its indices.
        /// </summary>
        public int gasCount;
        public GasData* gases;

        /// <summary>The turbocharger or supercharger (<c>Engine.GetForcedInduction()</c>); null if none.</summary>
        public ForcedInductionData* forcedInduction;

        public FuelData fuel;
        public IgnitionData ignition;
        public ThrottleData throttle;

        /// <summary>Engine.throttleValue: the plate position last set through the throttle.</summary>
        public double throttleValue;
    }

    /// <summary>Mirror of <see cref="GasSystem.Mix"/>.</summary>
    public struct MixData
    {
        public double p_fuel;
        public double p_inert;
        public double p_o2;
    }

    /// <summary>Mirror of <see cref="GasSystem"/>, its State inline (momentum[0], momentum[1] as momentum_x, momentum_y).</summary>
    public struct GasData
    {
        public double n_mol;
        public double E_k;
        public double V;
        public double momentum_x;
        public double momentum_y;
        public MixData mix;

        public int degreesOfFreedom;

        public double chokedFlowLimit;
        public double chokedFlowFactorCached;

        public double width;
        public double height;
        public double dx;
        public double dy;
    }

    /// <summary>Mirror of <see cref="GasSystem.FlowParameters"/>, with pointers for the two systems.</summary>
    public unsafe struct FlowParams
    {
        public double k_flow;
        public double dt;
        public double direction_x, direction_y;
        public double crossSectionArea_0, crossSectionArea_1;
        public GasData* system_0, system_1;
    }

    /// <summary>Mirror of <see cref="Function"/>: what <c>SampleTriangle</c> reads (the samples, <c>size</c> of them).</summary>
    public unsafe struct FunctionData
    {
        public double* x;
        public double* y;
        public int size;
        public double filterRadius;
        public double inputScale;
        public double outputScale;
    }

    /// <summary>Mirror of <see cref="Crankshaft"/>.</summary>
    public struct CrankshaftData
    {
        /// <summary>Its rigid body (<see cref="DenseSolver.bodies"/>).</summary>
        public int body;
        public double tdc;
    }

    /// <summary>Mirror of <see cref="CylinderBank"/>.</summary>
    public struct CylinderBankData
    {
        public double bore;
        public double deckHeight;
        public int index;
        public double dx;
        public double dy;
        public double x;
        public double y;
    }

    /// <summary>Mirror of <see cref="Piston"/>.</summary>
    public struct PistonData
    {
        /// <summary>Its rigid body (<see cref="DenseSolver.bodies"/>).</summary>
        public int body;
        public int bank;
        /// <summary>Its cylinder wall: the managed LineConstraint's index into <see cref="DenseSolver.constraints"/>.</summary>
        public int cylinderConstraint;
        /// <summary>Its index within its bank (and head).</summary>
        public int cylinderIndex;
        /// <summary>Its connecting rod's rigid body: read by the capture only.</summary>
        public int rodBody;
        public double compressionHeight;
        public double displacement;
        public double blowby_k;
    }

    /// <summary>Mirror of <see cref="CylinderHead.Cylinder"/>.</summary>
    public struct HeadCylinderData
    {
        public int exhaustSystem;
        public int intake;
        public double soundAttenuation;
        public double headerPrimaryLength;
    }

    /// <summary>Mirror of <see cref="CylinderHead"/>.</summary>
    public unsafe struct CylinderHeadData
    {
        /// <summary>One per cylinder of its bank.</summary>
        public HeadCylinderData* cylinders;
        public int bank;
        public int valvetrain;
        public int exhaustPortFlow;
        public int intakePortFlow;
        public double intakeRunnerCrossSectionArea;
        public double exhaustRunnerCrossSectionArea;
        public double combustionChamberVolume;
    }

    /// <summary>Mirror of <see cref="Camshaft"/>.</summary>
    public unsafe struct CamshaftData
    {
        public int crankshaft;
        public int lobeProfile;
        /// <summary>As the managed lobeAngles (<c>GetLobeCenterline</c>), <c>lobes</c> of them.</summary>
        public double* lobeAngles;
        public int lobes;
        public double advance;
        public double reductionRatio;
    }

    public static class ValvetrainKind
    {
        public const int Standard = 0;
        public const int VariableLift = 1;
    }

    /// <summary>Mirror of <see cref="StandardValvetrain"/> and <see cref="VariableLiftValvetrain"/> (a standard one leaves the variable-lift fields unused).</summary>
    public unsafe struct ValvetrainData
    {
        public int kind;
        public int intakeCamshaft;
        public int exhaustCamshaft;
        public int highLiftIntakeCamshaft;
        public int highLiftExhaustCamshaft;
        public double minRpm;
        public double manifoldVacuum;
        public double minThrottlePosition;
        /// <summary>Variable lift: whether each cylinder's intake and exhaust valve follows its high-lift cam (1) or not (0).</summary>
        public byte* intakeOnHighLift;
        public byte* exhaustOnHighLift;
    }

    /// <summary>Mirror of <see cref="CombustionChamber.FlameEvent"/>.</summary>
    public struct FlameEventData
    {
        public double efficiency;
        public double flameSpeed;
        public double lastVolume;
        public double travel_x;
        public double travel_y;
        public MixData globalMix;
    }

    /// <summary>Mirror of <see cref="CombustionChamber.FrictionModelParams"/>.</summary>
    public struct FrictionModelData
    {
        public double frictionCoeff;
        public double breakawayFriction;
        public double breakawayFrictionVelocity;
        public double viscousFrictionCoefficient;
    }

    /// <summary>Mirror of <see cref="CombustionChamber"/>.</summary>
    public unsafe struct ChamberData
    {
        public const int StateSamples = 256;

        public int piston;
        public int head;

        /// <summary>Gas system indices (see <see cref="DenseParts.gases"/>).</summary>
        public int system;
        public int intakeRunnerAndManifold;
        public int exhaustRunnerAndPrimary;

        public int meanPistonSpeedToTurbulence;

        public FlameEventData flameEvent;
        public bool lit;
        /// <summary><see cref="CombustionChamber.ignitions"/>: display bookkeeping, read by no step.</summary>
        public int ignitions;
        public FrictionModelData frictionModel;

        public double intakeFlowRate;
        public double exhaustFlowRate;
        public double manifoldToRunnerFlowRate;
        public double primaryToCollectorFlowRate;
        public double cylinderCrossSectionSurfaceArea;
        public double lastTimestepTotalExhaustFlow;
        public double lastTimestepTotalIntakeFlow;
        public double exhaustFlow;
        public double crankcasePressure;
        /// <summary><see cref="StateSamples"/> each.</summary>
        public double* pressure;
        public double* pistonSpeed;

        /// <summary>Its <see cref="DeterministicRandom"/>'s state.</summary>
        public ulong random;
    }

    /// <summary>Mirror of <see cref="Intake"/>.</summary>
    public struct IntakeData
    {
        public int system;
        public int atmosphere;

        public double throttle;
        public double flow;

        public double crossSectionArea;
        public double inputFlowK;
        public double idleFlowK;
        public double molecularAfr;
        public double idleThrottlePlatePosition;
        public double velocityDecay;
        public double atmospherePressure;
        public double atmosphereTemperature;
        /// <summary>1: the throttle draws from the forced induction's charge air.</summary>
        public int boosted;
    }

    /// <summary>Mirror of <see cref="ExhaustSystem"/>.</summary>
    public struct ExhaustData
    {
        public int system;
        public int atmosphere;

        public double length;
        public double collectorCrossSectionArea;
        public double outletFlowRate;
        public double audioVolume;
        public double velocityDecay;
        public int index;
        public double flow;
        /// <summary>1: the outlet flows through the forced induction's turbines.</summary>
        public int turbine;
    }

    /// <summary>Mirror of <see cref="Fuel"/>.</summary>
    public struct FuelData
    {
        public double molecularMass;
        public double energyDensity;
        public double molecularAfr;
        public double maxBurningEfficiency;
        public double burningEfficiencyRandomness;
        public double lowEfficiencyAttenuation;
        public double maxTurbulenceEffect;
        public double maxDilutionEffect;
        public int turbulenceToFlameSpeedRatio;
    }

    /// <summary>Mirror of <see cref="IgnitionModule.SparkPlug"/>.</summary>
    public struct SparkPlugData
    {
        public double angle;
        public bool ignitionEvent;
        public bool enabled;
    }

    /// <summary>Mirror of <see cref="IgnitionModule"/>.</summary>
    public unsafe struct IgnitionData
    {
        public bool enabled;
        public int timingCurve;
        /// <summary>One per cylinder (global index).</summary>
        public SparkPlugData* plugs;
        public int crankshaft;
        public int cylinderCount;
        public double lastCrankshaftAngle;
        public double revLimit;
        public double revLimitTimer;
        public double limiterDuration;
        public int sparksPerCycle;
    }

    public static class ThrottleKind
    {
        /// <summary>The base <see cref="Throttle"/>: keeps the speed control, sets nothing.</summary>
        public const int Base = 0;
        public const int DirectLinkage = 1;
        public const int Governor = 2;
    }

    /// <summary>Mirror of <see cref="Throttle"/>, <see cref="DirectThrottleLinkage"/> and <see cref="Governor"/>.</summary>
    public struct ThrottleData
    {
        public int kind;
        public double speedControl;

        // Direct linkage and governor
        public double gamma;

        // Direct linkage
        public double throttlePosition;

        // Governor
        public double minSpeed;
        public double maxSpeed;
        public double minVelocity;
        public double maxVelocity;
        public double k_s;
        public double k_d;
        public double targetSpeed;
        public double currentThrottle;
        public double velocity;
    }
}
