// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>How a tuned value is shown: the conversion from the description's SI value to the slider's unit and back.</summary>
    readonly struct TuningScale
    {
        public readonly Func<double, double> show;
        public readonly Func<double, double> store;

        public TuningScale(Func<double, double> show, Func<double, double> store)
        {
            this.show = show;
            this.store = store;
        }

        public static TuningScale Linear(double unit) => new TuningScale(v => v / unit, v => v * unit);
        public static readonly TuningScale Plain = Linear(1);
        public static readonly TuningScale Decibels = new TuningScale(v => 20 * Math.Log10(v), v => Math.Pow(10, v / 20));
    }

    /// <summary>
    /// One tuned value: a tick that replaces the script's value (shown, greyed, until then) with the slider's, which starts
    /// from the script's. A slider drag is one undo step. The range widens to a value outside it rather than clamping it.
    /// </summary>
    sealed class TunedValueField : VisualElement
    {
        readonly Toggle tick;
        readonly Slider slider;
        readonly Func<TunedValue> current;
        readonly double script;
        readonly TuningScale scale;
        readonly float min, max;
        int dragGroup = -1;

        /// <param name="current">Reads the value as tuned now.</param>
        /// <param name="tune">Changes it: an undo name and the new value.</param>
        public TunedValueField(string label, string tooltip, double script, TuningScale scale, float min, float max,
            Func<TunedValue> current, Action<string, TunedValue> tune)
        {
            this.current = current;
            this.script = script;
            this.scale = scale;
            this.min = min;
            this.max = max;
            AddToClassList("tuned-value");

            tick = new Toggle { tooltip = "Replace the script's value." };
            tick.AddToClassList("tuned-value__tick");
            slider = new Slider(label, min, max) { showInputField = true, tooltip = tooltip };
            slider.AddToClassList("tuned-value__slider");
            Add(tick);
            Add(slider);

            // Ticked: the script's value, exactly; unticked: back to the script's.
            tick.RegisterValueChangedCallback(e =>
                tune(e.newValue ? "Tune " + label : "Untune " + label, new TunedValue { overridden = e.newValue, value = e.newValue ? script : 0 }));
            slider.RegisterValueChangedCallback(e =>
            {
                if (!current().overridden) return;
                tune("Tune " + label, new TunedValue { overridden = true, value = scale.store(e.newValue) });
                if (dragGroup >= 0) Undo.CollapseUndoOperations(dragGroup);
            });
            slider.RegisterCallback<PointerDownEvent>(_ => dragGroup = Undo.GetCurrentGroup(), TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => dragGroup = -1);
            Refresh();
        }

        /// <summary>Shows the value as tuned now (after a change, an undo or another engine).</summary>
        public void Refresh()
        {
            TunedValue value = current();
            float shown = (float)scale.show(value.overridden ? value.value : script);
            slider.lowValue = Mathf.Min(min, shown);
            slider.highValue = Mathf.Max(max, shown);
            tick.SetValueWithoutNotify(value.overridden);
            slider.SetValueWithoutNotify(shown);
            slider.SetEnabled(value.overridden);
        }
    }
}
