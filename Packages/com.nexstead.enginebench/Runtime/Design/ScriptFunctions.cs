// Ported from engine-sim (https://github.com/ange-yaghi/engine-sim): scripting/include/actions.h
// (GenerateHarmonicCamLobeNode, k_28inH2O, k_carb), es/actions/actions.mr (circle_area),
// es/utilities/utilities.mr. MIT licence, Copyright (c) 2022 Ange Yaghi; see Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Design
{
    /// <summary>The script library's helper functions, computed as engine-sim computes them.</summary>
    public static class ScriptFunctions
    {
        /// <summary>
        /// A symmetric cam lobe (harmonic_cam_lobe): <paramref name="lift"/> at the centre, falling as
        /// <c>lift · (½ + ½ cos kx)^γ</c> to zero, with <c>k</c> set so the lift is 50 thou at half of
        /// <paramref name="durationAt50Thou"/> either side. <paramref name="steps"/> samples a side.
        /// </summary>
        public static FunctionNode HarmonicCamLobe(double durationAt50Thou = 0.0, double gamma = 1.0, double lift = 300 * ScriptUnits.thou,
            int steps = 100)
        {
            var function = new FunctionNode();
            double angle = durationAt50Thou / 4;
            double s = Math.Pow(2 * Units.Distance(50, Units.thou) / lift, 1 / gamma) - 1;
            double k = Math.Acos(s) / angle;
            double extents = Constants.pi / k;

            double step = extents / (steps - 5.0);
            for (int i = 0; i < steps; ++i)
            {
                if (i == 0)
                {
                    function.AddSample(0.0, lift);
                }
                else
                {
                    double x = i * step;
                    double y = (x >= extents)
                        ? 0.0
                        : lift * Math.Pow(0.5 + 0.5 * Math.Cos(k * x), gamma);
                    function.AddSample(x, y);
                    function.AddSample(-x, y);
                }
            }

            function.filterRadius = step;
            return function;
        }

        /// <summary>A flow coefficient from a flow rate (cfm) measured at 28 inches of water (k_28inH2O).</summary>
        public static double K_28inH2O(double flow = 1.0) => GasSystem.K_28inH2O(flow);

        /// <summary>A flow coefficient from a flow rate (cfm) measured as carburettors are, at 1.5 inches of mercury (k_carb).</summary>
        public static double K_carb(double flow = 1.0) => GasSystem.K_carb(flow);

        public static double CircleArea(double radius) => ScriptUnits.pi * radius * radius;

        public static double RodMomentOfInertia(double mass, double length) => (1 / 12.0) * mass * length * length;

        public static double DiskMomentOfInertia(double mass, double radius) => (1 / 2.0) * mass * radius * radius;
    }
}
