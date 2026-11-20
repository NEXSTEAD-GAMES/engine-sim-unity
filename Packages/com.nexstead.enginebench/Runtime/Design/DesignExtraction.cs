// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;
using U = Nexstead.EngineBench.Design.ScriptUnits;

namespace Nexstead.EngineBench.Design
{
    /// <summary>
    /// Reads an engine's design out of its description: what the generator derives (journals, lobe centrelines, plug angles,
    /// wiring, deck height) back into the layout, firing and parts it derives them from, every value into the design's
    /// units. Firing is read from where each piston reaches top dead centre (the crank's geometry), cam timing from there,
    /// and a spark set apart from top dead centre into the timing curve, the cycle counted from the first cylinder's top
    /// dead centre. The design builds the same engine: each value within a rounding step or two of the description's, the
    /// crank's journals in the generator's order, and where a script times its spark and cams off top dead centre, its
    /// cycle relabelled and that lead in the timing curve and lobe centres. Engines a design can't hold yet (more than two
    /// banks or one crank, cam lobes not made by the harmonic generator) have none.
    /// </summary>
    public static class DesignExtraction
    {
        /// <summary>How close angles (°) must be to count as one.</summary>
        const double SameAngle = 1e-6;
        /// <summary>How close a refitted cam lobe must come to the original, relative to its lift.</summary>
        const double LobeFit = 1e-9;

        /// <summary>The design <paramref name="description"/> holds; null if a design can't hold it, with <paramref name="reason"/> saying why.</summary>
        public static EngineDesign FromDescription(EngineDescription description, out string reason)
        {
            try
            {
                EngineDesign design = Read(description);
                List<string> problems = EngineGenerator.Problems(design);
                reason = problems.Count > 0 ? string.Join(" ", problems) : null;
                return problems.Count > 0 ? null : design;
            }
            catch (NotSupportedException e)
            {
                reason = e.Message;
                return null;
            }
        }

        static EngineDesign Read(EngineDescription description)
        {
            EngineParametersDescription e = description.engine;
            if (e.crankshafts.Length != 1) throw new NotSupportedException("Engines with more than one crankshaft can't be designed yet.");

            CylinderBankDescription[] banks = e.cylinderBanks;
            if (banks.Length > 2) throw new NotSupportedException($"Engines with {banks.Length} banks can't be designed yet.");
            int cylinders = 0;
            foreach (CylinderBankDescription bank in banks)
            {
                if (bank.cylinderCount != banks[0].cylinderCount) throw new NotSupportedException("Banks with different cylinder counts can't be designed yet.");
                cylinders += bank.cylinderCount;
            }

            CrankshaftDescription crank = e.crankshafts[0];
            PistonDescription piston = e.pistons[0];
            ConnectingRodDescription rod0 = e.connectingRods[0];
            var design = new EngineDesign
            {
                name = e.name,
                starterTorque = e.starterTorque,
                starterSpeed = e.starterSpeed / U.rpm,
                redline = e.redline / U.rpm,
                dynoMinSpeed = e.dynoMinSpeed / U.rpm,
                dynoMaxSpeed = e.dynoMaxSpeed / U.rpm,
                dynoHoldStep = e.dynoHoldStep / U.rpm,
                simulationFrequency = e.simulationFrequency,
                highFrequencyGain = e.highFrequencyGain,
                noise = e.noise,
                jitter = e.jitter,
                throttle = Throttle(e.throttle),
                cylinders = cylinders,
                bore = banks[0].bore,
                stroke = 2 * crank.@throw,
                rodLength = rod0.length,
                compressionHeight = piston.compressionHeight,
                pistonMass = piston.mass,
                wristPinPosition = piston.wristPinPosition,
                rodMass = rod0.mass,
                rodMomentOfInertia = rod0.momentOfInertia,
                rodCenterOfMass = rod0.centerOfMass,
                crankMass = crank.mass,
                flywheelMass = crank.flywheelMass,
                crankMomentOfInertia = crank.momentOfInertia,
                crankFrictionTorque = crank.frictionTorque,
                displayDepth = banks[0].displayDepth,
                intakes = Intakes(e.intakes),
                exhausts = Exhausts(e.exhaustSystems, description.impulseResponses),
                ignition = Ignition(e.ignitionModule, description.functions),
                forcedInduction = ForcedInduction(e),
                fuel = Fuel(e.fuel, description.functions),
                vehicle = Vehicle(description.vehicle),
                transmission = new TransmissionDesign
                {
                    gearRatios = (double[])description.transmission.gearRatios.Clone(), maxClutchTorque = description.transmission.maxClutchTorque,
                },
            };
            design.deckClearance = banks[0].deckHeight - (design.stroke / 2 + design.rodLength + design.compressionHeight);

            ReadLayout(design, banks, crank, e.connectingRods);
            double[] tdcPhases = ReadFiring(design, e, out double sparkLead);
            for (int i = 0; i < design.ignition.timingCurve.Length; ++i) design.ignition.timingCurve[i].advance += sparkLead;

            design.cylinderSettings = new CylinderDesign[cylinders];
            design.flipBankDisplay = new bool[banks.Length];
            var heads = new HeadDesign[banks.Length];
            for (int b = 0, c = 0; b < banks.Length; ++b)
            {
                CylinderHeadDescription head = e.cylinderHeads[b];
                design.flipBankDisplay[b] = head.flipDisplay;
                heads[b] = Head(head, description.functions);
                foreach (HeadCylinderDescription cylinder in head.cylinders)
                {
                    design.cylinderSettings[c] = new CylinderDesign
                    {
                        blowby = e.pistons[c].blowby / ScriptFunctions.K_28inH2O(1.0), soundAttenuation = cylinder.soundAttenuation,
                        headerPrimaryLength = cylinder.headerPrimaryLength, intake = cylinder.intake, exhaust = cylinder.exhaustSystem,
                    };
                    ++c;
                }
            }

            design.heads = heads.Length == 2 && SameHead(heads[0], heads[1]) ? new[] { heads[0] } : heads;
            design.camshaft = Camshafts(e, description.functions, tdcPhases[0] * U.deg);
            return design;
        }

        static void ReadLayout(EngineDesign design, CylinderBankDescription[] banks, CrankshaftDescription crank, ConnectingRodDescription[] rods)
        {
            double first = banks[0].angle / U.deg;
            if (banks.Length == 1)
            {
                if (Math.Abs(first) > SameAngle) throw new NotSupportedException("Inline engines with a leaning bank can't be designed yet.");
                design.layout = EngineLayout.Inline;
                return;
            }

            double second = banks[1].angle / U.deg;
            if (Math.Abs(first + second) > SameAngle) throw new NotSupportedException("Two banks not either side of upright can't be designed yet.");
            if (Math.Abs(Math.Abs(first) - 90) <= SameAngle)
            {
                design.layout = EngineLayout.Flat;
                design.banksMirrored = first < 0;
            }
            else
            {
                design.layout = EngineLayout.V;
                design.bankAngle = Math.Abs(second - first);
                design.banksMirrored = first > 0;
            }

            int journals = crank.rodJournalAngles.Length;
            if (design.layout == EngineLayout.V && journals == design.cylinders) design.splitPins = true;
            else if (design.layout == EngineLayout.V && journals != design.cylinders / 2)
            {
                throw new NotSupportedException($"A V with {journals} crank pins for {design.cylinders} cylinders can't be designed yet.");
            }
        }

        /// <summary>
        /// Each cylinder's compression top dead centre in the cycle (°): where the crank's geometry brings its piston up
        /// (journal, bank and the crank's top dead centre), on the turn its spark fires in. The design's firing angles are
        /// these, counted from the first: even firing becomes a firing order, anything else firing angles.
        /// <paramref name="sparkLead"/> is how far the sparks fire before top dead centre beyond their timing (° , the same for
        /// every cylinder). Returns the phases as the description counts them, for reading the cams.
        /// </summary>
        static double[] ReadFiring(EngineDesign design, EngineParametersDescription e, out double sparkLead)
        {
            int n = design.cylinders;
            var phases = new double[n];
            sparkLead = 0;
            CrankshaftDescription crank = e.crankshafts[0];
            for (int b = 0, c = 0; b < e.cylinderBanks.Length; ++b)
            {
                double direction = e.cylinderBanks[b].angle / U.deg + 90;
                for (int k = 0; k < e.cylinderBanks[b].cylinderCount; ++k, ++c)
                {
                    SparkPlugDescription plug = e.ignitionModule.plugs[c];
                    if (!plug.enabled) throw new NotSupportedException($"Cylinder {c + 1} has no spark: such engines can't be designed yet.");
                    double spark = Mod(plug.angle / U.deg, 720);
                    double journal = crank.rodJournalAngles[e.connectingRods[c].journal] / U.deg;
                    double top = Mod(crank.tdc / U.deg + journal - direction, 360);
                    // The top dead centre nearest the spark: the one it fires on.
                    double compression = Distance(top, spark) <= Distance(top + 360, spark) ? top : top + 360;
                    double lead = Wrapped(compression - spark);
                    if (c == 0) sparkLead = lead;
                    else if (Math.Abs(lead - sparkLead) > SameAngle) throw new NotSupportedException("Sparks timed apart from cylinder to cylinder can't be designed yet.");
                    phases[c] = compression;
                }
            }

            double first = double.MaxValue;
            foreach (double phase in phases) first = Math.Min(first, phase);
            var angles = new double[n];
            for (int c = 0; c < n; ++c) angles[c] = phases[c] - first;

            var order = new int[n];
            for (int c = 0; c < n; ++c) order[c] = c + 1;
            Array.Sort(order, (a, b) => angles[a - 1] != angles[b - 1] ? angles[a - 1].CompareTo(angles[b - 1]) : a.CompareTo(b));
            bool even = true;
            for (int k = 0; k < n && even; ++k) even = Math.Abs(angles[order[k] - 1] - 720.0 * k / n) <= SameAngle;
            design.firingOrder = order;
            design.firingAngles = even ? Array.Empty<double>() : angles;
            return phases;
        }

        static double Mod(double value, double period)
        {
            double r = value % period;
            return r < 0 ? r + period : r;
        }

        /// <summary>Apart on the cycle (°).</summary>
        static double Distance(double a, double b)
        {
            double d = Mod(a - b, 720);
            return Math.Min(d, 720 - d);
        }

        /// <param name="firing0">Cylinder 1's compression top dead centre in the description's cycle (rad).</param>
        static CamshaftDesign Camshafts(EngineParametersDescription e, FunctionDescription[] functions, double firing0)
        {
            ValvetrainDescription valvetrain = e.cylinderHeads[0].valvetrain;
            CamshaftDescription intake = valvetrain.intakeCamshaft, exhaust = valvetrain.exhaustCamshaft;
            if (intake.advance != exhaust.advance || intake.baseRadius != exhaust.baseRadius)
            {
                throw new NotSupportedException("Intake and exhaust camshafts timed or sized apart can't be designed yet.");
            }

            foreach (CylinderHeadDescription head in e.cylinderHeads)
            {
                ValvetrainDescription v = head.valvetrain;
                bool fourStroke = v.intakeCamshaft.reductionRatio == 0.5 && v.exhaustCamshaft.reductionRatio == 0.5
                    && (v.type != ValvetrainDescription.VariableLift
                        || (v.highLiftIntakeCamshaft.reductionRatio == 0.5 && v.highLiftExhaustCamshaft.reductionRatio == 0.5));
                if (!fourStroke)
                {
                    throw new NotSupportedException("A camshaft that doesn't turn once every two revolutions (two-stroke timing) can't be designed yet.");
                }
            }

            var cams = new CamshaftDesign
            {
                intakeLobe = Lobe(functions[intake.lobeProfile]),
                exhaustLobe = Lobe(functions[exhaust.lobeProfile]),
                intakeLobeCenter = IntakeCenter(intake, firing0),
                exhaustLobeCenter = ExhaustCenter(exhaust, firing0),
                advance = intake.advance / U.deg,
                baseRadius = intake.baseRadius,
            };

            if (valvetrain.type == ValvetrainDescription.VariableLift)
            {
                CamshaftDescription highIntake = valvetrain.highLiftIntakeCamshaft, highExhaust = valvetrain.highLiftExhaustCamshaft;
                cams.variableLift = new VariableLiftDesign
                {
                    enabled = true,
                    intakeLobe = Lobe(functions[highIntake.lobeProfile]),
                    exhaustLobe = Lobe(functions[highExhaust.lobeProfile]),
                    intakeLobeCenter = IntakeCenter(highIntake, firing0),
                    exhaustLobeCenter = ExhaustCenter(highExhaust, firing0),
                    advance = highIntake.advance / U.deg,
                    baseRadius = highIntake.baseRadius,
                    minRpm = valvetrain.minRpm / U.rpm,
                    minSpeed = valvetrain.minSpeed / U.mph,
                    manifoldVacuum = valvetrain.manifoldVacuum,
                    minThrottlePosition = valvetrain.minThrottlePosition,
                };
            }
            else if (valvetrain.type != ValvetrainDescription.Standard)
            {
                throw new NotSupportedException($"The '{valvetrain.type}' valvetrain can't be designed yet.");
            }

            return cams;
        }

        /// <summary>Degrees after overlap top dead centre to the first lobe's peak (the generator puts it at 360° + centre + firing angle).</summary>
        static double IntakeCenter(CamshaftDescription camshaft, double firing) => Wrapped((camshaft.lobeCenterlines[0] - 2 * Constants.pi - firing) / U.deg);

        /// <summary>Degrees from the first lobe's peak to overlap top dead centre (the generator puts it at 360° − centre + firing angle).</summary>
        static double ExhaustCenter(CamshaftDescription camshaft, double firing) => Wrapped((2 * Constants.pi + firing - camshaft.lobeCenterlines[0]) / U.deg);

        /// <summary>An angle (°) into −360 to 360: lobe centres are read modulo the cycle.</summary>
        static double Wrapped(double degrees)
        {
            double r = degrees % 720.0;
            if (r > 360) r -= 720;
            if (r < -360) r += 720;
            return r;
        }

        /// <summary>
        /// The harmonic lobe a profile was made with: lift at the centre, samples a side from the count, the duration and
        /// shape from the spacing and one sample; refitted and checked against every sample.
        /// </summary>
        static LobeDesign Lobe(FunctionDescription profile)
        {
            int count = profile.x.Length;
            if (count < 13 || count % 2 == 0) throw new NotSupportedException("A cam lobe not made by the harmonic generator can't be designed yet.");
            int centre = count / 2;
            double lift = profile.y[centre];
            int steps = (count + 1) / 2;
            double step = profile.x[centre + 1];
            double extents = step * (steps - 5.0);
            double k = Constants.pi / extents;

            // The shape from the sample nearest half lift (where it is best conditioned).
            int pick = centre + 1;
            for (int i = centre + 1; i < count; ++i)
            {
                if (profile.y[i] > 0 && Math.Abs(profile.y[i] - lift / 2) < Math.Abs(profile.y[pick] - lift / 2)) pick = i;
            }

            double gamma = Math.Log(profile.y[pick] / lift) / Math.Log(0.5 + 0.5 * Math.Cos(k * profile.x[pick]));
            double s = Math.Pow(2 * Units.Distance(50, Units.thou) / lift, 1 / gamma) - 1;
            double duration = 4 * Math.Acos(s) / k;
            var lobe = new LobeDesign { durationAt50Thou = duration / U.deg, gamma = gamma, lift = lift, steps = steps };

            // The refit must make the same profile.
            FunctionNode refit = ScriptFunctions.HarmonicCamLobe(lobe.durationAt50Thou * U.deg, lobe.gamma, lobe.lift, lobe.steps);
            var samples = new List<(double x, double y)>(refit.Samples);
            samples.Sort((a, b) => a.x.CompareTo(b.x));
            if (samples.Count != count) throw new NotSupportedException("A cam lobe not made by the harmonic generator can't be designed yet.");
            for (int i = 0; i < count; ++i)
            {
                if (Math.Abs(samples[i].x - profile.x[i]) > LobeFit * extents || Math.Abs(samples[i].y - profile.y[i]) > LobeFit * lift)
                {
                    throw new NotSupportedException("A cam lobe not made by the harmonic generator can't be designed yet.");
                }
            }

            return lobe;
        }

        static HeadDesign Head(CylinderHeadDescription head, FunctionDescription[] functions)
        {
            FunctionDescription intake = functions[head.intakePortFlow], exhaust = functions[head.exhaustPortFlow];
            return new HeadDesign
            {
                chamberVolume = head.combustionChamberVolume,
                intakeRunnerVolume = head.intakeRunnerVolume,
                intakeRunnerCrossSectionArea = head.intakeRunnerCrossSectionArea,
                exhaustRunnerVolume = head.exhaustRunnerVolume,
                exhaustRunnerCrossSectionArea = head.exhaustRunnerCrossSectionArea,
                intakeFlow = Flow(intake),
                exhaustFlow = Flow(exhaust),
                flowSmoothing = intake.filterRadius / U.thou,
            };
        }

        static FlowSample[] Flow(FunctionDescription function)
        {
            var samples = new FlowSample[function.x.Length];
            double perCfm = ScriptFunctions.K_28inH2O(1.0);
            for (int i = 0; i < samples.Length; ++i) samples[i] = new FlowSample(function.x[i] / U.thou, function.y[i] / perCfm);
            return samples;
        }

        static bool SameHead(HeadDesign a, HeadDesign b)
        {
            if (a.chamberVolume != b.chamberVolume || a.intakeRunnerVolume != b.intakeRunnerVolume ||
                a.intakeRunnerCrossSectionArea != b.intakeRunnerCrossSectionArea || a.exhaustRunnerVolume != b.exhaustRunnerVolume ||
                a.exhaustRunnerCrossSectionArea != b.exhaustRunnerCrossSectionArea || a.flowSmoothing != b.flowSmoothing)
            {
                return false;
            }

            return SameFlow(a.intakeFlow, b.intakeFlow) && SameFlow(a.exhaustFlow, b.exhaustFlow);
        }

        static bool SameFlow(FlowSample[] a, FlowSample[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i)
            {
                if (a[i].lift != b[i].lift || a[i].flow != b[i].flow) return false;
            }

            return true;
        }

        static IntakeDesign[] Intakes(IntakeDescription[] intakes)
        {
            double perCfm = ScriptFunctions.K_carb(1.0);
            var designs = new IntakeDesign[intakes.Length];
            for (int i = 0; i < intakes.Length; ++i)
            {
                IntakeDescription intake = intakes[i];
                designs[i] = new IntakeDesign
                {
                    plenumVolume = intake.plenumVolume, plenumCrossSectionArea = intake.plenumCrossSectionArea, intakeFlowRate = intake.inputFlowK / perCfm,
                    idleFlowRate = intake.idleFlowK / perCfm, runnerFlowRate = intake.runnerFlowRate / perCfm, runnerLength = intake.runnerLength,
                    idleThrottlePlatePosition = intake.idleThrottlePlatePosition, velocityDecay = intake.velocityDecay, molecularAfr = intake.molecularAfr,
                    atmospherePressure = intake.atmospherePressure, atmosphereTemperature = intake.atmosphereTemperature,
                };
            }

            return designs;
        }

        static ExhaustDesign[] Exhausts(ExhaustSystemDescription[] exhausts, ImpulseResponseDescription[] responses)
        {
            double perCfm = ScriptFunctions.K_carb(1.0);
            var designs = new ExhaustDesign[exhausts.Length];
            for (int i = 0; i < exhausts.Length; ++i)
            {
                ExhaustSystemDescription exhaust = exhausts[i];
                ImpulseResponseDescription response = responses[exhaust.impulseResponse];
                designs[i] = new ExhaustDesign
                {
                    length = exhaust.length, collectorCrossSectionArea = exhaust.collectorCrossSectionArea, outletFlowRate = exhaust.outletFlowRate / perCfm,
                    primaryFlowRate = exhaust.primaryFlowRate / perCfm, primaryTubeLength = exhaust.primaryTubeLength, velocityDecay = exhaust.velocityDecay,
                    audioVolume = exhaust.audioVolume, impulseResponse = response.file, impulseResponseVolume = response.volume,
                };
            }

            return designs;
        }

        static ForcedInductionDesign ForcedInduction(EngineParametersDescription e)
        {
            if (!e.HasForcedInduction) return new ForcedInductionDesign();

            ForcedInductionDescription f = e.forcedInduction;
            Simulation.ForcedInduction.Parameters p = EngineBuilder.ForcedInductionParameters(f);
            var design = new ForcedInductionDesign
            {
                type = p.type switch
                {
                    Simulation.ForcedInduction.Type.Roots => ForcedInductionKind.Roots,
                    Simulation.ForcedInduction.Type.TwinScrew => ForcedInductionKind.TwinScrew,
                    Simulation.ForcedInduction.Type.Centrifugal => ForcedInductionKind.Centrifugal,
                    _ => ForcedInductionKind.Turbocharger,
                },
                arrangement = p.arrangement, turbineAr = p.turbineAr, turbineEfficiency = p.turbineEfficiency, inertia = p.inertia,
                ballBearings = p.ballBearings, boost = p.boost, boostControl = p.boostControl, externalWastegate = p.externalWastegate,
                twinScroll = p.twinScroll, sequentialSpeed = p.sequentialSpeed / U.rpm, driveRatio = p.driveRatio, displacement = p.displacement,
                bypassValve = p.bypassValve, compressorDiameter = p.compressorDiameter, compressorFlow = p.compressorFlow,
                compressorEfficiency = p.compressorEfficiency, maxSpeed = p.maxSpeed / U.rpm, blowoffValve = p.blowoffValve,
                blowoffType = p.blowoffType, intercoolerEffectiveness = p.intercoolerEffectiveness, chargeVolume = p.chargeVolume,
                whistlePitch = p.whistlePitch,
            };

            // The script's turbocharger() and supercharger() fix what doesn't apply to them; a design holds only those.
            bool turbo = design.type == ForcedInductionKind.Turbocharger;
            ForcedInductionDescription made = turbo
                ? new TurbochargerNode().Describe()
                : new SuperchargerNode(type: p.type).Describe();
            bool fixedAsMade = turbo
                ? f.driveRatio == made.driveRatio && f.displacement == made.displacement && f.bypassValve == made.bypassValve
                : f.arrangement == made.arrangement && f.turbineAr == made.turbineAr && f.turbineEfficiency == made.turbineEfficiency
                    && f.inertia == made.inertia && f.ballBearings == made.ballBearings && f.boost == made.boost
                    && f.boostControl == made.boostControl && f.externalWastegate == made.externalWastegate
                    && f.twinScroll == made.twinScroll && f.sequentialSpeed == made.sequentialSpeed;
            if (!fixedAsMade)
            {
                throw new NotSupportedException(turbo
                    ? "A turbocharger with a crank drive, displacement or bypass valve can't be designed."
                    : "A supercharger with turbocharger settings (turbine, wastegate, boost target) can't be designed.");
            }

            return design;
        }

        static IgnitionDesign Ignition(IgnitionModuleDescription ignition, FunctionDescription[] functions)
        {
            if (ignition.sparksPerCycle != 1)
            {
                throw new NotSupportedException("More than one spark a cycle (two-stroke ignition) can't be designed yet.");
            }

            FunctionDescription curve = functions[ignition.timingCurve];
            var timing = new TimingSample[curve.x.Length];
            for (int i = 0; i < timing.Length; ++i) timing[i] = new TimingSample(curve.x[i] / U.rpm, curve.y[i] / U.deg);
            return new IgnitionDesign
            {
                timingCurve = timing, timingSmoothing = curve.filterRadius / U.rpm, revLimit = ignition.revLimit / U.rpm,
                limiterDuration = ignition.limiterDuration,
            };
        }

        static FuelDesign Fuel(FuelDescription fuel, FunctionDescription[] functions)
        {
            FunctionDescription curve = functions[fuel.turbulenceToFlameSpeedRatio];
            var design = new FuelDesign
            {
                molecularMass = fuel.molecularMass, energyDensity = fuel.energyDensity, density = fuel.density, molecularAfr = fuel.molecularAfr,
                maxBurningEfficiency = fuel.maxBurningEfficiency, burningEfficiencyRandomness = fuel.burningEfficiencyRandomness,
                lowEfficiencyAttenuation = fuel.lowEfficiencyAttenuation, maxTurbulenceEffect = fuel.maxTurbulenceEffect,
                maxDilutionEffect = fuel.maxDilutionEffect,
            };

            // Gasoline's own curve stays the default; another is the fuel's own.
            if (!IsDefaultTurbulence(curve))
            {
                design.turbulenceToFlameSpeedRatio = new CurveSample[curve.x.Length];
                for (int i = 0; i < curve.x.Length; ++i) design.turbulenceToFlameSpeedRatio[i] = new CurveSample(curve.x[i], curve.y[i]);
                design.turbulenceSmoothing = curve.filterRadius;
            }

            return design;
        }

        static bool IsDefaultTurbulence(FunctionDescription curve)
        {
            FunctionNode gasoline = FuelNode.DefaultTurbulenceToFlameSpeedRatio();
            if (curve.filterRadius != gasoline.filterRadius || curve.x.Length != gasoline.Samples.Count) return false;
            for (int i = 0; i < curve.x.Length; ++i)
            {
                if (curve.x[i] != gasoline.Samples[i].x || curve.y[i] != gasoline.Samples[i].y) return false;
            }

            return true;
        }

        static ThrottleDesign Throttle(ThrottleDescription throttle) =>
            throttle.type == ThrottleDescription.GovernorType
                ? new ThrottleDesign
                {
                    governed = true, governorMinSpeed = throttle.minSpeed / U.rpm, governorMaxSpeed = throttle.maxSpeed / U.rpm,
                    governorMinVelocity = throttle.minVelocity, governorMaxVelocity = throttle.maxVelocity, governorK_s = throttle.k_s,
                    governorK_d = throttle.k_d, governorGamma = throttle.gamma,
                }
                : new ThrottleDesign { gamma = throttle.gamma };

        static VehicleDesign Vehicle(VehicleDescription vehicle) => new VehicleDesign
        {
            mass = vehicle.mass, dragCoefficient = vehicle.dragCoefficient, crossSectionArea = vehicle.crossSectionArea, diffRatio = vehicle.diffRatio,
            tireRadius = vehicle.tireRadius, rollingResistance = vehicle.rollingResistance,
        };
    }
}
