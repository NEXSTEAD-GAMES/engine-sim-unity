// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// The engine seen from the front, drawn from an <see cref="EnginePose"/>: banks with their liners, heads and valves (each
    /// valve drops by its lift, drawn twice its size), pistons, rods and the crank with its pins; a chamber glows when its
    /// cylinder fires. Cylinders behind one another in a bank are drawn stepped back and dimmer. The view is centred on what
    /// the drawing covers in any pose, and fits it.
    /// </summary>
    sealed class EngineView : VisualElement
    {
        static readonly Color Liner = new Color32(0x4a, 0x52, 0x61, 0xFF);
        static readonly Color Head = new Color32(0x35, 0x3b, 0x48, 0xFF);
        static readonly Color PistonColor = new Color32(0xc9, 0xcd, 0xd3, 0xFF);
        static readonly Color RodColor = new Color32(0x9a, 0xa0, 0xa6, 0xFF);
        static readonly Color CrankColor = new Color32(0x6d, 0x74, 0x80, 0xFF);
        static readonly Color Intake = new Color32(0x4f, 0xc3, 0xf7, 0xFF);
        static readonly Color Exhaust = new Color32(0xff, 0x8a, 0x50, 0xFF);
        static readonly Color Flame = new Color32(0xff, 0xb0, 0x20, 0xFF);
        const double ValveLiftScale = 2.0;
        // The drawing's shapes: the head's height and half width (bores), the liner's skirt below the stroke (compression
        // heights), the piston skirt below the wrist pin (compression heights), the step back of each cylinder behind
        // another (bores of the first bank) and the crank web's radius (crank throws).
        const double HeadHeight = 0.55, HeadHalfWidth = 0.65, LinerSkirt = 1.4, PistonSkirt = 0.7, DepthStep = 0.16, WebRadius = 1.45;
        // The share of the view the engine fills, along its longer side.
        const float Fill = 0.92f;

        EngineGeometry geometry;
        EnginePose pose;
        // What the drawing covers in any pose (m, y up).
        double left, bottom, right, top;
        readonly Label caption;

        public EngineView()
        {
            AddToClassList("engine-view");
            generateVisualContent += Draw;
            caption = new Label();
            caption.AddToClassList("engine-view__caption");
            Add(caption);
        }

        /// <summary>The engine to draw; null clears the view.</summary>
        public void SetEngine(EngineGeometry engine)
        {
            geometry = engine;
            pose = engine != null ? new EnginePose(engine.cylinders.Length) : null;
            if (engine != null) Frame();

            MarkDirtyRepaint();
        }

        /// <summary>
        /// What the drawing covers in any pose: every cylinder's head and liner, its piston at the bottom of the stroke and its
        /// big end anywhere round the crank, each stepped back as drawn, and the crank web.
        /// </summary>
        void Frame()
        {
            double crankThrow = geometry.crankThrow, web = crankThrow * WebRadius, step = geometry.banks[0].bore * DepthStep;
            left = geometry.crankX - web;
            right = geometry.crankX + web;
            bottom = geometry.crankY - web;
            top = geometry.crankY + web;
            foreach (EngineGeometry.Cylinder cylinder in geometry.cylinders)
            {
                EngineGeometry.Bank bank = geometry.banks[cylinder.bank];
                double shift = cylinder.depth * step, half = bank.bore / 2, deck = bank.deckHeight;
                double linerBottom = deck - 2 * crankThrow - cylinder.compressionHeight * LinerSkirt;
                double crankAlong = (geometry.crankX - bank.x) * bank.dx + (geometry.crankY - bank.y) * bank.dy;
                double skirt = crankAlong - crankThrow + cylinder.rodLength - cylinder.compressionHeight * PistonSkirt;

                void Include(double along, double across)
                {
                    double x = bank.x + bank.dx * along - bank.dy * across + shift, y = bank.y + bank.dy * along + bank.dx * across + shift;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    bottom = Math.Min(bottom, y);
                    top = Math.Max(top, y);
                }

                foreach (double side in new[] { -1.0, 1.0 })
                {
                    Include(deck + bank.bore * HeadHeight, side * bank.bore * HeadHalfWidth);
                    Include(linerBottom, side * half);
                    Include(skirt, side * half);
                }

                left = Math.Min(left, geometry.crankX - crankThrow + shift);
                right = Math.Max(right, geometry.crankX + crankThrow + shift);
                bottom = Math.Min(bottom, geometry.crankY - crankThrow + shift);
                top = Math.Max(top, geometry.crankY + crankThrow + shift);
            }
        }

        /// <summary>The pose to draw now, and what the caption says about it.</summary>
        public void Show(EnginePose shown, string note)
        {
            if (geometry == null || shown == null) return;
            Array.Copy(shown.pistonTravel, pose.pistonTravel, pose.Cylinders);
            Array.Copy(shown.bigEndX, pose.bigEndX, pose.Cylinders);
            Array.Copy(shown.bigEndY, pose.bigEndY, pose.Cylinders);
            Array.Copy(shown.littleEndX, pose.littleEndX, pose.Cylinders);
            Array.Copy(shown.littleEndY, pose.littleEndY, pose.Cylinders);
            Array.Copy(shown.intakeLift, pose.intakeLift, pose.Cylinders);
            Array.Copy(shown.exhaustLift, pose.exhaustLift, pose.Cylinders);
            Array.Copy(shown.flash, pose.flash, pose.Cylinders);
            pose.crankAngle = shown.crankAngle;
            if (caption.text != note) caption.text = note;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            Rect area = contentRect;
            if (geometry == null || area.width < 20 || area.height < 20) return;

            float scale = (float)Math.Min(area.width / (right - left), area.height / (top - bottom)) * Fill;
            // World (m, y up) to the element (pixels, y down), what the drawing covers at the centre.
            double middleX = (left + right) / 2, middleY = (bottom + top) / 2;
            Vector2 ToView(double x, double y) =>
                new Vector2(area.center.x + (float)(x - middleX) * scale, area.center.y - (float)(y - middleY) * scale);

            Painter2D painter = context.painter2D;
            float depthStep = (float)(geometry.banks[0].bore * DepthStep) * scale;

            // Back to front: the deepest cylinder of every bank first.
            int deepest = 0;
            foreach (EngineGeometry.Cylinder c in geometry.cylinders) deepest = Math.Max(deepest, c.depth);
            for (int depth = deepest; depth >= 0; --depth)
            {
                float fade = 1f - 0.5f * depth / Math.Max(1, deepest);
                var shift = new Vector2(depth * depthStep, -depth * depthStep);
                for (int c = 0; c < geometry.cylinders.Length; ++c)
                {
                    if (geometry.cylinders[c].depth != depth) continue;
                    DrawCylinder(painter, c, fade, shift, ToView);
                }
            }

            // The crank on top: its web, then a pin at every rod's big end.
            Vector2 centre = ToView(geometry.crankX, geometry.crankY);
            float web = (float)(geometry.crankThrow * WebRadius) * scale;
            painter.fillColor = new Color(CrankColor.r, CrankColor.g, CrankColor.b, 0.35f);
            painter.BeginPath();
            painter.Arc(centre, web, Angle.Degrees(0), Angle.Degrees(360));
            painter.Fill();
            painter.lineCap = LineCap.Round;
            for (int c = 0; c < pose.Cylinders; ++c)
            {
                Vector2 pin = ToView(pose.bigEndX[c], pose.bigEndY[c]);
                painter.strokeColor = CrankColor;
                painter.lineWidth = Math.Max(3f, (float)geometry.crankThrow * 0.35f * scale);
                painter.BeginPath();
                painter.MoveTo(centre);
                painter.LineTo(pin);
                painter.Stroke();
            }

            painter.fillColor = PistonColor;
            for (int c = 0; c < pose.Cylinders; ++c)
            {
                painter.BeginPath();
                painter.Arc(ToView(pose.bigEndX[c], pose.bigEndY[c]), Math.Max(2.5f, (float)geometry.crankThrow * 0.16f * scale),
                    Angle.Degrees(0), Angle.Degrees(360));
                painter.Fill();
            }

            painter.fillColor = Liner;
            painter.BeginPath();
            painter.Arc(centre, Math.Max(3f, (float)geometry.crankThrow * 0.22f * scale), Angle.Degrees(0), Angle.Degrees(360));
            painter.Fill();
        }

        void DrawCylinder(Painter2D painter, int c, float fade, Vector2 shift, Func<double, double, Vector2> toView)
        {
            EngineGeometry.Cylinder cylinder = geometry.cylinders[c];
            EngineGeometry.Bank bank = geometry.banks[cylinder.bank];
            double half = bank.bore / 2;
            // Along the bank (a) and across it (b), from the bank's origin.
            Vector2 At(double a, double b) =>
                toView(bank.x + bank.dx * a - bank.dy * b, bank.y + bank.dy * a + bank.dx * b) + shift;

            double deck = bank.deckHeight;
            double stroke = 2 * geometry.crankThrow;
            double linerBottom = deck - stroke - cylinder.compressionHeight * LinerSkirt;

            // The head over the deck.
            double headTop = deck + bank.bore * HeadHeight, headSide = bank.bore * HeadHalfWidth;
            Quad(painter, At(deck, -headSide), At(deck, headSide), At(headTop, headSide), At(headTop, -headSide), Faded(Head, fade));

            // The burning charge, between the crown and the deck.
            double crown = pose.pistonTravel[c] + cylinder.compressionHeight;
            if (pose.flash[c] > 0.01)
            {
                Color flame = Flame;
                flame.a = (float)Math.Min(1, pose.flash[c]) * 0.85f * fade;
                Quad(painter, At(crown, -half), At(crown, half), At(deck, half), At(deck, -half), flame);
            }

            // The liner walls.
            painter.strokeColor = Faded(Liner, fade);
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(At(linerBottom, -half));
            painter.LineTo(At(deck, -half));
            painter.MoveTo(At(linerBottom, half));
            painter.LineTo(At(deck, half));
            painter.Stroke();

            // The valves: a stem through the head and a head that drops by the lift.
            Valve(painter, At, deck, -half * 0.45, half * 0.38, pose.intakeLift[c] * ValveLiftScale, Faded(Intake, fade));
            Valve(painter, At, deck, half * 0.45, half * 0.38, pose.exhaustLift[c] * ValveLiftScale, Faded(Exhaust, fade));

            // The rod, then the piston over its little end.
            painter.strokeColor = Faded(RodColor, fade);
            painter.lineWidth = Math.Max(3f, (float)(bank.bore * 0.12) * Vector2.Distance(At(0, 0), At(1, 0)));
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(toView(pose.bigEndX[c], pose.bigEndY[c]) + shift);
            painter.LineTo(toView(pose.littleEndX[c], pose.littleEndY[c]) + shift);
            painter.Stroke();

            double skirt = pose.pistonTravel[c] - cylinder.compressionHeight * PistonSkirt;
            Quad(painter, At(skirt, -half * 0.94), At(skirt, half * 0.94), At(crown, half * 0.94), At(crown, -half * 0.94), Faded(PistonColor, fade));
        }

        static void Valve(Painter2D painter, Func<double, double, Vector2> at, double deck, double across, double halfWidth, double lift, Color color)
        {
            double face = deck - lift;
            painter.strokeColor = color;
            painter.lineWidth = 2f;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(at(face, across));
            painter.LineTo(at(deck + halfWidth * 2.5, across));
            painter.Stroke();
            painter.lineWidth = 4f;
            painter.BeginPath();
            painter.MoveTo(at(face, across - halfWidth));
            painter.LineTo(at(face, across + halfWidth));
            painter.Stroke();
        }

        static void Quad(Painter2D painter, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(b);
            painter.LineTo(c);
            painter.LineTo(d);
            painter.ClosePath();
            painter.Fill();
        }

        static Color Faded(Color color, float fade) => new Color(color.r, color.g, color.b, color.a * fade);
    }
}
