// Port of engine-sim include/vehicle_drag_constraint.h and src/vehicle_drag_constraint.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.

using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class VehicleDragConstraint : Constraint
    {
        public double ks = 10.0;
        public double kd = 1.0;

        Vehicle vehicle;

        public VehicleDragConstraint() : base(1, 1)
        {
        }

        /// <summary>Read access for the dense core's snapshot.</summary>
        internal Vehicle DraggedVehicle => vehicle;

        public void Initialize(RigidBody rotatingMass, Vehicle vehicle)
        {
            bodies[0] = rotatingMass;
            this.vehicle = vehicle;
        }

        public override void Calculate(Output output, SystemState system)
        {
            output.C[0] = 0;

            // Upstream fills a second body's columns although the constraint has one body.
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

            double airDensity =
                Units.AirMolecularMass * Units.Pressure(1.0, Units.atm)
                / (Constants.R * Units.Celcius(25.0));
            double v = vehicle.GetSpeed();
            double v_squared = v * v;
            double c_d = vehicle.GetDragCoefficient();
            double A = vehicle.GetCrossSectionArea();
            double rollingResistance = vehicle.GetRollingResistance();

            output.limits[0, 0] =
                -vehicle.LinearForceToVirtualTorque(rollingResistance + 0.5 * airDensity * v_squared * c_d * A);
            output.limits[0, 1] = 0;
        }
    }
}
