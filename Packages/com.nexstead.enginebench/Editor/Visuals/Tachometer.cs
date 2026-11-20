// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// A rev counter: a 270° dial in thousands of rpm up to a little past the rev limit, the zone from the redline on in red,
    /// a needle and the revs in figures.
    /// </summary>
    sealed class Tachometer : VisualElement
    {
        const float StartAngle = 135f, Sweep = 270f;
        static readonly Color Track = new Color32(0x2a, 0x2f, 0x3a, 0xFF);
        static readonly Color Tick = new Color32(0x9a, 0xa0, 0xa6, 0xFF);
        static readonly Color Needle = new Color32(0x38, 0xd3, 0xb0, 0xFF);
        static readonly Color Danger = new Color32(0xff, 0x4d, 0x4d, 0xFF);

        readonly Label figures;
        double redline = 6000, maxRpm = 7000;
        double rpm = -1;

        public Tachometer()
        {
            AddToClassList("tach");
            generateVisualContent += Draw;
            figures = new Label("–");
            figures.AddToClassList("tach__rpm");
            Add(figures);
            var unit = new Label("RPM");
            unit.AddToClassList("tach__unit");
            Add(unit);
        }

        /// <summary>The dial for an engine: red from <paramref name="redlineRpm"/>, the scale running a tenth past <paramref name="revLimitRpm"/>.</summary>
        public void SetRange(double redlineRpm, double revLimitRpm)
        {
            redline = redlineRpm;
            maxRpm = Math.Max(1000, Math.Ceiling(Math.Max(redlineRpm, revLimitRpm) * 1.1 / 1000) * 1000);
            MarkDirtyRepaint();
        }

        /// <summary>The revs to show; negative for none (not running).</summary>
        public void Show(double revs)
        {
            string text = revs >= 0 ? revs.ToString("#,0") : "–";
            if (figures.text != text) figures.text = text;
            figures.EnableInClassList("tach__rpm--over", revs >= redline);
            if (Math.Abs(revs - rpm) < 1) return;
            rpm = revs;
            MarkDirtyRepaint();
        }

        float AngleOf(double revs) => StartAngle + Sweep * (float)(Math.Min(Math.Max(revs, 0), maxRpm) / maxRpm);

        static Vector2 Polar(Vector2 centre, float radius, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return centre + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * radius;
        }

        void Draw(MeshGenerationContext context)
        {
            Rect area = contentRect;
            float size = Mathf.Min(area.width, area.height);
            if (size < 40) return;
            var centre = new Vector2(area.center.x, area.y + size / 2);
            float radius = size / 2 - 4;
            Painter2D painter = context.painter2D;

            painter.lineCap = LineCap.Butt;
            painter.lineWidth = 6;
            painter.strokeColor = Track;
            painter.BeginPath();
            painter.Arc(centre, radius - 3, Angle.Degrees(StartAngle), Angle.Degrees(StartAngle + Sweep));
            painter.Stroke();

            painter.strokeColor = Danger;
            painter.BeginPath();
            painter.Arc(centre, radius - 3, Angle.Degrees(AngleOf(redline)), Angle.Degrees(StartAngle + Sweep));
            painter.Stroke();

            // Ticks every 500 rpm (every 1000 past 12,000), figures in thousands at the long ones.
            double major = maxRpm > 12000 ? 2000 : 1000;
            double minor = major / 2;
            float fontSize = Mathf.Clamp(size * 0.065f, 9, 14);
            painter.strokeColor = Tick;
            for (double r = 0; r <= maxRpm + 0.5; r += minor)
            {
                bool isMajor = Math.Abs(r / major - Math.Round(r / major)) < 1e-6;
                float a = AngleOf(r);
                painter.lineWidth = isMajor ? 2 : 1;
                painter.BeginPath();
                painter.MoveTo(Polar(centre, radius - (isMajor ? 16 : 11), a));
                painter.LineTo(Polar(centre, radius - 7, a));
                painter.Stroke();
                if (!isMajor) continue;

                string label = (r / 1000).ToString("0");
                Vector2 at = Polar(centre, radius - 16 - fontSize, a);
                // Centred on its point: figures are about 0.6 em wide.
                at -= new Vector2(label.Length * fontSize * 0.3f, fontSize * 0.6f);
                context.DrawText(label, at, fontSize, r >= redline ? Danger : Tick);
            }

            if (rpm < 0) return;
            float needle = AngleOf(rpm);
            painter.lineCap = LineCap.Round;
            painter.lineWidth = 3;
            painter.strokeColor = rpm >= redline ? Danger : Needle;
            painter.BeginPath();
            painter.MoveTo(Polar(centre, radius * 0.12f, needle + 180));
            painter.LineTo(Polar(centre, radius - 12, needle));
            painter.Stroke();
            painter.fillColor = Track;
            painter.BeginPath();
            painter.Arc(centre, 6, Angle.Degrees(0), Angle.Degrees(360));
            painter.Fill();
        }
    }
}
