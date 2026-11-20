// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// Torque and power against rpm from the dyno pulls (<see cref="DynoSweep"/>): the latest pull bright, the one before it
    /// faint, with the latest's peaks; the redline; and a marker at the live engine's revs, with a dot at its torque while
    /// the dyno is on.
    /// </summary>
    sealed class DynoGraph : VisualElement
    {
        static readonly Color Grid = new Color32(0x2a, 0x2f, 0x3a, 0xFF);
        static readonly Color TextColor = new Color32(0x9a, 0xa0, 0xa6, 0xFF);
        static readonly Color TorqueColor = new Color32(0x38, 0xd3, 0xb0, 0xFF);
        static readonly Color PowerColor = new Color32(0xff, 0xb0, 0x20, 0xFF);
        static readonly Color Danger = new Color32(0xff, 0x4d, 0x4d, 0xFF);
        static readonly Color Marker = new Color32(0xe8, 0xea, 0xed, 0xFF);
        const float Left = 40, Right = 40, Top = 18, Bottom = 16;

        IReadOnlyList<(double rpm, double torque)> latest = Array.Empty<(double, double)>();
        IReadOnlyList<(double rpm, double torque)> previous = Array.Empty<(double, double)>();
        int latestCount = -1, previousCount = -1;
        double maxRpm = 7000, redline = 6000;
        double liveRpm = -1, liveTorque = double.NaN;

        public DynoGraph()
        {
            AddToClassList("trace-view");
            generateVisualContent += Draw;
        }

        /// <summary>The rpm scale: to a tenth past the higher of the redline and the rev limit, as the rev counter's.</summary>
        public void SetRange(double redlineRpm, double revLimitRpm)
        {
            redline = redlineRpm;
            maxRpm = Math.Max(1000, Math.Ceiling(Math.Max(redlineRpm, revLimitRpm) * 1.1 / 1000) * 1000);
            MarkDirtyRepaint();
        }

        /// <summary>The pulls to draw; redraws when they have grown or changed.</summary>
        public void SetPulls(IReadOnlyList<(double rpm, double torque)> latestPull, IReadOnlyList<(double rpm, double torque)> previousPull)
        {
            if (latestPull == latest && previousPull == previous && latestPull.Count == latestCount && previousPull.Count == previousCount) return;
            latest = latestPull;
            previous = previousPull;
            latestCount = latestPull.Count;
            previousCount = previousPull.Count;
            MarkDirtyRepaint();
        }

        /// <summary>The live engine's revs (negative for none) and its torque on the dyno (NaN when the dyno is off).</summary>
        public void Show(double rpm, double torque)
        {
            bool same = Math.Abs(rpm - liveRpm) < 5 && (double.IsNaN(torque) ? double.IsNaN(liveTorque) : Math.Abs(torque - liveTorque) < 0.5);
            if (same) return;
            liveRpm = rpm;
            liveTorque = torque;
            MarkDirtyRepaint();
        }

        static double Kilowatts((double rpm, double torque) point) => point.torque * point.rpm * Math.PI / 30 / 1000;

        /// <summary>The next scale top up from <paramref name="value"/>: 1, 2 or 5 times a power of ten.</summary>
        static double NiceCeiling(double value)
        {
            if (value <= 0) return 1;
            double decade = Math.Pow(10, Math.Floor(Math.Log10(value)));
            foreach (double step in new[] { 1.0, 2.0, 5.0, 10.0 })
            {
                if (step * decade >= value) return step * decade;
            }

            return 10 * decade;
        }

        void Draw(MeshGenerationContext context)
        {
            Rect area = contentRect;
            var plot = Rect.MinMaxRect(area.xMin + Left, area.yMin + Top, area.xMax - Right, area.yMax - Bottom);
            if (plot.width < 40 || plot.height < 30) return;
            Painter2D painter = context.painter2D;

            double torqueTop = 0, powerTop = 0;
            foreach (IReadOnlyList<(double rpm, double torque)> pull in new[] { latest, previous })
            {
                foreach ((double rpm, double torque) point in pull)
                {
                    torqueTop = Math.Max(torqueTop, point.torque);
                    powerTop = Math.Max(powerTop, Kilowatts(point));
                }
            }

            if (!double.IsNaN(liveTorque)) torqueTop = Math.Max(torqueTop, liveTorque);
            torqueTop = NiceCeiling(torqueTop * 1.05);
            powerTop = NiceCeiling(powerTop * 1.05);

            float X(double rpm) => plot.xMin + (float)(Math.Min(Math.Max(rpm, 0), maxRpm) / maxRpm) * plot.width;
            float YTorque(double torque) => plot.yMax - (float)(Math.Max(torque, 0) / torqueTop) * plot.height;
            float YPower(double kw) => plot.yMax - (float)(Math.Max(kw, 0) / powerTop) * plot.height;

            // Grid: every 1000 rpm (2000 past 12,000) and quarters of the scales.
            double step = maxRpm > 12000 ? 2000 : 1000;
            painter.strokeColor = Grid;
            painter.lineWidth = 1;
            painter.BeginPath();
            for (double rpm = 0; rpm <= maxRpm + 0.5; rpm += step)
            {
                painter.MoveTo(new Vector2(X(rpm), plot.yMin));
                painter.LineTo(new Vector2(X(rpm), plot.yMax));
            }

            for (int q = 0; q <= 4; ++q)
            {
                float y = plot.yMax - q / 4f * plot.height;
                painter.MoveTo(new Vector2(plot.xMin, y));
                painter.LineTo(new Vector2(plot.xMax, y));
            }

            painter.Stroke();
            for (double rpm = 0; rpm <= maxRpm + 0.5; rpm += step)
            {
                context.DrawText((rpm / 1000).ToString("0"), new Vector2(X(rpm) - 3, plot.yMax + 2), 10, TextColor);
            }

            context.DrawText("×1000 rpm", new Vector2(plot.xMax - 50, plot.yMax + 2), 10, TextColor);
            context.DrawText($"{torqueTop:0} N·m", new Vector2(area.xMin, plot.yMin - 6), 10, TorqueColor);
            context.DrawText($"{powerTop:0} kW", new Vector2(plot.xMax + 2, plot.yMin - 6), 10, PowerColor);

            painter.strokeColor = Danger;
            painter.BeginPath();
            painter.MoveTo(new Vector2(X(redline), plot.yMin));
            painter.LineTo(new Vector2(X(redline), plot.yMax));
            painter.Stroke();

            Curve(painter, previous, X, p => YTorque(p.torque), Faint(TorqueColor));
            Curve(painter, previous, X, p => YPower(Kilowatts(p)), Faint(PowerColor));
            Curve(painter, latest, X, p => YTorque(p.torque), TorqueColor);
            Curve(painter, latest, X, p => YPower(Kilowatts(p)), PowerColor);

            if (latest.Count > 0)
            {
                (double rpm, double torque) peakTorque = latest[0], peakPower = latest[0];
                foreach ((double rpm, double torque) point in latest)
                {
                    if (point.torque > peakTorque.torque) peakTorque = point;
                    if (Kilowatts(point) > Kilowatts(peakPower)) peakPower = point;
                }

                context.DrawText($"{peakTorque.torque:0} N·m at {peakTorque.rpm:#,0}", new Vector2(plot.xMin + 4, plot.yMin + 2), 10, TorqueColor);
                context.DrawText($"{Kilowatts(peakPower):0} kW ({Kilowatts(peakPower) / 0.7457:0} hp) at {peakPower.rpm:#,0}",
                    new Vector2(plot.xMin + 4, plot.yMin + 15), 10, PowerColor);
            }
            else
            {
                context.DrawText("Sweep: a full-throttle pull on the dyno, to the redline", new Vector2(plot.xMin + 4, plot.yMin + 2), 10, TextColor);
            }

            if (liveRpm < 0) return;
            painter.strokeColor = new Color(Marker.r, Marker.g, Marker.b, 0.5f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(X(liveRpm), plot.yMin));
            painter.LineTo(new Vector2(X(liveRpm), plot.yMax));
            painter.Stroke();
            if (double.IsNaN(liveTorque)) return;
            painter.fillColor = Marker;
            painter.BeginPath();
            painter.Arc(new Vector2(X(liveRpm), YTorque(liveTorque)), 3.5f, Angle.Degrees(0), Angle.Degrees(360));
            painter.Fill();
        }

        static void Curve(Painter2D painter, IReadOnlyList<(double rpm, double torque)> pull, Func<double, float> x,
            Func<(double rpm, double torque), float> y, Color color)
        {
            if (pull.Count < 2) return;
            painter.strokeColor = color;
            painter.lineWidth = 1.5f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x(pull[0].rpm), y(pull[0])));
            for (int i = 1; i < pull.Count; ++i) painter.LineTo(new Vector2(x(pull[i].rpm), y(pull[i])));
            painter.Stroke();
        }

        static Color Faint(Color color) => new Color(color.r, color.g, color.b, 0.3f);
    }
}
