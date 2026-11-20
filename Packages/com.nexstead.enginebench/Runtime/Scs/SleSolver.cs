// Port of simple-2d-constraint-solver include/sle_solver.h and src/sle_solver.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>Solves the constraint system's linear equations, J W Jᵀ λ = right.</summary>
    public abstract class SleSolver
    {
        readonly bool supportsLimits;

        protected SleSolver(bool supportsLimits)
        {
            this.supportsLimits = supportsLimits;
        }

        public virtual bool Solve(
            SparseMatrix J,
            Matrix W,
            Matrix right,
            Matrix result,
            Matrix previous)
        {
            return false;
        }

        public virtual bool SolveWithLimits(
            SparseMatrix J,
            Matrix W,
            Matrix right,
            Matrix limits,
            Matrix result,
            Matrix previous)
        {
            return false;
        }

        public bool SupportsLimits() => supportsLimits;
    }
}
