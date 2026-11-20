// Port of engine-sim include/starter_motor.h and src/starter_motor.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.

using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class StarterMotor : Constraint
    {
        public double ks = 10.0;
        public double kd = 1.0;
        public double maxTorque = Units.Torque(80.0, Units.ft_lb);
        public double rotationSpeed = -Units.Rpm(200.0);
        public bool enabled = false;

        public StarterMotor() : base(1, 1)
        {
        }

        public void ConnectCrankshaft(Crankshaft crankshaft)
        {
            bodies[0] = crankshaft.body;
        }

        public override void Calculate(Output output, SystemState state)
        {
            output.J[0, 0] = 0;
            output.J[0, 1] = 0;
            output.J[0, 2] = 1;

            output.J_dot[0, 0] = 0;
            output.J_dot[0, 1] = 0;
            output.J_dot[0, 2] = 0;

            output.ks[0] = ks;
            output.kd[0] = kd;

            output.C[0] = 0;

            output.v_bias[0] = -rotationSpeed;

            if (rotationSpeed < 0)
            {
                output.limits[0, 0] = enabled ? -maxTorque : 0.0;
                output.limits[0, 1] = 0.0;
            }
            else
            {
                output.limits[0, 0] = 0.0;
                output.limits[0, 1] = enabled ? maxTorque : 0.0;
            }
        }
    }
}
