// Port of simple-2d-constraint-solver include/system_state.h and src/system_state.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    public sealed class SystemState
    {
        public int[] indexMap;

        public double[] a_theta;
        public double[] v_theta;
        public double[] theta;

        public double[] a_x;
        public double[] a_y;
        public double[] v_x;
        public double[] v_y;
        public double[] p_x;
        public double[] p_y;

        public double[] f_x;
        public double[] f_y;
        public double[] t;

        public double[] r_x;
        public double[] r_y;
        public double[] r_t;

        public double[] m;

        public int n;
        public int n_c;
        public double dt;

        public void Copy(SystemState state)
        {
            Resize(state.n, state.n_c);

            if (state.n == 0)
            {
                return;
            }

            Array.Copy(state.indexMap, indexMap, n_c);

            Array.Copy(state.a_theta, a_theta, n);
            Array.Copy(state.v_theta, v_theta, n);
            Array.Copy(state.theta, theta, n);

            Array.Copy(state.a_x, a_x, n);
            Array.Copy(state.a_y, a_y, n);
            Array.Copy(state.v_x, v_x, n);
            Array.Copy(state.v_y, v_y, n);
            Array.Copy(state.p_x, p_x, n);
            Array.Copy(state.p_y, p_y, n);

            Array.Copy(state.f_x, f_x, n);
            Array.Copy(state.f_y, f_y, n);
            Array.Copy(state.t, t, n);

            Array.Copy(state.m, m, n);

            Array.Copy(state.r_x, r_x, n_c * 2);
            Array.Copy(state.r_y, r_y, n_c * 2);
            Array.Copy(state.r_t, r_t, n_c * 2);
        }

        public void Resize(int bodyCount, int constraintCount)
        {
            if (n >= bodyCount && n_c >= constraintCount)
            {
                return;
            }

            n = bodyCount;
            n_c = constraintCount;

            indexMap = new int[n_c];

            a_theta = new double[n];
            v_theta = new double[n];
            theta = new double[n];

            a_x = new double[n];
            a_y = new double[n];
            v_x = new double[n];
            v_y = new double[n];
            p_x = new double[n];
            p_y = new double[n];

            f_x = new double[n];
            f_y = new double[n];
            t = new double[n];

            m = new double[n];

            r_x = new double[n_c * 2];
            r_y = new double[n_c * 2];
            r_t = new double[n_c * 2];
        }

        public void LocalToWorld(double x, double y, out double x_t, out double y_t, int body)
        {
            double x0 = p_x[body];
            double y0 = p_y[body];
            double theta = this.theta[body];

            double cos_theta = Math.Cos(theta);
            double sin_theta = Math.Sin(theta);

            x_t = cos_theta * x - sin_theta * y + x0;
            y_t = sin_theta * x + cos_theta * y + y0;
        }

        public void VelocityAtPoint(double x, double y, out double v_x, out double v_y, int body)
        {
            LocalToWorld(x, y, out double w_x, out double w_y, body);

            double v_theta = this.v_theta[body];
            double angularToLinear_x = -v_theta * (w_y - p_y[body]);
            double angularToLinear_y = v_theta * (w_x - p_x[body]);

            v_x = this.v_x[body] + angularToLinear_x;
            v_y = this.v_y[body] + angularToLinear_y;
        }

        public void ApplyForce(double x_l, double y_l, double f_x, double f_y, int body)
        {
            LocalToWorld(x_l, y_l, out double w_x, out double w_y, body);

            this.f_x[body] += f_x;
            this.f_y[body] += f_y;

            t[body] +=
                (w_y - p_y[body]) * -f_x +
                (w_x - p_x[body]) * f_y;
        }
    }
}
