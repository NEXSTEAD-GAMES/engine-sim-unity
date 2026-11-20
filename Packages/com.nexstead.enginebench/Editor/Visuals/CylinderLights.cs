// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// A lamp per cylinder, in the engine's firing order (its spark plugs' angles over the cycle), each lit as its cylinder's
    /// charge burns in the replayed cycle. Cylinders are numbered as the engine's description lists them, bank by bank.
    /// </summary>
    sealed class CylinderLights : VisualElement
    {
        readonly List<Lamp> lamps = new List<Lamp>();

        public CylinderLights() => AddToClassList("cylinder-lights");

        /// <summary>Lamps for <paramref name="engine"/>'s cylinders; none for null.</summary>
        public void SetEngine(EngineDescription engine)
        {
            Clear();
            lamps.Clear();
            if (engine == null) return;

            foreach (int cylinder in FiringOrder(engine.engine))
            {
                var lamp = new Lamp(cylinder);
                lamps.Add(lamp);
                Add(lamp);
            }
        }

        /// <summary>
        /// The cylinders in firing order: those with a spark plug by its angle in the cycle, from the first after top dead
        /// centre; then any without one, in their order.
        /// </summary>
        public static int[] FiringOrder(EngineParametersDescription engine)
        {
            int count = 0;
            foreach (CylinderBankDescription bank in engine.cylinderBanks) count += bank.cylinderCount;
            SparkPlugDescription[] plugs = engine.ignitionModule.plugs ?? Array.Empty<SparkPlugDescription>();
            var order = new int[count];
            for (int c = 0; c < count; ++c) order[c] = c;

            double CycleAngle(int c) =>
                c < plugs.Length && plugs[c].enabled ? Utilities.PositiveMod(plugs[c].angle, 4 * Constants.pi) : double.MaxValue;
            // Stable: equal angles keep the description's order.
            Array.Sort(order, (a, b) =>
            {
                int byAngle = CycleAngle(a).CompareTo(CycleAngle(b));
                return byAngle != 0 ? byAngle : a.CompareTo(b);
            });
            return order;
        }

        /// <summary>Lights each lamp by its cylinder's glow (0 dark to 1 just fired).</summary>
        public void Show(double[] glow)
        {
            foreach (Lamp lamp in lamps) lamp.Light(glow != null && lamp.Cylinder < glow.Length ? glow[lamp.Cylinder] : 0);
        }

        sealed class Lamp : VisualElement
        {
            static readonly Color Dark = new Color32(0x2a, 0x2f, 0x3a, 0xFF);
            static readonly Color Lit = new Color32(0xff, 0xb0, 0x20, 0xFF);
            float level = -1;

            public Lamp(int cylinder)
            {
                Cylinder = cylinder;
                AddToClassList("cylinder-lamp");
                tooltip = $"Cylinder {cylinder + 1}";
                generateVisualContent += Draw;
                var number = new Label((cylinder + 1).ToString());
                number.AddToClassList("cylinder-lamp__number");
                Add(number);
            }

            public int Cylinder { get; }

            public void Light(double glow)
            {
                float next = Mathf.Round((float)glow * 32) / 32;
                if (next == level) return;
                level = next;
                EnableInClassList("cylinder-lamp--lit", level > 0.5f);
                MarkDirtyRepaint();
            }

            void Draw(MeshGenerationContext context)
            {
                Rect area = contentRect;
                float radius = Mathf.Min(area.width, area.height) / 2;
                if (radius < 2) return;
                Painter2D painter = context.painter2D;
                painter.fillColor = Color.Lerp(Dark, Lit, Mathf.Max(0, level));
                painter.BeginPath();
                painter.Arc(area.center, radius, Angle.Degrees(0), Angle.Degrees(360));
                painter.Fill();
            }
        }
    }
}
