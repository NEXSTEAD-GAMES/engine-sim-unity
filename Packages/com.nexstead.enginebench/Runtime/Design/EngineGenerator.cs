// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;
using U = Nexstead.EngineBench.Design.ScriptUnits;

namespace Nexstead.EngineBench.Design
{
    /// <summary>
    /// Builds an engine from an <see cref="EngineDesign"/> with the script nodes, deriving what follows from the layout and
    /// the firing as engine scripts write it by hand:
    /// <list type="bullet">
    /// <item>Banks: an inline engine's upright; a V's at half the bank angle either side, the first to the right; a flat
    /// engine's level, the first to the left (the other way round when mirrored). Top dead centre: along the first bank.</item>
    /// <item>Firing: each cylinder fires at its firing angle (by default an even share of the cycle times its place in the
    /// firing order); its spark plug is at that angle, its lobes at the lobe centres from it.</item>
    /// <item>Journals: one a cylinder (inline, flat, a V with split pins) or one a pair across the banks (V), in cylinder
    /// order, each at the angle that brings its cylinder to top dead centre as it fires.</item>
    /// <item>Deck height: half the stroke, the rod, the compression height and the deck clearance.</item>
    /// </list>
    /// </summary>
    public static class EngineGenerator
    {
        const double Cycle = 2 * 360 * U.deg;
        /// <summary>How close two angles (°) must be to count as one: firing angles read back from radians are a rounding step off.</summary>
        const double SameAngle = 1e-6;

        /// <summary>What keeps the design from being built, in words; empty when it can be.</summary>
        public static List<string> Problems(EngineDesign design)
        {
            List<string> problems = FiringProblems(design);
            int n = design.cylinders;
            if (design.cylinderSettings == null || design.cylinderSettings.Length != n)
            {
                problems.Add($"There are {design.cylinderSettings?.Length ?? 0} cylinder settings, not {n}.");
            }
            else
            {
                int intakes = design.intakes?.Length ?? 0, exhausts = design.exhausts?.Length ?? 0;
                for (int c = 0; c < n; ++c)
                {
                    CylinderDesign cylinder = design.cylinderSettings[c];
                    if (cylinder.intake < 0 || cylinder.intake >= intakes) problems.Add($"Cylinder {c + 1} uses intake {cylinder.intake + 1}; there are {intakes}.");
                    if (cylinder.exhaust < 0 || cylinder.exhaust >= exhausts)
                    {
                        problems.Add($"Cylinder {c + 1} uses exhaust {cylinder.exhaust + 1}; there are {exhausts}.");
                    }
                }
            }

            if (design.flipBankDisplay == null || design.flipBankDisplay.Length < design.Banks) problems.Add("Each bank needs its display side.");
            if (design.heads == null || (design.heads.Length != 1 && design.heads.Length != design.Banks))
            {
                problems.Add($"There is one head for all banks, or one for each bank; there are {design.heads?.Length ?? 0}.");
            }
            else
            {
                foreach (HeadDesign head in design.heads)
                {
                    if (head.intakeFlow.Length == 0 || head.exhaustFlow.Length == 0) problems.Add("Each head needs its intake and exhaust flow.");
                }
            }

            if (design.bore <= 0 || design.stroke <= 0 || design.rodLength <= 0) problems.Add("Bore, stroke and rod length must be positive.");
            if (design.rodLength <= design.stroke / 2) problems.Add("The rod must be longer than the crank throw (half the stroke).");
            if (design.ignition.timingCurve.Length == 0) problems.Add("The ignition needs a timing curve.");
            if (design.simulationFrequency <= 0) problems.Add("The simulation frequency must be positive.");
            return problems;
        }

        /// <summary>What keeps the layout, cylinder count and firing from being built, in words; empty when they can be.</summary>
        public static List<string> FiringProblems(EngineDesign design)
        {
            var problems = new List<string>();
            int n = design.cylinders;
            if (n < 1) problems.Add("An engine needs at least one cylinder.");
            if (design.layout != EngineLayout.Inline && n % 2 != 0) problems.Add($"A {Describe(design.layout)} engine needs an even number of cylinders.");
            if (design.layout == EngineLayout.V && (design.bankAngle <= 0 || design.bankAngle >= 180))
            {
                problems.Add("A V engine's bank angle is between 0° and 180°.");
            }

            if (design.firingAngles.Length > 0)
            {
                if (design.firingAngles.Length != n) problems.Add($"There are {design.firingAngles.Length} firing angles, not {n}.");
                foreach (double angle in design.firingAngles)
                {
                    if (angle < 0 || angle >= 720) problems.Add($"A firing angle of {angle:0.#}° is outside the cycle (0° to 720°).");
                }
            }
            else if (design.firingOrder == null || design.firingOrder.Length != n)
            {
                problems.Add($"The firing order lists {design.firingOrder?.Length ?? 0} cylinders, not {n}.");
            }
            else
            {
                var seen = new bool[n + 1];
                foreach (int cylinder in design.firingOrder)
                {
                    if (cylinder < 1 || cylinder > n) problems.Add($"The firing order names cylinder {cylinder}; the engine has 1 to {n}.");
                    else if (seen[cylinder]) problems.Add($"The firing order names cylinder {cylinder} twice.");
                    else seen[cylinder] = true;
                }
            }

            if (problems.Count == 0 && design.layout == EngineLayout.V && !design.splitPins)
            {
                // A V's pairs share a pin: both cylinders must reach top dead centre with the pin at the same angle.
                double[] firing = FiringAngles(design);
                double[] banks = BankDegrees(design);
                int perBank = design.CylindersPerBank;
                for (int i = 0; i < perBank; ++i)
                {
                    double first = Mod360(firing[i]), second = Mod360(firing[perBank + i] + banks[1] - banks[0]);
                    double apart = Math.Abs(first - second);
                    if (Math.Min(apart, 360 - apart) > SameAngle)
                    {
                        problems.Add($"Cylinders {i + 1} and {perBank + i + 1} share a crank pin, but their firing needs the pin at " +
                                     $"{first:0.#}° for one and {second:0.#}° for the other.");
                    }
                }
            }

            return problems;
        }

        /// <summary>The engine's description, as engine-sim would build it from the same engine written as a script.</summary>
        /// <exception cref="ArgumentException">The design has <see cref="Problems"/>.</exception>
        public static EngineDescription Describe(EngineDesign design) => Build(design).engine.Describe(Vehicle(design.vehicle), Transmission(design.transmission));

        /// <summary>
        /// Each cylinder's firing angle in the cycle (°, 0 to 720): its own (odd firing), or an even share of the cycle times
        /// its place in the firing order. Indexed by cylinder (0 for cylinder 1).
        /// </summary>
        public static double[] FiringAngles(EngineDesign design)
        {
            if (design.firingAngles.Length > 0) return (double[])design.firingAngles.Clone();
            var angles = new double[design.cylinders];
            for (int k = 0; k < design.firingOrder.Length; ++k) angles[design.firingOrder[k] - 1] = 720.0 * k / design.cylinders;
            return angles;
        }

        /// <summary>The banks' angles (°) from upright, as the generator sets them.</summary>
        public static double[] BankDegrees(EngineDesign design)
        {
            double sign = design.banksMirrored ? -1 : 1;
            return design.layout switch
            {
                EngineLayout.Inline => new[] { 0.0 },
                EngineLayout.Flat => new[] { sign * 90.0, sign * -90.0 },
                _ => new[] { sign * -design.bankAngle / 2.0, sign * design.bankAngle / 2.0 },
            };
        }

        /// <summary>The script nodes the design builds; for checks and tools that read the parts.</summary>
        public static (EngineNode engine, IReadOnlyList<CylinderBankNode> banks) Build(EngineDesign design)
        {
            List<string> problems = Problems(design);
            if (problems.Count > 0) throw new ArgumentException("The design can't be built: " + string.Join(" ", problems), nameof(design));

            int n = design.cylinders, banks = design.Banks, perBank = design.CylindersPerBank;
            var engine = new EngineNode(
                name: design.name,
                starterTorque: design.starterTorque,
                starterSpeed: design.starterSpeed * U.rpm,
                redline: design.redline * U.rpm,
                dynoMinSpeed: design.dynoMinSpeed * U.rpm,
                dynoMaxSpeed: design.dynoMaxSpeed * U.rpm,
                dynoHoldStep: design.dynoHoldStep * U.rpm,
                fuel: Fuel(design.fuel),
                throttle: Throttle(design.throttle),
                simulationFrequency: design.simulationFrequency,
                hfGain: design.highFrequencyGain,
                noise: design.noise,
                jitter: design.jitter);

            // Banks and top dead centre.
            double[] bankAngles = BankAngles(design);
            double[] bankDegrees = BankDegrees(design);
            var crankshaft = new CrankshaftNode(
                @throw: design.stroke / 2,
                flywheelMass: design.flywheelMass,
                mass: design.crankMass,
                frictionTorque: design.crankFrictionTorque,
                momentOfInertia: design.crankMomentOfInertia,
                positionX: 0.0,
                positionY: 0.0,
                tdc: 90 * U.deg + bankAngles[0]);

            // Journals: a cylinder firing at angle c on a bank at angle b reaches top dead centre with its journal at
            // c + b - b0 (the crank turning the other way to the cycle angle), in whole degrees where the angles are.
            double[] firing = FiringAngles(design);
            bool sharedPins = design.layout == EngineLayout.V && !design.splitPins;
            var journals = new RodJournalNode[n];
            for (int b = 0; b < banks; ++b)
            {
                for (int i = 0; i < perBank; ++i)
                {
                    int c = b * perBank + i;
                    if (sharedPins && b == 1)
                    {
                        journals[c] = journals[i];
                        continue;
                    }

                    journals[c] = new RodJournalNode(Mod360(firing[c] + bankDegrees[b] - bankDegrees[0]) * U.deg);
                    crankshaft.AddRodJournal(journals[c]);
                }
            }

            engine.AddCrankshaft(crankshaft);

            var pistonParameters = new PistonParameters(mass: design.pistonMass, compressionHeight: design.compressionHeight,
                wristPinPosition: design.wristPinPosition, displacement: 0.0);
            var rodParameters = new ConnectingRodParameters(mass: design.rodMass, momentOfInertia: design.rodMomentOfInertia,
                centerOfMass: design.rodCenterOfMass, length: design.rodLength);
            var bankParameters = new CylinderBankParameters(bore: design.bore,
                deckHeight: design.stroke / 2 + design.rodLength + design.compressionHeight + design.deckClearance, displayDepth: design.displayDepth);

            var intakes = new IntakeNode[design.intakes.Length];
            for (int i = 0; i < intakes.Length; ++i) intakes[i] = Intake(design.intakes[i]);
            var exhausts = new ExhaustSystemNode[design.exhausts.Length];
            for (int i = 0; i < exhausts.Length; ++i) exhausts[i] = Exhaust(design.exhausts[i]);

            // One lobe profile each for every intake and every exhaust camshaft (and each high-lift pair).
            CamshaftDesign cams = design.camshaft;
            VariableLiftDesign variable = cams.variableLift;
            var standard = new CamSet(cams.intakeLobe, cams.exhaustLobe, cams.intakeLobeCenter, cams.exhaustLobeCenter, cams.advance, cams.baseRadius);
            CamSet highLift = variable.enabled
                ? new CamSet(variable.intakeLobe, variable.exhaustLobe, variable.intakeLobeCenter, variable.exhaustLobeCenter, variable.advance,
                    variable.baseRadius)
                : null;

            var wires = new IgnitionWireNode[n];
            var bankNodes = new CylinderBankNode[banks];
            for (int b = 0; b < banks; ++b)
            {
                var bank = new CylinderBankNode(bankParameters, angle: bankAngles[b]);
                (CamshaftNode intake, CamshaftNode exhaust) camshafts = standard.Camshafts();
                (CamshaftNode intake, CamshaftNode exhaust) highLiftCamshafts = highLift?.Camshafts() ?? default;
                for (int i = 0; i < perBank; ++i)
                {
                    int c = b * perBank + i;
                    CylinderDesign cylinder = design.cylinderSettings[c];
                    wires[c] = new IgnitionWireNode();
                    bank.AddCylinder(
                        new PistonNode(pistonParameters, blowby: ScriptFunctions.K_28inH2O(cylinder.blowby)),
                        new ConnectingRodNode(rodParameters),
                        journals[c],
                        intakes[cylinder.intake],
                        exhausts[cylinder.exhaust],
                        wires[c],
                        soundAttenuation: cylinder.soundAttenuation,
                        primaryLength: cylinder.headerPrimaryLength);

                    double phase = Phase(design, c);
                    standard.AddLobes(camshafts, phase);
                    highLift?.AddLobes(highLiftCamshafts, phase);
                }

                ValvetrainNode valvetrain = highLift == null
                    ? new StandardValvetrainNode(camshafts.intake, camshafts.exhaust)
                    : new VariableLiftValvetrainNode(highLiftCamshafts.intake, highLiftCamshafts.exhaust, camshafts.intake, camshafts.exhaust,
                        minRpm: variable.minRpm * U.rpm, minSpeed: variable.minSpeed * U.mph, manifoldVacuum: variable.manifoldVacuum,
                        minThrottlePosition: variable.minThrottlePosition);
                bank.SetCylinderHead(Head(design.HeadOf(b), valvetrain, design.flipBankDisplay[b]));
                engine.AddCylinderBank(bank);
                bankNodes[b] = bank;
            }

            var timingCurve = new FunctionNode(design.ignition.timingSmoothing * U.rpm);
            foreach (TimingSample sample in design.ignition.timingCurve) timingCurve.AddSample(sample.speed * U.rpm, sample.advance * U.deg);
            var ignition = new IgnitionModuleNode(timingCurve, revLimit: design.ignition.revLimit * U.rpm, limiterDuration: design.ignition.limiterDuration);
            for (int c = 0; c < n; ++c) ignition.ConnectWire(wires[c], Phase(design, c));
            engine.AddIgnitionModule(ignition);

            ForcedInductionNode forcedInduction = ForcedInduction(design.forcedInduction);
            if (forcedInduction != null) engine.AddForcedInduction(forcedInduction);

            return (engine, bankNodes);
        }

        /// <summary>
        /// A cylinder's firing angle (rad): its own, or its place in the firing order as a share of the cycle, written as
        /// engine scripts write even firing.
        /// </summary>
        static double Phase(EngineDesign design, int cylinder) =>
            design.firingAngles.Length > 0
                ? design.firingAngles[cylinder] * U.deg
                : (Array.IndexOf(design.firingOrder, cylinder + 1) / (double)design.cylinders) * Cycle;

        /// <summary>An intake and an exhaust lobe profile, and the camshafts' timing; makes a bank's pair and lobes them.</summary>
        sealed class CamSet
        {
            const double Rot360 = 360 * U.deg;
            readonly FunctionNode intakeLobe, exhaustLobe;
            readonly CamshaftParameters parameters;
            readonly double intakeCenter, exhaustCenter;

            public CamSet(LobeDesign intake, LobeDesign exhaust, double intakeCenterDegrees, double exhaustCenterDegrees, double advanceDegrees,
                double baseRadius)
            {
                intakeLobe = Lobe(intake);
                exhaustLobe = Lobe(exhaust);
                intakeCenter = intakeCenterDegrees * U.deg;
                exhaustCenter = exhaustCenterDegrees * U.deg;
                parameters = new CamshaftParameters(advance: advanceDegrees * U.deg, baseRadius: baseRadius);
            }

            public (CamshaftNode intake, CamshaftNode exhaust) Camshafts() =>
                (new CamshaftNode(parameters, lobeProfile: intakeLobe), new CamshaftNode(parameters, lobeProfile: exhaustLobe));

            public void AddLobes((CamshaftNode intake, CamshaftNode exhaust) camshafts, double phase)
            {
                camshafts.exhaust.AddLobe(Rot360 - exhaustCenter + phase);
                camshafts.intake.AddLobe(Rot360 + intakeCenter + phase);
            }
        }

        static double[] BankAngles(EngineDesign design)
        {
            double sign = design.banksMirrored ? -1 : 1;
            switch (design.layout)
            {
                case EngineLayout.Inline:
                    return new[] { 0.0 * U.deg };
                case EngineLayout.Flat:
                    return new[] { sign * 90.0 * U.deg, sign * -90.0 * U.deg };
                default:
                    double v = design.bankAngle * U.deg;
                    return new[] { sign * -v / 2.0, sign * v / 2.0 };
            }
        }

        static double Mod360(double degrees)
        {
            double r = degrees % 360.0;
            return r < 0 ? r + 360.0 : r;
        }

        static string Describe(EngineLayout layout) => layout == EngineLayout.V ? "V" : layout == EngineLayout.Flat ? "flat" : "inline";

        static FunctionNode Lobe(LobeDesign lobe) =>
            ScriptFunctions.HarmonicCamLobe(durationAt50Thou: lobe.durationAt50Thou * U.deg, gamma: lobe.gamma, lift: lobe.lift, steps: lobe.steps);

        static CylinderHeadNode Head(HeadDesign head, ValvetrainNode valvetrain, bool flipDisplay)
        {
            var intakeFlow = new FunctionNode(head.flowSmoothing * U.thou);
            foreach (FlowSample sample in head.intakeFlow) intakeFlow.AddFlowSample(sample.lift, sample.flow);
            var exhaustFlow = new FunctionNode(head.flowSmoothing * U.thou);
            foreach (FlowSample sample in head.exhaustFlow) exhaustFlow.AddFlowSample(sample.lift, sample.flow);

            return new CylinderHeadNode(valvetrain, intakeFlow, exhaustFlow,
                chamberVolume: head.chamberVolume,
                intakeRunnerVolume: head.intakeRunnerVolume,
                intakeRunnerCrossSectionArea: head.intakeRunnerCrossSectionArea,
                exhaustRunnerVolume: head.exhaustRunnerVolume,
                exhaustRunnerCrossSectionArea: head.exhaustRunnerCrossSectionArea,
                flipDisplay: flipDisplay);
        }

        static IntakeNode Intake(IntakeDesign intake) =>
            new IntakeNode(
                plenumVolume: intake.plenumVolume,
                plenumCrossSectionArea: intake.plenumCrossSectionArea,
                intakeFlowRate: ScriptFunctions.K_carb(intake.intakeFlowRate),
                idleFlowRate: ScriptFunctions.K_carb(intake.idleFlowRate),
                molecularAfr: intake.molecularAfr,
                idleThrottlePlatePosition: intake.idleThrottlePlatePosition,
                runnerLength: intake.runnerLength,
                runnerFlowRate: ScriptFunctions.K_carb(intake.runnerFlowRate),
                velocityDecay: intake.velocityDecay,
                atmospherePressure: intake.atmospherePressure,
                atmosphereTemperature: intake.atmosphereTemperature);

        static ForcedInductionNode ForcedInduction(ForcedInductionDesign f)
        {
            if (f == null) return null;
            switch (f.type)
            {
                case ForcedInductionKind.Turbocharger:
                    return new TurbochargerNode(
                        arrangement: f.arrangement,
                        compressorDiameter: f.compressorDiameter,
                        compressorFlow: f.compressorFlow,
                        compressorEfficiency: f.compressorEfficiency,
                        maxSpeed: f.maxSpeed * U.rpm,
                        turbineAr: f.turbineAr,
                        turbineEfficiency: f.turbineEfficiency,
                        inertia: f.inertia,
                        ballBearings: f.ballBearings,
                        boost: f.boost,
                        blowoffValve: f.blowoffValve,
                        intercoolerEffectiveness: f.intercoolerEffectiveness,
                        chargeVolume: f.chargeVolume,
                        sequentialSpeed: f.sequentialSpeed * U.rpm,
                        blowoffType: f.blowoffType,
                        boostControl: f.boostControl,
                        externalWastegate: f.externalWastegate,
                        twinScroll: f.twinScroll,
                        whistlePitch: f.whistlePitch);
                case ForcedInductionKind.Roots:
                case ForcedInductionKind.TwinScrew:
                case ForcedInductionKind.Centrifugal:
                    return new SuperchargerNode(
                        type: f.type == ForcedInductionKind.Roots ? Simulation.ForcedInduction.Type.Roots
                            : f.type == ForcedInductionKind.TwinScrew ? Simulation.ForcedInduction.Type.TwinScrew
                            : Simulation.ForcedInduction.Type.Centrifugal,
                        driveRatio: f.driveRatio,
                        displacement: f.displacement,
                        compressorDiameter: f.compressorDiameter,
                        compressorFlow: f.compressorFlow,
                        compressorEfficiency: f.compressorEfficiency,
                        maxSpeed: f.maxSpeed * U.rpm,
                        bypassValve: f.bypassValve,
                        blowoffValve: f.blowoffValve,
                        intercoolerEffectiveness: f.intercoolerEffectiveness,
                        chargeVolume: f.chargeVolume,
                        blowoffType: f.blowoffType,
                        whistlePitch: f.whistlePitch);
                default:
                    return null;
            }
        }

        static ExhaustSystemNode Exhaust(ExhaustDesign exhaust) =>
            new ExhaustSystemNode(
                new ExhaustSystemParameters(
                    length: exhaust.length,
                    collectorCrossSectionArea: exhaust.collectorCrossSectionArea,
                    outletFlowRate: ScriptFunctions.K_carb(exhaust.outletFlowRate),
                    primaryTubeLength: exhaust.primaryTubeLength,
                    primaryFlowRate: ScriptFunctions.K_carb(exhaust.primaryFlowRate),
                    audioVolume: exhaust.audioVolume,
                    velocityDecay: exhaust.velocityDecay),
                new ImpulseResponseNode(exhaust.impulseResponse, exhaust.impulseResponseVolume));

        static FuelNode Fuel(FuelDesign fuel)
        {
            FunctionNode turbulence = null;
            if (fuel.turbulenceToFlameSpeedRatio.Length > 0)
            {
                turbulence = new FunctionNode(fuel.turbulenceSmoothing);
                foreach (CurveSample sample in fuel.turbulenceToFlameSpeedRatio) turbulence.AddSample(sample.x, sample.y);
            }

            return new FuelNode(
                molecularMass: fuel.molecularMass,
                energyDensity: fuel.energyDensity,
                density: fuel.density,
                molecularAfr: fuel.molecularAfr,
                turbulenceToFlameSpeedRatio: turbulence,
                maxBurningEfficiency: fuel.maxBurningEfficiency,
                burningEfficiencyRandomness: fuel.burningEfficiencyRandomness,
                lowEfficiencyAttenuation: fuel.lowEfficiencyAttenuation,
                maxTurbulenceEffect: fuel.maxTurbulenceEffect,
                maxDilutionEffect: fuel.maxDilutionEffect);
        }

        static ThrottleNode Throttle(ThrottleDesign throttle) =>
            throttle.governed
                ? new GovernorNode(
                    minSpeed: throttle.governorMinSpeed * U.rpm,
                    maxSpeed: throttle.governorMaxSpeed * U.rpm,
                    minV: throttle.governorMinVelocity,
                    maxV: throttle.governorMaxVelocity,
                    k_s: throttle.governorK_s,
                    k_d: throttle.governorK_d,
                    gamma: throttle.governorGamma)
                : new DirectThrottleLinkageNode(throttle.gamma);

        static VehicleNode Vehicle(VehicleDesign vehicle) =>
            new VehicleNode(
                mass: vehicle.mass,
                dragCoefficient: vehicle.dragCoefficient,
                crossSectionalArea: vehicle.crossSectionArea,
                diffRatio: vehicle.diffRatio,
                tireRadius: vehicle.tireRadius,
                rollingResistance: vehicle.rollingResistance);

        static TransmissionNode Transmission(TransmissionDesign transmission)
        {
            var node = new TransmissionNode(transmission.maxClutchTorque);
            foreach (double ratio in transmission.gearRatios) node.AddGear(ratio);
            return node;
        }
    }
}
