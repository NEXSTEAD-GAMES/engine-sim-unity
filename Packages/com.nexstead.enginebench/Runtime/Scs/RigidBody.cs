// Port of simple-2d-constraint-solver include/rigid_body.h and src/rigid_body.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    public sealed class RigidBody
    {
        public double p_x;
        public double p_y;

        public double v_x;
        public double v_y;

        public double theta;
        public double v_theta;

        public double m;
        public double I;

        public int index = -1;

        public RigidBody()
        {
            Reset();
        }

        public void LocalToWorld(double x, double y, out double w_x, out double w_y)
        {
            double cos_theta = Math.Cos(theta);
            double sin_theta = Math.Sin(theta);

            w_x = cos_theta * x - sin_theta * y + p_x;
            w_y = sin_theta * x + cos_theta * y + p_y;
        }

        public void WorldToLocal(double x, double y, out double l_x, out double l_y)
        {
            double cos_theta = Math.Cos(theta);
            double sin_theta = Math.Sin(theta);

            l_x = cos_theta * (x - p_x) + sin_theta * (y - p_y);
            l_y = -sin_theta * (x - p_x) + cos_theta * (y - p_y);
        }

        public void Reset()
        {
            p_x = p_y = 0.0;
            v_x = v_y = 0.0;
            theta = 0.0;
            v_theta = 0.0;
            m = 0.0;
            I = 0.0;
        }

        public double Energy()
        {
            double speed_2 = v_x * v_x + v_y * v_y;
            double E_k = 0.5 * m * speed_2;
            double E_r = 0.5 * I * v_theta * v_theta;

            return E_k + E_r;
        }
    }
}
