// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// Each constraint kind's Calculate, as Runtime/Scs/{FixedPosition,Line,Link,Clutch,
// RotationFriction}Constraint.cs and Runtime/Simulation/{Dynamometer,StarterMotor,
// VehicleDragConstraint}.cs compute it. Only what the optimised system reads is written (J for the
// constraint's own bodies, C, v_bias, limits); J_dot, ks and kd are not read there.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public static unsafe partial class SolverOps
    {
        static void Calculate(DenseSolver* s, ConstraintData* c, ConstraintOutputData* output, SystemStateData* state)
        {
            switch (c->kind)
            {
                case ConstraintKind.FixedPosition: CalculateFixedPosition(c, output, state); break;
                case ConstraintKind.Line: CalculateLine(c, output, state); break;
                case ConstraintKind.Link: CalculateLink(c, output, state); break;
                case ConstraintKind.Clutch: CalculateClutch(c, output); break;
                case ConstraintKind.RotationFriction: CalculateRotationFriction(c, output); break;
                case ConstraintKind.Dynamometer: CalculateDynamometer(s, c, output); break;
                case ConstraintKind.StarterMotor: CalculateStarterMotor(c, output); break;
                case ConstraintKind.VehicleDrag: CalculateVehicleDrag(s, c, output); break;
            }
        }

        static void NoLimits(ConstraintOutputData* output)
        {
            for (int i = 0; i < Scs.Constraint.MaxConstraintCount; ++i)
            {
                output->limits[i * 2 + 0] = -double.MaxValue;
                output->limits[i * 2 + 1] = double.MaxValue;
            }
        }

        static void CalculateFixedPosition(ConstraintData* c, ConstraintOutputData* output, SystemStateData* state)
        {
            int body = c->body0;

            double q1 = state->p_x[body];
            double q2 = state->p_y[body];
            double q3 = state->theta[body];

            double cos_q3 = Math.Cos(q3);
            double sin_q3 = Math.Sin(q3);

            double current_x = q1 + cos_q3 * c->local_x - sin_q3 * c->local_y;
            double current_y = q2 + sin_q3 * c->local_x + cos_q3 * c->local_y;

            double dx_dq1 = 1.0;
            double dx_dq2 = 0.0;
            double dx_dq3 = -sin_q3 * c->local_x - cos_q3 * c->local_y;

            double dy_dq1 = 0.0;
            double dy_dq2 = 1.0;
            double dy_dq3 = cos_q3 * c->local_x - sin_q3 * c->local_y;

            double C1 = current_x - c->world_x;
            double C2 = current_y - c->world_y;

            output->J[0 * 6 + 0] = dx_dq1;
            output->J[0 * 6 + 1] = dx_dq2;
            output->J[0 * 6 + 2] = dx_dq3;

            output->J[1 * 6 + 0] = dy_dq1;
            output->J[1 * 6 + 1] = dy_dq2;
            output->J[1 * 6 + 2] = dy_dq3;

            output->C[0] = C1;
            output->C[1] = C2;

            output->v_bias[0] = 0;
            output->v_bias[1] = 0;

            NoLimits(output);
        }

        static void CalculateLine(ConstraintData* c, ConstraintOutputData* output, SystemStateData* state)
        {
            int body = c->body0;

            double q1 = state->p_x[body];
            double q2 = state->p_y[body];
            double q3 = state->theta[body];

            double cos_q3 = Math.Cos(q3);
            double sin_q3 = Math.Sin(q3);

            double bodyX = q1 + cos_q3 * c->local_x - sin_q3 * c->local_y;
            double bodyY = q2 + sin_q3 * c->local_x + cos_q3 * c->local_y;

            double perpX = -c->dy;
            double perpY = c->dx;

            double deltaX = bodyX - c->p0_x;
            double deltaY = bodyY - c->p0_y;

            double C = deltaX * perpX + deltaY * perpY;

            double dC_dq1 = 1.0 * perpX;
            double dC_dq2 = 1.0 * perpY;
            double dC_dq3 =
                (-sin_q3 * c->local_x - cos_q3 * c->local_y) * perpX +
                (cos_q3 * c->local_x - sin_q3 * c->local_y) * perpY;

            output->J[0] = dC_dq1;
            output->J[1] = dC_dq2;
            output->J[2] = dC_dq3;

            output->C[0] = C;
            output->v_bias[0] = 0;

            NoLimits(output);
        }

        static void CalculateLink(ConstraintData* c, ConstraintOutputData* output, SystemStateData* state)
        {
            int body = c->body0;
            int linkedBody = c->body1;

            double q1 = state->p_x[body];
            double q2 = state->p_y[body];
            double q3 = state->theta[body];

            double q4 = state->p_x[linkedBody];
            double q5 = state->p_y[linkedBody];
            double q6 = state->theta[linkedBody];

            double cos_q3 = Math.Cos(q3);
            double sin_q3 = Math.Sin(q3);

            double cos_q6 = Math.Cos(q6);
            double sin_q6 = Math.Sin(q6);

            double bodyX = q1 + cos_q3 * c->local_x - sin_q3 * c->local_y;
            double bodyY = q2 + sin_q3 * c->local_x + cos_q3 * c->local_y;

            double linkedBodyX = q4 + cos_q6 * c->local_x_2 - sin_q6 * c->local_y_2;
            double linkedBodyY = q5 + sin_q6 * c->local_x_2 + cos_q6 * c->local_y_2;

            double C1 = bodyX - linkedBodyX;
            double C2 = bodyY - linkedBodyY;

            output->J[0 * 6 + 0] = 1.0;
            output->J[0 * 6 + 1] = 0.0;
            output->J[0 * 6 + 2] = -sin_q3 * c->local_x - cos_q3 * c->local_y;

            output->J[1 * 6 + 0] = 0.0;
            output->J[1 * 6 + 1] = 1.0;
            output->J[1 * 6 + 2] = cos_q3 * c->local_x - sin_q3 * c->local_y;

            output->J[0 * 6 + 3] = -1.0;
            output->J[0 * 6 + 4] = 0.0;
            output->J[0 * 6 + 5] = sin_q6 * c->local_x_2 + cos_q6 * c->local_y_2;

            output->J[1 * 6 + 3] = 0.0;
            output->J[1 * 6 + 4] = -1.0;
            output->J[1 * 6 + 5] = -cos_q6 * c->local_x_2 + sin_q6 * c->local_y_2;

            output->C[0] = C1;
            output->C[1] = C2;

            output->v_bias[0] = 0;
            output->v_bias[1] = 0;

            NoLimits(output);
        }

        static void CalculateClutch(ConstraintData* c, ConstraintOutputData* output)
        {
            output->C[0] = 0;

            output->J[0] = 0.0;
            output->J[1] = 0.0;
            output->J[2] = -1.0;

            output->J[3] = 0.0;
            output->J[4] = 0.0;
            output->J[5] = 1.0;

            output->v_bias[0] = 0;

            output->limits[0] = c->minTorque;
            output->limits[1] = c->maxTorque;
        }

        static void CalculateRotationFriction(ConstraintData* c, ConstraintOutputData* output)
        {
            output->C[0] = 0;

            output->J[0] = 0.0;
            output->J[1] = 0.0;
            output->J[2] = 1.0;

            output->v_bias[0] = 0;

            output->limits[0] = c->minTorque;
            output->limits[1] = c->maxTorque;
        }

        /// <summary>Reads the crank body's own v_theta (the start of the step), not the system state, as upstream.</summary>
        static void CalculateDynamometer(DenseSolver* s, ConstraintData* c, ConstraintOutputData* output)
        {
            output->J[0] = 0;
            output->J[1] = 0;
            output->J[2] = 1;

            output->C[0] = 0;

            if (s->bodies[c->body0].v_theta < 0)
            {
                output->v_bias[0] = c->rotationSpeed;
                output->limits[0] = (c->hold && c->enabled) ? -c->maxTorque : 0.0;
                output->limits[1] = c->enabled ? c->maxTorque : 0.0;
            }
            else
            {
                output->v_bias[0] = -c->rotationSpeed;
                output->limits[0] = c->enabled ? -c->maxTorque : 0.0;
                output->limits[1] = (c->hold && c->enabled) ? c->maxTorque : 0.0;
            }
        }

        static void CalculateStarterMotor(ConstraintData* c, ConstraintOutputData* output)
        {
            output->J[0] = 0;
            output->J[1] = 0;
            output->J[2] = 1;

            output->C[0] = 0;
            output->v_bias[0] = -c->rotationSpeed;

            if (c->rotationSpeed < 0)
            {
                output->limits[0] = c->enabled ? -c->maxTorque : 0.0;
                output->limits[1] = 0.0;
            }
            else
            {
                output->limits[0] = 0.0;
                output->limits[1] = c->enabled ? c->maxTorque : 0.0;
            }
        }

        /// <summary>Vehicle.GetSpeed and LinearForceToVirtualTorque read the rotating mass body itself (I, v_theta), as upstream.</summary>
        static void CalculateVehicleDrag(DenseSolver* s, ConstraintData* c, ConstraintOutputData* output)
        {
            output->C[0] = 0;

            output->J[0] = 0.0;
            output->J[1] = 0.0;
            output->J[2] = -1.0;

            output->v_bias[0] = 0;

            RigidBodyData* rotatingMass = &s->bodies[c->body0];

            double airDensity =
                Units.AirMolecularMass * Units.Pressure(1.0, Units.atm)
                / (Constants.R * Units.Celcius(25.0));

            // Vehicle.GetSpeed
            double E_r = 0.5 * rotatingMass->I * rotatingMass->v_theta * rotatingMass->v_theta;
            double v = Math.Sqrt(2 * E_r / c->vehicleMass);

            double v_squared = v * v;
            double c_d = c->dragCoefficient;
            double A = c->crossSectionArea;
            double rollingResistance = c->rollingResistance;

            // Vehicle.LinearForceToVirtualTorque
            double force = rollingResistance + 0.5 * airDensity * v_squared * c_d * A;
            double rotationToKineticRatio = Math.Sqrt(rotatingMass->I / c->vehicleMass);

            output->limits[0] = -(rotationToKineticRatio * force);
            output->limits[1] = 0;
        }
    }
}
