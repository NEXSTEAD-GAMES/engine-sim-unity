// Port of simple-2d-constraint-solver include/link_constraint.h and src/link_constraint.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>Joins a point of one body to a point of another (a pin joint).</summary>
    public sealed class LinkConstraint : Constraint
    {
        public double local_x_1;
        public double local_y_1;
        public double local_x_2;
        public double local_y_2;
        public double ks;
        public double kd;

        public LinkConstraint()
            : base(2, 2)
        {
            local_x_1 = local_y_1 = 0.0;
            local_x_2 = local_y_2 = 0.0;
            ks = 10.0;
            kd = 1.0;
        }

        public void SetBody1(RigidBody body) => bodies[0] = body;
        public void SetBody2(RigidBody body) => bodies[1] = body;

        public void SetLocalPosition1(double x, double y)
        {
            local_x_1 = x;
            local_y_1 = y;
        }

        public void SetLocalPosition2(double x, double y)
        {
            local_x_2 = x;
            local_y_2 = y;
        }

        public override void Calculate(Output output, SystemState state)
        {
            int body = bodies[0].index;
            int linkedBody = bodies[1].index;

            double q1 = state.p_x[body];
            double q2 = state.p_y[body];
            double q3 = state.theta[body];

            double q4 = state.p_x[linkedBody];
            double q5 = state.p_y[linkedBody];
            double q6 = state.theta[linkedBody];

            double q3_dot = state.v_theta[body];
            double q6_dot = state.v_theta[linkedBody];

            double cos_q3 = Math.Cos(q3);
            double sin_q3 = Math.Sin(q3);

            double cos_q6 = Math.Cos(q6);
            double sin_q6 = Math.Sin(q6);

            double bodyX = q1 + cos_q3 * local_x_1 - sin_q3 * local_y_1;
            double bodyY = q2 + sin_q3 * local_x_1 + cos_q3 * local_y_1;

            double linkedBodyX = q4 + cos_q6 * local_x_2 - sin_q6 * local_y_2;
            double linkedBodyY = q5 + sin_q6 * local_x_2 + cos_q6 * local_y_2;

            double C1 = bodyX - linkedBodyX;
            double C2 = bodyY - linkedBodyY;

            output.J[0, 0] = 1.0;
            output.J[0, 1] = 0.0;
            output.J[0, 2] = -sin_q3 * local_x_1 - cos_q3 * local_y_1;

            output.J[1, 0] = 0.0;
            output.J[1, 1] = 1.0;
            output.J[1, 2] = cos_q3 * local_x_1 - sin_q3 * local_y_1;

            output.J[0, 3] = -1.0;
            output.J[0, 4] = 0.0;
            output.J[0, 5] = sin_q6 * local_x_2 + cos_q6 * local_y_2;

            output.J[1, 3] = 0.0;
            output.J[1, 4] = -1.0;
            output.J[1, 5] = -cos_q6 * local_x_2 + sin_q6 * local_y_2;

            output.J_dot[0, 0] = 0;
            output.J_dot[0, 1] = 0;
            output.J_dot[0, 2] =
                -cos_q3 * q3_dot * local_x_1 + sin_q3 * q3_dot * local_y_1;

            output.J_dot[1, 0] = 0;
            output.J_dot[1, 1] = 0;
            output.J_dot[1, 2] =
                -sin_q3 * q3_dot * local_x_1 - cos_q3 * q3_dot * local_y_1;

            output.J_dot[0, 3] = 0;
            output.J_dot[0, 4] = 0;
            output.J_dot[0, 5] =
                cos_q6 * q6_dot * local_x_2 - sin_q6 * q6_dot * local_y_2;

            output.J_dot[1, 3] = 0;
            output.J_dot[1, 4] = 0;
            output.J_dot[1, 5] =
                sin_q6 * q6_dot * local_x_2 + cos_q6 * q6_dot * local_y_2;

            output.kd[0] = output.kd[1] = kd;
            output.ks[0] = ks;
            output.ks[1] = ks;

            output.C[0] = C1;
            output.C[1] = C2;

            output.v_bias[0] = 0;
            output.v_bias[1] = 0;

            NoLimits(output);
        }
    }
}
