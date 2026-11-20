// Port of engine-sim include/dynamometer.h and src/dynamometer.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.

using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>
    /// Holds the crank at <see cref="rotationSpeed"/> within ±<see cref="maxTorque"/>. It drives toward the crank's current turning
    /// direction, so from rest it turns the engine backwards: engage it only on a turning engine.
    /// </summary>
    public sealed class Dynamometer : Constraint
    {
        public double rotationSpeed = 0.0;
        public double ks = 10.0;
        public double kd = 1.0;
        public double maxTorque = Units.Torque(10000.0, Units.ft_lb);
        public bool hold = false;
        public bool enabled = false;

        public Dynamometer() : base(1, 1)
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

            if (bodies[0].v_theta < 0)
            {
                output.v_bias[0] = rotationSpeed;
                output.limits[0, 0] = (hold && enabled) ? -maxTorque : 0.0;
                output.limits[0, 1] = enabled ? maxTorque : 0.0;
            }
            else
            {
                output.v_bias[0] = -rotationSpeed;
                output.limits[0, 0] = enabled ? -maxTorque : 0.0;
                output.limits[0, 1] = (hold && enabled) ? maxTorque : 0.0;
            }
        }

        public double GetTorque()
        {
            return (bodies[0].v_theta > 0)
                ? -F_t[0, 0]
                : F_t[0, 0];
        }
    }
}
