// Port of engine-sim include/utilities.h and src/utilities.cpp, plus the C++ library
// semantics the port needs where C# differs.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public static class Utilities
    {
        public static double ModularDistance(double a0, double b0, double mod = 1.0)
        {
            double a, b;
            if (a0 < b0)
            {
                a = a0;
                b = b0;
            }
            else
            {
                a = b0;
                b = a0;
            }

            return Fmin(b - a, a + mod - b);
        }

        public static double PositiveMod(double x, double mod)
        {
            if (x < 0)
            {
                x = Math.Ceiling(-x / mod) * mod + x;
            }

            return x % mod;
        }

        public static double ErfApproximation(double x)
        {
            const double a1 = 0.278393;
            const double a2 = 0.230389;
            const double a3 = 0.000972;
            const double a4 = 0.078108;

            double x2 = x * x;
            double x3 = x2 * x;
            double x4 = x3 * x;

            double q = 1 / (1 + a1 * x + a2 * x2 + a3 * x3 + a4 * x4);
            double q2 = q * q;
            double q4 = q2 * q2;

            return 1 - q4;
        }

        public static double Clamp(double x, double x0 = 0.0, double x1 = 1.0)
        {
            if (x <= x0) return x0;
            else if (x >= x1) return x1;
            else return x;
        }

        public static float Clamp(float x, float x0 = 0.0f, float x1 = 1.0f)
        {
            if (x <= x0) return x0;
            else if (x >= x1) return x1;
            else return x;
        }

        // ---- C++ library semantics ----

        /// <summary><c>std::min</c>: <c>(b &lt; a) ? b : a</c>. Differs from <see cref="Math.Min(double, double)"/> on NaN and signed zero.</summary>
        public static double StdMin(double a, double b) => (b < a) ? b : a;

        /// <summary><c>std::max</c>: <c>(a &lt; b) ? b : a</c>.</summary>
        public static double StdMax(double a, double b) => (a < b) ? b : a;

        public static int StdMin(int a, int b) => (b < a) ? b : a;
        public static int StdMax(int a, int b) => (a < b) ? b : a;

        /// <summary><c>std::fmin</c>: a NaN argument yields the other.</summary>
        public static double Fmin(double a, double b)
        {
            if (double.IsNaN(a)) return b;
            if (double.IsNaN(b)) return a;
            return (b < a) ? b : a;
        }

        /// <summary><c>std::fmax</c>: a NaN argument yields the other.</summary>
        public static double Fmax(double a, double b)
        {
            if (double.IsNaN(a)) return b;
            if (double.IsNaN(b)) return a;
            return (a < b) ? b : a;
        }

        /// <summary>
        /// <c>std::round</c>: halves round away from zero. Built on <see cref="Math.Floor(double)"/>, which Burst compiles, and
        /// exact for every double: <c>a - Floor(a)</c> has no rounding error. Zero and NaN come back as they went in.
        /// </summary>
        public static double Round(double x)
        {
            double a = Math.Abs(x);
            double r = Math.Floor(a);
            if (a - r >= 0.5) r += 1.0;
            if (x < 0) return -r;
            return x > 0 ? r : x;
        }
    }
}
