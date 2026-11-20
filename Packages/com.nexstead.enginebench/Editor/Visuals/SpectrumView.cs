// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// The spectrum of what plays: the last <see cref="Size"/> output samples (93 ms), Hann-windowed, in dB of full scale
    /// over 20 Hz to 20 kHz on a log scale, eased between updates. A line marks the firing frequency (a cylinder firing per
    /// two crank turns each), where an engine's sound has its first strong harmonic.
    /// </summary>
    sealed class SpectrumView : VisualElement
    {
        public const int Size = 4096;
        const float MinHz = 20, MaxHz = 20000;
        const float FloorDb = -90;
        /// <summary>How far each update moves the shown level towards the new one.</summary>
        const float Easing = 0.4f;
        static readonly Color Grid = new Color32(0x2a, 0x2f, 0x3a, 0xFF);
        static readonly Color TextColor = new Color32(0x9a, 0xa0, 0xa6, 0xFF);
        static readonly Color Curve = new Color32(0x38, 0xd3, 0xb0, 0xFF);
        static readonly Color Firing = new Color32(0xff, 0xb0, 0x20, 0xFF);

        readonly float[] samples = new float[Size];
        readonly float[] re = new float[Size], im = new float[Size];
        readonly float[] window = new float[Size];
        readonly Fft fft = new Fft(Size);
        readonly float[] level = new float[Size / 2 + 1];
        readonly double windowSum;
        bool hasSignal;
        double firingHz;

        public SpectrumView()
        {
            AddToClassList("trace-view");
            generateVisualContent += Draw;
            for (int i = 0; i < Size; ++i)
            {
                window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Size));
                windowSum += window[i];
            }

            Reset();
        }

        void Reset()
        {
            for (int k = 0; k < level.Length; ++k) level[k] = FloorDb;
        }

        /// <summary>Reads the latest output of <paramref name="from"/>; null clears the view.</summary>
        public void Show(EngineCapture from, double firingFrequency)
        {
            if (from == null)
            {
                if (!hasSignal) return;
                hasSignal = false;
                Reset();
                MarkDirtyRepaint();
                return;
            }

            if (from.ReadAudio(samples) < Size) return;
            for (int i = 0; i < Size; ++i)
            {
                re[i] = samples[i] * window[i];
                im[i] = 0;
            }

            fft.Forward(re, im);
            // A full-scale sine reads 0 dB: its peak bin holds the amplitude times half the window's sum.
            double unit = 2 / windowSum;
            for (int k = 0; k < level.Length; ++k)
            {
                double amplitude = Math.Sqrt((double)re[k] * re[k] + (double)im[k] * im[k]) * unit;
                float db = Math.Max(FloorDb, (float)(20 * Math.Log10(Math.Max(amplitude, 1e-9))));
                level[k] += (db - level[k]) * Easing;
            }

            firingHz = firingFrequency;
            hasSignal = true;
            MarkDirtyRepaint();
        }

        static float Position(double hz) => (float)(Math.Log(hz / MinHz) / Math.Log(MaxHz / MinHz));

        void Draw(MeshGenerationContext context)
        {
            Rect area = contentRect;
            if (area.width < 40 || area.height < 20) return;
            Painter2D painter = context.painter2D;
            float X(double hz) => area.xMin + Position(hz) * area.width;
            float Y(float db) => area.yMin + db / FloorDb * area.height;

            painter.strokeColor = Grid;
            painter.lineWidth = 1;
            painter.BeginPath();
            foreach (float hz in new[] { 50f, 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f })
            {
                painter.MoveTo(new Vector2(X(hz), area.yMin));
                painter.LineTo(new Vector2(X(hz), area.yMax));
            }

            for (float db = -20; db > FloorDb; db -= 20)
            {
                painter.MoveTo(new Vector2(area.xMin, Y(db)));
                painter.LineTo(new Vector2(area.xMax, Y(db)));
            }

            painter.Stroke();
            context.DrawText("100 Hz", new Vector2(X(100) + 3, area.yMax - 15), 10, TextColor);
            context.DrawText("1 kHz", new Vector2(X(1000) + 3, area.yMax - 15), 10, TextColor);
            context.DrawText("10 kHz", new Vector2(X(10000) + 3, area.yMax - 15), 10, TextColor);
            for (float db = -20; db > FloorDb; db -= 20) context.DrawText($"{db:0} dB", new Vector2(area.xMin + 3, Y(db) - 13), 10, TextColor);
            if (!hasSignal) return;

            if (firingHz > MinHz && firingHz < MaxHz)
            {
                painter.strokeColor = Firing;
                painter.BeginPath();
                painter.MoveTo(new Vector2(X(firingHz), area.yMin));
                painter.LineTo(new Vector2(X(firingHz), area.yMax));
                painter.Stroke();
                context.DrawText($"firing {firingHz:0} Hz", new Vector2(X(firingHz) + 3, area.yMin + 2), 10, Firing);
            }

            // Per pixel column, the strongest bin under it (or, below one bin per column, the level between its two bins).
            double binHz = (double)EngineRenderer.SampleRate / Size;
            int columns = (int)area.width;
            var points = new Vector2[columns];
            for (int column = 0; column < columns; ++column)
            {
                double from = MinHz * Math.Pow(MaxHz / MinHz, column / (double)columns) / binHz;
                double to = MinHz * Math.Pow(MaxHz / MinHz, (column + 1) / (double)columns) / binHz;
                float db;
                if (to - from < 1)
                {
                    int k = (int)from;
                    float t = (float)(from - k);
                    db = Mathf.Lerp(level[Math.Min(k, level.Length - 1)], level[Math.Min(k + 1, level.Length - 1)], t);
                }
                else
                {
                    db = FloorDb;
                    for (int k = (int)from; k < Math.Min((int)to, level.Length); ++k) db = Math.Max(db, level[k]);
                }

                points[column] = new Vector2(area.xMin + column + 0.5f, Y(db));
            }

            painter.fillColor = new Color(Curve.r, Curve.g, Curve.b, 0.22f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(area.xMin, area.yMax));
            foreach (Vector2 point in points) painter.LineTo(point);
            painter.LineTo(new Vector2(area.xMax, area.yMax));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = Curve;
            painter.lineWidth = 1.2f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            for (int i = 0; i < points.Length; ++i)
            {
                if (i == 0) painter.MoveTo(points[i]);
                else painter.LineTo(points[i]);
            }

            painter.Stroke();
        }
    }
}
