// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using System.IO;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Simulation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using U = Nexstead.EngineBench.Design.ScriptUnits;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// The Bench's Design section: the engine's design (<see cref="EngineDesign"/>): layout, cylinders and firing, the bottom
    /// end, camshafts, head, intake, exhausts, forced induction and limits, with what they make (displacement, compression, piston speed) and
    /// what keeps them from building; every other value under Advanced parts. Every value has a slider within a range wider
    /// than the presets span (table cells are kept within theirs). The crank, cam timing and ignition follow from the layout
    /// and the firing (<see cref="EngineGenerator"/>). It changes the Bench's in-memory copy of the engine: changes go
    /// through Undo, reach the project when the Bench saves, and play on Restart. An engine no design can hold yet says
    /// so.
    /// </summary>
    sealed class DesignSection : VisualElement
    {
        const string CustomOrder = "Custom…";
        /// <summary>Above this mean piston speed (m/s) real engines don't hold together.</summary>
        const double PistonSpeedLimit = 25;
        /// <summary>Below this rod to stroke ratio the rod's angle loads the bore walls hard.</summary>
        const double RodRatioLimit = 1.4;

        readonly EngineAsset engine;
        readonly Action designChanged;
        readonly List<Action> refreshers = new List<Action>();
        readonly VisualElement checks = new VisualElement();
        readonly Label figures = new Label();

        /// <param name="designChanged">Called after each change: the window tells whether a Restart is due.</param>
        public DesignSection(EngineAsset engine, Action designChanged)
        {
            this.engine = engine;
            this.designChanged = designChanged;
            if (engine.Design == null)
            {
                DesignExtraction.FromDescription(engine.Description, out string reason);
                Add(new HelpBox($"This engine can't be designed yet: {reason} Its sound can still be tuned.", HelpBoxMessageType.Info));
                return;
            }

            Hint(this, "The crank, cam timing and ignition follow from the layout and the firing. Changes play on Restart.");
            figures.AddToClassList("design__figures");
            Add(figures);
            Add(checks);

            BuildLayout();
            BuildBottomEnd();
            BuildCamshafts();
            BuildBreathing();
            BuildForcedInduction();
            BuildLimits();
            BuildAdvanced();
            Refresh();
        }

        EngineDesign Design => engine.Design;

        /// <summary>Shows the design as it is now (after an undo, say).</summary>
        public void Refresh()
        {
            if (Design == null) return;
            foreach (Action refresh in refreshers) refresh();
            ShowFiguresAndChecks();
        }

        // ---- Sections ----

        void BuildLayout()
        {
            Caption(this, "Layout");
            var layout = new EnumField("Layout", Design.layout) { tooltip = "Inline: one row. V: two banks at the bank angle. Flat: two opposed banks." };
            layout.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is EngineLayout picked && picked != Design.layout) Change("Change engine layout", d => d.Reshape(picked, d.cylinders));
            });
            refreshers.Add(() => layout.SetValueWithoutNotify(Design.layout));
            Add(layout);

            Whole(this, "Cylinders", "Two banks take an even number.", 1, 16, () => Design.cylinders,
                count => Design.Reshape(Design.layout, count), "Change cylinder count");

            VisualElement bankAngle = Range(this, "Bank angle (°)", "The angle between the banks. Paired cylinders share a crank pin, so the " +
                "firing that works depends on it: 90° for an even-firing V8, 60° for a V12, 120° for a V6.", 10, 170, () => Design.bankAngle,
                v => Design.bankAngle = v, "Change bank angle");
            VisualElement mirrored = Flag(this, "Banks mirrored", "Which side the first bank sits on.", () => Design.banksMirrored,
                v => Design.banksMirrored = v, "Mirror banks");
            VisualElement split = Flag(this, "Split crank pins", "A crank pin for each cylinder, not one shared by each pair across the banks: " +
                "a V then fires evenly at any bank angle.", () => Design.splitPins, v => Design.splitPins = v, "Change crank pins");
            refreshers.Add(() =>
            {
                bankAngle.EnableInClassList("bench__section--hidden", Design.layout != EngineLayout.V);
                mirrored.EnableInClassList("bench__section--hidden", Design.layout == EngineLayout.Inline);
                split.EnableInClassList("bench__section--hidden", Design.layout != EngineLayout.V);
            });

            Flag(this, "Odd firing", "Set each cylinder's firing angle, for engines that don't fire at even intervals (V-twins, odd-fire V6s).",
                () => Design.firingAngles.Length > 0, odd => Design.firingAngles = odd ? StartingAngles(Design) : Array.Empty<double>(), "Change firing");

            var order = new DropdownField("Firing order")
            {
                tooltip = "The cylinders in the order they fire, numbered bank by bank (on two banks, the first bank is 1 to half). Each fires " +
                          "an even share of the cycle after the one before.",
            };
            var custom = new TextField("Custom order") { isDelayed = true, tooltip = "Cylinder numbers separated by dashes: 1-3-4-2." };
            order.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == CustomOrder)
                {
                    custom.RemoveFromClassList("bench__section--hidden");
                    return;
                }

                int[] picked = FiringOrders.Parse(e.newValue);
                if (picked != null && FiringOrders.Format(picked) != FiringOrders.Format(Design.firingOrder)) Change("Change firing order", d => d.firingOrder = picked);
            });
            custom.RegisterValueChangedCallback(e =>
            {
                int[] typed = FiringOrders.Parse(e.newValue) ?? Array.Empty<int>();
                Change("Change firing order", d => d.firingOrder = typed);
            });
            refreshers.Add(() =>
            {
                bool even = Design.firingAngles.Length == 0;
                var choices = new List<string>();
                foreach (int[] common in FiringOrders.For(Design)) choices.Add(FiringOrders.Format(common));
                choices.Add(CustomOrder);
                order.choices = choices;
                string current = FiringOrders.Format(Design.firingOrder);
                bool listed = choices.Contains(current);
                order.SetValueWithoutNotify(listed ? current : CustomOrder);
                custom.SetValueWithoutNotify(current);
                order.EnableInClassList("bench__section--hidden", !even);
                custom.EnableInClassList("bench__section--hidden", !even || listed);
            });
            Add(order);
            Add(custom);

            var angles = new VisualElement();
            Add(angles);
            Rows(angles, "Each cylinder's firing angle in the cycle (°, 0 to 720).", () => Design.firingAngles.Length, null, null, i => new[]
            {
                Cell("Angle", 0, 719.999, () => Design.firingAngles[i], v => Design.firingAngles[i] = v),
            });
            refreshers.Add(() => angles.EnableInClassList("bench__section--hidden", Design.firingAngles.Length == 0));
        }

        void BuildBottomEnd()
        {
            Caption(this, "Bottom end");
            Range(this, "Bore (mm)", "The cylinders' diameter.", 20, 200, () => Design.bore / U.mm, v => Design.bore = v * U.mm, "Change bore");
            Range(this, "Stroke (mm)", "How far each piston travels: twice the crank throw.", 20, 200, () => Design.stroke / U.mm,
                v => Design.stroke = v * U.mm, "Change stroke");
            Range(this, "Rod length (mm)", "Centre to centre.", 50, 400, () => Design.rodLength / U.mm, v => Design.rodLength = v * U.mm,
                "Change rod length");
            Range(this, "Compression height (mm)", "From the wrist pin to the piston crown.", 10, 60, () => Design.compressionHeight / U.mm,
                v => Design.compressionHeight = v * U.mm, "Change compression height");
            Range(this, "Deck clearance (mm)", "The deck above the crown at top dead centre.", -5, 10, () => Design.deckClearance / U.mm,
                v => Design.deckClearance = v * U.mm, "Change deck clearance");
            Range(this, "Piston mass (g)", "With its pin and rings.", 20, 2000, () => Design.pistonMass / U.g, v => Design.pistonMass = v * U.g,
                "Change piston mass");
            Range(this, "Rod mass (g)", "The rod's moment of inertia is set apart (Advanced parts).", 20, 3000, () => Design.rodMass / U.g,
                v => Design.rodMass = v * U.g, "Change rod mass");
        }

        void BuildCamshafts()
        {
            Caption(this, "Camshafts");
            CamshaftDesign Cams() => Design.camshaft;
            Range(this, "Intake duration (°)", "Crank degrees the intake valve is open past 50 thou of lift.", 120, 320,
                () => Cams().intakeLobe.durationAt50Thou, v => Cams().intakeLobe.durationAt50Thou = v, "Change intake duration");
            Range(this, "Intake lift (mm)", "The intake valve's peak lift.", 1, 25, () => Cams().intakeLobe.lift / U.mm,
                v => Cams().intakeLobe.lift = v * U.mm, "Change intake lift");
            Range(this, "Exhaust duration (°)", "Crank degrees the exhaust valve is open past 50 thou of lift.", 120, 320,
                () => Cams().exhaustLobe.durationAt50Thou, v => Cams().exhaustLobe.durationAt50Thou = v, "Change exhaust duration");
            Range(this, "Exhaust lift (mm)", "The exhaust valve's peak lift.", 1, 25, () => Cams().exhaustLobe.lift / U.mm,
                v => Cams().exhaustLobe.lift = v * U.mm, "Change exhaust lift");
            Range(this, "Intake lobe centre (°)", "Crank degrees from overlap top dead centre to the intake lobe's peak.", -180, 180,
                () => Cams().intakeLobeCenter, v => Cams().intakeLobeCenter = v, "Change intake lobe centre");
            Range(this, "Exhaust lobe centre (°)", "Crank degrees from the exhaust lobe's peak to overlap top dead centre.", -180, 180,
                () => Cams().exhaustLobeCenter, v => Cams().exhaustLobeCenter = v, "Change exhaust lobe centre");
            Range(this, "Cam advance (°)", "Turns both camshafts earlier.", -30, 30, () => Cams().advance, v => Cams().advance = v, "Change cam advance");
        }

        void BuildBreathing()
        {
            Caption(this, "Head and intake");
            if (Design.intakes.Length > 1) Hint(this, "Intake 1's; the others' are under Advanced parts.");
            Range(this, "Chamber volume (cc)", "The combustion chamber at top dead centre: with the bore and stroke, it sets the compression.", 10, 600,
                () => Design.heads[0].chamberVolume / U.cc, v => Design.heads[0].chamberVolume = v * U.cc, "Change chamber volume");
            IntakeMainFields(this, 0);

            for (int i = 0; i < Design.exhausts.Length; ++i)
            {
                int index = i;
                ExhaustDesign Exhaust() => Design.exhausts[index];
                var foldout = new Foldout { text = $"Exhaust {i + 1}", value = false };
                Add(foldout);
                refreshers.Add(() => foldout.text = $"Exhaust {index + 1} ({Cylinders(index)})");
                Range(foldout, "Length (m)", "Collector to outlet: with the collector's size, the system's volume.", 0.1f, 20, () => Exhaust().length,
                    v => Exhaust().length = v, "Change exhaust length");
                Range(foldout, "Primary length (mm)", "The header pipes' length, before the collector.", 50, 2500, () => Exhaust().primaryTubeLength / U.mm,
                    v => Exhaust().primaryTubeLength = v * U.mm, "Change primary length");
                Range(foldout, "Outlet flow (cfm)", "What the outlet (the muffler) flows, as carburettors are rated.", 10, 3000,
                    () => Exhaust().outletFlowRate, v => Exhaust().outletFlowRate = v, "Change outlet flow");
                Range(foldout, "Loudness", "This exhaust's level in the sound.", 0, 10, () => Exhaust().audioVolume, v => Exhaust().audioVolume = v,
                    "Change exhaust loudness");
                foldout.Add(ImpulseResponsePicker(index));
            }
        }

        void IntakeMainFields(VisualElement parent, int index)
        {
            IntakeDesign Intake() => Design.intakes[index];
            Range(parent, "Throttle flow (cfm)", "What the throttle body (or carburettor) flows wide open, as carburettors are rated.", 10, 3000,
                () => Intake().intakeFlowRate, v => Intake().intakeFlowRate = v, "Change throttle flow");
            Range(parent, "Runner length (mm)", "The intake runners, plenum to port.", 20, 1500, () => Intake().runnerLength / U.mm,
                v => Intake().runnerLength = v * U.mm, "Change runner length");
            Range(parent, "Plenum volume (L)", "The intake plenum behind the throttle.", 0.2f, 10, () => Intake().plenumVolume / U.L,
                v => Intake().plenumVolume = v * U.L, "Change plenum volume");
        }

        void BuildForcedInduction()
        {
            Caption(this, "Forced induction");
            ForcedInductionDesign Fi() => Design.forcedInduction;
            bool Turbo() => Fi().type == ForcedInductionKind.Turbocharger;
            bool Displacement() => Fi().type == ForcedInductionKind.Roots || Fi().type == ForcedInductionKind.TwinScrew;
            bool Centrifugal() => Fi().type == ForcedInductionKind.Turbocharger || Fi().type == ForcedInductionKind.Centrifugal;

            var type = new EnumField("Type", Fi().type)
            {
                tooltip = "A turbocharger spools with the exhaust flow and whistles; a Roots or twin-screw blower whines, locked to the revs, " +
                          "and gives boost from low down; a centrifugal supercharger whistles, its boost rising with the revs. Its sounds are " +
                          "an output of their own (see the render API).",
            };
            type.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is ForcedInductionKind picked && picked != Fi().type)
                {
                    Change("Change forced induction", d => d.forcedInduction = ForcedInductionDesign.Defaults(picked));
                }
            });
            refreshers.Add(() => type.SetValueWithoutNotify(Fi().type));
            Add(type);

            var parts = new VisualElement();
            Add(parts);
            refreshers.Add(() => parts.EnableInClassList("bench__section--hidden", Fi().type == ForcedInductionKind.None));

            var arrangement = new EnumField("Turbos", Fi().arrangement)
            {
                tooltip = "Single; twin (two in parallel, each a turbo as set here); or sequential (the second joins above the switch speed).",
            };
            arrangement.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is ForcedInduction.Arrangement picked && picked != Fi().arrangement) Change("Change turbos", d => d.forcedInduction.arrangement = picked);
            });
            refreshers.Add(() => arrangement.SetValueWithoutNotify(Fi().arrangement));
            parts.Add(arrangement);

            VisualElement sequential = Range(parts, "Second turbo joins at (rpm)", "", 1000, 12000, () => Fi().sequentialSpeed,
                v => Fi().sequentialSpeed = v, "Change forced induction");
            VisualElement boost = Range(parts, "Boost (kPa)", "The wastegate's set point, above the intake's air pressure (100 kPa is about 14.5 psi).",
                10, 300, () => Fi().boost / U.kPa, v => Fi().boost = v * U.kPa, "Change boost");
            var control = new EnumField("Boost control", Fi().boostControl)
            {
                tooltip = "Electronic: holds the set point. Spring: the wastegate opens on pressure alone, so the boost overshoots and creeps up with the flow.",
            };
            control.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is ForcedInduction.BoostControl picked && picked != Fi().boostControl) Change("Change boost control", d => d.forcedInduction.boostControl = picked);
            });
            refreshers.Add(() => control.SetValueWithoutNotify(Fi().boostControl));
            parts.Add(control);
            VisualElement turbine = Range(parts, "Turbine A/R", "The turbine housing: smaller spools sooner and chokes the top end.", 0.3f, 1.6f,
                () => Fi().turbineAr, v => Fi().turbineAr = v, "Change turbine");
            VisualElement inertia = Range(parts, "Rotor inertia (g·cm²)", "The turbo's rotating assembly: heavier lags more.", 10, 3000,
                () => Fi().inertia * 1e7, v => Fi().inertia = v * 1e-7, "Change turbine");
            VisualElement twinScroll = Flag(parts, "Twin scroll", "A divided turbine housing keeps the cylinder groups' pulses apart: better spool.",
                () => Fi().twinScroll, v => Fi().twinScroll = v, "Change turbine");
            VisualElement bearings = Flag(parts, "Ball bearings", "Less drag than journal bearings: a little quicker spool and wind-down.",
                () => Fi().ballBearings, v => Fi().ballBearings = v, "Change turbine");
            VisualElement external = Flag(parts, "External wastegate", "Larger, and vents through its own pipe with a rasp of its own.",
                () => Fi().externalWastegate, v => Fi().externalWastegate = v, "Change wastegate");

            VisualElement drive = Range(parts, "Drive ratio", "Compressor speed / crank speed (a centrifugal's gearbox step-up included).", 0.5f, 15,
                () => Fi().driveRatio, v => Fi().driveRatio = v, "Change drive ratio");
            VisualElement displacement = Range(parts, "Displacement (L)", "Air the rotors move per turn.", 0.3f, 6, () => Fi().displacement / U.L,
                v => Fi().displacement = v * U.L, "Change displacement");
            VisualElement bypass = Flag(parts, "Bypass valve", "Recirculates the delivery at part throttle; without it the blower pumps against the shut throttle.",
                () => Fi().bypassValve, v => Fi().bypassValve = v, "Change bypass valve");

            VisualElement diameter = Range(parts, "Compressor wheel (mm)", "Its tip diameter: with the shaft speed, the boost.", 20, 200,
                () => Fi().compressorDiameter / U.mm, v => Fi().compressorDiameter = v * U.mm, "Change compressor");
            VisualElement flow = Range(parts, "Compressor flow (kg/s)", "The most air it moves.", 0.01f, 3, () => Fi().compressorFlow,
                v => Fi().compressorFlow = v, "Change compressor");

            var blowoff = new EnumField("Blow-off valve", Fi().blowoffValve ? Fi().blowoffType : ForcedInduction.BlowoffType.None)
            {
                tooltip = "Vents the charge when the throttle shuts: to the air (a sharp hiss) or back to the intake (a soft whoosh). None: no " +
                          "valve, the charge stays in the pipes (the model's surge flutter is rare: it needs reverse flow through the compressor).",
            };
            blowoff.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is ForcedInduction.BlowoffType picked)
                {
                    Change("Change blow-off valve", d =>
                    {
                        d.forcedInduction.blowoffType = picked;
                        d.forcedInduction.blowoffValve = picked != ForcedInduction.BlowoffType.None;
                    });
                }
            });
            refreshers.Add(() => blowoff.SetValueWithoutNotify(Fi().blowoffValve ? Fi().blowoffType : ForcedInduction.BlowoffType.None));
            parts.Add(blowoff);

            Range(parts, "Intercooler", "How much of the compression heat it takes out (0: none).", 0, 1, () => Fi().intercoolerEffectiveness,
                v => Fi().intercoolerEffectiveness = v, "Change intercooler");
            Range(parts, "Charge volume (L)", "Pipes and intercooler between the compressor and the throttle: more fills slower.", 0.5f, 30,
                () => Fi().chargeVolume / U.L, v => Fi().chargeVolume = v * U.L, "Change charge volume");
            Range(parts, "Whistle pitch", "Scales the whistle's or the whine's pitch (sound only).", 0.25f, 4, () => Fi().whistlePitch,
                v => Fi().whistlePitch = v, "Change whistle pitch");

            refreshers.Add(() =>
            {
                foreach (VisualElement turboOnly in new[] { (VisualElement)arrangement, boost, control, turbine, inertia, twinScroll, bearings, external })
                {
                    turboOnly.EnableInClassList("bench__section--hidden", !Turbo());
                }

                sequential.EnableInClassList("bench__section--hidden", !Turbo() || Fi().arrangement != ForcedInduction.Arrangement.Sequential);
                drive.EnableInClassList("bench__section--hidden", Turbo());
                displacement.EnableInClassList("bench__section--hidden", !Displacement());
                bypass.EnableInClassList("bench__section--hidden", !Displacement());
                diameter.EnableInClassList("bench__section--hidden", !Centrifugal());
                flow.EnableInClassList("bench__section--hidden", !Centrifugal());
                blowoff.EnableInClassList("bench__section--hidden", Displacement());
            });
        }

        void BuildLimits()
        {
            Caption(this, "Limits");
            Range(this, "Redline (rpm)", "Where the rev counter turns red; the dyno pull ends here.", 1000, 20000, () => Design.redline,
                v => Design.redline = v, "Change redline");
            Range(this, "Rev limit (rpm)", "Where the limiter cuts the spark.", 1000, 21000, () => Design.ignition.revLimit,
                v => Design.ignition.revLimit = v, "Change rev limit");
            Range(this, "Limiter cut (s)", "How long each cut lasts.", 0.01f, 1, () => Design.ignition.limiterDuration,
                v => Design.ignition.limiterDuration = v, "Change limiter cut");
        }

        VisualElement ImpulseResponsePicker(int index)
        {
            var picker = new DropdownField("Exhaust filter")
            {
                tooltip = "The impulse response the exhaust sounds through: the muffler, and the space to the listener, from 1 (least " +
                          "muffled) to 7 (most), and two variants. Another one plays as loud: its volume is set so it passes as much energy.",
            };
            picker.RegisterValueChangedCallback(e =>
            {
                ExhaustFilter picked = Array.Find(ExhaustFilters.All, f => f.Label == e.newValue);
                ExhaustDesign exhaust = Design.exhausts[index];
                if (picked == null || picked.response.file == exhaust.impulseResponse) return;
                ImpulseResponseAsset from = EnginePresets.ImpulseResponseFromFile(exhaust.impulseResponse);
                ImpulseResponseAsset to = EnginePresets.ImpulseResponseFromFile(picked.response.file);
                double volume = from != null ? EnginePresets.MatchedVolume(exhaust.impulseResponseVolume, from, to) : picked.response.volume;
                Change("Pick exhaust filter", d =>
                {
                    d.exhausts[index].impulseResponse = picked.response.file;
                    d.exhausts[index].impulseResponseVolume = volume;
                });
            });
            refreshers.Add(() =>
            {
                string file = Design.exhausts[index].impulseResponse;
                ExhaustFilter current = ExhaustFilters.Of(file);
                var choices = new List<string>();
                if (current == null) choices.Add(Path.GetFileNameWithoutExtension(file));
                foreach (ExhaustFilter f in ExhaustFilters.All) choices.Add(f.Label);
                picker.choices = choices;
                picker.SetValueWithoutNotify(current?.Label ?? Path.GetFileNameWithoutExtension(file));
            });
            return picker;
        }

        /// <summary>Which cylinders breathe out through exhaust <paramref name="index"/>.</summary>
        string Cylinders(int index)
        {
            var list = new List<int>();
            for (int c = 0; c < Design.cylinderSettings.Length; ++c)
            {
                if (Design.cylinderSettings[c].exhaust == index) list.Add(c + 1);
            }

            return list.Count > 0 ? "cylinders " + string.Join(", ", list) : "no cylinders";
        }

        /// <summary>
        /// The firing angles to start odd firing from: the even firing's when the order names every cylinder once, else an
        /// even share of the cycle by cylinder number.
        /// </summary>
        static double[] StartingAngles(EngineDesign design)
        {
            int n = design.cylinders;
            var seen = new bool[n + 1];
            bool whole = design.firingOrder != null && design.firingOrder.Length == n;
            for (int k = 0; whole && k < n; ++k)
            {
                int cylinder = design.firingOrder[k];
                whole = cylinder >= 1 && cylinder <= n && !seen[cylinder];
                if (whole) seen[cylinder] = true;
            }

            if (whole) return EngineGenerator.FiringAngles(design);
            var angles = new double[n];
            for (int c = 0; c < n; ++c) angles[c] = 720.0 * c / n;
            return angles;
        }

        // ---- Advanced parts: every other value of the design ----

        void BuildAdvanced()
        {
            var advanced = new Foldout { text = "Advanced parts…", value = false };
            advanced.AddToClassList("design__advanced");
            Add(advanced);
            Hint(advanced, "Every other value the engine is built from, as engine scripts set them.");

            Foldout engineParts = Group(advanced, "Engine");
            var name = new TextField("Name") { isDelayed = true, tooltip = "The engine's name in its description." };
            name.RegisterValueChangedCallback(e => Change("Rename engine design", d => d.name = e.newValue));
            refreshers.Add(() => name.SetValueWithoutNotify(Design.name));
            engineParts.Add(name);
            Range(engineParts, "Starter torque (N·m)", "The starter motor's torque.", 0, 500, () => Design.starterTorque, v => Design.starterTorque = v,
                "Change starter torque");
            Range(engineParts, "Starter speed (rpm)", "The speed the starter turns the engine at (negative: the other way).", -1000, 1000,
                () => Design.starterSpeed, v => Design.starterSpeed = v, "Change starter speed");
            Range(engineParts, "Dyno lowest speed (rpm)", "Where a dyno pull starts.", 0, 5000, () => Design.dynoMinSpeed, v => Design.dynoMinSpeed = v,
                "Change dyno speed range");
            Range(engineParts, "Dyno highest speed (rpm)", "The dyno's top speed.", 1000, 21000, () => Design.dynoMaxSpeed, v => Design.dynoMaxSpeed = v,
                "Change dyno speed range");
            Range(engineParts, "Dyno hold step (rpm)", "The dyno speed step.", 10, 1000, () => Design.dynoHoldStep, v => Design.dynoHoldStep = v,
                "Change dyno hold step");
            Range(engineParts, "Simulation frequency (Hz)", "Simulation steps per second, also the rate the exhaust is sampled at: before " +
                "the filter, the sound has nothing above half of it. The render cost grows in proportion.", 2000, 60000,
                () => Design.simulationFrequency, v => Design.simulationFrequency = v, "Change simulation frequency");
            Hint(engineParts, "The synthesizer's mix (high-frequency gain, noise, jitter) is in Sound, where it plays at once.");

            Foldout throttle = Group(advanced, "Throttle");
            ThrottleDesign Throttle() => Design.throttle;
            Flag(throttle, "Governed", "A governor holds the speed the pedal sets, as on small engines.", () => Throttle().governed,
                v => Throttle().governed = v, "Change throttle");
            Range(throttle, "Gamma", "Direct linkage: shapes the plate's travel against the pedal's.", 0.2f, 5, () => Throttle().gamma,
                v => Throttle().gamma = v, "Change throttle gamma");
            Range(throttle, "Governor lowest speed (rpm)", "The speed at the pedal's rest.", 0, 10000, () => Throttle().governorMinSpeed,
                v => Throttle().governorMinSpeed = v, "Change governor");
            Range(throttle, "Governor highest speed (rpm)", "The speed at full pedal.", 0, 10000, () => Throttle().governorMaxSpeed,
                v => Throttle().governorMaxSpeed = v, "Change governor");
            Range(throttle, "Governor min. velocity", "The plate's fastest closing.", -10, 10, () => Throttle().governorMinVelocity,
                v => Throttle().governorMinVelocity = v, "Change governor");
            Range(throttle, "Governor max. velocity", "The plate's fastest opening.", -10, 10, () => Throttle().governorMaxVelocity,
                v => Throttle().governorMaxVelocity = v, "Change governor");
            Range(throttle, "Governor k_s", "The governor's response to the speed error.", 0, 10, () => Throttle().governorK_s,
                v => Throttle().governorK_s = v, "Change governor");
            Range(throttle, "Governor k_d", "The governor's damping.", 0, 1000, () => Throttle().governorK_d, v => Throttle().governorK_d = v,
                "Change governor");
            Range(throttle, "Governor gamma", "Shapes the governor's plate travel.", 0, 1, () => Throttle().governorGamma,
                v => Throttle().governorGamma = v, "Change governor");

            Foldout bottomEnd = Group(advanced, "Bottom end");
            Range(bottomEnd, "Wrist pin offset (mm)", "The wrist pin's offset from the bore's axis.", -10, 10, () => Design.wristPinPosition / U.mm,
                v => Design.wristPinPosition = v * U.mm, "Change wrist pin offset");
            Range(bottomEnd, "Rod moment of inertia (kg·m²)", "About the rod's centre of mass.", 0, 0.05f, () => Design.rodMomentOfInertia,
                v => Design.rodMomentOfInertia = v, "Change rod moment of inertia");
            Range(bottomEnd, "Rod centre of mass (mm)", "From the rod's middle, towards the little end.", -50, 50, () => Design.rodCenterOfMass / U.mm,
                v => Design.rodCenterOfMass = v * U.mm, "Change rod centre of mass");
            Range(bottomEnd, "Crank mass (kg)", "", 1, 250, () => Design.crankMass, v => Design.crankMass = v, "Change crank mass");
            Range(bottomEnd, "Flywheel mass (kg)", "", 0, 150, () => Design.flywheelMass, v => Design.flywheelMass = v, "Change flywheel mass");
            Range(bottomEnd, "Crank moment of inertia (kg·m²)", "Crank, flywheel and all that turns with them.", 0.01f, 10,
                () => Design.crankMomentOfInertia, v => Design.crankMomentOfInertia = v, "Change crank moment of inertia");
            Range(bottomEnd, "Crank friction (N·m)", "", 0, 100, () => Design.crankFrictionTorque, v => Design.crankFrictionTorque = v,
                "Change crank friction");

            Foldout cylinders = Group(advanced, "Cylinders and banks");
            Rows(cylinders, "Per cylinder: blow-by (cfm at 28 inH2O), loudness, header pipe (mm), intake and exhaust (from 1).",
                () => Design.cylinderSettings.Length, null, null, i => new[]
                {
                    Cell("Blow-by", 0, 2, () => Design.cylinderSettings[i].blowby, v => Design.cylinderSettings[i].blowby = v),
                    Cell("Loudness", 0, 3, () => Design.cylinderSettings[i].soundAttenuation, v => Design.cylinderSettings[i].soundAttenuation = v),
                    Cell("Header (mm)", 0, 1500, () => Design.cylinderSettings[i].headerPrimaryLength / U.mm,
                        v => Design.cylinderSettings[i].headerPrimaryLength = v * U.mm),
                    Cell("Intake", 1, () => Design.intakes.Length, () => Design.cylinderSettings[i].intake + 1,
                        v => Design.cylinderSettings[i].intake = (int)Math.Round(v) - 1),
                    Cell("Exhaust", 1, () => Design.exhausts.Length, () => Design.cylinderSettings[i].exhaust + 1,
                        v => Design.cylinderSettings[i].exhaust = (int)Math.Round(v) - 1),
                });
            Range(cylinders, "Display depth", "How far back cylinders are drawn one behind another (display only).", 0, 1, () => Design.displayDepth,
                v => Design.displayDepth = v, "Change display depth");
            for (int b = 0; b < 2; ++b)
            {
                int bank = b;
                VisualElement flip = Flag(cylinders, $"Bank {b + 1} valves on the other side", "Display only.",
                    () => bank < Design.flipBankDisplay.Length && Design.flipBankDisplay[bank], v => Design.flipBankDisplay[bank] = v, "Change bank display");
                refreshers.Add(() => flip.EnableInClassList("bench__section--hidden", bank >= Design.Banks));
            }

            Foldout heads = Group(advanced, "Heads");
            HeadFields(heads, 0, "Bank 1's head (every bank's, unless bank 2 has its own).");
            VisualElement own = Flag(heads, "Bank 2 has its own head", "A second head, with its own chamber, runners and flow.",
                () => Design.heads.Length > 1, v => Design.heads = v ? new[] { Design.heads[0], Copy(Design.heads[0]) } : new[] { Design.heads[0] },
                "Change heads");
            var second = new VisualElement();
            heads.Add(second);
            HeadFields(second, 1, "Bank 2's head.");
            refreshers.Add(() =>
            {
                own.EnableInClassList("bench__section--hidden", Design.Banks < 2);
                second.EnableInClassList("bench__section--hidden", Design.heads.Length < 2);
            });

            Foldout cams = Group(advanced, "Camshafts");
            LobeFields(cams, "Intake lobe", () => Design.camshaft.intakeLobe);
            LobeFields(cams, "Exhaust lobe", () => Design.camshaft.exhaustLobe);
            Range(cams, "Base circle radius (mm)", "", 5, 40, () => Design.camshaft.baseRadius / U.mm, v => Design.camshaft.baseRadius = v * U.mm,
                "Change base circle");

            Foldout variable = Group(advanced, "Variable valve lift");
            VariableLiftDesign Lift() => Design.camshaft.variableLift;
            Flag(variable, "Enabled", "A second, high-lift pair of profiles the valvetrain switches to.", () => Lift().enabled, v => Lift().enabled = v,
                "Change variable lift");
            Range(variable, "High-lift intake duration (°)", "", 120, 320, () => Lift().intakeLobe.durationAt50Thou,
                v => Lift().intakeLobe.durationAt50Thou = v, "Change variable lift");
            Range(variable, "High-lift intake lift (mm)", "", 1, 25, () => Lift().intakeLobe.lift / U.mm, v => Lift().intakeLobe.lift = v * U.mm,
                "Change variable lift");
            Range(variable, "High-lift exhaust duration (°)", "", 120, 320, () => Lift().exhaustLobe.durationAt50Thou,
                v => Lift().exhaustLobe.durationAt50Thou = v, "Change variable lift");
            Range(variable, "High-lift exhaust lift (mm)", "", 1, 25, () => Lift().exhaustLobe.lift / U.mm, v => Lift().exhaustLobe.lift = v * U.mm,
                "Change variable lift");
            LobeFields(variable, "High-lift intake lobe", () => Lift().intakeLobe);
            LobeFields(variable, "High-lift exhaust lobe", () => Lift().exhaustLobe);
            Range(variable, "High-lift intake centre (°)", "", -180, 180, () => Lift().intakeLobeCenter, v => Lift().intakeLobeCenter = v,
                "Change variable lift");
            Range(variable, "High-lift exhaust centre (°)", "", -180, 180, () => Lift().exhaustLobeCenter, v => Lift().exhaustLobeCenter = v,
                "Change variable lift");
            Range(variable, "High-lift cam advance (°)", "", -30, 30, () => Lift().advance, v => Lift().advance = v, "Change variable lift");
            Range(variable, "High-lift base circle (mm)", "", 5, 40, () => Lift().baseRadius / U.mm, v => Lift().baseRadius = v * U.mm,
                "Change variable lift");
            Range(variable, "Switches above (rpm)", "", 1000, 15000, () => Lift().minRpm, v => Lift().minRpm = v, "Change variable lift");
            Range(variable, "…and above (mph)", "The road speed it also needs.", 0, 100, () => Lift().minSpeed, v => Lift().minSpeed = v,
                "Change variable lift");
            Range(variable, "…with throttle past", "", 0, 1, () => Lift().minThrottlePosition, v => Lift().minThrottlePosition = v, "Change variable lift");
            Range(variable, "…and vacuum below (kPa)", "", 0, 101, () => Lift().manifoldVacuum / U.kPa, v => Lift().manifoldVacuum = v * U.kPa,
                "Change variable lift");

            for (int i = 0; i < Design.intakes.Length; ++i)
            {
                int index = i;
                IntakeDesign Intake() => Design.intakes[index];
                Foldout intake = Group(advanced, $"Intake {i + 1}");
                if (i > 0) IntakeMainFields(intake, i);
                Range(intake, "Plenum area (cm²)", "", 5, 200, () => Intake().plenumCrossSectionArea / U.cm2,
                    v => Intake().plenumCrossSectionArea = v * U.cm2, "Change intake");
                Range(intake, "Runner flow (cfm)", "", 10, 1500, () => Intake().runnerFlowRate, v => Intake().runnerFlowRate = v, "Change intake");
                Range(intake, "Idle flow (cfm)", "What the idle circuit lets past the closed throttle.", 0, 5, () => Intake().idleFlowRate,
                    v => Intake().idleFlowRate = v, "Change intake");
                Range(intake, "Idle throttle position", "Where the closed plate sits (1: shut).", 0.9f, 1, () => Intake().idleThrottlePlatePosition,
                    v => Intake().idleThrottlePlatePosition = v, "Change intake");
                Range(intake, "Velocity decay", "", 0, 1, () => Intake().velocityDecay, v => Intake().velocityDecay = v, "Change intake");
                Range(intake, "Molecular air-fuel ratio", "", 5, 20, () => Intake().molecularAfr, v => Intake().molecularAfr = v, "Change intake");
                Range(intake, "Air pressure (kPa)", "The air the intake draws from: about 101 at sea level, less at altitude. Boost comes from " +
                    "Forced induction, measured above it.",
                    30, 400, () => Intake().atmospherePressure / U.kPa, v => Intake().atmospherePressure = v * U.kPa, "Change intake");
                Range(intake, "Air temperature (°C)", "The air the intake draws from.", -40, 150,
                    () => Intake().atmosphereTemperature - U.K0, v => Intake().atmosphereTemperature = v + U.K0, "Change intake");
            }

            for (int i = 0; i < Design.exhausts.Length; ++i)
            {
                int index = i;
                ExhaustDesign Exhaust() => Design.exhausts[index];
                Foldout exhaust = Group(advanced, $"Exhaust {i + 1}");
                Range(exhaust, "Collector area (cm²)", "", 10, 200, () => Exhaust().collectorCrossSectionArea / U.cm2,
                    v => Exhaust().collectorCrossSectionArea = v * U.cm2, "Change exhaust");
                Range(exhaust, "Primary flow (cfm)", "", 10, 2000, () => Exhaust().primaryFlowRate, v => Exhaust().primaryFlowRate = v, "Change exhaust");
                Range(exhaust, "Velocity decay", "", 0, 1, () => Exhaust().velocityDecay, v => Exhaust().velocityDecay = v, "Change exhaust");
                Range(exhaust, "Filter volume", "The exhaust filter's volume (set as loud as the filter it replaced when picked).", 0, 0.05f,
                    () => Exhaust().impulseResponseVolume,
                    v => Exhaust().impulseResponseVolume = v, "Change exhaust");
            }

            Foldout forcedInduction = Group(advanced, "Forced induction");
            ForcedInductionDesign Fi() => Design.forcedInduction;
            Range(forcedInduction, "Compressor efficiency", "Of a turbocharger's or centrifugal supercharger's compressor.", 0.4f, 0.85f,
                () => Fi().compressorEfficiency, v => Fi().compressorEfficiency = v, "Change compressor");
            Range(forcedInduction, "Turbine efficiency", "", 0.3f, 0.85f, () => Fi().turbineEfficiency, v => Fi().turbineEfficiency = v,
                "Change turbine");
            Range(forcedInduction, "Shaft speed limit (rpm)", "0: from the compressor wheel's tip speed (520 m/s).", 0, 300000,
                () => Fi().maxSpeed, v => Fi().maxSpeed = v, "Change compressor");

            Foldout ignition = Group(advanced, "Ignition");
            Range(ignition, "Timing smoothing (rpm)", "How far the timing curve is smoothed over speed.", 100, 5000, () => Design.ignition.timingSmoothing,
                v => Design.ignition.timingSmoothing = v, "Change timing smoothing");
            Rows(ignition, "Timing: speed (rpm), advance (°).", () => Design.ignition.timingCurve.Length,
                () => Change("Add timing point", d => d.ignition.timingCurve = Appended(d.ignition.timingCurve, p => p.speed, 1000,
                    (p, x) => new TimingSample(x, p.advance))),
                () => Change("Remove timing point", d => d.ignition.timingCurve = Trimmed(d.ignition.timingCurve)), i => new[]
                {
                    Cell("Speed", 0, 25000, () => Design.ignition.timingCurve[i].speed, v => Design.ignition.timingCurve[i].speed = v),
                    Cell("Advance", -60, 90, () => Design.ignition.timingCurve[i].advance, v => Design.ignition.timingCurve[i].advance = v),
                });

            Foldout fuel = Group(advanced, "Fuel");
            FuelDesign Fuel() => Design.fuel;
            Range(fuel, "Molecular mass (g)", "", 10, 500, () => Fuel().molecularMass / U.g, v => Fuel().molecularMass = v * U.g, "Change fuel");
            Range(fuel, "Energy density (kJ/g)", "", 10, 100, () => Fuel().energyDensity / (U.kJ / U.g), v => Fuel().energyDensity = v * (U.kJ / U.g),
                "Change fuel");
            Range(fuel, "Density (kg/L)", "", 0.3f, 1.5f, () => Fuel().density / (U.kg / U.L), v => Fuel().density = v * (U.kg / U.L), "Change fuel");
            Range(fuel, "Molecular air-fuel ratio", "", 5, 20, () => Fuel().molecularAfr, v => Fuel().molecularAfr = v, "Change fuel");
            Range(fuel, "Max. burning efficiency", "", 0, 1, () => Fuel().maxBurningEfficiency, v => Fuel().maxBurningEfficiency = v, "Change fuel");
            Range(fuel, "Burning randomness", "", 0, 1, () => Fuel().burningEfficiencyRandomness, v => Fuel().burningEfficiencyRandomness = v,
                "Change fuel");
            Range(fuel, "Low efficiency attenuation", "", 0, 1, () => Fuel().lowEfficiencyAttenuation, v => Fuel().lowEfficiencyAttenuation = v,
                "Change fuel");
            Range(fuel, "Max. turbulence effect", "", 0, 20, () => Fuel().maxTurbulenceEffect, v => Fuel().maxTurbulenceEffect = v, "Change fuel");
            Range(fuel, "Max. dilution effect", "", 0, 2000, () => Fuel().maxDilutionEffect, v => Fuel().maxDilutionEffect = v, "Change fuel");
            Range(fuel, "Turbulence smoothing", "", 0.5f, 20, () => Fuel().turbulenceSmoothing, v => Fuel().turbulenceSmoothing = v, "Change fuel");
            Rows(fuel, "Flame speed against turbulence; none: gasoline's.", () => Fuel().turbulenceToFlameSpeedRatio.Length,
                () => Change("Add turbulence point", d => d.fuel.turbulenceToFlameSpeedRatio = Appended(d.fuel.turbulenceToFlameSpeedRatio, p => p.x, 5,
                    (p, x) => new CurveSample(x, p.y))),
                () => Change("Remove turbulence point", d => d.fuel.turbulenceToFlameSpeedRatio = Trimmed(d.fuel.turbulenceToFlameSpeedRatio)), i => new[]
                {
                    Cell("Turbulence", 0, 100, () => Fuel().turbulenceToFlameSpeedRatio[i].x, v => Fuel().turbulenceToFlameSpeedRatio[i].x = v),
                    Cell("Ratio", 0, 200, () => Fuel().turbulenceToFlameSpeedRatio[i].y, v => Fuel().turbulenceToFlameSpeedRatio[i].y = v),
                });

            Foldout vehicle = Group(advanced, "Vehicle and gearbox");
            Hint(vehicle, "What the engine drives; the Bench runs it on the dyno, so these don't change what it plays there.");
            Range(vehicle, "Mass (kg)", "", 50, 5000, () => Design.vehicle.mass, v => Design.vehicle.mass = v, "Change vehicle");
            Range(vehicle, "Drag coefficient", "", 0, 2, () => Design.vehicle.dragCoefficient, v => Design.vehicle.dragCoefficient = v, "Change vehicle");
            Range(vehicle, "Frontal area (m²)", "", 0.1f, 10, () => Design.vehicle.crossSectionArea, v => Design.vehicle.crossSectionArea = v,
                "Change vehicle");
            Range(vehicle, "Final drive", "", 1, 10, () => Design.vehicle.diffRatio, v => Design.vehicle.diffRatio = v, "Change vehicle");
            Range(vehicle, "Tyre radius (mm)", "", 100, 600, () => Design.vehicle.tireRadius / U.mm, v => Design.vehicle.tireRadius = v * U.mm,
                "Change vehicle");
            Range(vehicle, "Rolling resistance (N)", "", 0, 5000, () => Design.vehicle.rollingResistance, v => Design.vehicle.rollingResistance = v,
                "Change vehicle");
            Range(vehicle, "Clutch torque (N·m)", "", 50, 5000, () => Design.transmission.maxClutchTorque, v => Design.transmission.maxClutchTorque = v,
                "Change gearbox");
            Rows(vehicle, "Gear ratios.", () => Design.transmission.gearRatios.Length,
                () => Change("Add gear", d => d.transmission.gearRatios = Appended(d.transmission.gearRatios, _ => 0, 0, (r, _) => r)),
                () => Change("Remove gear", d => d.transmission.gearRatios = Trimmed(d.transmission.gearRatios)), i => new[]
                {
                    Cell("Ratio", 0.3, 6, () => Design.transmission.gearRatios[i], v => Design.transmission.gearRatios[i] = v),
                }, i => $"Gear {i + 1}");
        }

        void HeadFields(VisualElement parent, int bank, string caption)
        {
            HeadDesign Head() => Design.HeadOf(bank);
            Hint(parent, caption);
            if (bank > 0)
            {
                Range(parent, "Chamber volume (cc)", "", 10, 600, () => Head().chamberVolume / U.cc, v => Head().chamberVolume = v * U.cc, "Change head");
            }

            Range(parent, "Intake runner volume (cc)", "", 20, 500, () => Head().intakeRunnerVolume / U.cc, v => Head().intakeRunnerVolume = v * U.cc,
                "Change head");
            Range(parent, "Intake runner area (cm²)", "", 2, 120, () => Head().intakeRunnerCrossSectionArea / U.cm2,
                v => Head().intakeRunnerCrossSectionArea = v * U.cm2, "Change head");
            Range(parent, "Exhaust runner volume (cc)", "", 20, 500, () => Head().exhaustRunnerVolume / U.cc, v => Head().exhaustRunnerVolume = v * U.cc,
                "Change head");
            Range(parent, "Exhaust runner area (cm²)", "", 2, 120, () => Head().exhaustRunnerCrossSectionArea / U.cm2,
                v => Head().exhaustRunnerCrossSectionArea = v * U.cm2, "Change head");
            Range(parent, "Flow smoothing (thou)", "How far the flow curves are smoothed over lift.", 10, 100, () => Head().flowSmoothing,
                v => Head().flowSmoothing = v, "Change head");
            FlowRows(parent, "Intake flow: lift (thou), flow (cfm at 28 inH2O).", () => Head().intakeFlow, f => Head().intakeFlow = f);
            FlowRows(parent, "Exhaust flow: lift (thou), flow (cfm at 28 inH2O).", () => Head().exhaustFlow, f => Head().exhaustFlow = f);
        }

        void LobeFields(VisualElement parent, string name, Func<LobeDesign> lobe)
        {
            Range(parent, $"{name} gamma", "The lobe's shape: higher is fuller.", 0.3f, 4, () => lobe().gamma, v => lobe().gamma = v, "Change " + name);
            Whole(parent, $"{name} samples", "Samples a side of the lobe's peak.", 20, 1000, () => lobe().steps, v => lobe().steps = v, "Change " + name);
        }

        static HeadDesign Copy(HeadDesign head) => new HeadDesign
        {
            chamberVolume = head.chamberVolume, intakeRunnerVolume = head.intakeRunnerVolume, intakeRunnerCrossSectionArea = head.intakeRunnerCrossSectionArea,
            exhaustRunnerVolume = head.exhaustRunnerVolume, exhaustRunnerCrossSectionArea = head.exhaustRunnerCrossSectionArea,
            intakeFlow = (FlowSample[])head.intakeFlow.Clone(), exhaustFlow = (FlowSample[])head.exhaustFlow.Clone(), flowSmoothing = head.flowSmoothing,
        };

        void FlowRows(VisualElement parent, string hint, Func<FlowSample[]> get, Action<FlowSample[]> set) =>
            Rows(parent, hint, () => get().Length,
                () => Change("Add flow point", _ => set(Appended(get(), p => p.lift, 50, (p, x) => new FlowSample(x, p.flow)))),
                () => Change("Remove flow point", _ => set(Trimmed(get()))), i => new[]
                {
                    Cell("Lift", 0, 1000, () => get()[i].lift, v => get()[i].lift = v),
                    Cell("Flow", 0, 1000, () => get()[i].flow, v => get()[i].flow = v),
                });

        // ---- What the design makes, and what keeps it from building ----

        void ShowFiguresAndChecks()
        {
            EngineDesign d = Design;
            double swept = Math.PI / 4 * d.bore * d.bore * d.stroke;
            double pistonSpeed = 2 * d.stroke * d.redline / 60;
            double rodRatio = d.rodLength / d.stroke;
            double chamber = d.heads[0].chamberVolume;
            string crank = "";
            List<string> problems = EngineGenerator.Problems(d);
            if (problems.Count == 0)
            {
                Simulation.CrankshaftDescription built = engine.Description.engine.crankshafts[0];
                var pins = new List<string>();
                foreach (double angle in built.rodJournalAngles) pins.Add($"{Mod360(angle / U.deg):0.#}°");
                string firing = d.firingAngles.Length > 0 ? "odd firing" : $"fires every {720.0 / d.cylinders:0.#}°";
                crank = $"\nCrank pins {string.Join(", ", pins)} · {firing}";
            }

            figures.text = $"{d.cylinders * swept / U.L:0.00} L · compression {(swept + chamber) / chamber:0.0}:1 · " +
                           $"bore/stroke {d.bore / d.stroke:0.00} · rod/stroke {rodRatio:0.00}\nMean piston speed at the redline {pistonSpeed:0.0} m/s" + crank;

            checks.Clear();
            foreach (string problem in problems) checks.Add(new HelpBox(problem + " The engine plays as it was before this change.", HelpBoxMessageType.Error));
            if (pistonSpeed > PistonSpeedLimit)
            {
                checks.Add(new HelpBox($"At the redline the pistons average {pistonSpeed:0.0} m/s: past about {PistonSpeedLimit} m/s real engines don't " +
                                       "hold together (the simulation runs regardless).", HelpBoxMessageType.Warning));
            }

            if (rodRatio < RodRatioLimit)
            {
                checks.Add(new HelpBox($"The rods are {rodRatio:0.00} times the stroke: below about {RodRatioLimit} they push the pistons hard into " +
                                       "the bores (the simulation runs regardless).", HelpBoxMessageType.Warning));
            }
        }

        static double Mod360(double degrees)
        {
            double r = degrees % 360.0;
            return r < 0 ? r + 360.0 : r;
        }

        // ---- Changes ----

        /// <summary>Changes the design, with Undo, and builds the engine's description from it when it has no problems.</summary>
        void Change(string undoName, Action<EngineDesign> edit)
        {
            Undo.RecordObject(engine, undoName);
            edit(Design);
            if (EngineGenerator.Problems(Design).Count == 0) engine.SetDesign(Design, EnginePresets.ImpulseResponseFromFile);
            designChanged();
            Refresh();
        }

        // ---- Fields ----

        /// <summary>
        /// A value on a slider from <paramref name="min"/> to <paramref name="max"/>, dragged or typed in (and kept within
        /// the range); a drag is one undo step.
        /// </summary>
        VisualElement Range(VisualElement parent, string label, string tooltip, float min, float max, Func<double> get, Action<double> set, string undoName)
        {
            var slider = new Slider(label, min, max) { showInputField = true, tooltip = tooltip };
            slider.AddToClassList("design__range");
            Draggable(slider, e => Change(undoName, _ => set(Math.Min(Math.Max(e.newValue, min), max))));
            refreshers.Add(() => slider.SetValueWithoutNotify((float)get()));
            parent.Add(slider);
            return slider;
        }

        /// <summary>A whole number on a slider from <paramref name="min"/> to <paramref name="max"/>.</summary>
        VisualElement Whole(VisualElement parent, string label, string tooltip, int min, int max, Func<int> get, Action<int> set, string undoName)
        {
            var slider = new SliderInt(label, min, max) { showInputField = true, tooltip = tooltip };
            slider.AddToClassList("design__range");
            Draggable(slider, e =>
            {
                int kept = Math.Min(Math.Max(e.newValue, min), max);
                if (kept != get()) Change(undoName, _ => set(kept));
            });
            refreshers.Add(() => slider.SetValueWithoutNotify(get()));
            parent.Add(slider);
            return slider;
        }

        /// <summary>Calls <paramref name="changed"/> for each change of the slider, folding a drag's changes into one undo step.</summary>
        static void Draggable<T>(BaseSlider<T> slider, Action<ChangeEvent<T>> changed) where T : IComparable<T>
        {
            int dragGroup = -1;
            slider.RegisterValueChangedCallback(e =>
            {
                changed(e);
                if (dragGroup >= 0) Undo.CollapseUndoOperations(dragGroup);
            });
            slider.RegisterCallback<PointerDownEvent>(_ => dragGroup = Undo.GetCurrentGroup(), TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerUpEvent>(_ => dragGroup = -1, TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => dragGroup = -1);
        }

        VisualElement Flag(VisualElement parent, string label, string tooltip, Func<bool> get, Action<bool> set, string undoName)
        {
            var toggle = new Toggle(label) { tooltip = tooltip };
            toggle.RegisterValueChangedCallback(e => Change(undoName, _ => set(e.newValue)));
            refreshers.Add(() => toggle.SetValueWithoutNotify(get()));
            parent.Add(toggle);
            return toggle;
        }

        /// <summary>A value of a table row: its name, its range, and how to read and write it.</summary>
        readonly struct RowCell
        {
            public readonly string name;
            public readonly double min;
            public readonly Func<double> max;
            public readonly Func<double> get;
            public readonly Action<double> set;

            public RowCell(string name, double min, Func<double> max, Func<double> get, Action<double> set)
            {
                this.name = name;
                this.min = min;
                this.max = max;
                this.get = get;
                this.set = set;
            }
        }

        static RowCell Cell(string name, double min, double max, Func<double> get, Action<double> set) => new RowCell(name, min, () => max, get, set);

        static RowCell Cell(string name, double min, Func<int> max, Func<double> get, Action<double> set) => new RowCell(name, min, () => max(), get, set);

        /// <summary>
        /// A table: a row a point (or cylinder, or gear), its cells side by side and kept within their ranges, rebuilt when the
        /// count changes; add and remove the last row where <paramref name="add"/> and <paramref name="remove"/> are given.
        /// </summary>
        void Rows(VisualElement parent, string hint, Func<int> count, Action add, Action remove, Func<int, RowCell[]> cells, Func<int, string> rowName = null)
        {
            Hint(parent, hint);
            var table = new VisualElement();
            parent.Add(table);
            var shown = new List<Action>();
            int built = -1;
            refreshers.Add(() =>
            {
                int n = count();
                if (n != built)
                {
                    built = n;
                    table.Clear();
                    shown.Clear();
                    for (int i = 0; i < n; ++i) table.Add(Row(i, rowName?.Invoke(i) ?? $"{i + 1}", cells(i), shown));
                }

                foreach (Action show in shown) show();
            });

            if (add == null) return;
            var buttons = new VisualElement();
            buttons.AddToClassList("bench__row");
            buttons.Add(new Button(add) { text = "Add" });
            buttons.Add(new Button(remove) { text = "Remove last" });
            parent.Add(buttons);
        }

        VisualElement Row(int index, string name, RowCell[] cells, List<Action> shown)
        {
            var row = new VisualElement();
            row.AddToClassList("design__row");
            var label = new Label(name);
            label.AddToClassList("design__row-name");
            row.Add(label);
            foreach (RowCell cell in cells)
            {
                RowCell c = cell;
                var field = new DoubleField { isDelayed = true, tooltip = $"{c.name} ({c.min:0.###} to {c.max():0.###})", formatString = "0.####" };
                field.AddToClassList("design__cell");
                field.RegisterValueChangedCallback(e =>
                {
                    double kept = Math.Min(Math.Max(e.newValue, c.min), c.max());
                    Change($"Change {c.name.ToLowerInvariant()}", _ => c.set(kept));
                });
                shown.Add(() => field.SetValueWithoutNotify(c.get()));
                row.Add(field);
            }

            return row;
        }

        static Foldout Group(VisualElement parent, string title)
        {
            var group = new Foldout { text = title, value = false };
            parent.Add(group);
            return group;
        }

        /// <summary>
        /// The array with one more point: <paramref name="next"/> of its last at the next position along
        /// <paramref name="position"/> (the last spacing on, or <paramref name="step"/> past a single point), so no two
        /// points of a curve share a position; the default point when empty.
        /// </summary>
        static T[] Appended<T>(T[] array, Func<T, double> position, double step, Func<T, double, T> next)
        {
            var longer = new T[array.Length + 1];
            Array.Copy(array, longer, array.Length);
            if (array.Length == 0) return longer;

            T last = array[array.Length - 1];
            double spacing = array.Length >= 2 ? position(last) - position(array[array.Length - 2]) : step;
            longer[array.Length] = next(last, position(last) + (spacing > 0 ? spacing : step));
            return longer;
        }

        /// <summary>The array without its last element.</summary>
        static T[] Trimmed<T>(T[] array)
        {
            if (array.Length == 0) return array;
            var shorter = new T[array.Length - 1];
            Array.Copy(array, shorter, shorter.Length);
            return shorter;
        }

        static void Caption(VisualElement parent, string text)
        {
            var caption = new Label(text);
            caption.AddToClassList("bench__caption");
            parent.Add(caption);
        }

        static void Hint(VisualElement parent, string text)
        {
            var hint = new Label(text);
            hint.AddToClassList("bench__hint");
            parent.Add(hint);
        }
    }
}
