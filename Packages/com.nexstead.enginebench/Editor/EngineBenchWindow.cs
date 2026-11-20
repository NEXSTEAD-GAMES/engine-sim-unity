// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// Window > Nx Engine Bench (UI Toolkit). The top bar picks the engine (a preset, or an engine saved in the project), which
    /// the Bench works on as an in-memory copy: Save writes the changes over the engine, or saves a preset's copy as a new
    /// one; switching engine or closing the window asks first and drops changes not saved. It switches sections: <b>Design</b>
    /// (<see cref="DesignSection"/>), <b>Sound</b> (<see cref="SoundSection"/>), <b>Export</b> (<see cref="ExportSection"/>) and
    /// those other packages add
    /// (<see cref="BenchExtension"/>). The main area shows the engine (<see cref="BenchVisuals"/>): its front view replaying
    /// captured cycles slowed down, with the run controls over it (start and stop, throttle or dyno, volume), the instruments,
    /// the exhaust signal, the spectrum of what plays and dyno pulls. It plays through a hidden <see cref="EngineAudioSource"/>,
    /// in Edit or Play Mode.
    /// </summary>
    public sealed class EngineBenchWindow : EditorWindow
    {
        const string StyleSheetPath = "Packages/" + EnginePresets.PackageName + "/Editor/EngineBench.uss";
        const string DesignSectionName = "Design";
        const string SoundSectionName = "Sound";
        const string ExportSectionName = "Export";
        /// <summary>Start turns the engine over at this much throttle at least, and this long at least (simulated s), as the Engine Demo does.</summary>
        const float CrankThrottle = 0.15f;
        const double CrankSeconds = 1.5;
        /// <summary>It has fired once it runs this much faster than its starter turns it.</summary>
        const double FiredOverStarter = 1.5;
        /// <summary>The starter gives up after this long, in simulated seconds.</summary>
        const double CrankTimeout = 5;
        /// <summary>The dyno takes the crank once it turns at least this fast (rpm): from rest it would drive it backwards.</summary>
        const double DynoEngageRpm = 30;
        /// <summary>The Sound section and the views follow the design this long (ms) after its last change, not on every slider step.</summary>
        const long FollowDesignDelay = 250;

        // The engine on the bench: always an in-memory copy of a saved engine (EnginePresets.TemporaryCopy), so nothing
        // reaches the project until it is saved.
        [SerializeField] EngineAsset engine;
        // The saved engine it is a copy of: the preset or project engine picked, or the engine a preset's copy was saved as.
        [SerializeField] EngineAsset source;
        [SerializeField] string section = DesignSectionName;
        // A package whose tab OpenOn asked for before the window was built.
        [NonSerialized] string pendingPackage;
        [SerializeField] float volume = 1f;
        // The engine changed since it was picked or last saved.
        [SerializeField] bool modified;

        /// <summary>Where Save writes the engine: its source, if that can be saved over; none for a preset's copy, which Save saves as a new engine.</summary>
        EngineAsset Target => EnginePresets.IsEditable(source) ? source : null;

        // The live engine and what drives it.
        EngineAudioSource live;
        float throttle;
        bool dyno;
        float dynoRpm = 2000f;
        bool cranking;
        bool dynoEngaged;
        // What the live engine was built from (tuning and description revision), to tell when a Restart is due.
        string builtState;

        readonly BenchContext context;
        List<BenchExtension> extensions = new List<BenchExtension>();
        readonly Dictionary<string, (ToolbarToggle tab, VisualElement content)> sections = new Dictionary<string, (ToolbarToggle, VisualElement)>();

        ToolbarButton engineButton, saveButton;
        Button startButton;
        VisualElement designHost;
        DesignSection design;
        VisualElement soundHost;
        SoundSection sound;
        // The engine's revision the Sound section was built for: another description takes a new one.
        int soundRevision;
        ExportSection export;
        Slider throttleSlider, dynoRpmSlider;
        VisualElement restartNotice;
        Label startNotice;
        Label noEngine;
        IVisualElementScheduledItem followDesign;

        // The views (built with the window's GUI: elements can't be made while the window itself is), and the instrument panel's figures.
        BenchVisuals visuals;
        // Dyno pulls: while one runs it drives the engine, and the run controls wait.
        readonly DynoSweep sweep = new DynoSweep();
        bool sweeping;
        Button sweepButton;
        VisualElement runControls;
        double lastFrame;
        Label engineTitle, engineDetails, torqueValue, powerValue, costValue, bufferedValue, underrunValue, codeValue, failure;

        public EngineBenchWindow() => context = new BenchContext(PutOnBench);

        [MenuItem("Window/Nx Engine Bench")]
        static void Open() => GetWindow<EngineBenchWindow>("Nx Engine Bench");

        /// <summary>
        /// Opens the Bench's window on the tab an extension from <paramref name="packageName"/> adds (see <see cref="BenchExtension"/>),
        /// or on its last tab when that package adds none. For packages that extend the Bench: Nx Vehicle Sound System opens it on
        /// its Export bank tab.
        /// </summary>
        public static EngineBenchWindow OpenOn(string packageName)
        {
            EngineBenchWindow window = GetWindow<EngineBenchWindow>("Nx Engine Bench");
            // A window not built yet opens the tab once its extensions are made.
            if (!window.OpenPackageSection(packageName)) window.pendingPackage = packageName;
            return window;
        }

        void OnEnable() => Undo.undoRedoPerformed += TuningUndone;

        void OnDisable()
        {
            Undo.undoRedoPerformed -= TuningUndone;
            StopLive();
            foreach (BenchExtension extension in extensions)
            {
                try
                {
                    extension.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            extensions.Clear();
        }

        /// <summary>The window is closed: its copy of the engine goes with it, and any change not saved.</summary>
        void OnDestroy()
        {
            if (engine != null) DestroyImmediate(engine);
        }

        public override void SaveChanges()
        {
            if (Save()) base.SaveChanges();
        }

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            visuals = new BenchVisuals();
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styles != null) root.styleSheets.Add(styles);
            root.AddToClassList("bench");

            root.Add(BuildToolbar());
            var body = new VisualElement();
            body.AddToClassList("bench__body");
            root.Add(body);

            var left = new ScrollView();
            left.AddToClassList("bench__left");
            body.Add(left);
            noEngine = Hint("Pick an engine in the top bar: a preset to start from, or an engine saved in the project. The Bench " +
                            "works on a copy of it: changes reach the project only when you save.");
            left.Add(noEngine);

            designHost = new VisualElement();
            AddSection(left, DesignSectionName, designHost);
            soundHost = new VisualElement();
            AddSection(left, SoundSectionName, soundHost);
            export = new ExportSection(OpenPackageSection);
            AddSection(left, ExportSectionName, export);

            extensions = BenchExtensions.CreateAll();
            foreach (BenchExtension extension in extensions)
            {
                VisualElement content;
                try
                {
                    content = extension.CreateGUI(context) ?? new VisualElement();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    content = new HelpBox($"{extension.Title} failed: {e.Message}", HelpBoxMessageType.Error);
                }

                AddSection(left, extension.Title, content);
            }

            body.Add(BuildMain());

            // Whatever engine the window kept is worked on as a copy.
            if (engine != null && !EnginePresets.IsTemporary(engine))
            {
                source = engine;
                engine = EnginePresets.TemporaryCopy(engine);
            }

            ShowEngine(engine);
            SelectSection(sections.ContainsKey(section) ? section : DesignSectionName);
            if (pendingPackage != null)
            {
                OpenPackageSection(pendingPackage);
                pendingPackage = null;
            }
            root.schedule.Execute(Tick).Every(100);
            lastFrame = EditorApplication.timeSinceStartup;
            root.schedule.Execute(Animate).Every(16);
        }

        // ---- Top bar ----

        VisualElement BuildToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("bench__toolbar");

            engineButton = new ToolbarButton { tooltip = "The engine on the bench, worked on as a copy: Save writes it to the project." };
            engineButton.AddToClassList("bench__engine-menu");
            engineButton.clicked += () => EngineMenu(engineButton.worldBound);
            toolbar.Add(engineButton);

            saveButton = new ToolbarButton(() => Save());
            saveButton.AddToClassList("save-button");
            toolbar.Add(saveButton);
            toolbar.Add(new ToolbarSpacer());

            var tabs = new VisualElement { name = "tabs" };
            tabs.AddToClassList("bench__row");
            toolbar.Add(tabs);
            return toolbar;
        }

        /// <summary>Shows the section an extension from <paramref name="packageName"/> adds, if one does.</summary>
        bool OpenPackageSection(string packageName)
        {
            foreach (BenchExtension extension in extensions)
            {
                UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(extension.GetType().Assembly);
                if (package == null || package.name != packageName || !sections.ContainsKey(extension.Title)) continue;
                SelectSection(extension.Title);
                return true;
            }

            return false;
        }

        void AddSection(VisualElement parent, string title, VisualElement content)
        {
            var tab = new ToolbarToggle { text = title };
            tab.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) SelectSection(title);
                else if (section == title) tab.SetValueWithoutNotify(true);
            });
            rootVisualElement.Q("tabs").Add(tab);

            content.AddToClassList("bench__section");
            parent.Add(content);
            sections[title] = (tab, content);
        }

        void SelectSection(string title)
        {
            section = title;
            foreach (KeyValuePair<string, (ToolbarToggle tab, VisualElement content)> entry in sections)
            {
                bool selected = entry.Key == title;
                entry.Value.tab.SetValueWithoutNotify(selected);
                entry.Value.content.EnableInClassList("bench__section--hidden", !selected || engine == null);
            }
        }

        // ---- Engine ----

        /// <summary>
        /// Puts a copy of <paramref name="saved"/> on the bench in place of the engine there, which goes, with any change not
        /// saved. A running engine restarts as the new one.
        /// </summary>
        void PutOnBench(EngineAsset saved)
        {
            sweep.Clear();
            EndSweep();
            EngineAsset left = engine;
            engine = EnginePresets.TemporaryCopy(saved);
            source = saved;
            modified = false;
            ShowEngine(engine);
            if (live != null)
            {
                live.Engine = engine;
                Cranked();
            }

            if (left != null) DestroyImmediate(left);
        }

        void ShowEngine(EngineAsset shown)
        {
            followDesign?.Pause();
            ShowEngineBar();
            noEngine.EnableInClassList("bench__section--hidden", shown != null);
            context.SetEngine(shown, shown != null ? source : null);

            designHost.Clear();
            design = null;
            if (shown != null)
            {
                design = new DesignSection(shown, DesignChanged);
                designHost.Add(design);
            }

            ShowSound(shown);
            export.ShowEngine(shown);
            visuals.SetEngine(shown);
            ShowFigures(shown);
            SelectSection(section);
        }

        /// <summary>
        /// The engine's name in the top bar, with whether it is saved; Save while there is something to save: a preset's copy
        /// as a new engine, a saved engine's changes over it; and the prompt on closing with changes unsaved.
        /// </summary>
        void ShowEngineBar()
        {
            EngineAsset target = Target;
            string state = engine == null ? "" : target == null ? " (unsaved)" : modified ? " (changed)" : "";
            engineButton.text = engine == null ? "Pick an engine ▾" : $"{engine.name}{state} ▾";
            saveButton.text = target == null ? "Save preset…" : "Save";
            saveButton.tooltip = target == null
                ? "Saves this engine in the project, where you pick."
                : $"Writes the changes to {AssetDatabase.GetAssetPath(target)}.";
            saveButton.EnableInClassList("bench__section--hidden", engine == null || (target != null && !modified));
            hasUnsavedChanges = modified;
            saveChangesMessage = engine != null ? $"{engine.name} has unsaved changes. Save them?" : "";
        }

        void ShowSound(EngineAsset shown)
        {
            soundHost.Clear();
            sound = null;
            if (shown == null) return;
            sound = new SoundSection(shown, () =>
            {
                live?.ApplyTunedMix();
                MarkModified();
            });
            soundRevision = shown.Revision;
            soundHost.Add(sound);
            dynoRpmSlider.highValue = (float)Units.ToRpm(shown.Description.engine.redline);
        }

        /// <summary>
        /// After a design change: the title at once; shortly after the last change, the sound tuning follows the new
        /// description. The views show the running engine, so they follow on Restart; with nothing running, with the sound.
        /// </summary>
        void DesignChanged()
        {
            MarkModified();
            followDesign ??= rootVisualElement.schedule.Execute(FollowDesign);
            followDesign.ExecuteLater(FollowDesignDelay);
        }

        void FollowDesign()
        {
            if (engine == null) return;
            ShowSound(engine);
            ShowFigures(engine);
            SelectSection(section);
            if (live == null) visuals.SetEngine(engine);
        }

        void MarkModified()
        {
            if (engine == null || modified) return;
            modified = true;
            ShowEngineBar();
        }

        void EngineMenu(Rect position)
        {
            var menu = new GenericMenu();
            foreach (string name in EnginePresets.EngineNames())
            {
                EngineAsset preset = EnginePresets.LoadEngine(name);
                menu.AddItem(new GUIContent("Presets/" + preset.Description.engine.name), false, () => Pick(preset));
            }

            string[] saved = EnginePresets.ProjectEnginePaths();
            if (saved.Length == 0) menu.AddDisabledItem(new GUIContent("In the project/None saved yet"));
            foreach (string path in saved)
            {
                string label = path.Substring("Assets/".Length, path.Length - "Assets/".Length - ".asset".Length);
                menu.AddItem(new GUIContent("In the project/" + label), source != null && AssetDatabase.GetAssetPath(source) == path,
                    () => Pick(AssetDatabase.LoadAssetAtPath<EngineAsset>(path)));
            }

            menu.DropDown(position);
        }

        /// <summary>
        /// Puts a copy of <paramref name="picked"/> on the bench, unless the user keeps the unsaved changes to the engine there;
        /// the copy left goes, with any change not saved.
        /// </summary>
        void Pick(EngineAsset picked)
        {
            if (modified)
            {
                int answer = EditorUtility.DisplayDialogComplex("Unsaved changes", $"{engine.name} has unsaved changes.",
                    Target == null ? "Save…" : "Save", "Cancel", "Discard");
                if (answer == 1 || (answer == 0 && !Save())) return;
            }

            PutOnBench(picked);
        }

        /// <summary>
        /// Saves the engine: over the saved engine it was copied from, or, a preset's copy, as a new engine where the user
        /// picks. False if it isn't saved.
        /// </summary>
        bool Save()
        {
            if (engine == null) return true;
            EngineAsset target = Target;
            if (target != null)
            {
                EnginePresets.SaveOver(engine, target);
            }
            else
            {
                string folder = AssetDatabase.IsValidFolder(EnginePresets.DefaultEngineFolder) ? EnginePresets.DefaultEngineFolder : "Assets";
                string path = EditorUtility.SaveFilePanelInProject("Save engine preset", engine.name, "asset", "Where to save the engine.", folder);
                if (string.IsNullOrEmpty(path)) return false;

                try
                {
                    source = EnginePresets.SaveAs(engine, path);
                }
                catch (ArgumentException e)
                {
                    EditorUtility.DisplayDialog("Save engine preset", e.Message, "OK");
                    return false;
                }

                context.SetEngine(engine, source);
            }

            modified = false;
            ShowEngineBar();
            ShowFigures(engine);
            EditorGUIUtility.PingObject(source);
            return true;
        }

        void TuningUndone()
        {
            if (engine == null) return;
            MarkModified();
            design?.Refresh();
            if (sound != null && soundRevision == engine.Revision) sound.Refresh();
            else ShowSound(engine);
            ShowFigures(engine);
            live?.ApplyTunedMix();
        }

        // ---- Run controls, over the engine view ----

        VisualElement BuildControls()
        {
            var controls = new VisualElement();
            controls.AddToClassList("run-controls");

            restartNotice = new VisualElement();
            restartNotice.AddToClassList("run-controls__notice");
            restartNotice.AddToClassList("bench__section--hidden");
            Text(restartNotice, "The engine changed since the start.", "run-controls__notice-text");
            restartNotice.Add(new Button(Restart) { text = "Restart", tooltip = "Starts again from a fresh engine, as designed and tuned now." });
            controls.Add(restartNotice);
            startNotice = Text(controls, "It didn't fire on its starter. With Dyno ticked, the dyno turns it.", "run-controls__notice-text");
            startNotice.AddToClassList("bench__section--hidden");

            var row = new VisualElement();
            row.AddToClassList("run-controls__row");
            startButton = new Button(() =>
            {
                if (live == null) StartLive();
                else StopLive();
            }) { tooltip = "Start: turns the engine over until it fires (or the dyno takes it). Stop: silences it." };
            startButton.AddToClassList("start-button");
            row.Add(startButton);

            runControls = new VisualElement();
            runControls.AddToClassList("run-controls__inputs");
            var dynoToggle = new Toggle
            {
                text = "Dyno", value = dyno,
                tooltip = "A dynamometer holds the crank at the dyno speed, at full throttle: the engine's torque there is its load.",
            };
            dynoToggle.AddToClassList("run-controls__dyno");
            throttleSlider = new Slider("Throttle", 0f, 1f) { value = throttle, showInputField = true, tooltip = "0 closed, 1 open." };
            dynoRpmSlider = new Slider("Dyno rpm", 0f, 8000f) { value = dynoRpm, showInputField = true, tooltip = "The speed the dyno holds." };
            var volumeSlider = new Slider("Volume", 0f, 1f) { value = volume, tooltip = "The listening level." };
            volumeSlider.AddToClassList("run-controls__volume");
            dynoToggle.RegisterValueChangedCallback(e =>
            {
                dyno = e.newValue;
                dynoEngaged = false;
                ShowDynoOrThrottle();
                Push();
            });
            throttleSlider.RegisterValueChangedCallback(e =>
            {
                throttle = e.newValue;
                Push();
            });
            dynoRpmSlider.RegisterValueChangedCallback(e =>
            {
                dynoRpm = e.newValue;
                Push();
            });
            volumeSlider.RegisterValueChangedCallback(e =>
            {
                volume = e.newValue;
                if (live != null) live.Volume = volume;
            });
            runControls.Add(dynoToggle);
            runControls.Add(throttleSlider);
            runControls.Add(dynoRpmSlider);
            runControls.Add(volumeSlider);
            row.Add(runControls);
            controls.Add(row);

            ShowDynoOrThrottle();
            ShowRunning();
            return controls;
        }

        /// <summary>The throttle without the dyno; the dyno's speed with it (it runs at full throttle).</summary>
        void ShowDynoOrThrottle()
        {
            throttleSlider.EnableInClassList("bench__section--hidden", dyno);
            dynoRpmSlider.EnableInClassList("bench__section--hidden", !dyno);
        }

        void ShowRunning()
        {
            bool running = live != null;
            startButton.text = running ? "Stop" : "Start";
            startButton.EnableInClassList("start-button--running", running);
        }

        /// <summary>Builds the engine fresh and turns it over, the dyno taking it once it turns.</summary>
        void StartLive()
        {
            if (engine == null) return;

            var holder = new GameObject("Nx Engine Bench") { hideFlags = HideFlags.HideAndDontSave };
            var audioSource = holder.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            live = holder.AddComponent<EngineAudioSource>();
            live.playInEditMode = true;
            live.Volume = volume;
            live.Engine = engine;
            live.captureForBench = true;
            live.Restart();
            Cranked();
            ShowRunning();
        }

        void Restart()
        {
            if (live == null) return;
            live.Restart();
            Cranked();
            visuals.SetEngine(engine);
        }

        /// <summary>A fresh engine on the bench: turn it over, the dyno waiting for it to turn.</summary>
        void Cranked()
        {
            cranking = true;
            dynoEngaged = false;
            startNotice.AddToClassList("bench__section--hidden");
            builtState = BuildState();
            Push();
        }

        /// <summary>
        /// While Start turns the engine over, by the simulation's own clock and revs (the sound thread runs the simulation, so the
        /// window's clock can run ahead of it): with the dyno wanted, the dyno takes the crank once it turns or after the demo's
        /// 1.5 s of cranking; else the starter turns it 1.5 s, and on until it runs on its own, or gives up.
        /// </summary>
        void Crank()
        {
            EngineCapture capture = live.Capture;
            if (capture == null) return;
            double rpm = capture.Rpm;
            double simulated = capture.PublishedSteps / (double)capture.SimulationFrequency;
            // As the reference's dyno runs do: the dyno takes the crank once it turns, or once the starter has turned it a while.
            bool held = dyno && (rpm >= DynoEngageRpm || simulated >= CrankSeconds);
            bool fired = rpm >= FiredOverStarter * Units.ToRpm(engine.Description.engine.starterSpeed);
            if (!held && simulated < CrankTimeout && (simulated < CrankSeconds || !fired)) return;

            dynoEngaged = held;
            if (!held && !fired) startNotice.RemoveFromClassList("bench__section--hidden");

            cranking = false;
            Push();
        }

        void StopLive()
        {
            sweep.Stop();
            EndSweep();
            if (live != null) DestroyImmediate(live.gameObject);
            live = null;
            cranking = false;
            dynoEngaged = false;
            startNotice?.AddToClassList("bench__section--hidden");
            if (startButton != null) ShowRunning();
        }

        /// <summary>Sends the controls to the live engine: the run controls', or a dyno pull's while one runs.</summary>
        void Push()
        {
            if (live == null) return;
            live.Ignition = true;
            live.Starter = cranking && !sweeping;
            if (sweeping)
            {
                live.Throttle = 1;
                live.DynoEnabled = true;
                live.DynoHold = false;
                live.DynoRpm = (float)sweep.Speed;
                return;
            }

            bool held = dyno && dynoEngaged;
            live.DynoEnabled = held;
            live.DynoHold = true;
            live.DynoRpm = dynoRpm;
            float wanted = held ? 1f : dyno ? 0f : throttle;
            live.Throttle = cranking ? Math.Max(wanted, CrankThrottle) : wanted;
        }

        /// <summary>What the next Restart builds from: the tuning's build values and the description's revision.</summary>
        string BuildState() => engine != null ? JsonUtility.ToJson(engine.Tuning.build) + "#" + engine.Revision : null;

        // ---- Main area ----

        VisualElement BuildMain()
        {
            var main = new VisualElement();
            main.AddToClassList("bench__main");

            var stage = new VisualElement();
            stage.AddToClassList("bench__stage");
            visuals.EngineView.Add(BuildControls());
            stage.Add(visuals.EngineView);
            stage.Add(BuildInstrument());
            main.Add(stage);

            var traces = new VisualElement();
            traces.AddToClassList("bench__traces");
            traces.Add(TracePanel("EXHAUST SIGNAL · LAST 60 MS",
                "Each exhaust's pressure signal, the synthesizer's input that the sound is made from, in real time.", visuals.Scope));
            traces.Add(TracePanel("OUTPUT SPECTRUM",
                "The sound playing now, by frequency; the line marks the firing frequency.", visuals.Spectrum));
            traces.Add(BuildDynoPanel());
            main.Add(traces);
            return main;
        }

        VisualElement BuildDynoPanel()
        {
            var panel = new VisualElement
            {
                tooltip = "Torque and power from a pull on the dyno: full throttle, the dyno letting the engine speed up 500 rpm a second " +
                          "to the redline, as engine-sim runs one. The faint curves are the pull before.",
            };
            panel.AddToClassList("trace-panel");
            var header = new VisualElement();
            header.AddToClassList("trace-panel__header");
            Text(header, "DYNO PULL · TORQUE AND POWER", "instrument__caption");
            sweepButton = new Button(() =>
            {
                if (sweeping) StopSweep();
                else StartSweep();
            }) { text = "Sweep", tooltip = "Runs a pull on the running engine." };
            sweepButton.AddToClassList("trace-panel__button");
            header.Add(sweepButton);
            panel.Add(header);
            panel.Add(visuals.Dyno);
            return panel;
        }

        void StartSweep()
        {
            EngineCapture capture = live != null && live.Playing ? live.Capture : null;
            if (capture == null || cranking || engine == null) return;
            sweep.Start(capture, engine.Description.engine);
            sweeping = true;
            runControls.SetEnabled(false);
            sweepButton.text = "Stop";
            Push();
        }

        void StopSweep()
        {
            sweep.Stop();
            EndSweep();
        }

        /// <summary>After a pull (finished or stopped): the run controls drive the engine again.</summary>
        void EndSweep()
        {
            if (!sweeping) return;
            sweeping = false;
            runControls.SetEnabled(true);
            sweepButton.text = "Sweep";
            Push();
        }

        static VisualElement TracePanel(string title, string tooltip, VisualElement view)
        {
            var panel = new VisualElement { tooltip = tooltip };
            panel.AddToClassList("trace-panel");
            Text(panel, title, "instrument__caption");
            panel.Add(view);
            return panel;
        }

        VisualElement BuildInstrument()
        {
            var panel = new ScrollView();
            panel.AddToClassList("instrument");
            engineTitle = Text(panel, "", "instrument__title");
            engineDetails = Text(panel, "", "instrument__subtitle");
            panel.Add(visuals.Tachometer);

            Text(panel, "DYNO", "instrument__caption");
            torqueValue = Readout(panel, "Torque");
            powerValue = Readout(panel, "Power");

            Text(panel, "FIRING ORDER", "instrument__caption");
            panel.Add(visuals.Lights);

            Text(panel, "RENDERING", "instrument__caption");
            costValue = Readout(panel, "Render cost (s per s of sound)");
            bufferedValue = Readout(panel, "Buffered");
            underrunValue = Readout(panel, "Underrun");
            codeValue = Readout(panel, "Engine code");
            failure = Text(panel, "", "instrument__failure");
            return panel;
        }

        void ShowFigures(EngineAsset shown)
        {
            if (shown == null)
            {
                engineTitle.text = "No engine";
                engineDetails.text = "";
                return;
            }

            EngineParametersDescription e = shown.Description.engine;
            int cylinders = 0;
            foreach (CylinderBankDescription bank in e.cylinderBanks) cylinders += bank.cylinderCount;
            engineTitle.text = shown.name;
            engineDetails.text = $"{e.name} · {cylinders} cylinder{(cylinders == 1 ? "" : "s")}\nredline {Units.ToRpm(e.redline):#,0} · " +
                                 $"rev limit {Units.ToRpm(e.ignitionModule.revLimit):#,0} rpm";
        }

        /// <summary>Every frame: the views, by the real time since the last.</summary>
        void Animate()
        {
            double now = EditorApplication.timeSinceStartup;
            // A long pause (the Editor busy) restarts the replay's clock rather than skipping through it.
            double seconds = Math.Min(now - lastFrame, 0.1);
            lastFrame = now;
            if (live != null && cranking) Crank();
            if (live != null && dyno && !dynoEngaged && !cranking && live.Rpm >= DynoEngageRpm)
            {
                dynoEngaged = true;
                Push();
            }

            if (sweeping)
            {
                sweep.Update(live != null && live.Playing ? live.Capture : null);
                if (sweep.Running) Push();
                else EndSweep();
            }

            visuals.Animate(live, seconds, sweep, (dyno && dynoEngaged) || sweeping);
        }

        /// <summary>Ten times a second: the readouts, and whether the tuning changed since the live engine was built.</summary>
        void Tick()
        {
            sweepButton.SetEnabled(sweeping || (live != null && live.Playing && !cranking));
            bool burst = BurstBlockRenderer.RunsBurstCompiled() == 1;
            codeValue.text = burst ? "Burst-compiled" : "managed: Burst compilation is off";
            Mark(codeValue, burst ? "value--good" : "value--caution");

            if (live == null || !live.Playing)
            {
                torqueValue.text = powerValue.text = "–";
                costValue.text = bufferedValue.text = underrunValue.text = "–";
                Mark(costValue, null);
                failure.text = "";
                restartNotice.AddToClassList("bench__section--hidden");
                return;
            }

            EngineCapture capture = live.Capture;
            if (((dyno && dynoEngaged) || sweeping) && capture != null)
            {
                double torque = capture.DynoTorque;
                double watts = torque * Units.Rpm(capture.Rpm);
                torqueValue.text = $"{torque:0} N·m";
                powerValue.text = $"{watts / 1000:0} kW · {watts / 745.7:0} hp";
            }
            else
            {
                torqueValue.text = powerValue.text = "dyno off";
            }

            double cost = live.RenderCost;
            costValue.text = cost.ToString("0.00");
            Mark(costValue, cost >= 1 ? "value--danger" : cost >= 0.9 ? "value--caution" : null);
            bufferedValue.text = $"{live.BufferedSeconds * 1000:0} ms";
            underrunValue.text = $"{live.UnderrunSeconds:0.0} s of {live.ProducedSeconds + live.UnderrunSeconds:0.0} s";
            failure.text = live.Failure != null ? "The simulation stopped: " + live.Failure.Message : "";
            restartNotice.EnableInClassList("bench__section--hidden", BuildState() == builtState);
        }

        static void Mark(Label label, string state)
        {
            label.EnableInClassList("value--good", state == "value--good");
            label.EnableInClassList("value--caution", state == "value--caution");
            label.EnableInClassList("value--danger", state == "value--danger");
        }

        static Label Readout(VisualElement parent, string name)
        {
            var row = new VisualElement();
            row.AddToClassList("readout");
            Text(row, name, "readout__name");
            Label value = Text(row, "–", "readout__value");
            parent.Add(row);
            return value;
        }

        static Label Text(VisualElement parent, string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        static Label Hint(string text)
        {
            var label = new Label(text);
            label.AddToClassList("bench__hint");
            return label;
        }
    }
}
