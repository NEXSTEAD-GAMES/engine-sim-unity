// Port of simple-2d-constraint-solver include/nsv_ode_solver.h and src/nsv_ode_solver.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>Semi-implicit (symplectic) Euler: velocities first, then positions from the new velocities.</summary>
    public sealed class NsvOdeSolver : OdeSolver
    {
        public override void Start(SystemState initial, double dt)
        {
            base.Start(initial, dt);
        }

        public override bool Step(SystemState system)
        {
            system.dt = dt;
            return true;
        }

        public override void Solve(SystemState system)
        {
            system.dt = dt;

            for (int i = 0; i < system.n; ++i)
            {
                system.v_x[i] += system.a_x[i] * dt;
                system.v_y[i] += system.a_y[i] * dt;
                system.v_theta[i] += system.a_theta[i] * dt;

                system.p_x[i] += system.v_x[i] * dt;
                system.p_y[i] += system.v_y[i] * dt;
                system.theta[i] += system.v_theta[i] * dt;
            }
        }

        public override void End()
        {
            /* void */
        }
    }
}
