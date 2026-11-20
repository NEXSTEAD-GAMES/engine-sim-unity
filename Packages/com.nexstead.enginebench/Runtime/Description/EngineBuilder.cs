// Replays an engine description through the simulation's Initialize and setter calls, in
// the order the upstream script layer makes them (engine-sim scripting/include/engine_node.h
// and the node classes it uses). Mirrors the C++ reference's engine builder.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>An engine with the vehicle and transmission it drives.</summary>
    public sealed class EngineAssembly
    {
        public Engine engine;
        public Vehicle vehicle;
        public Transmission transmission;
    }

    public static class EngineBuilder
    {
        public static EngineAssembly Build(EngineDescription description)
        {
            if (description == null) throw new ArgumentNullException(nameof(description));
            if (description.format != EngineDescription.FormatName || description.version != EngineDescription.FormatVersion)
            {
                throw new ArgumentException(
                    $"Unsupported engine description '{description.format}' version {description.version}; expected " +
                    $"'{EngineDescription.FormatName}' version {EngineDescription.FormatVersion}.");
            }

            var functions = BuildFunctions(description.functions);
            var impulseResponses = BuildImpulseResponses(description.impulseResponses);

            return new EngineAssembly
            {
                engine = BuildEngine(description.engine, functions, impulseResponses),
                vehicle = BuildVehicle(description.vehicle),
                transmission = BuildTransmission(description.transmission),
            };
        }

        static Function[] BuildFunctions(FunctionDescription[] descriptions)
        {
            var functions = new Function[descriptions.Length];
            for (int i = 0; i < descriptions.Length; ++i)
            {
                FunctionDescription f = descriptions[i];
                if (f.x.Length != f.y.Length) throw new ArgumentException($"Function {i}: {f.x.Length} x values, {f.y.Length} y values.");

                // As FunctionNode::generate: capacity = sample count.
                var function = new Function();
                function.Initialize(f.x.Length, f.filterRadius);
                function.SetInputScale(f.inputScale);
                function.SetOutputScale(f.outputScale);
                for (int j = 0; j < f.x.Length; ++j)
                {
                    function.AddSample(f.x[j], f.y[j]);
                }

                functions[i] = function;
            }

            return functions;
        }

        static ImpulseResponse[] BuildImpulseResponses(ImpulseResponseDescription[] descriptions)
        {
            var impulseResponses = new ImpulseResponse[descriptions.Length];
            for (int i = 0; i < descriptions.Length; ++i)
            {
                var impulseResponse = new ImpulseResponse();
                impulseResponse.Initialize(descriptions[i].file, descriptions[i].volume);
                impulseResponses[i] = impulseResponse;
            }

            return impulseResponses;
        }

        static Throttle BuildThrottle(ThrottleDescription t)
        {
            switch (t.type)
            {
                case ThrottleDescription.DirectLinkage:
                {
                    var throttle = new DirectThrottleLinkage();
                    throttle.Initialize(new DirectThrottleLinkage.Parameters { gamma = t.gamma });
                    return throttle;
                }
                case ThrottleDescription.GovernorType:
                {
                    var throttle = new Governor();
                    throttle.Initialize(new Governor.Parameters
                    {
                        minSpeed = t.minSpeed,
                        maxSpeed = t.maxSpeed,
                        minVelocity = t.minVelocity,
                        maxVelocity = t.maxVelocity,
                        k_s = t.k_s,
                        k_d = t.k_d,
                        gamma = t.gamma,
                    });
                    return throttle;
                }
                default:
                    throw new ArgumentException($"Unknown throttle type '{t.type}'.");
            }
        }

        static Camshaft BuildCamshaft(CamshaftDescription c, Crankshaft crankshaft, Function[] functions)
        {
            var camshaft = new Camshaft();
            camshaft.Initialize(new Camshaft.Parameters
            {
                lobes = c.lobeCenterlines.Length,
                advance = c.advance,
                crankshaft = crankshaft,
                lobeProfile = functions[c.lobeProfile],
                baseRadius = c.baseRadius,
                reductionRatio = c.reductionRatio,
            });
            for (int i = 0; i < c.lobeCenterlines.Length; ++i)
            {
                camshaft.SetLobeCenterline(i, c.lobeCenterlines[i]);
            }

            return camshaft;
        }

        static Valvetrain BuildValvetrain(ValvetrainDescription v, Engine engine, Function[] functions)
        {
            Crankshaft crankshaft = engine.GetCrankshaft(0);
            switch (v.type)
            {
                case ValvetrainDescription.Standard:
                {
                    var valvetrain = new StandardValvetrain();
                    valvetrain.Initialize(new StandardValvetrain.Parameters
                    {
                        intakeCamshaft = BuildCamshaft(v.intakeCamshaft, crankshaft, functions),
                        exhaustCamshaft = BuildCamshaft(v.exhaustCamshaft, crankshaft, functions),
                    });
                    return valvetrain;
                }
                case ValvetrainDescription.VariableLift:
                {
                    var valvetrain = new VariableLiftValvetrain();
                    valvetrain.Initialize(new VariableLiftValvetrain.Parameters
                    {
                        intakeCamshaft = BuildCamshaft(v.intakeCamshaft, crankshaft, functions),
                        exhaustCamshaft = BuildCamshaft(v.exhaustCamshaft, crankshaft, functions),
                        highLiftIntakeCamshaft = BuildCamshaft(v.highLiftIntakeCamshaft, crankshaft, functions),
                        highLiftExhaustCamshaft = BuildCamshaft(v.highLiftExhaustCamshaft, crankshaft, functions),
                        minRpm = v.minRpm,
                        minSpeed = v.minSpeed,
                        minThrottlePosition = v.minThrottlePosition,
                        manifoldVacuum = v.manifoldVacuum,
                        engine = engine,
                    });
                    return valvetrain;
                }
                default:
                    throw new ArgumentException($"Unknown valvetrain type '{v.type}'.");
            }
        }

        static Engine BuildEngine(EngineParametersDescription e, Function[] functions, ImpulseResponse[] impulseResponses)
        {
            if (e.cylinderHeads.Length != e.cylinderBanks.Length) throw new ArgumentException("One cylinder head per bank expected.");
            if (e.connectingRods.Length != e.pistons.Length) throw new ArgumentException("One connecting rod per piston expected.");

            var engine = new Engine();
            var parameters = new Engine.Parameters
            {
                crankshaftCount = e.crankshafts.Length,
                cylinderBanks = e.cylinderBanks.Length,
                cylinderCount = e.pistons.Length,
                exhaustSystemCount = e.exhaustSystems.Length,
                intakeCount = e.intakes.Length,
                name = e.name,
                starterTorque = e.starterTorque,
                starterSpeed = e.starterSpeed,
                redline = e.redline,
                dynoMinSpeed = e.dynoMinSpeed,
                dynoMaxSpeed = e.dynoMaxSpeed,
                dynoHoldStep = e.dynoHoldStep,
                throttle = BuildThrottle(e.throttle),
                initialSimulationFrequency = e.simulationFrequency,
                initialHighFrequencyGain = e.highFrequencyGain,
                initialNoise = e.noise,
                initialJitter = e.jitter,
            };
            engine.Initialize(parameters);

            for (int i = 0; i < parameters.crankshaftCount; ++i)
            {
                CrankshaftDescription c = e.crankshafts[i];
                Crankshaft crankshaft = engine.GetCrankshaft(i);
                crankshaft.Initialize(new Crankshaft.Parameters
                {
                    mass = c.mass,
                    flywheelMass = c.flywheelMass,
                    momentOfInertia = c.momentOfInertia,
                    crankThrow = c.@throw,
                    pos_x = c.positionX,
                    pos_y = c.positionY,
                    tdc = c.tdc,
                    frictionTorque = c.frictionTorque,
                    rodJournals = c.rodJournalAngles.Length,
                });
                for (int j = 0; j < c.rodJournalAngles.Length; ++j)
                {
                    crankshaft.SetRodJournalAngle(j, c.rodJournalAngles[j]);
                }
            }

            int cylinderBase = 0;
            for (int b = 0; b < parameters.cylinderBanks; ++b)
            {
                CylinderBankDescription bj = e.cylinderBanks[b];
                int cylinderCount = bj.cylinderCount;

                CylinderBank bank = engine.GetCylinderBank(b);
                bank.Initialize(new CylinderBank.Parameters
                {
                    crankshaft = engine.GetCrankshaft(0),
                    positionX = bj.positionX,
                    positionY = bj.positionY,
                    angle = bj.angle,
                    bore = bj.bore,
                    deckHeight = bj.deckHeight,
                    displayDepth = bj.displayDepth,
                    cylinderCount = cylinderCount,
                    index = b,
                });

                for (int j = 0; j < cylinderCount; ++j)
                {
                    int g = cylinderBase + j;
                    PistonDescription pj = e.pistons[g];
                    ConnectingRodDescription rj = e.connectingRods[g];
                    Piston piston = engine.GetPiston(g);
                    ConnectingRod rod = engine.GetConnectingRod(g);

                    piston.Initialize(new Piston.Parameters
                    {
                        Rod = rod,
                        Bank = bank,
                        CylinderIndex = j,
                        BlowbyFlowCoefficient = pj.blowby,
                        CompressionHeight = pj.compressionHeight,
                        WristPinPosition = pj.wristPinPosition,
                        Displacement = pj.displacement,
                        mass = pj.mass,
                    });

                    rod.Initialize(new ConnectingRod.Parameters
                    {
                        mass = rj.mass,
                        momentOfInertia = rj.momentOfInertia,
                        centerOfMass = rj.centerOfMass,
                        length = rj.length,
                        piston = piston,
                        crankshaft = engine.GetCrankshaft(rj.crankshaft),
                        journal = rj.journal,
                    });
                }

                CylinderHeadDescription hj = e.cylinderHeads[b];
                CylinderHead head = engine.GetHead(b);
                head.Initialize(new CylinderHead.Parameters
                {
                    Bank = bank,
                    Valvetrain = BuildValvetrain(hj.valvetrain, engine, functions),
                    IntakePortFlow = functions[hj.intakePortFlow],
                    ExhaustPortFlow = functions[hj.exhaustPortFlow],
                    CombustionChamberVolume = hj.combustionChamberVolume,
                    IntakeRunnerVolume = hj.intakeRunnerVolume,
                    IntakeRunnerCrossSectionArea = hj.intakeRunnerCrossSectionArea,
                    ExhaustRunnerVolume = hj.exhaustRunnerVolume,
                    ExhaustRunnerCrossSectionArea = hj.exhaustRunnerCrossSectionArea,
                    FlipDisplay = hj.flipDisplay,
                });

                // As CylinderBankNode::generate: each cylinder (re)initializes the intake and
                // exhaust system it uses.
                if (hj.cylinders.Length != cylinderCount) throw new ArgumentException($"Head {b}: cylinder count differs from its bank.");
                for (int j = 0; j < cylinderCount; ++j)
                {
                    HeadCylinderDescription cj = hj.cylinders[j];

                    ExhaustSystem exhaust = engine.GetExhaustSystem(cj.exhaustSystem);
                    exhaust.Initialize(ExhaustParameters(e.exhaustSystems[cj.exhaustSystem], impulseResponses));

                    Intake intake = engine.GetIntake(cj.intake);
                    intake.Initialize(IntakeParameters(e.intakes[cj.intake]));

                    head.SetIntake(j, intake);
                    head.SetExhaustSystem(j, exhaust);
                    head.SetSoundAttenuation(j, cj.soundAttenuation);
                    head.SetHeaderPrimaryLength(j, cj.headerPrimaryLength);
                }

                cylinderBase += cylinderCount;
            }

            if (cylinderBase != parameters.cylinderCount) throw new ArgumentException("Bank cylinder counts don't add up to the piston count.");

            // As the script: after the intakes, exhausts and crankshafts
            if (e.HasForcedInduction)
            {
                var forcedInduction = new ForcedInduction();
                forcedInduction.Initialize(ForcedInductionParameters(e.forcedInduction));
                engine.SetForcedInduction(forcedInduction);
            }

            {
                IgnitionModuleDescription im = e.ignitionModule;
                IgnitionModule module = engine.GetIgnitionModule();
                module.Initialize(new IgnitionModule.Parameters
                {
                    crankshaft = engine.GetCrankshaft(0),
                    cylinderCount = parameters.cylinderCount,
                    revLimit = im.revLimit,
                    timingCurve = functions[im.timingCurve],
                    limiterDuration = im.limiterDuration,
                    sparksPerCycle = im.sparksPerCycle,
                });
                for (int i = 0; i < parameters.cylinderCount; ++i)
                {
                    if (im.plugs[i].enabled)
                    {
                        module.SetFiringOrder(i, im.plugs[i].angle);
                    }
                }
            }

            {
                FuelDescription f = e.fuel;
                engine.GetFuel().Initialize(new Fuel.Parameters
                {
                    molecularMass = f.molecularMass,
                    energyDensity = f.energyDensity,
                    density = f.density,
                    molecularAfr = f.molecularAfr,
                    burningEfficiencyRandomness = f.burningEfficiencyRandomness,
                    lowEfficiencyAttenuation = f.lowEfficiencyAttenuation,
                    maxBurningEfficiency = f.maxBurningEfficiency,
                    maxTurbulenceEffect = f.maxTurbulenceEffect,
                    maxDilutionEffect = f.maxDilutionEffect,
                    turbulenceToFlameSpeedRatio = functions[f.turbulenceToFlameSpeedRatio],
                });
            }

            {
                CombustionChamberDescription c = e.combustionChamber;
                for (int i = 0; i < parameters.cylinderCount; ++i)
                {
                    Piston piston = engine.GetPiston(i);
                    engine.GetChamber(i).Initialize(new CombustionChamber.Parameters
                    {
                        CrankcasePressure = c.crankcasePressure,
                        Fuel = engine.GetFuel(),
                        StartingPressure = Units.Pressure(1.0, Units.atm),
                        StartingTemperature = Units.Celcius(25.0),
                        MeanPistonSpeedToTurbulence = functions[c.meanPistonSpeedToTurbulence],
                        Piston = piston,
                        Head = engine.GetHead(piston.GetCylinderBank().GetIndex()),
                    });
                }
            }

            return engine;
        }

        static ExhaustSystem.Parameters ExhaustParameters(ExhaustSystemDescription x, ImpulseResponse[] impulseResponses)
        {
            return new ExhaustSystem.Parameters
            {
                length = x.length,
                collectorCrossSectionArea = x.collectorCrossSectionArea,
                outletFlowRate = x.outletFlowRate,
                primaryTubeLength = x.primaryTubeLength,
                primaryFlowRate = x.primaryFlowRate,
                velocityDecay = x.velocityDecay,
                audioVolume = x.audioVolume,
                impulseResponse = impulseResponses[x.impulseResponse],
            };
        }

        public static ForcedInduction.Parameters ForcedInductionParameters(ForcedInductionDescription f) => new ForcedInduction.Parameters
        {
            type = f.type switch
            {
                ForcedInductionDescription.Turbocharger => ForcedInduction.Type.Turbocharger,
                ForcedInductionDescription.Roots => ForcedInduction.Type.Roots,
                ForcedInductionDescription.TwinScrew => ForcedInduction.Type.TwinScrew,
                ForcedInductionDescription.Centrifugal => ForcedInduction.Type.Centrifugal,
                _ => throw new ArgumentException($"Unknown forced induction type '{f.type}'."),
            },
            arrangement = f.arrangement switch
            {
                ForcedInductionDescription.Single => ForcedInduction.Arrangement.Single,
                ForcedInductionDescription.Twin => ForcedInduction.Arrangement.Twin,
                ForcedInductionDescription.Sequential => ForcedInduction.Arrangement.Sequential,
                _ => throw new ArgumentException($"Unknown forced induction arrangement '{f.arrangement}'."),
            },
            compressorDiameter = f.compressorDiameter,
            compressorFlow = f.compressorFlow,
            compressorEfficiency = f.compressorEfficiency,
            maxSpeed = f.maxSpeed,
            turbineAr = f.turbineAr,
            turbineEfficiency = f.turbineEfficiency,
            inertia = f.inertia,
            ballBearings = f.ballBearings,
            boost = f.boost,
            blowoffValve = f.blowoffValve,
            blowoffType = f.blowoffType switch
            {
                ForcedInductionDescription.Atmospheric => ForcedInduction.BlowoffType.Atmospheric,
                ForcedInductionDescription.Recirculating => ForcedInduction.BlowoffType.Recirculating,
                ForcedInductionDescription.NoBlowoff => ForcedInduction.BlowoffType.None,
                _ => throw new ArgumentException($"Unknown blow-off type '{f.blowoffType}'."),
            },
            boostControl = f.boostControl switch
            {
                ForcedInductionDescription.Electronic => ForcedInduction.BoostControl.Electronic,
                ForcedInductionDescription.Spring => ForcedInduction.BoostControl.Spring,
                _ => throw new ArgumentException($"Unknown boost control '{f.boostControl}'."),
            },
            externalWastegate = f.externalWastegate,
            twinScroll = f.twinScroll,
            whistlePitch = f.whistlePitch,
            driveRatio = f.driveRatio,
            displacement = f.displacement,
            bypassValve = f.bypassValve,
            intercoolerEffectiveness = f.intercoolerEffectiveness,
            chargeVolume = f.chargeVolume,
            sequentialSpeed = f.sequentialSpeed,
        };

        static Intake.Parameters IntakeParameters(IntakeDescription x)
        {
            return new Intake.Parameters
            {
                volume = x.plenumVolume,
                CrossSectionArea = x.plenumCrossSectionArea,
                InputFlowK = x.inputFlowK,
                IdleFlowK = x.idleFlowK,
                RunnerFlowRate = x.runnerFlowRate,
                MolecularAfr = x.molecularAfr,
                IdleThrottlePlatePosition = x.idleThrottlePlatePosition,
                RunnerLength = x.runnerLength,
                VelocityDecay = x.velocityDecay,
                AtmospherePressure = x.atmospherePressure,
                AtmosphereTemperature = x.atmosphereTemperature,
            };
        }

        static Vehicle BuildVehicle(VehicleDescription v)
        {
            var vehicle = new Vehicle();
            vehicle.Initialize(new Vehicle.Parameters
            {
                mass = v.mass,
                dragCoefficient = v.dragCoefficient,
                crossSectionArea = v.crossSectionArea,
                diffRatio = v.diffRatio,
                tireRadius = v.tireRadius,
                rollingResistance = v.rollingResistance,
            });
            return vehicle;
        }

        static Transmission BuildTransmission(TransmissionDescription t)
        {
            var transmission = new Transmission();
            transmission.Initialize(new Transmission.Parameters
            {
                GearCount = t.gearRatios.Length,
                GearRatios = t.gearRatios,
                MaxClutchTorque = t.maxClutchTorque,
            });
            return transmission;
        }
    }
}
