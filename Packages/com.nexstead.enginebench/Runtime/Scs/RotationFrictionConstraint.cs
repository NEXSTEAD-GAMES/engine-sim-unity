// Port of simple-2d-constraint-solver include/rotation_friction_constraint.h and
// src/rotation_friction_constraint.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>Opposes a body's rotation with a torque within [minTorque, maxTorque] (Coulomb friction).</summary>
    public sealed class RotationFrictionConstraint : Constraint
    {
        public double maxTorque;
        public double minTorque;
        public double ks;
        public double kd;

        public RotationFrictionConstraint()
            : base(1, 1)
        {
            ks = 10.0;
            kd = 1.0;
            maxTorque = double.MaxValue;
            minTorque = -double.MaxValue;
        }

        public void SetBody(RigidBody body) => bodies[0] = body;

        public override void Calculate(Output output, SystemState state)
        {
            output.C[0] = 0;

            output.J[0, 0] = 0.0;
            output.J[0, 1] = 0.0;
            output.J[0, 2] = 1.0;

            output.J_dot[0, 0] = 0;
            output.J_dot[0, 1] = 0;
            output.J_dot[0, 2] = 0;

            output.kd[0] = kd;
            output.ks[0] = ks;

            output.v_bias[0] = 0;

            output.limits[0, 0] = minTorque;
            output.limits[0, 1] = maxTorque;
        }
    }
}
