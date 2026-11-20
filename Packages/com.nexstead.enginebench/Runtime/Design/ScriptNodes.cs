// Ported from engine-sim (https://github.com/ange-yaghi/engine-sim): es/objects/objects.mr, es/actions/actions.mr,
// es/part-library/parts/heads.mr (add_flow_sample) and the nodes in scripting/include. MIT licence,
// Copyright (c) 2022 Ange Yaghi; see Third Party Notices.md.
// The forced induction nodes (turbocharger, supercharger, add_forced_induction) are the community fork's
// (engine-sim-community-ultimate cf96419, kyan0s-git's model), MIT; see Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Design
{
    // engine-sim's script nodes: each class is a node of its script library, with that node's inputs and defaults, so
    // an engine written with them builds what the same engine script builds. EngineNode.Describe turns them into an
    // EngineDescription. Optional arguments left out take the script's default; nullable ones default to a value the
    // script derives from other inputs.

    /// <summary>A sampled function (function): samples added in any order, a filter radius for smoothing.</summary>
    public sealed class FunctionNode
    {
        public double filterRadius;
        readonly List<(double x, double y)> samples = new List<(double, double)>();

        public FunctionNode(double filterRadius = 1.0) => this.filterRadius = filterRadius;

        public IReadOnlyList<(double x, double y)> Samples => samples;

        public FunctionNode AddSample(double x, double y)
        {
            samples.Add((x, y));
            return this;
        }

        /// <summary>A head's flow at a valve lift (add_flow_sample): <paramref name="lift"/> in thou, <paramref name="flow"/> in cfm at 28 inH2O.</summary>
        public FunctionNode AddFlowSample(double lift, double flow) => AddSample(lift * ScriptUnits.thou, ScriptFunctions.K_28inH2O(flow));
    }

    /// <summary>A crank pin (rod_journal), at an angle on its crankshaft.</summary>
    public sealed class RodJournalNode
    {
        public readonly double angle;

        public RodJournalNode(double angle = 0) => this.angle = angle;
    }

    /// <summary>A crankshaft (crankshaft) and its rod journals, in the order added.</summary>
    public sealed class CrankshaftNode
    {
        public readonly double @throw, flywheelMass, mass, frictionTorque, momentOfInertia, positionX, positionY, tdc;
        public readonly List<RodJournalNode> rodJournals = new List<RodJournalNode>();

        public CrankshaftNode(double @throw = 0.0, double flywheelMass = 0.0, double mass = 0.0, double frictionTorque = 0.0,
            double momentOfInertia = 0.0, double positionX = 0.0, double positionY = 0.0, double tdc = 0.0)
        {
            this.@throw = @throw;
            this.flywheelMass = flywheelMass;
            this.mass = mass;
            this.frictionTorque = frictionTorque;
            this.momentOfInertia = momentOfInertia;
            this.positionX = positionX;
            this.positionY = positionY;
            this.tdc = tdc;
        }

        public CrankshaftNode AddRodJournal(RodJournalNode journal)
        {
            rodJournals.Add(journal);
            return this;
        }
    }

    /// <summary>A piston's values (piston_parameters).</summary>
    public sealed class PistonParameters
    {
        public readonly double blowby, compressionHeight, wristPinPosition, displacement, mass;

        public PistonParameters(double blowby = 0.0, double compressionHeight = 0.0, double wristPinPosition = 0.0, double displacement = 0.0,
            double mass = 0.0)
        {
            this.blowby = blowby;
            this.compressionHeight = compressionHeight;
            this.wristPinPosition = wristPinPosition;
            this.displacement = displacement;
            this.mass = mass;
        }
    }

    /// <summary>A piston (piston): its parameters, with its own blow-by.</summary>
    public sealed class PistonNode
    {
        public readonly PistonParameters parameters;
        public readonly double blowby;

        public PistonNode(PistonParameters parameters = null, double? blowby = null)
        {
            this.parameters = parameters ?? new PistonParameters();
            this.blowby = blowby ?? this.parameters.blowby;
        }
    }

    /// <summary>A connecting rod's values (connecting_rod_parameters).</summary>
    public sealed class ConnectingRodParameters
    {
        public readonly double mass, momentOfInertia, centerOfMass, length;

        public ConnectingRodParameters(double mass = 0.0, double momentOfInertia = 0.0, double centerOfMass = 0.0, double length = 0.0)
        {
            this.mass = mass;
            this.momentOfInertia = momentOfInertia;
            this.centerOfMass = centerOfMass;
            this.length = length;
        }
    }

    /// <summary>A connecting rod (connecting_rod).</summary>
    public sealed class ConnectingRodNode
    {
        public readonly ConnectingRodParameters parameters;

        public ConnectingRodNode(ConnectingRodParameters parameters = null) => this.parameters = parameters ?? new ConnectingRodParameters();
    }

    /// <summary>A bank's values (cylinder_bank_parameters).</summary>
    public sealed class CylinderBankParameters
    {
        public readonly double angle, bore, deckHeight, positionX, positionY, displayDepth;

        public CylinderBankParameters(double angle = 0.0, double bore = 0.0, double deckHeight = 0.0, double positionX = 0.0, double positionY = 0.0,
            double displayDepth = 0.5)
        {
            this.angle = angle;
            this.bore = bore;
            this.deckHeight = deckHeight;
            this.positionX = positionX;
            this.positionY = positionY;
            this.displayDepth = displayDepth;
        }
    }

    /// <summary>A cylinder bank (cylinder_bank): its cylinders in the order added, and its head.</summary>
    public sealed class CylinderBankNode
    {
        public readonly double angle, bore, deckHeight, positionX, positionY, displayDepth;
        public readonly List<Cylinder> cylinders = new List<Cylinder>();
        public CylinderHeadNode head;

        /// <summary>A cylinder (add_cylinder) and what it connects to.</summary>
        public sealed class Cylinder
        {
            public PistonNode piston;
            public ConnectingRodNode connectingRod;
            public RodJournalNode rodJournal;
            public IntakeNode intake;
            public ExhaustSystemNode exhaustSystem;
            public IgnitionWireNode ignitionWire;
            public double soundAttenuation;
            public double primaryLength;
        }

        public CylinderBankNode(CylinderBankParameters parameters = null, double? angle = null, double? bore = null, double? deckHeight = null,
            double? positionX = null, double? positionY = null, double? displayDepth = null)
        {
            parameters ??= new CylinderBankParameters();
            this.angle = angle ?? parameters.angle;
            this.bore = bore ?? parameters.bore;
            this.deckHeight = deckHeight ?? parameters.deckHeight;
            this.positionX = positionX ?? parameters.positionX;
            this.positionY = positionY ?? parameters.positionY;
            this.displayDepth = displayDepth ?? parameters.displayDepth;
        }

        public CylinderBankNode AddCylinder(PistonNode piston, ConnectingRodNode connectingRod, RodJournalNode rodJournal, IntakeNode intake,
            ExhaustSystemNode exhaustSystem, IgnitionWireNode ignitionWire, double soundAttenuation = 1.0, double primaryLength = 0.0)
        {
            cylinders.Add(new Cylinder
            {
                piston = piston, connectingRod = connectingRod, rodJournal = rodJournal, intake = intake, exhaustSystem = exhaustSystem,
                ignitionWire = ignitionWire, soundAttenuation = soundAttenuation, primaryLength = primaryLength,
            });
            return this;
        }

        public CylinderBankNode SetCylinderHead(CylinderHeadNode cylinderHead)
        {
            head = cylinderHead;
            return this;
        }
    }

    /// <summary>A camshaft's values (camshaft_parameters).</summary>
    public sealed class CamshaftParameters
    {
        public readonly double advance, baseRadius, reductionRatio;
        public readonly FunctionNode lobeProfile;

        public CamshaftParameters(double advance = 0.0, double baseRadius = 0.0, FunctionNode lobeProfile = null, double reductionRatio = 0.5)
        {
            this.advance = advance;
            this.baseRadius = baseRadius;
            this.lobeProfile = lobeProfile ?? new FunctionNode();
            this.reductionRatio = reductionRatio;
        }
    }

    /// <summary>A camshaft (camshaft): its lobes' centrelines in the order added, in crank degrees as the scripts give them.</summary>
    public sealed class CamshaftNode
    {
        public readonly double advance, baseRadius, reductionRatio;
        public readonly FunctionNode lobeProfile;
        public readonly List<double> lobes = new List<double>();

        /// <param name="reductionRatio">Camshaft turns per crankshaft turn: 0.5 for a four-stroke, 1 for a two-stroke.</param>
        public CamshaftNode(CamshaftParameters parameters = null, double? advance = null, double? baseRadius = null, FunctionNode lobeProfile = null,
            double? reductionRatio = null)
        {
            parameters ??= new CamshaftParameters();
            this.advance = advance ?? parameters.advance;
            this.baseRadius = baseRadius ?? parameters.baseRadius;
            this.lobeProfile = lobeProfile ?? parameters.lobeProfile;
            this.reductionRatio = reductionRatio ?? parameters.reductionRatio;
        }

        public CamshaftNode AddLobe(double centerline)
        {
            lobes.Add(centerline);
            return this;
        }
    }

    /// <summary>A valvetrain: one intake and one exhaust camshaft (standard_valvetrain), or two of each (variable lift).</summary>
    public abstract class ValvetrainNode
    {
    }

    /// <summary>One intake and one exhaust camshaft (standard_valvetrain).</summary>
    public sealed class StandardValvetrainNode : ValvetrainNode
    {
        public readonly CamshaftNode intakeCamshaft, exhaustCamshaft;

        public StandardValvetrainNode(CamshaftNode intakeCamshaft, CamshaftNode exhaustCamshaft)
        {
            this.intakeCamshaft = intakeCamshaft;
            this.exhaustCamshaft = exhaustCamshaft;
        }
    }

    /// <summary>
    /// Two cam profiles per valve, switched to the high-lift pair over a speed, a road speed, a manifold pressure and a
    /// throttle position (upstream's two-profile valvetrain).
    /// </summary>
    public sealed class VariableLiftValvetrainNode : ValvetrainNode
    {
        public readonly CamshaftNode highLiftIntakeCamshaft, highLiftExhaustCamshaft, intakeCamshaft, exhaustCamshaft;
        public readonly double minRpm, minSpeed, manifoldVacuum, minThrottlePosition;

        public VariableLiftValvetrainNode(CamshaftNode highLiftIntakeCamshaft, CamshaftNode highLiftExhaustCamshaft, CamshaftNode intakeCamshaft,
            CamshaftNode exhaustCamshaft, double minRpm = 5800 * ScriptUnits.rpm, double minSpeed = 10 * ScriptUnits.mph,
            double manifoldVacuum = 1.0 * ScriptUnits.atm - 5.0 * ScriptUnits.inHg, double minThrottlePosition = 0.3)
        {
            this.highLiftIntakeCamshaft = highLiftIntakeCamshaft;
            this.highLiftExhaustCamshaft = highLiftExhaustCamshaft;
            this.intakeCamshaft = intakeCamshaft;
            this.exhaustCamshaft = exhaustCamshaft;
            this.minRpm = minRpm;
            this.minSpeed = minSpeed;
            this.manifoldVacuum = manifoldVacuum;
            this.minThrottlePosition = minThrottlePosition;
        }
    }

    /// <summary>A cylinder head (generic_cylinder_head; cylinder_head builds a standard valvetrain from two camshafts).</summary>
    public sealed class CylinderHeadNode
    {
        public const double DefaultChamberVolume = 118.0 * ScriptUnits.cc;
        public const double DefaultRunnerVolume = 300.0 * ScriptUnits.cc;

        public readonly FunctionNode intakePortFlow, exhaustPortFlow;
        public readonly ValvetrainNode valvetrain;
        public readonly double chamberVolume, intakeRunnerVolume, intakeRunnerCrossSectionArea, exhaustRunnerVolume, exhaustRunnerCrossSectionArea;
        public readonly bool flipDisplay;

        public CylinderHeadNode(ValvetrainNode valvetrain, FunctionNode intakePortFlow = null, FunctionNode exhaustPortFlow = null,
            double chamberVolume = DefaultChamberVolume, double intakeRunnerVolume = DefaultRunnerVolume, double? intakeRunnerCrossSectionArea = null,
            double exhaustRunnerVolume = DefaultRunnerVolume, double? exhaustRunnerCrossSectionArea = null, bool flipDisplay = false)
        {
            this.valvetrain = valvetrain;
            this.intakePortFlow = intakePortFlow ?? new FunctionNode();
            this.exhaustPortFlow = exhaustPortFlow ?? new FunctionNode();
            this.chamberVolume = chamberVolume;
            this.intakeRunnerVolume = intakeRunnerVolume;
            this.intakeRunnerCrossSectionArea = intakeRunnerCrossSectionArea ?? ScriptFunctions.CircleArea(0.75 * ScriptUnits.inch);
            this.exhaustRunnerVolume = exhaustRunnerVolume;
            this.exhaustRunnerCrossSectionArea = exhaustRunnerCrossSectionArea ?? ScriptFunctions.CircleArea(0.85 * ScriptUnits.inch);
            this.flipDisplay = flipDisplay;
        }

        /// <summary>A head with a standard valvetrain from two camshafts (cylinder_head).</summary>
        public static CylinderHeadNode Standard(CamshaftNode intakeCamshaft, CamshaftNode exhaustCamshaft, FunctionNode intakePortFlow = null,
            FunctionNode exhaustPortFlow = null, double chamberVolume = DefaultChamberVolume, double intakeRunnerVolume = DefaultRunnerVolume,
            double? intakeRunnerCrossSectionArea = null, double exhaustRunnerVolume = DefaultRunnerVolume, double? exhaustRunnerCrossSectionArea = null,
            bool flipDisplay = false) =>
            new CylinderHeadNode(new StandardValvetrainNode(intakeCamshaft, exhaustCamshaft), intakePortFlow, exhaustPortFlow, chamberVolume,
                intakeRunnerVolume, intakeRunnerCrossSectionArea, exhaustRunnerVolume, exhaustRunnerCrossSectionArea, flipDisplay);
    }

    /// <summary>An intake (intake): plenum, throttle body and runners.</summary>
    public sealed class IntakeNode
    {
        public readonly double plenumVolume, plenumCrossSectionArea, intakeFlowRate, idleFlowRate, molecularAfr, idleThrottlePlatePosition,
            throttleGamma, runnerLength, runnerFlowRate, velocityDecay, atmospherePressure, atmosphereTemperature;

        /// <param name="atmospherePressure">The air the intake draws from: altitude. A forced induction draws from it too.</param>
        public IntakeNode(double plenumVolume = 2.0 * ScriptUnits.L, double plenumCrossSectionArea = 100.0 * ScriptUnits.cm2, double intakeFlowRate = 0.0,
            double idleFlowRate = 0.0, double molecularAfr = 25.0 / 2.0, double idleThrottlePlatePosition = 0.975, double throttleGamma = 2.0,
            double runnerLength = 4.0 * ScriptUnits.inch, double? runnerFlowRate = null, double velocityDecay = 0.25,
            double atmospherePressure = 1.0 * ScriptUnits.atm, double atmosphereTemperature = 25.0 + ScriptUnits.K0)
        {
            this.plenumVolume = plenumVolume;
            this.plenumCrossSectionArea = plenumCrossSectionArea;
            this.intakeFlowRate = intakeFlowRate;
            this.idleFlowRate = idleFlowRate;
            this.molecularAfr = molecularAfr;
            this.idleThrottlePlatePosition = idleThrottlePlatePosition;
            this.throttleGamma = throttleGamma;
            this.runnerLength = runnerLength;
            this.runnerFlowRate = runnerFlowRate ?? ScriptFunctions.K_carb(200.0);
            this.velocityDecay = velocityDecay;
            this.atmospherePressure = atmospherePressure;
            this.atmosphereTemperature = atmosphereTemperature;
        }
    }

    /// <summary>
    /// A turbocharger or supercharger for <see cref="EngineNode.AddForcedInduction"/> (the community fork's forced induction,
    /// kyan0s-git's model): <see cref="TurbochargerNode"/> or <see cref="SuperchargerNode"/>.
    /// </summary>
    public abstract class ForcedInductionNode
    {
        public abstract ForcedInductionDescription Describe();

        internal static string TypeName(ForcedInduction.Type type) => type switch
        {
            ForcedInduction.Type.Roots => ForcedInductionDescription.Roots,
            ForcedInduction.Type.TwinScrew => ForcedInductionDescription.TwinScrew,
            ForcedInduction.Type.Centrifugal => ForcedInductionDescription.Centrifugal,
            _ => ForcedInductionDescription.Turbocharger,
        };

        internal static string ArrangementName(ForcedInduction.Arrangement arrangement) => arrangement switch
        {
            ForcedInduction.Arrangement.Twin => ForcedInductionDescription.Twin,
            ForcedInduction.Arrangement.Sequential => ForcedInductionDescription.Sequential,
            _ => ForcedInductionDescription.Single,
        };

        internal static string BlowoffName(ForcedInduction.BlowoffType blowoff) => blowoff switch
        {
            ForcedInduction.BlowoffType.Recirculating => ForcedInductionDescription.Recirculating,
            ForcedInduction.BlowoffType.None => ForcedInductionDescription.NoBlowoff,
            _ => ForcedInductionDescription.Atmospheric,
        };

        internal static string BoostControlName(ForcedInduction.BoostControl control) =>
            control == ForcedInduction.BoostControl.Spring ? ForcedInductionDescription.Spring : ForcedInductionDescription.Electronic;
    }

    /// <summary>
    /// A turbocharger (turbocharger): its turbine at the outlet of every exhaust system, its compressor feeding every intake.
    /// <paramref name="compressorFlow"/> is the compressor's maximum mass flow; <paramref name="maxSpeed"/> 0 takes the shaft limit
    /// from the compressor wheel's tip speed; <paramref name="boost"/> is the wastegate's set point (gauge).
    /// </summary>
    public sealed class TurbochargerNode : ForcedInductionNode
    {
        public readonly ForcedInduction.Arrangement arrangement;
        public readonly double compressorDiameter, compressorFlow, compressorEfficiency, maxSpeed, turbineAr, turbineEfficiency, inertia, boost,
            intercoolerEffectiveness, chargeVolume, sequentialSpeed, whistlePitch;
        public readonly bool ballBearings, blowoffValve, externalWastegate, twinScroll;
        public readonly ForcedInduction.BlowoffType blowoffType;
        public readonly ForcedInduction.BoostControl boostControl;

        public TurbochargerNode(ForcedInduction.Arrangement arrangement = ForcedInduction.Arrangement.Single,
            double compressorDiameter = 60 * ScriptUnits.mm, double compressorFlow = 0.3 * ScriptUnits.kg / ScriptUnits.sec,
            double compressorEfficiency = 0.75, double maxSpeed = 0.0, double turbineAr = 0.64, double turbineEfficiency = 0.7,
            double inertia = 0.00003, bool ballBearings = false, double boost = 100.0 * ScriptUnits.kPa, bool blowoffValve = true,
            double intercoolerEffectiveness = 0.7, double chargeVolume = 6.0 * ScriptUnits.L, double sequentialSpeed = 4000 * ScriptUnits.rpm,
            ForcedInduction.BlowoffType blowoffType = ForcedInduction.BlowoffType.Atmospheric,
            ForcedInduction.BoostControl boostControl = ForcedInduction.BoostControl.Electronic, bool externalWastegate = false,
            bool twinScroll = false, double whistlePitch = 1.0)
        {
            this.arrangement = arrangement;
            this.compressorDiameter = compressorDiameter;
            this.compressorFlow = compressorFlow;
            this.compressorEfficiency = compressorEfficiency;
            this.maxSpeed = maxSpeed;
            this.turbineAr = turbineAr;
            this.turbineEfficiency = turbineEfficiency;
            this.inertia = inertia;
            this.ballBearings = ballBearings;
            this.boost = boost;
            this.blowoffValve = blowoffValve;
            this.intercoolerEffectiveness = intercoolerEffectiveness;
            this.chargeVolume = chargeVolume;
            this.sequentialSpeed = sequentialSpeed;
            this.blowoffType = blowoffType;
            this.boostControl = boostControl;
            this.externalWastegate = externalWastegate;
            this.twinScroll = twinScroll;
            this.whistlePitch = whistlePitch;
        }

        public override ForcedInductionDescription Describe() => new ForcedInductionDescription
        {
            type = ForcedInductionDescription.Turbocharger, arrangement = ArrangementName(arrangement),
            compressorDiameter = compressorDiameter, compressorFlow = compressorFlow, compressorEfficiency = compressorEfficiency,
            maxSpeed = maxSpeed, turbineAr = turbineAr, turbineEfficiency = turbineEfficiency, inertia = inertia, ballBearings = ballBearings,
            boost = boost, blowoffValve = blowoffValve, blowoffType = BlowoffName(blowoffType), boostControl = BoostControlName(boostControl),
            externalWastegate = externalWastegate, twinScroll = twinScroll, whistlePitch = whistlePitch,
            // As the script's turbocharger(): no crank drive, displacement or bypass valve
            driveRatio = 1.0, displacement = 0.0, bypassValve = false,
            intercoolerEffectiveness = intercoolerEffectiveness, chargeVolume = chargeVolume, sequentialSpeed = sequentialSpeed,
        };
    }

    /// <summary>
    /// A crank-driven supercharger (supercharger): Roots or twin-screw (<paramref name="displacement"/> per rotor turn) or
    /// centrifugal (<paramref name="compressorDiameter"/>, <paramref name="compressorFlow"/>). <paramref name="driveRatio"/> is
    /// compressor speed / crank speed, a centrifugal's gearbox step-up included.
    /// </summary>
    public sealed class SuperchargerNode : ForcedInductionNode
    {
        public readonly ForcedInduction.Type type;
        public readonly double driveRatio, displacement, compressorDiameter, compressorFlow, compressorEfficiency, maxSpeed,
            intercoolerEffectiveness, chargeVolume, whistlePitch;
        public readonly bool bypassValve, blowoffValve;
        public readonly ForcedInduction.BlowoffType blowoffType;

        public SuperchargerNode(ForcedInduction.Type type = ForcedInduction.Type.Roots, double driveRatio = 2.0,
            double displacement = 1.9 * ScriptUnits.L, double compressorDiameter = 90 * ScriptUnits.mm,
            double compressorFlow = 0.5 * ScriptUnits.kg / ScriptUnits.sec, double compressorEfficiency = 0.75, double maxSpeed = 0.0,
            bool bypassValve = true, bool blowoffValve = true, double intercoolerEffectiveness = 0.0, double chargeVolume = 4.0 * ScriptUnits.L,
            ForcedInduction.BlowoffType blowoffType = ForcedInduction.BlowoffType.Atmospheric, double whistlePitch = 1.0)
        {
            if (type == ForcedInduction.Type.Turbocharger) throw new ArgumentException("A supercharger is Roots, twin-screw or centrifugal.", nameof(type));
            this.type = type;
            this.driveRatio = driveRatio;
            this.displacement = displacement;
            this.compressorDiameter = compressorDiameter;
            this.compressorFlow = compressorFlow;
            this.compressorEfficiency = compressorEfficiency;
            this.maxSpeed = maxSpeed;
            this.bypassValve = bypassValve;
            this.blowoffValve = blowoffValve;
            this.intercoolerEffectiveness = intercoolerEffectiveness;
            this.chargeVolume = chargeVolume;
            this.blowoffType = blowoffType;
            this.whistlePitch = whistlePitch;
        }

        public override ForcedInductionDescription Describe() => new ForcedInductionDescription
        {
            type = TypeName(type), arrangement = ForcedInductionDescription.Single,
            compressorDiameter = compressorDiameter, compressorFlow = compressorFlow, compressorEfficiency = compressorEfficiency,
            maxSpeed = maxSpeed,
            // As the script's supercharger(): no turbine, a ball-bearing shaft, no boost target or sequential stage
            turbineAr = 0.64, turbineEfficiency = 0.7, inertia = 0.00003, ballBearings = true, boost = 0.0,
            blowoffValve = blowoffValve, blowoffType = BlowoffName(blowoffType), boostControl = ForcedInductionDescription.Electronic,
            externalWastegate = false, twinScroll = false, whistlePitch = whistlePitch,
            driveRatio = driveRatio, displacement = displacement, bypassValve = bypassValve,
            intercoolerEffectiveness = intercoolerEffectiveness, chargeVolume = chargeVolume, sequentialSpeed = 0.0,
        };
    }

    /// <summary>An impulse response for an exhaust (impulse_response): a file under engine-sim's root, and its volume.</summary>
    public sealed class ImpulseResponseNode
    {
        public readonly string file;
        public readonly double volume;

        public ImpulseResponseNode(string file, double volume = 1.0)
        {
            this.file = file;
            this.volume = volume;
        }
    }

    /// <summary>An exhaust system's values (exhaust_system_parameters); its length defaults to its volume over its collector's area.</summary>
    public sealed class ExhaustSystemParameters
    {
        public readonly double length, collectorCrossSectionArea, outletFlowRate, primaryTubeLength, primaryFlowRate, audioVolume, velocityDecay;

        public ExhaustSystemParameters(double volume = 100.0 * ScriptUnits.L, double? length = null, double? collectorCrossSectionArea = null,
            double? outletFlowRate = null, double primaryTubeLength = 10.0 * ScriptUnits.inch, double? primaryFlowRate = null, double audioVolume = 1.0,
            double velocityDecay = 1.0)
        {
            this.collectorCrossSectionArea = collectorCrossSectionArea ?? ScriptFunctions.CircleArea(2.0 * ScriptUnits.inch);
            this.length = length ?? volume / this.collectorCrossSectionArea;
            this.outletFlowRate = outletFlowRate ?? ScriptFunctions.K_carb(1000.0);
            this.primaryTubeLength = primaryTubeLength;
            this.primaryFlowRate = primaryFlowRate ?? ScriptFunctions.K_carb(100.0);
            this.audioVolume = audioVolume;
            this.velocityDecay = velocityDecay;
        }
    }

    /// <summary>An exhaust system (exhaust_system): its parameters, overridden one by one, and its impulse response.</summary>
    public sealed class ExhaustSystemNode
    {
        public readonly double length, collectorCrossSectionArea, outletFlowRate, primaryTubeLength, primaryFlowRate, audioVolume, velocityDecay;
        public readonly ImpulseResponseNode impulseResponse;

        public ExhaustSystemNode(ExhaustSystemParameters parameters, ImpulseResponseNode impulseResponse, double? length = null,
            double? collectorCrossSectionArea = null, double? outletFlowRate = null, double? primaryTubeLength = null, double? primaryFlowRate = null,
            double? audioVolume = null, double? velocityDecay = null)
        {
            parameters ??= new ExhaustSystemParameters();
            this.length = length ?? parameters.length;
            this.collectorCrossSectionArea = collectorCrossSectionArea ?? parameters.collectorCrossSectionArea;
            this.outletFlowRate = outletFlowRate ?? parameters.outletFlowRate;
            this.primaryTubeLength = primaryTubeLength ?? parameters.primaryTubeLength;
            this.primaryFlowRate = primaryFlowRate ?? parameters.primaryFlowRate;
            this.audioVolume = audioVolume ?? parameters.audioVolume;
            this.velocityDecay = velocityDecay ?? parameters.velocityDecay;
            this.impulseResponse = impulseResponse;
        }
    }

    /// <summary>A spark plug lead (ignition_wire): the cylinders it is added with, fired at the angle it is connected at.</summary>
    public sealed class IgnitionWireNode
    {
    }

    /// <summary>The ignition (ignition_module): its timing advance against speed, rev limit, and the wires in the order connected.</summary>
    public sealed class IgnitionModuleNode
    {
        public readonly FunctionNode timingCurve;
        public readonly double revLimit, limiterDuration, sparksPerCycle;
        public readonly List<(IgnitionWireNode wire, double angle)> posts = new List<(IgnitionWireNode, double)>();

        /// <param name="sparksPerCycle">1 for a four-stroke; 2 fires each plug every revolution (a two-stroke).</param>
        public IgnitionModuleNode(FunctionNode timingCurve, double revLimit = 7000.0 * ScriptUnits.rpm, double limiterDuration = 0.5 * ScriptUnits.sec,
            double sparksPerCycle = 1)
        {
            this.timingCurve = timingCurve;
            this.revLimit = revLimit;
            this.limiterDuration = limiterDuration;
            this.sparksPerCycle = sparksPerCycle;
        }

        public IgnitionModuleNode ConnectWire(IgnitionWireNode wire, double angle)
        {
            posts.Add((wire, angle));
            return this;
        }
    }

    /// <summary>The fuel (fuel), gasoline by default.</summary>
    public sealed class FuelNode
    {
        public readonly string name;
        public readonly double molecularMass, energyDensity, density, molecularAfr, maxBurningEfficiency, burningEfficiencyRandomness,
            lowEfficiencyAttenuation, maxTurbulenceEffect, maxDilutionEffect;
        public readonly FunctionNode turbulenceToFlameSpeedRatio;

        public FuelNode(string name = "Gasoline [Default]", double molecularMass = 100 * ScriptUnits.g,
            double energyDensity = 48.1 * ScriptUnits.kJ / ScriptUnits.g, double density = 0.755 * ScriptUnits.kg / ScriptUnits.L,
            double molecularAfr = 25 / 2.0, FunctionNode turbulenceToFlameSpeedRatio = null, double maxBurningEfficiency = 0.8,
            double burningEfficiencyRandomness = 0.5, double lowEfficiencyAttenuation = 0.6, double maxTurbulenceEffect = 2.0,
            double maxDilutionEffect = 10.0)
        {
            this.name = name;
            this.molecularMass = molecularMass;
            this.energyDensity = energyDensity;
            this.density = density;
            this.molecularAfr = molecularAfr;
            this.turbulenceToFlameSpeedRatio = turbulenceToFlameSpeedRatio ?? DefaultTurbulenceToFlameSpeedRatio();
            this.maxBurningEfficiency = maxBurningEfficiency;
            this.burningEfficiencyRandomness = burningEfficiencyRandomness;
            this.lowEfficiencyAttenuation = lowEfficiencyAttenuation;
            this.maxTurbulenceEffect = maxTurbulenceEffect;
            this.maxDilutionEffect = maxDilutionEffect;
        }

        /// <summary>The flame speed's rise with turbulence (turbulence_to_flame_speed_ratio_default).</summary>
        public static FunctionNode DefaultTurbulenceToFlameSpeedRatio() =>
            new FunctionNode(5.0)
                .AddSample(0.0, 3.0)
                .AddSample(5.0, 1.5 * 5.0)
                .AddSample(10.0, 1.5 * 10.0)
                .AddSample(15.0, 1.5 * 15.0)
                .AddSample(20.0, 1.5 * 20.0)
                .AddSample(25.0, 1.5 * 25.0)
                .AddSample(30.0, 1.5 * 30.0)
                .AddSample(35.0, 1.5 * 35.0)
                .AddSample(40.0, 1.5 * 40.0)
                .AddSample(45.0, 1.5 * 45.0);
    }

    /// <summary>How the throttle input reaches the throttle plate.</summary>
    public abstract class ThrottleNode
    {
    }

    /// <summary>A direct linkage (direct_throttle_linkage): the plate follows the pedal, shaped by <see cref="gamma"/>.</summary>
    public sealed class DirectThrottleLinkageNode : ThrottleNode
    {
        public readonly double gamma;

        public DirectThrottleLinkageNode(double gamma = 1.0) => this.gamma = gamma;
    }

    /// <summary>A speed governor (governor): the pedal sets a target speed between its limits, which the governor holds.</summary>
    public sealed class GovernorNode : ThrottleNode
    {
        public readonly double minSpeed, maxSpeed, minV, maxV, k_s, k_d, gamma;

        public GovernorNode(double minSpeed = 1.0, double maxSpeed = 1.0, double minV = -2.0, double maxV = 2.0, double k_s = 1.0, double k_d = 300.0,
            double gamma = 0.1)
        {
            this.minSpeed = minSpeed;
            this.maxSpeed = maxSpeed;
            this.minV = minV;
            this.maxV = maxV;
            this.k_s = k_s;
            this.k_d = k_d;
            this.gamma = gamma;
        }
    }

    /// <summary>The vehicle the engine drives (vehicle).</summary>
    public sealed class VehicleNode
    {
        public readonly double mass, dragCoefficient, crossSectionalArea, diffRatio, tireRadius, rollingResistance;

        public VehicleNode(double mass = 1000 * ScriptUnits.kg, double dragCoefficient = 0.25,
            double crossSectionalArea = (72 * ScriptUnits.inch) * (72 * ScriptUnits.inch), double diffRatio = 3.42, double tireRadius = 10 * ScriptUnits.inch,
            double rollingResistance = 2000)
        {
            this.mass = mass;
            this.dragCoefficient = dragCoefficient;
            this.crossSectionalArea = crossSectionalArea;
            this.diffRatio = diffRatio;
            this.tireRadius = tireRadius;
            this.rollingResistance = rollingResistance;
        }

        /// <summary>The vehicle engine-sim's application drives when a script sets none.</summary>
        public static VehicleNode ApplicationDefault() =>
            new VehicleNode(
                mass: Units.Mass(1597, Units.kg),
                diffRatio: 3.42,
                tireRadius: Units.Distance(10, Units.inch),
                dragCoefficient: 0.25,
                crossSectionalArea: Units.Distance(6.0, Units.foot) * Units.Distance(6.0, Units.foot),
                rollingResistance: 2000.0);
    }

    /// <summary>The transmission (transmission) and its gears, in the order added.</summary>
    public sealed class TransmissionNode
    {
        public readonly double maxClutchTorque;
        public readonly List<double> gears = new List<double>();

        public TransmissionNode(double maxClutchTorque = 1000 * ScriptUnits.lb_ft) => this.maxClutchTorque = maxClutchTorque;

        public TransmissionNode AddGear(double ratio)
        {
            gears.Add(ratio);
            return this;
        }

        /// <summary>The six-speed gearbox engine-sim's application uses when a script sets none.</summary>
        public static TransmissionNode ApplicationDefault() =>
            new TransmissionNode(Units.Torque(1000.0, Units.ft_lb))
                .AddGear(2.97)
                .AddGear(2.07)
                .AddGear(1.43)
                .AddGear(1.00)
                .AddGear(0.84)
                .AddGear(0.56);
    }
}
