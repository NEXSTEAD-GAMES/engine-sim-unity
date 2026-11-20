// Port of simple-2d-constraint-solver include/fixed_position_constraint.h and src/fixed_position_constraint.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>Pins a point of a body to a point in the world.</summary>
    public sealed class FixedPositionConstraint : Constraint
    {
        public double local_x;
        public double local_y;
        public double world_x;
        public double world_y;
        public double ks;
        public double kd;

        public FixedPositionConstraint()
            : base(2, 1)
        {
            local_x = local_y = 0.0;
            world_x = world_y = 0.0;
            ks = 10.0;
            kd = 1.0;
        }

        public void SetBody(RigidBody body) => bodies[0] = body;

        public void SetWorldPosition(double x, double y)
        {
            world_x = x;
            world_y = y;
        }

        public void SetLocalPosition(double x, double y)
        {
            local_x = x;
            local_y = y;
        }

        public override void Calculate(Output output, SystemState state)
        {
            int body = bodies[0].index;

            double q1 = state.p_x[body];
            double q2 = state.p_y[body];
            double q3 = state.theta[body];

            double q3_dot = state.v_theta[body];

            double cos_q3 = Math.Cos(q3);
            double sin_q3 = Math.Sin(q3);

            double current_x = q1 + cos_q3 * local_x - sin_q3 * local_y;
            double current_y = q2 + sin_q3 * local_x + cos_q3 * local_y;

            double dx_dq1 = 1.0;
            double dx_dq2 = 0.0;
            double dx_dq3 = -sin_q3 * local_x - cos_q3 * local_y;

            double dy_dq1 = 0.0;
            double dy_dq2 = 1.0;
            double dy_dq3 = cos_q3 * local_x - sin_q3 * local_y;

            double C1 = current_x - world_x;
            double C2 = current_y - world_y;

            output.J[0, 0] = dx_dq1;
            output.J[0, 1] = dx_dq2;
            output.J[0, 2] = dx_dq3;

            output.J[1, 0] = dy_dq1;
            output.J[1, 1] = dy_dq2;
            output.J[1, 2] = dy_dq3;

            output.J_dot[0, 0] = 0;
            output.J_dot[0, 1] = 0;
            output.J_dot[0, 2] =
                -cos_q3 * q3_dot * local_x + sin_q3 * q3_dot * local_y;

            output.J_dot[1, 0] = 0;
            output.J_dot[1, 1] = 0;
            output.J_dot[1, 2] =
                -sin_q3 * q3_dot * local_x - cos_q3 * q3_dot * local_y;

            output.ks[0] = ks;
            output.ks[1] = ks;

            output.kd[0] = output.kd[1] = kd;

            output.C[0] = C1;
            output.C[1] = C2;

            output.v_bias[0] = 0;
            output.v_bias[1] = 0;

            NoLimits(output);
        }
    }
}
