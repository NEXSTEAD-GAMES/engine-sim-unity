// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// The exhaust signal over the last <see cref="Seconds"/>, in real time: each exhaust's input to the synthesizer, the
    /// pressure pulses the sound is made from, one trace per exhaust. The scale follows the signal's range, easing back as it
    /// shrinks.
    /// </summary>
    sealed class ScopeView : VisualElement
    {
        public const double Seconds = 0.06;
        /// <summary>How much of its range the scale gives back per update when the signal shrinks.</summary>
        const float Easing = 0.03f;
        static readonly Color Grid = new Color32(0x2a, 0x2f, 0x3a, 0xFF);
        static readonly Color TextColor = new Color32(0x9a, 0xa0, 0xa6, 0xFF);
        static readonly Color[] Traces =
        {
            new Color32(0x38, 0xd3, 0xb0, 0xFF), new Color32(0x4f, 0xc3, 0xf7, 0xFF), new Color32(0xff, 0xb0, 0x20, 0xFF),
            new Color32(0xc7, 0x92, 0xea, 0xFF),
        };

        EngineCapture capture;
        CaptureSteps steps;
        bool hasSignal;
        float low, high;

        public ScopeView()
        {
            AddToClassList("trace-view");
            generateVisualContent += Draw;
        }

        /// <summary>Reads the latest steps of <paramref name="from"/>; null clears the scope.</summary>
        public void Show(EngineCapture from)
        {
            if (from == null)
            {
                if (!hasSignal) return;
                hasSignal = false;
                capture = null;
                MarkDirtyRepaint();
                return;
            }

            if (from != capture)
            {
                capture = from;
                int count = Math.Min(from.ReadableSteps, (int)Math.Round(Seconds * from.SimulationFrequency));
                steps = new CaptureSteps(count, from.Cylinders, from.Exhausts);
                low = high = 0;
            }

            long published = from.PublishedSteps;
            if (published < steps.Count || !from.ReadSteps(published - steps.Count, steps)) return;

            float min = 0, max = 0;
            foreach (float value in steps.exhaustInput)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            low = Math.Min(min, low * (1 - Easing));
            high = Math.Max(max, high * (1 - Easing));
            hasSignal = true;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            Rect area = contentRect;
            if (area.width < 20 || area.height < 20) return;
            Painter2D painter = context.painter2D;

            // A line every 10 ms.
            painter.strokeColor = Grid;
            painter.lineWidth = 1;
            painter.BeginPath();
            for (int ms = 10; ms < Seconds * 1000; ms += 10)
            {
                float x = area.xMax - (float)(ms / (Seconds * 1000)) * area.width;
                painter.MoveTo(new Vector2(x, area.yMin));
                painter.LineTo(new Vector2(x, area.yMax));
            }

            painter.Stroke();
            context.DrawText($"{Seconds * 1000:0} ms ago", new Vector2(area.xMin + 4, area.yMax - 15), 10, TextColor);
            context.DrawText("now", new Vector2(area.xMax - 24, area.yMax - 15), 10, TextColor);
            if (!hasSignal || high - low <= 0) return;

            float span = (high - low) * 1.1f;
            float top = high + (span - (high - low)) / 2;
            float Y(float value) => area.yMin + (top - value) / span * area.height;

            // Zero.
            painter.strokeColor = Grid;
            painter.BeginPath();
            painter.MoveTo(new Vector2(area.xMin, Y(0)));
            painter.LineTo(new Vector2(area.xMax, Y(0)));
            painter.Stroke();

            // Per pixel column, the lowest and highest value of its steps: every pulse shows, however narrow.
            int count = steps.Count, exhausts = steps.Exhausts;
            int columns = Math.Max(1, Math.Min(count, (int)area.width));
            painter.lineWidth = 1.2f;
            painter.lineJoin = LineJoin.Round;
            for (int x = 0; x < exhausts; ++x)
            {
                painter.strokeColor = Traces[x % Traces.Length];
                painter.BeginPath();
                for (int column = 0; column < columns; ++column)
                {
                    int from = column * count / columns, to = Math.Max(from + 1, (column + 1) * count / columns);
                    float min = float.MaxValue, max = float.MinValue;
                    for (int s = from; s < to; ++s)
                    {
                        float value = steps.exhaustInput[s * exhausts + x];
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }

                    float px = area.xMin + (column + 0.5f) / columns * area.width;
                    if (column == 0) painter.MoveTo(new Vector2(px, Y(min)));
                    else painter.LineTo(new Vector2(px, Y(min)));
                    if (max > min) painter.LineTo(new Vector2(px, Y(max)));
                }

                painter.Stroke();
            }
        }
    }
}
