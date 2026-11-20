// Port of simple-2d-constraint-solver include/line_constraint.h and src/line_constraint.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>Keeps a point of a body on a line through <c>p0</c> along <c>(dx, dy)</c>.</summary>
    public sealed class LineConstraint : Constraint
    {
        public double local_x;
        public double local_y;
        public double p0_x;
        public double p0_y;
        public double dx;
        public double dy;
        public double ks;
        public double kd;

        public LineConstraint()
            : base(1, 1)
        {
            local_x = local_y = 0.0;
            p0_x = p0_y = 0.0;
            dx = dy = 0.0;
            ks = 10.0;
            kd = 1.0;
        }

        public void SetBody(RigidBody body) => bodies[0] = body;

        public override void Calculate(Output output, SystemState state)
        {
            int body = bodies[0].index;

            double q1 = state.p_x[body];
            double q2 = state.p_y[body];
            double q3 = state.theta[body];

            double q3_dot = state.v_theta[body];

            double cos_q3 = Math.Cos(q3);
            double sin_q3 = Math.Sin(q3);

            double bodyX = q1 + cos_q3 * local_x - sin_q3 * local_y;
            double bodyY = q2 + sin_q3 * local_x + cos_q3 * local_y;

            double perpX = -dy;
            double perpY = dx;

            double deltaX = bodyX - p0_x;
            double deltaY = bodyY - p0_y;

            double C = deltaX * perpX + deltaY * perpY;

            double dC_dq1 = 1.0 * perpX;
            double dC_dq2 = 1.0 * perpY;
            double dC_dq3 =
                (-sin_q3 * local_x - cos_q3 * local_y) * perpX +
                (cos_q3 * local_x - sin_q3 * local_y) * perpY;

            output.J[0, 0] = dC_dq1;
            output.J[0, 1] = dC_dq2;
            output.J[0, 2] = dC_dq3;

            output.J_dot[0, 0] = 0.0;
            output.J_dot[0, 1] = 0.0;
            output.J_dot[0, 2] =
                (-cos_q3 * q3_dot * local_x + sin_q3 * q3_dot * local_y) * perpX +
                (-sin_q3 * q3_dot * local_x - cos_q3 * q3_dot * local_y) * perpY;

            output.ks[0] = ks;
            output.kd[0] = kd;

            output.C[0] = C;

            output.v_bias[0] = 0;

            NoLimits(output);
        }
    }
}
