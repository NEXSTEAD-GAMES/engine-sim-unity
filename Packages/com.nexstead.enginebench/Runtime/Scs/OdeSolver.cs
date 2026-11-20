// Port of simple-2d-constraint-solver include/ode_solver.h and src/ode_solver.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    public class OdeSolver
    {
        protected double dt;

        public virtual void Start(SystemState initial, double dt)
        {
            this.dt = dt;
        }

        public virtual bool Step(SystemState system)
        {
            return true;
        }

        public virtual void Solve(SystemState system)
        {
            /* void */
        }

        public virtual void End()
        {
            /* void */
        }
    }
}
