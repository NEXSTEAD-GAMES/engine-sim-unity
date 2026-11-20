// Port of simple-2d-constraint-solver include/clutch_constraint.h and src/clutch_constraint.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>Equal angular speed of two bodies, transmitting a torque within [minTorque, maxTorque].</summary>
    public sealed class ClutchConstraint : Constraint
    {
        public double maxTorque;
        public double minTorque;
        public double ks;
        public double kd;

        public ClutchConstraint()
            : base(1, 2)
        {
            ks = 10.0;
            kd = 1.0;
            maxTorque = double.MaxValue;
            minTorque = -double.MaxValue;
        }

        public void SetBody1(RigidBody body) => bodies[0] = body;
        public void SetBody2(RigidBody body) => bodies[1] = body;

        public override void Calculate(Output output, SystemState state)
        {
            output.C[0] = 0;

            output.J[0, 0] = 0.0;
            output.J[0, 1] = 0.0;
            output.J[0, 2] = -1.0;

            output.J[0, 3] = 0.0;
            output.J[0, 4] = 0.0;
            output.J[0, 5] = 1.0;

            output.J_dot[0, 0] = 0;
            output.J_dot[0, 1] = 0;
            output.J_dot[0, 2] = 0;

            output.J_dot[0, 3] = 0;
            output.J_dot[0, 4] = 0;
            output.J_dot[0, 5] = 0;

            output.kd[0] = kd;
            output.ks[0] = ks;

            output.v_bias[0] = 0;

            output.limits[0, 0] = minTorque;
            output.limits[0, 1] = maxTorque;
        }
    }
}
