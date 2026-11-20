// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Simulation;
using UnityEditor;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// The Bench's Sound section: the synthesizer's mix, played at once (<see cref="EngineSoundTuning.mixer"/>). Everything
    /// else about the engine has its one place in the Design section; an engine without a design is tuned here
    /// instead, on top of its description (<see cref="EngineSoundTuning.build"/>, on Restart), and so is any engine that
    /// still holds such tuning, so that none is hidden. It changes the Bench's in-memory copy of the engine: changes go
    /// through Undo and reach the project when the Bench saves. Built for one engine; the Bench builds another for another.
    /// </summary>
    sealed class SoundSection : VisualElement
    {
        // Flow constants as the scripts write them, k_carb(cfm), which is linear in the flow.
        static readonly TuningScale CarbCfm = TuningScale.Linear(GasSystem.K_carb(1.0));
        static readonly TuningScale Inches = TuningScale.Linear(Units.inch);
        static readonly TuningScale SquareInches = TuningScale.Linear(Units.inch * Units.inch);

        readonly EngineAsset engine;
        readonly Action mixChanged;
        readonly List<Action> refreshers = new List<Action>();

        /// <param name="mixChanged">Called after each change: the live engine plays the tuned mix from its next block.</param>
        public SoundSection(EngineAsset engine, Action mixChanged)
        {
            this.engine = engine;
            this.mixChanged = mixChanged;

            bool tunesEngine = engine.Design == null || engine.Tuning.TunesBuild();
            Hint(this, "A ticked value replaces the engine's own and is kept on the engine when you save it. The mixer plays at once." +
                       (engine.Design != null ? " Everything else about the engine is in Design." : ""));

            EngineDescription script = engine.Description;
            EngineSoundMix loaded = EngineSoundMix.AsLoaded(script.engine.highFrequencyGain, script.engine.noise, script.engine.jitter);

            Caption(this, "Mixer");
            Tuned(this, "Filter amount", "How much of the sound goes through the exhaust filter, the rest being the raw exhaust pulses " +
                "(engine-sim's X: convolution level). 1: all of it.", loaded.convolution, TuningScale.Plain, 0, 1,
                t => t.mixer.convolution, (t, v) => t.mixer.convolution = v);
            Tuned(this, "High freq. gain", "Mixes the pulses' rate of change into the sound: brighter and raspier (engine-sim's C).",
                loaded.highFrequencyGain, TuningScale.Plain, 0, 1, t => t.mixer.highFrequencyGain, (t, v) => t.mixer.highFrequencyGain = v);
            Tuned(this, "Low freq. noise", "How much low-passed noise modulates the pulses: rougher, breathier (engine-sim's V).",
                loaded.lowFrequencyNoise, TuningScale.Plain, 0, 1, t => t.mixer.lowFrequencyNoise, (t, v) => t.mixer.lowFrequencyNoise = v);
            Tuned(this, "Noise cutoff (Hz)", "The cutoff of that noise's low-pass filter.", loaded.lowFrequencyNoiseCutoff, TuningScale.Plain,
                20, 20000, t => t.mixer.lowFrequencyNoiseCutoff, (t, v) => t.mixer.lowFrequencyNoiseCutoff = v);
            Tuned(this, "High freq. noise", "Random jitter in reading the simulation's samples (engine-sim's B).", loaded.highFrequencyNoise,
                TuningScale.Plain, 0, 1, t => t.mixer.highFrequencyNoise, (t, v) => t.mixer.highFrequencyNoise = v);

            if (tunesEngine) BuildEngineTuning(script);

            var reset = new Button(() => Tune("Reset engine sound tuning", t => t.Clear())) { text = "Reset all to the engine's own" };
            Add(reset);
        }

        /// <summary>
        /// What applies when the engine is built, tuned on top of its description: for an engine without a design, or one that
        /// still holds such tuning (which then replaces its design's values).
        /// </summary>
        void BuildEngineTuning(EngineDescription script)
        {
            Caption(this, "Engine (on Restart)");
            if (engine.Design != null)
            {
                Hint(this, "This engine was tuned here before it had a design: these replace its design's values. Untick them to edit " +
                           "the values in Design.");
            }

            Tuned(this, "Sim. frequency (Hz)", "Steps per second of the simulation, also the rate the exhaust is sampled: before the " +
                "filter, the sound has nothing above half of it. The render cost grows in proportion.", script.engine.simulationFrequency,
                TuningScale.Plain, 2000, 60000, t => t.build.simulationFrequency, (t, v) => t.build.simulationFrequency = v);

            for (int i = 0; i < script.engine.exhaustSystems.Length; ++i) Add(Exhaust(script, i));
            for (int h = 0; h < script.engine.cylinderHeads.Length; ++h) Add(Head(script, h));
        }

        /// <summary>Shows the tuning as it is now (after an undo, say).</summary>
        public void Refresh()
        {
            foreach (Action refresh in refreshers) refresh();
        }

        VisualElement Exhaust(EngineDescription script, int index)
        {
            ExhaustSystemDescription exhaust = script.engine.exhaustSystems[index];
            var foldout = new Foldout { text = $"Exhaust {index + 1} ({Venting(script, index)}; on Restart)", value = false };

            ImpulseResponseAsset scripted = engine.ImpulseResponses[exhaust.impulseResponse];
            double scriptedVolume = script.impulseResponses[exhaust.impulseResponse].volume;
            EngineSoundTuning.ExhaustSystem Existing() => index < engine.Tuning.build.exhaustSystems.Length ? engine.Tuning.build.exhaustSystems[index] : null;

            // The package's filters, and the script's own if it isn't one of them.
            var filters = new List<(string label, ImpulseResponseAsset asset)>();
            foreach (ExhaustFilter f in ExhaustFilters.All)
            {
                ImpulseResponseAsset asset = EnginePresets.ImpulseResponseFromFile(f.response.file);
                if (asset != null) filters.Add((f.Label, asset));
            }

            if (!filters.Exists(f => f.asset == scripted)) filters.Insert(0, (scripted.name, scripted));
            var choices = new List<string>();
            foreach ((string label, ImpulseResponseAsset _) in filters) choices.Add(label);

            var filter = new DropdownField("Filter", choices, 0)
            {
                tooltip = "The impulse response the exhaust's pulses go through: the muffler, and the space between the pipe and the " +
                          "listener, from 1 (least muffled) to 7 (most), and two variants. Another one plays as loud: its volume is set so " +
                          "it passes as much energy. The script's own: back to the script's filter and volume.",
            };
            filter.RegisterValueChangedCallback(e =>
            {
                int at = choices.IndexOf(e.newValue);
                if (at < 0) return;
                ImpulseResponseAsset picked = filters[at].asset;
                EngineSoundTuning.ExhaustSystem tuned = Existing();
                ImpulseResponseAsset current = tuned != null && tuned.impulseResponse != null ? tuned.impulseResponse : scripted;
                if (picked == current) return;
                double currentVolume = tuned != null ? tuned.impulseResponseVolume.Or(scriptedVolume) : scriptedVolume;
                Tune("Pick exhaust filter", t =>
                {
                    EngineSoundTuning.ExhaustSystem x = t.Exhaust(index);
                    if (picked == scripted)
                    {
                        x.impulseResponse = null;
                        x.impulseResponseVolume = default;
                        return;
                    }

                    x.impulseResponse = picked;
                    x.impulseResponseVolume = new TunedValue { overridden = true, value = EnginePresets.MatchedVolume(currentVolume, current, picked) };
                });
            });
            void ShowFilter()
            {
                EngineSoundTuning.ExhaustSystem tuned = Existing();
                ImpulseResponseAsset shown = tuned != null && tuned.impulseResponse != null ? tuned.impulseResponse : scripted;
                int at = filters.FindIndex(f => f.asset == shown);
                filter.SetValueWithoutNotify(at >= 0 ? choices[at] : shown.name);
            }

            ShowFilter();
            refreshers.Add(ShowFilter);
            foldout.Add(filter);

            TunedValue Get(Func<EngineSoundTuning.ExhaustSystem, TunedValue> value) => Existing() is EngineSoundTuning.ExhaustSystem x ? value(x) : default;
            Tuned(foldout, "Filter volume (dB)", "The filter's gain.", scriptedVolume, TuningScale.Decibels, -100, 0,
                _ => Get(x => x.impulseResponseVolume), (t, v) => t.Exhaust(index).impulseResponseVolume = v);
            Tuned(foldout, "Pipe volume (dB)", "This exhaust's share of the sound. 0 dB is audio_volume 1 in the script.",
                exhaust.audioVolume, TuningScale.Decibels, -60, 20, _ => Get(x => x.audioVolume), (t, v) => t.Exhaust(index).audioVolume = v);
            Tuned(foldout, "Pipe length (in)", "From the collector to the outlet: delays the pulses, and the sound falls with the square of " +
                "the whole path. With the collector area, also the exhaust's volume.", exhaust.length, Inches, 1, 600,
                _ => Get(x => x.length), (t, v) => t.Exhaust(index).length = v);
            Tuned(foldout, "Collector area (sq in)", "The pipe's cross-section.", exhaust.collectorCrossSectionArea, SquareInches, 0.5f, 30,
                _ => Get(x => x.collectorCrossSectionArea), (t, v) => t.Exhaust(index).collectorCrossSectionArea = v);
            Tuned(foldout, "Outlet flow (cfm)", "How freely the exhaust vents (the muffler's restriction), as k_carb(cfm) in the scripts.",
                exhaust.outletFlowRate, CarbCfm, 10, 3000, _ => Get(x => x.outletFlowRate), (t, v) => t.Exhaust(index).outletFlowRate = v);
            Tuned(foldout, "Primary tube length (in)", "Added to each header's length.", exhaust.primaryTubeLength, Inches, 0, 60,
                _ => Get(x => x.primaryTubeLength), (t, v) => t.Exhaust(index).primaryTubeLength = v);
            Tuned(foldout, "Primary flow (cfm)", "How freely each cylinder's primary flows into the collector, as k_carb(cfm).",
                exhaust.primaryFlowRate, CarbCfm, 10, 1000, _ => Get(x => x.primaryFlowRate), (t, v) => t.Exhaust(index).primaryFlowRate = v);
            Tuned(foldout, "Velocity decay", "How fast the gas in the primaries loses its velocity.", exhaust.velocityDecay, TuningScale.Plain,
                0, 1, _ => Get(x => x.velocityDecay), (t, v) => t.Exhaust(index).velocityDecay = v);
            return foldout;
        }

        VisualElement Head(EngineDescription script, int head)
        {
            HeadCylinderDescription[] cylinders = script.engine.cylinderHeads[head].cylinders;
            var foldout = new Foldout { text = $"Head {head + 1} headers (on Restart)", value = false };
            for (int c = 0; c < cylinders.Length; ++c)
            {
                int index = c;
                TunedValue Get(Func<EngineSoundTuning.Cylinder, TunedValue> value)
                {
                    EngineSoundTuning.CylinderHead[] heads = engine.Tuning.build.cylinderHeads;
                    EngineSoundTuning.CylinderHead h = head < heads.Length ? heads[head] : null;
                    EngineSoundTuning.Cylinder cylinder = h != null && index < h.cylinders.Length ? h.cylinders[index] : null;
                    return cylinder != null ? value(cylinder) : default;
                }

                Tuned(foldout, $"Cylinder {c + 1} header (in)", "This cylinder's header primary. With the exhaust's primary tube and pipe, " +
                    "the path of its pulses to the outlet: when they arrive, and how loud.", cylinders[c].headerPrimaryLength, Inches, 0, 60,
                    _ => Get(x => x.headerPrimaryLength), (t, v) => t.HeadCylinder(head, index).headerPrimaryLength = v);
                Tuned(foldout, $"Cylinder {c + 1} loudness", "Scales this cylinder's pulses in the sound.", cylinders[c].soundAttenuation,
                    TuningScale.Plain, 0, 2, _ => Get(x => x.soundAttenuation), (t, v) => t.HeadCylinder(head, index).soundAttenuation = v);
            }

            return foldout;
        }

        /// <summary>Which heads' cylinders vent into exhaust system <paramref name="exhaust"/>.</summary>
        static string Venting(EngineDescription script, int exhaust)
        {
            var parts = new List<string>();
            CylinderHeadDescription[] heads = script.engine.cylinderHeads;
            for (int h = 0; h < heads.Length; ++h)
            {
                int count = 0;
                foreach (HeadCylinderDescription c in heads[h].cylinders)
                {
                    if (c.exhaustSystem == exhaust) ++count;
                }

                if (count > 0) parts.Add($"head {h + 1}, {count} cyl.");
            }

            return parts.Count > 0 ? string.Join("; ", parts) : "no cylinders";
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

        void Tuned(VisualElement parent, string label, string tooltip, double script, TuningScale scale, float min, float max,
            Func<EngineSoundTuning, TunedValue> get, Action<EngineSoundTuning, TunedValue> set)
        {
            var field = new TunedValueField(label, tooltip, script, scale, min, max, () => get(engine.Tuning),
                (undoName, value) => Tune(undoName, t => set(t, value)));
            refreshers.Add(field.Refresh);
            parent.Add(field);
        }

        /// <summary>Changes the tuning, with Undo, and sends the mixer to the live engine.</summary>
        void Tune(string undoName, Action<EngineSoundTuning> change)
        {
            Undo.RecordObject(engine, undoName);
            change(engine.Tuning);
            mixChanged();
            Refresh();
        }
    }
}
