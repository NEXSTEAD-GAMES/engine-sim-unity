// Port of engine-sim include/butterworth_low_pass_filter.h, a fourth-order Butterworth
// low-pass. Upstream is a template used with float and double; C# 9 has no generic
// arithmetic, so each instantiation is its own class with upstream's exact mixed-precision
// arithmetic (double literals in the coefficients promote to double, as in C++).
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary><c>ButterworthLowPassFilter&lt;float&gt;</c>.</summary>
    public sealed class ButterworthLowPassFilterFloat : Filter
    {
        readonly RingBuffer<float> y = new RingBuffer<float>();
        readonly RingBuffer<float> x = new RingBuffer<float>();
        readonly float[] a = new float[5];
        float f_4;

        public ButterworthLowPassFilterFloat()
        {
            y.Initialize(4);
            x.Initialize(4);

            for (int i = 0; i < 4; ++i)
            {
                y.Write(0);
                x.Write(0);
            }
        }

        public override float F(float sample) => FastF(sample);

        public float FastF(float sample)
        {
            float y_prev0 = y.Read(3), y_prev1 = y.Read(2), y_prev2 = y.Read(1), y_prev3 = y.Read(0);
            float x_prev0 = x.Read(3), x_prev1 = x.Read(2), x_prev2 = x.Read(1), x_prev3 = x.Read(0);

            float n = f_4 / a[0] * (sample + 4 * x_prev0 + 6 * x_prev1 + 4 * x_prev2 + x_prev3);
            float d = -a[1] * y_prev0 - a[2] * y_prev1 - a[3] * y_prev2 - a[4] * y_prev3;
            float result = n + d;

            x.RemoveBeginning(1);
            x.Write(sample);

            y.RemoveBeginning(1);
            y.Write(result);

            return result;
        }

        public void SetCutoffFrequency(float f_c, float sampleRate)
        {
            // std::tan of a float is tanf.
            float f = MathF.Tan((float)Constants.pi * f_c / sampleRate);
            float f_2 = f * f;
            float f_3 = f_2 * f;
            float f_4 = f_2 * f_2;
            float m = (float)(-2.0 * Math.Cos(5.0 * Constants.pi / 8.0));
            float n = (float)(-2.0 * Math.Cos(7.0 * Constants.pi / 8.0));

            a[0] = (float)(1.0 + (m + n) * f + (2.0 + n * m) * f_2 + (m + n) * f_3 + f_4);
            a[1] = (float)((-4.0 - 2.0 * (n + m) * f + 2.0 * (m + n) * f_3 + 4.0 * f_4) / a[0]);
            a[2] = (float)((6.0 - 2.0 * (2.0 + m * n) * f_2 + 6.0 * f_4) / a[0]);
            a[3] = (float)((-4.0 + 2.0 * (m + n) * f - 2.0 * (m + n) * f_3 + 4.0 * f_4) / a[0]);
            a[4] = (float)((1.0 - (n + m) * f + (2.0 + m * n) * f_2 - (m + n) * f_3 + f_4) / a[0]);
            this.f_4 = f_4;
        }

        // Read access for the dense core's snapshot.
        internal RingBuffer<float> YHistory => y;
        internal RingBuffer<float> XHistory => x;
        internal float[] Coefficients => a;
        internal float F4 => f_4;
    }

    /// <summary><c>ButterworthLowPassFilter&lt;double&gt;</c>.</summary>
    public sealed class ButterworthLowPassFilterDouble
    {
        readonly RingBuffer<double> y = new RingBuffer<double>();
        readonly RingBuffer<double> x = new RingBuffer<double>();
        readonly double[] a = new double[5];
        double f_4;

        public ButterworthLowPassFilterDouble()
        {
            y.Initialize(4);
            x.Initialize(4);

            for (int i = 0; i < 4; ++i)
            {
                y.Write(0);
                x.Write(0);
            }
        }

        public double FastF(double sample)
        {
            double y_prev0 = y.Read(3), y_prev1 = y.Read(2), y_prev2 = y.Read(1), y_prev3 = y.Read(0);
            double x_prev0 = x.Read(3), x_prev1 = x.Read(2), x_prev2 = x.Read(1), x_prev3 = x.Read(0);

            double n = f_4 / a[0] * (sample + 4 * x_prev0 + 6 * x_prev1 + 4 * x_prev2 + x_prev3);
            double d = -a[1] * y_prev0 - a[2] * y_prev1 - a[3] * y_prev2 - a[4] * y_prev3;
            double result = n + d;

            x.RemoveBeginning(1);
            x.Write(sample);

            y.RemoveBeginning(1);
            y.Write(result);

            return result;
        }

        public void SetCutoffFrequency(double f_c, double sampleRate)
        {
            double f = Math.Tan(Constants.pi * f_c / sampleRate);
            double f_2 = f * f;
            double f_3 = f_2 * f;
            double f_4 = f_2 * f_2;
            double m = -2.0 * Math.Cos(5.0 * Constants.pi / 8.0);
            double n = -2.0 * Math.Cos(7.0 * Constants.pi / 8.0);

            a[0] = 1.0 + (m + n) * f + (2.0 + n * m) * f_2 + (m + n) * f_3 + f_4;
            a[1] = (-4.0 - 2.0 * (n + m) * f + 2.0 * (m + n) * f_3 + 4.0 * f_4) / a[0];
            a[2] = (6.0 - 2.0 * (2.0 + m * n) * f_2 + 6.0 * f_4) / a[0];
            a[3] = (-4.0 + 2.0 * (m + n) * f - 2.0 * (m + n) * f_3 + 4.0 * f_4) / a[0];
            a[4] = (1.0 - (n + m) * f + (2.0 + m * n) * f_2 - (m + n) * f_3 + f_4) / a[0];
            this.f_4 = f_4;
        }

        // Read access for the dense core's snapshot.
        internal RingBuffer<double> YHistory => y;
        internal RingBuffer<double> XHistory => x;
        internal double[] Coefficients => a;
        internal double F4 => f_4;
    }
}
