// Ported from engine-sim (https://github.com/ange-yaghi/engine-sim): es/objects/objects.mr (engine) and the build order
// of scripting/include/engine_node.h and the nodes it generates. MIT licence, Copyright (c) 2022 Ange Yaghi; see
// Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Design
{
    /// <summary>
    /// An engine (engine): its crankshafts, banks and ignition, in the order added. <see cref="Describe"/> builds the
    /// <see cref="EngineDescription"/> engine-sim builds from the same script: the values each part is constructed with,
    /// parts in engine-sim's order, and shared functions written once, numbered in the order first used.
    /// </summary>
    public sealed class EngineNode
    {
        public readonly string name;
        public readonly double redline, starterSpeed, starterTorque, dynoMinSpeed, dynoMaxSpeed, dynoHoldStep, simulationFrequency, hfGain, jitter, noise;
        public readonly FuelNode fuel;
        public readonly ThrottleNode throttle;
        public readonly List<CrankshaftNode> crankshafts = new List<CrankshaftNode>();
        public readonly List<CylinderBankNode> cylinderBanks = new List<CylinderBankNode>();
        public IgnitionModuleNode ignitionModule;
        public ForcedInductionNode forcedInduction;

        public EngineNode(string name = "", double redline = 6000 * ScriptUnits.rpm, double starterSpeed = 200 * ScriptUnits.rpm,
            double starterTorque = 200 * ScriptUnits.lb_ft, double dynoMinSpeed = 1000 * ScriptUnits.rpm, double? dynoMaxSpeed = null,
            double dynoHoldStep = 100 * ScriptUnits.rpm, FuelNode fuel = null, double throttleGamma = 1.0, ThrottleNode throttle = null,
            double simulationFrequency = 10000, double hfGain = 0.01, double jitter = 0.5, double noise = 1.0)
        {
            this.name = name;
            this.redline = redline;
            this.starterSpeed = starterSpeed;
            this.starterTorque = starterTorque;
            this.dynoMinSpeed = dynoMinSpeed;
            this.dynoMaxSpeed = dynoMaxSpeed ?? redline;
            this.dynoHoldStep = dynoHoldStep;
            this.fuel = fuel ?? new FuelNode();
            this.throttle = throttle ?? new DirectThrottleLinkageNode(throttleGamma);
            this.simulationFrequency = simulationFrequency;
            this.hfGain = hfGain;
            this.jitter = jitter;
            this.noise = noise;
        }

        public EngineNode AddCrankshaft(CrankshaftNode crankshaft)
        {
            crankshafts.Add(crankshaft);
            return this;
        }

        public EngineNode AddCylinderBank(CylinderBankNode bank)
        {
            cylinderBanks.Add(bank);
            return this;
        }

        /// <summary>A turbocharger or supercharger (add_forced_induction); a second replaces the first.</summary>
        public EngineNode AddForcedInduction(ForcedInductionNode forcedInduction)
        {
            this.forcedInduction = forcedInduction;
            return this;
        }

        public EngineNode AddIgnitionModule(IgnitionModuleNode module)
        {
            ignitionModule = module;
            return this;
        }

        /// <summary>
        /// The description engine-sim builds from this engine, the <paramref name="vehicle"/> and the
        /// <paramref name="transmission"/> (by default the ones engine-sim's application uses when a script sets none).
        /// Each exhaust system has an impulse response entry of its own, in exhaust order, as every engine-sim export has.
        /// </summary>
        public EngineDescription Describe(VehicleNode vehicle = null, TransmissionNode transmission = null)
        {
            vehicle ??= VehicleNode.ApplicationDefault();
            transmission ??= TransmissionNode.ApplicationDefault();
            if (crankshafts.Count == 0) throw new InvalidOperationException("The engine has no crankshaft.");
            if (ignitionModule == null) throw new InvalidOperationException("The engine has no ignition module.");
            var functions = new FunctionRegistry();

            // Journals by crankshaft; intakes and exhausts in the order the cylinders first use them.
            var journals = new Dictionary<RodJournalNode, (int crankshaft, int index)>();
            for (int c = 0; c < crankshafts.Count; ++c)
            {
                for (int j = 0; j < crankshafts[c].rodJournals.Count; ++j) journals[crankshafts[c].rodJournals[j]] = (c, j);
            }

            var intakes = new List<IntakeNode>();
            var exhausts = new List<ExhaustSystemNode>();
            int cylinderCount = 0;
            foreach (CylinderBankNode bank in cylinderBanks)
            {
                if (bank.head == null) throw new InvalidOperationException("A cylinder bank has no head.");
                foreach (CylinderBankNode.Cylinder cylinder in bank.cylinders)
                {
                    if (!intakes.Contains(cylinder.intake)) intakes.Add(cylinder.intake);
                    if (!exhausts.Contains(cylinder.exhaustSystem)) exhausts.Add(cylinder.exhaustSystem);
                    ++cylinderCount;
                }
            }

            var e = new EngineParametersDescription
            {
                name = name,
                starterTorque = starterTorque,
                starterSpeed = starterSpeed,
                redline = redline,
                dynoMinSpeed = dynoMinSpeed,
                dynoMaxSpeed = dynoMaxSpeed,
                dynoHoldStep = dynoHoldStep,
                simulationFrequency = simulationFrequency,
                highFrequencyGain = hfGain,
                noise = noise,
                jitter = jitter,
                throttle = DescribeThrottle(throttle),
                crankshafts = new CrankshaftDescription[crankshafts.Count],
                cylinderBanks = new CylinderBankDescription[cylinderBanks.Count],
                cylinderHeads = new CylinderHeadDescription[cylinderBanks.Count],
                pistons = new PistonDescription[cylinderCount],
                connectingRods = new ConnectingRodDescription[cylinderCount],
                intakes = new IntakeDescription[intakes.Count],
                exhaustSystems = new ExhaustSystemDescription[exhausts.Count],
            };

            for (int c = 0; c < crankshafts.Count; ++c)
            {
                CrankshaftNode crank = crankshafts[c];
                var angles = new double[crank.rodJournals.Count];
                for (int j = 0; j < angles.Length; ++j) angles[j] = crank.rodJournals[j].angle;
                e.crankshafts[c] = new CrankshaftDescription
                {
                    mass = crank.mass, flywheelMass = crank.flywheelMass, momentOfInertia = crank.momentOfInertia, @throw = crank.@throw,
                    positionX = crank.positionX, positionY = crank.positionY, tdc = crank.tdc, frictionTorque = crank.frictionTorque,
                    rodJournalAngles = angles,
                };
            }

            for (int b = 0; b < cylinderBanks.Count; ++b)
            {
                CylinderBankNode bank = cylinderBanks[b];
                e.cylinderBanks[b] = new CylinderBankDescription
                {
                    angle = bank.angle, bore = bank.bore, deckHeight = bank.deckHeight, positionX = bank.positionX, positionY = bank.positionY,
                    displayDepth = bank.displayDepth, cylinderCount = bank.cylinders.Count,
                };
            }

            // Heads, bank by bank: their flow functions and camshafts' lobes are the first functions used.
            for (int b = 0; b < cylinderBanks.Count; ++b)
            {
                CylinderBankNode bank = cylinderBanks[b];
                CylinderHeadNode head = bank.head;
                var cylinders = new HeadCylinderDescription[bank.cylinders.Count];
                for (int i = 0; i < cylinders.Length; ++i)
                {
                    CylinderBankNode.Cylinder cylinder = bank.cylinders[i];
                    cylinders[i] = new HeadCylinderDescription
                    {
                        intake = intakes.IndexOf(cylinder.intake), exhaustSystem = exhausts.IndexOf(cylinder.exhaustSystem),
                        soundAttenuation = cylinder.soundAttenuation, headerPrimaryLength = cylinder.primaryLength,
                    };
                }

                var description = new CylinderHeadDescription
                {
                    combustionChamberVolume = head.chamberVolume,
                    intakeRunnerVolume = head.intakeRunnerVolume,
                    intakeRunnerCrossSectionArea = head.intakeRunnerCrossSectionArea,
                    exhaustRunnerVolume = head.exhaustRunnerVolume,
                    exhaustRunnerCrossSectionArea = head.exhaustRunnerCrossSectionArea,
                    flipDisplay = head.flipDisplay,
                };
                description.intakePortFlow = functions.IndexOf(head.intakePortFlow);
                description.exhaustPortFlow = functions.IndexOf(head.exhaustPortFlow);
                description.valvetrain = DescribeValvetrain(head.valvetrain, functions);
                description.cylinders = cylinders;
                e.cylinderHeads[b] = description;
            }

            // Pistons and rods bank by bank; each spark plug at its wire's angle, the last connection winning.
            var plugs = new SparkPlugDescription[cylinderCount];
            for (int i = 0; i < cylinderCount; ++i) plugs[i] = new SparkPlugDescription();
            var cylinderOf = new Dictionary<IgnitionWireNode, List<int>>();
            for (int b = 0, global = 0; b < cylinderBanks.Count; ++b)
            {
                foreach (CylinderBankNode.Cylinder cylinder in cylinderBanks[b].cylinders)
                {
                    if (!journals.TryGetValue(cylinder.rodJournal, out (int crankshaft, int index) journal))
                    {
                        throw new InvalidOperationException("A cylinder's rod journal is on no crankshaft.");
                    }

                    PistonNode piston = cylinder.piston;
                    e.pistons[global] = new PistonDescription
                    {
                        blowby = piston.blowby, compressionHeight = piston.parameters.compressionHeight,
                        wristPinPosition = piston.parameters.wristPinPosition, displacement = piston.parameters.displacement, mass = piston.parameters.mass,
                    };

                    ConnectingRodParameters rod = cylinder.connectingRod.parameters;
                    e.connectingRods[global] = new ConnectingRodDescription
                    {
                        mass = rod.mass, momentOfInertia = rod.momentOfInertia, centerOfMass = rod.centerOfMass, length = rod.length,
                        crankshaft = journal.crankshaft, journal = journal.index,
                    };

                    if (cylinder.ignitionWire != null)
                    {
                        if (!cylinderOf.TryGetValue(cylinder.ignitionWire, out List<int> wired)) cylinderOf[cylinder.ignitionWire] = wired = new List<int>();
                        wired.Add(global);
                    }

                    ++global;
                }
            }

            for (int i = 0; i < intakes.Count; ++i)
            {
                IntakeNode intake = intakes[i];
                e.intakes[i] = new IntakeDescription
                {
                    plenumVolume = intake.plenumVolume, plenumCrossSectionArea = intake.plenumCrossSectionArea, inputFlowK = intake.intakeFlowRate,
                    idleFlowK = intake.idleFlowRate, runnerFlowRate = intake.runnerFlowRate, molecularAfr = intake.molecularAfr,
                    idleThrottlePlatePosition = intake.idleThrottlePlatePosition, runnerLength = intake.runnerLength, velocityDecay = intake.velocityDecay,
                    atmospherePressure = intake.atmospherePressure, atmosphereTemperature = intake.atmosphereTemperature,
                };
            }

            var impulseResponses = new ImpulseResponseDescription[exhausts.Count];
            for (int i = 0; i < exhausts.Count; ++i)
            {
                ExhaustSystemNode exhaust = exhausts[i];
                if (exhaust.impulseResponse == null) throw new InvalidOperationException("An exhaust system has no impulse response.");
                impulseResponses[i] = new ImpulseResponseDescription { file = exhaust.impulseResponse.file, volume = exhaust.impulseResponse.volume };
                e.exhaustSystems[i] = new ExhaustSystemDescription
                {
                    length = exhaust.length, collectorCrossSectionArea = exhaust.collectorCrossSectionArea, outletFlowRate = exhaust.outletFlowRate,
                    primaryTubeLength = exhaust.primaryTubeLength, primaryFlowRate = exhaust.primaryFlowRate, velocityDecay = exhaust.velocityDecay,
                    audioVolume = exhaust.audioVolume, impulseResponse = i,
                };
            }

            foreach ((IgnitionWireNode wire, double angle) in ignitionModule.posts)
            {
                if (!cylinderOf.TryGetValue(wire, out List<int> wired)) continue;
                foreach (int cylinder in wired)
                {
                    plugs[cylinder].angle = angle;
                    plugs[cylinder].enabled = true;
                }
            }

            e.forcedInduction = forcedInduction?.Describe();

            e.ignitionModule = new IgnitionModuleDescription
            {
                timingCurve = functions.IndexOf(ignitionModule.timingCurve), revLimit = ignitionModule.revLimit,
                limiterDuration = ignitionModule.limiterDuration, plugs = plugs,
                // As the ignition module keeps it: a whole count, at least 1.
                sparksPerCycle = Math.Max(1, (int)ignitionModule.sparksPerCycle),
            };

            e.fuel = new FuelDescription
            {
                molecularMass = fuel.molecularMass, energyDensity = fuel.energyDensity, density = fuel.density, molecularAfr = fuel.molecularAfr,
                burningEfficiencyRandomness = fuel.burningEfficiencyRandomness, lowEfficiencyAttenuation = fuel.lowEfficiencyAttenuation,
                maxBurningEfficiency = fuel.maxBurningEfficiency, maxTurbulenceEffect = fuel.maxTurbulenceEffect, maxDilutionEffect = fuel.maxDilutionEffect,
                turbulenceToFlameSpeedRatio = functions.IndexOf(fuel.turbulenceToFlameSpeedRatio),
            };

            // The chambers' turbulence against mean piston speed, which engine-sim builds itself: half the speed, 30 samples.
            var meanPistonSpeedToTurbulence = new FunctionNode(1);
            for (int i = 0; i < 30; ++i)
            {
                double s = i;
                meanPistonSpeedToTurbulence.AddSample(s, s * 0.5);
            }

            e.combustionChamber = new CombustionChamberDescription
            {
                crankcasePressure = Units.Pressure(1.0, Units.atm), meanPistonSpeedToTurbulence = functions.IndexOf(meanPistonSpeedToTurbulence),
            };

            return new EngineDescription
            {
                format = EngineDescription.FormatName,
                version = EngineDescription.FormatVersion,
                functions = functions.Descriptions(),
                impulseResponses = impulseResponses,
                engine = e,
                vehicle = new VehicleDescription
                {
                    mass = vehicle.mass, dragCoefficient = vehicle.dragCoefficient, crossSectionArea = vehicle.crossSectionalArea,
                    diffRatio = vehicle.diffRatio, tireRadius = vehicle.tireRadius, rollingResistance = vehicle.rollingResistance,
                },
                transmission = new TransmissionDescription { gearRatios = transmission.gears.ToArray(), maxClutchTorque = transmission.maxClutchTorque },
            };
        }

        static ThrottleDescription DescribeThrottle(ThrottleNode throttle) => throttle switch
        {
            DirectThrottleLinkageNode direct => new ThrottleDescription { type = ThrottleDescription.DirectLinkage, gamma = direct.gamma },
            GovernorNode governor => new ThrottleDescription
            {
                type = ThrottleDescription.GovernorType, minSpeed = governor.minSpeed, maxSpeed = governor.maxSpeed, minVelocity = governor.minV,
                maxVelocity = governor.maxV, k_s = governor.k_s, k_d = governor.k_d, gamma = governor.gamma,
            },
            _ => throw new InvalidOperationException("Unknown throttle."),
        };

        static ValvetrainDescription DescribeValvetrain(ValvetrainNode valvetrain, FunctionRegistry functions)
        {
            switch (valvetrain)
            {
                case StandardValvetrainNode standard:
                {
                    var description = new ValvetrainDescription { type = ValvetrainDescription.Standard };
                    description.intakeCamshaft = DescribeCamshaft(standard.intakeCamshaft, functions);
                    description.exhaustCamshaft = DescribeCamshaft(standard.exhaustCamshaft, functions);
                    return description;
                }
                case VariableLiftValvetrainNode variable:
                {
                    var description = new ValvetrainDescription
                    {
                        type = ValvetrainDescription.VariableLift, minRpm = variable.minRpm, minSpeed = variable.minSpeed,
                        manifoldVacuum = variable.manifoldVacuum, minThrottlePosition = variable.minThrottlePosition,
                    };
                    description.intakeCamshaft = DescribeCamshaft(variable.intakeCamshaft, functions);
                    description.exhaustCamshaft = DescribeCamshaft(variable.exhaustCamshaft, functions);
                    description.highLiftIntakeCamshaft = DescribeCamshaft(variable.highLiftIntakeCamshaft, functions);
                    description.highLiftExhaustCamshaft = DescribeCamshaft(variable.highLiftExhaustCamshaft, functions);
                    return description;
                }
                default:
                    throw new InvalidOperationException("Unknown valvetrain.");
            }
        }

        static CamshaftDescription DescribeCamshaft(CamshaftNode camshaft, FunctionRegistry functions) => new CamshaftDescription
        {
            advance = camshaft.advance,
            baseRadius = camshaft.baseRadius,
            reductionRatio = camshaft.reductionRatio,
            lobeProfile = functions.IndexOf(camshaft.lobeProfile),
            lobeCenterlines = camshaft.lobes.ToArray(),
        };

        /// <summary>Functions written once each, numbered in the order first used.</summary>
        sealed class FunctionRegistry
        {
            readonly Dictionary<FunctionNode, int> indices = new Dictionary<FunctionNode, int>();
            readonly List<FunctionDescription> entries = new List<FunctionDescription>();

            public int IndexOf(FunctionNode function)
            {
                if (function == null) throw new InvalidOperationException("A function is missing.");
                if (indices.TryGetValue(function, out int index)) return index;

                // As engine-sim builds it: sized for its samples, each inserted in order of x.
                var built = new Function();
                built.Initialize(function.Samples.Count, function.filterRadius);
                foreach ((double x, double y) sample in function.Samples) built.AddSample(sample.x, sample.y);
                var xs = new double[built.Size];
                var ys = new double[built.Size];
                for (int i = 0; i < built.Size; ++i)
                {
                    xs[i] = built.X(i);
                    ys[i] = built.Y(i);
                }

                index = entries.Count;
                indices[function] = index;
                entries.Add(new FunctionDescription
                {
                    filterRadius = built.FilterRadius, inputScale = built.InputScale, outputScale = built.OutputScale, x = xs, y = ys,
                });
                return index;
            }

            public FunctionDescription[] Descriptions() => entries.ToArray();
        }
    }
}
