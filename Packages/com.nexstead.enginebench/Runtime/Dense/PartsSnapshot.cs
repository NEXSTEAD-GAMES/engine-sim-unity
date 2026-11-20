// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense core's engine parts: the snapshot of a loaded managed Engine. Ordinary managed code, run
// once; it copies the current state exactly.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public static unsafe partial class PartsOps
    {
        /// <summary>Copies <paramref name="engine"/>'s parts, in their current state, into <paramref name="parts"/>.</summary>
        public static void Snapshot(Engine engine, DenseMemory memory, DenseParts* parts)
        {
            new PartsSnapshot(engine, memory, parts).Take();
        }

        sealed class PartsSnapshot
        {
            readonly Engine engine;
            readonly DenseMemory memory;
            readonly DenseParts* parts;

            readonly Dictionary<Crankshaft, int> crankshafts = new Dictionary<Crankshaft, int>();
            readonly Dictionary<CylinderBank, int> banks = new Dictionary<CylinderBank, int>();
            readonly Dictionary<CylinderHead, int> heads = new Dictionary<CylinderHead, int>();
            readonly Dictionary<Piston, int> pistons = new Dictionary<Piston, int>();
            readonly Dictionary<Intake, int> intakes = new Dictionary<Intake, int>();
            readonly Dictionary<ExhaustSystem, int> exhausts = new Dictionary<ExhaustSystem, int>();

            readonly List<Valvetrain> valvetrains = new List<Valvetrain>();
            readonly List<Camshaft> camshafts = new List<Camshaft>();
            readonly List<Function> functions = new List<Function>();

            public PartsSnapshot(Engine engine, DenseMemory memory, DenseParts* parts)
            {
                this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
                this.memory = memory;
                this.parts = parts;
            }

            public void Take()
            {
                DenseParts* p = parts;

                p->crankshaftCount = engine.GetCrankshaftCount();
                p->bankCount = engine.GetCylinderBankCount();
                p->cylinderCount = engine.GetCylinderCount();
                p->intakeCount = engine.GetIntakeCount();
                p->exhaustCount = engine.GetExhaustSystemCount();

                for (int i = 0; i < p->crankshaftCount; ++i) crankshafts.Add(engine.GetCrankshaft(i), i);
                for (int i = 0; i < p->bankCount; ++i) banks.Add(engine.GetCylinderBank(i), i);
                for (int i = 0; i < p->bankCount; ++i) heads.Add(engine.GetHead(i), i);
                for (int i = 0; i < p->cylinderCount; ++i) pistons.Add(engine.GetPiston(i), i);
                for (int i = 0; i < p->intakeCount; ++i) intakes.Add(engine.GetIntake(i), i);
                for (int i = 0; i < p->exhaustCount; ++i) exhausts.Add(engine.GetExhaustSystem(i), i);

                // Gas systems: see DenseParts.gases.
                int intakeGases = 3 * p->cylinderCount;
                int exhaustGases = intakeGases + 2 * p->intakeCount;
                ForcedInduction forcedInduction = engine.GetForcedInduction();
                int chargeGas = exhaustGases + 2 * p->exhaustCount;
                p->gasCount = chargeGas + (forcedInduction != null ? 1 : 0);
                p->gases = memory.Alloc<GasData>(p->gasCount);

                p->crankshafts = memory.Alloc<CrankshaftData>(p->crankshaftCount);
                for (int i = 0; i < p->crankshaftCount; ++i)
                {
                    Crankshaft crankshaft = engine.GetCrankshaft(i);
                    p->crankshafts[i] = new CrankshaftData { body = crankshaft.body.index, tdc = crankshaft.GetTdc() };
                }

                p->banks = memory.Alloc<CylinderBankData>(p->bankCount);
                for (int i = 0; i < p->bankCount; ++i)
                {
                    CylinderBank bank = engine.GetCylinderBank(i);
                    p->banks[i] = new CylinderBankData
                    {
                        bore = bank.GetBore(),
                        deckHeight = bank.GetDeckHeight(),
                        index = bank.GetIndex(),
                        dx = bank.GetDx(),
                        dy = bank.GetDy(),
                        x = bank.GetX(),
                        y = bank.GetY(),
                    };
                }

                p->heads = memory.Alloc<CylinderHeadData>(p->bankCount);
                for (int i = 0; i < p->bankCount; ++i) p->heads[i] = Head(engine.GetHead(i));

                p->pistons = memory.Alloc<PistonData>(p->cylinderCount);
                for (int i = 0; i < p->cylinderCount; ++i)
                {
                    Piston piston = engine.GetPiston(i);
                    p->pistons[i] = new PistonData
                    {
                        body = piston.body.index,
                        bank = banks[piston.GetCylinderBank()],
                        cylinderConstraint = piston.GetCylinderConstraint().index,
                        cylinderIndex = piston.GetCylinderIndex(),
                        rodBody = piston.GetRod().body.index,
                        compressionHeight = piston.GetCompressionHeight(),
                        displacement = piston.GetDisplacement(),
                        blowby_k = piston.GetBlowbyK(),
                    };
                }

                p->chambers = memory.Alloc<ChamberData>(p->cylinderCount);
                for (int i = 0; i < p->cylinderCount; ++i)
                {
                    int system = 3 * i;
                    p->chambers[i] = Chamber(engine.GetChamber(i), system, system + 1, system + 2);
                }

                p->intakes = memory.Alloc<IntakeData>(p->intakeCount);
                for (int i = 0; i < p->intakeCount; ++i)
                {
                    int system = intakeGases + 2 * i;
                    p->intakes[i] = IntakePart(engine.GetIntake(i), system, system + 1);
                }

                p->exhausts = memory.Alloc<ExhaustData>(p->exhaustCount);
                for (int i = 0; i < p->exhaustCount; ++i)
                {
                    int system = exhaustGases + 2 * i;
                    p->exhausts[i] = ExhaustPart(engine.GetExhaustSystem(i), system, system + 1);
                }

                if (forcedInduction != null)
                {
                    parts->gases[chargeGas] = Gas(forcedInduction.GetChargeSystem());
                    var fed = new int[forcedInduction.IntakeCount];
                    for (int i = 0; i < fed.Length; ++i) fed[i] = intakes[forcedInduction.GetIntake(i)];
                    int crank = forcedInduction.Crankshaft != null ? crankshafts[forcedInduction.Crankshaft] : -1;
                    p->forcedInduction = ForcedInductionOps.Snapshot(forcedInduction, memory, p, chargeGas, crank, fed);
                }

                Fuel fuel = engine.GetFuel();
                p->fuel = new FuelData
                {
                    molecularMass = fuel.GetMolecularMass(),
                    energyDensity = fuel.GetEnergyDensity(),
                    molecularAfr = fuel.GetMolecularAfr(),
                    maxBurningEfficiency = fuel.GetMaxBurningEfficiency(),
                    burningEfficiencyRandomness = fuel.GetBurningEfficiencyRandomness(),
                    lowEfficiencyAttenuation = fuel.GetLowEfficiencyAttenuation(),
                    maxTurbulenceEffect = fuel.GetMaxTurbulenceEffect(),
                    maxDilutionEffect = fuel.GetMaxDilutionEffect(),
                    turbulenceToFlameSpeedRatio = FunctionIndex(fuel.GetTurbulenceToFlameSpeedRatio()),
                };

                p->ignition = Ignition(engine.GetIgnitionModule());
                p->throttle = ThrottlePart(engine.GetThrottleController());
                p->throttleValue = engine.GetThrottle();

                // Valvetrains name camshafts and camshafts name functions, so these go last, in that order.
                p->valvetrainCount = valvetrains.Count;
                p->valvetrains = memory.Alloc<ValvetrainData>(valvetrains.Count);
                for (int i = 0; i < valvetrains.Count; ++i) p->valvetrains[i] = ValvetrainPart(valvetrains[i]);

                p->camshaftCount = camshafts.Count;
                p->camshafts = memory.Alloc<CamshaftData>(camshafts.Count);
                for (int i = 0; i < camshafts.Count; ++i) p->camshafts[i] = CamshaftPart(camshafts[i]);

                p->functionCount = functions.Count;
                p->functions = memory.Alloc<FunctionData>(functions.Count);
                for (int i = 0; i < functions.Count; ++i) p->functions[i] = FunctionPart(functions[i]);
            }

            CylinderHeadData Head(CylinderHead head)
            {
                CylinderBank bank = head.GetCylinderBank();
                int cylinderCount = bank.GetCylinderCount();

                var data = new CylinderHeadData
                {
                    cylinders = memory.Alloc<HeadCylinderData>(cylinderCount),
                    bank = banks[bank],
                    valvetrain = ValvetrainIndex(head.GetValvetrain()),
                    exhaustPortFlow = FunctionIndex(head.GetExhaustPortFlow()),
                    intakePortFlow = FunctionIndex(head.GetIntakePortFlow()),
                    intakeRunnerCrossSectionArea = head.GetIntakeRunnerCrossSectionArea(),
                    exhaustRunnerCrossSectionArea = head.GetExhaustRunnerCrossSectionArea(),
                    combustionChamberVolume = head.GetCombustionChamberVolume(),
                };

                for (int i = 0; i < cylinderCount; ++i)
                {
                    data.cylinders[i] = new HeadCylinderData
                    {
                        exhaustSystem = exhausts[head.GetExhaustSystem(i)],
                        intake = intakes[head.GetIntake(i)],
                        soundAttenuation = head.GetSoundAttenuation(i),
                        headerPrimaryLength = head.GetHeaderPrimaryLength(i),
                    };
                }

                return data;
            }

            ChamberData Chamber(CombustionChamber chamber, int system, int intakeRunner, int exhaustRunner)
            {
                parts->gases[system] = Gas(chamber.system);
                parts->gases[intakeRunner] = Gas(chamber.intakeRunnerAndManifold);
                parts->gases[exhaustRunner] = Gas(chamber.exhaustRunnerAndPrimary);

                CombustionChamber.FlameEvent flame = chamber.flameEvent;
                CombustionChamber.FrictionModelParams friction = chamber.frictionModel;
                var data = new ChamberData
                {
                    piston = pistons[chamber.GetPiston()],
                    head = heads[chamber.GetCylinderHead()],
                    system = system,
                    intakeRunnerAndManifold = intakeRunner,
                    exhaustRunnerAndPrimary = exhaustRunner,
                    meanPistonSpeedToTurbulence = FunctionIndex(chamber.meanPistonSpeedToTurbulence),
                    flameEvent = new FlameEventData
                    {
                        efficiency = flame.efficiency,
                        flameSpeed = flame.flameSpeed,
                        lastVolume = flame.lastVolume,
                        travel_x = flame.travel_x,
                        travel_y = flame.travel_y,
                        globalMix = Mix(flame.globalMix),
                    },
                    lit = chamber.lit,
                    ignitions = chamber.ignitions,
                    frictionModel = new FrictionModelData
                    {
                        frictionCoeff = friction.frictionCoeff,
                        breakawayFriction = friction.breakawayFriction,
                        breakawayFrictionVelocity = friction.breakawayFrictionVelocity,
                        viscousFrictionCoefficient = friction.viscousFrictionCoefficient,
                    },
                    intakeFlowRate = chamber.GetIntakeFlowRate(),
                    exhaustFlowRate = chamber.GetExhaustFlowRate(),
                    manifoldToRunnerFlowRate = chamber.GetManifoldToRunnerFlowRate(),
                    primaryToCollectorFlowRate = chamber.GetPrimaryToCollectorFlowRate(),
                    cylinderCrossSectionSurfaceArea = chamber.GetCylinderCrossSectionSurfaceArea(),
                    lastTimestepTotalExhaustFlow = chamber.GetLastTimestepExhaustFlow(),
                    lastTimestepTotalIntakeFlow = chamber.GetLastTimestepIntakeFlow(),
                    exhaustFlow = chamber.GetLastIterationExhaustFlow(),
                    crankcasePressure = chamber.GetCrankcasePressure(),
                    pressure = memory.Alloc<double>(ChamberData.StateSamples),
                    pistonSpeed = memory.Alloc<double>(ChamberData.StateSamples),
                    random = chamber.GetRandomState(),
                };

                for (int i = 0; i < ChamberData.StateSamples; ++i)
                {
                    data.pressure[i] = chamber.GetPressureSample(i);
                    data.pistonSpeed[i] = chamber.GetPistonSpeedSample(i);
                }

                return data;
            }

            IntakeData IntakePart(Intake intake, int system, int atmosphere)
            {
                parts->gases[system] = Gas(intake.system);
                parts->gases[atmosphere] = Gas(intake.GetAtmosphere());

                return new IntakeData
                {
                    system = system,
                    atmosphere = atmosphere,
                    throttle = intake.throttle,
                    flow = intake.flow,
                    crossSectionArea = intake.GetPlenumCrossSectionArea(),
                    inputFlowK = intake.GetInputFlowK(),
                    idleFlowK = intake.GetIdleFlowK(),
                    molecularAfr = intake.GetMolecularAfr(),
                    idleThrottlePlatePosition = intake.GetIdleThrottlePlatePosition(),
                    velocityDecay = intake.GetVelocityDecay(),
                    atmospherePressure = intake.GetAtmospherePressure(),
                    atmosphereTemperature = intake.GetAtmosphereTemperature(),
                    boosted = intake.GetForcedInduction() != null ? 1 : 0,
                };
            }

            ExhaustData ExhaustPart(ExhaustSystem exhaust, int system, int atmosphere)
            {
                parts->gases[system] = Gas(exhaust.GetSystem());
                parts->gases[atmosphere] = Gas(exhaust.GetAtmosphere());

                return new ExhaustData
                {
                    system = system,
                    atmosphere = atmosphere,
                    length = exhaust.GetLength(),
                    collectorCrossSectionArea = exhaust.GetCollectorCrossSectionArea(),
                    outletFlowRate = exhaust.GetOutletFlowRate(),
                    audioVolume = exhaust.GetAudioVolume(),
                    velocityDecay = exhaust.GetVelocityDecay(),
                    index = exhaust.GetIndex(),
                    flow = exhaust.GetFlow(),
                    turbine = exhaust.GetForcedInduction() != null ? 1 : 0,
                };
            }

            IgnitionData Ignition(IgnitionModule module)
            {
                int cylinderCount = module.GetCylinderCount();
                var data = new IgnitionData
                {
                    enabled = module.enabled,
                    timingCurve = FunctionIndex(module.GetTimingCurve()),
                    plugs = memory.Alloc<SparkPlugData>(cylinderCount),
                    crankshaft = crankshafts[module.GetCrankshaft()],
                    cylinderCount = cylinderCount,
                    lastCrankshaftAngle = module.GetLastCrankshaftAngle(),
                    revLimit = module.GetRevLimit(),
                    revLimitTimer = module.GetRevLimitTimer(),
                    limiterDuration = module.GetLimiterDuration(),
                    sparksPerCycle = module.GetSparksPerCycle(),
                };

                for (int i = 0; i < cylinderCount; ++i)
                {
                    IgnitionModule.SparkPlug plug = module.GetPlug(i);
                    data.plugs[i] = new SparkPlugData { angle = plug.angle, ignitionEvent = plug.ignitionEvent, enabled = plug.enabled };
                }

                return data;
            }

            static ThrottleData ThrottlePart(Throttle throttle)
            {
                var data = new ThrottleData { speedControl = throttle.GetSpeedControl() };
                switch (throttle)
                {
                    case DirectThrottleLinkage direct:
                        data.kind = ThrottleKind.DirectLinkage;
                        data.gamma = direct.GetGamma();
                        data.throttlePosition = direct.GetThrottlePosition();
                        break;
                    case Governor governor:
                        data.kind = ThrottleKind.Governor;
                        data.minSpeed = governor.GetMinSpeed();
                        data.maxSpeed = governor.GetMaxSpeed();
                        data.minVelocity = governor.GetMinVelocity();
                        data.maxVelocity = governor.GetMaxVelocity();
                        data.k_s = governor.GetKs();
                        data.k_d = governor.GetKd();
                        data.gamma = governor.GetGamma();
                        data.targetSpeed = governor.GetTargetSpeed();
                        data.currentThrottle = governor.GetCurrentThrottle();
                        data.velocity = governor.GetVelocity();
                        break;
                    default:
                        if (throttle.GetType() != typeof(Throttle))
                        {
                            throw new NotSupportedException($"The dense core has no mirror of the {throttle.GetType().Name} throttle.");
                        }

                        data.kind = ThrottleKind.Base;
                        break;
                }

                return data;
            }

            ValvetrainData ValvetrainPart(Valvetrain valvetrain)
            {
                switch (valvetrain)
                {
                    case StandardValvetrain standard:
                        return new ValvetrainData
                        {
                            kind = ValvetrainKind.Standard,
                            intakeCamshaft = CamshaftIndex(standard.GetActiveIntakeCamshaft()),
                            exhaustCamshaft = CamshaftIndex(standard.GetActiveExhaustCamshaft()),
                        };
                    case VariableLiftValvetrain variableLift:
                        // The dense cam switch reads this engine's manifold pressure, speed and throttle.
                        if (variableLift.GetEngine() != engine)
                        {
                            throw new NotSupportedException("The dense core mirrors a variable-lift valvetrain of its own engine only.");
                        }

                        return new ValvetrainData
                        {
                            kind = ValvetrainKind.VariableLift,
                            intakeCamshaft = CamshaftIndex(variableLift.GetIntakeCamshaft()),
                            exhaustCamshaft = CamshaftIndex(variableLift.GetExhaustCamshaft()),
                            highLiftIntakeCamshaft = CamshaftIndex(variableLift.GetHighLiftIntakeCamshaft()),
                            highLiftExhaustCamshaft = CamshaftIndex(variableLift.GetHighLiftExhaustCamshaft()),
                            minRpm = variableLift.GetMinRpm(),
                            manifoldVacuum = variableLift.GetManifoldVacuum(),
                            minThrottlePosition = variableLift.GetMinThrottlePosition(),
                            intakeOnHighLift = Flags(variableLift.IntakeOnHighLift),
                            exhaustOnHighLift = Flags(variableLift.ExhaustOnHighLift),
                        };
                    default:
                        throw new NotSupportedException($"The dense core has no mirror of the {valvetrain?.GetType().Name ?? "missing"} valvetrain.");
                }
            }

            // A managed bool per element, as 1 or 0.
            byte* Flags(bool[] values)
            {
                byte* flags = memory.Alloc<byte>(values.Length);
                for (int i = 0; i < values.Length; ++i) flags[i] = values[i] ? (byte)1 : (byte)0;
                return flags;
            }

            CamshaftData CamshaftPart(Camshaft camshaft)
            {
                int lobes = camshaft.GetLobeCount();
                var data = new CamshaftData
                {
                    crankshaft = crankshafts[camshaft.GetCrankshaft()],
                    lobeProfile = FunctionIndex(camshaft.GetLobeProfile()),
                    lobeAngles = memory.Alloc<double>(lobes),
                    lobes = lobes,
                    advance = camshaft.GetAdvance(),
                    reductionRatio = camshaft.GetReductionRatio(),
                };

                for (int i = 0; i < lobes; ++i) data.lobeAngles[i] = camshaft.GetLobeCenterline(i);

                return data;
            }

            FunctionData FunctionPart(Function function)
            {
                int size = function.Size;
                var data = new FunctionData
                {
                    x = memory.Alloc<double>(size),
                    y = memory.Alloc<double>(size),
                    size = size,
                    filterRadius = function.FilterRadius,
                    inputScale = function.InputScale,
                    outputScale = function.OutputScale,
                };

                for (int i = 0; i < size; ++i)
                {
                    data.x[i] = function.X(i);
                    data.y[i] = function.Y(i);
                }

                return data;
            }

            int ValvetrainIndex(Valvetrain valvetrain) => IndexOf(valvetrains, valvetrain);
            int CamshaftIndex(Camshaft camshaft) => IndexOf(camshafts, camshaft);
            int FunctionIndex(Function function) => IndexOf(functions, function);

            /// <summary>The item's index in <paramref name="list"/> (by reference), adding it if new; -1 for none.</summary>
            static int IndexOf<T>(List<T> list, T item) where T : class
            {
                if (item == null) return -1;

                for (int i = 0; i < list.Count; ++i)
                {
                    if (ReferenceEquals(list[i], item)) return i;
                }

                list.Add(item);
                return list.Count - 1;
            }

            static GasData Gas(GasSystem gas)
            {
                return new GasData
                {
                    n_mol = gas.N(),
                    E_k = gas.KineticEnergy(),
                    V = gas.Volume(),
                    momentum_x = gas.GetMomentum(0),
                    momentum_y = gas.GetMomentum(1),
                    mix = Mix(gas.GetMix()),
                    degreesOfFreedom = gas.DegreesOfFreedom(),
                    chokedFlowLimit = gas.GetChokedFlowLimit(),
                    chokedFlowFactorCached = gas.GetChokedFlowFactorCached(),
                    width = gas.GetWidth(),
                    height = gas.GetHeight(),
                    dx = gas.GetDx(),
                    dy = gas.GetDy(),
                };
            }

            static MixData Mix(GasSystem.Mix mix) => new MixData { p_fuel = mix.p_fuel, p_inert = mix.p_inert, p_o2 = mix.p_o2 };
        }
    }
}
