// Port of simple-2d-constraint-solver include/gauss_seidel_sle_solver.h and src/gauss_seidel_sle_solver.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    public sealed class GaussSeidelSleSolver : SleSolver
    {
        public int maxIterations;
        public double minDelta;

        readonly Matrix M = new Matrix();
        readonly SparseMatrix reg = new SparseMatrix(3);

        public GaussSeidelSleSolver()
            : base(true)
        {
            maxIterations = 128;
            minDelta = 1E-1;

            M.Initialize(1, 1);
        }

        // Upstream's definition names the fourth and fifth parameters the other way round from its declaration, and the body
        // follows the definition: the caller's `result` is the warm start and its `previous` receives the solution. Kept as is;
        // engine-sim never calls it (this solver supports limits, so the system calls SolveWithLimits).
        public override bool Solve(
            SparseMatrix J,
            Matrix W,
            Matrix right,
            Matrix previous,
            Matrix result)
        {
            int n = right.GetHeight();

            result.Resize(1, n);

            if (previous != null && previous.GetHeight() == n)
            {
                result.Set(previous);
            }

            J.RightScale(W, reg);
            reg.MultiplyTranspose(J, M);

            for (int i = 0; i < maxIterations; ++i)
            {
                double maxDelta = SolveIteration(
                    M,
                    right,
                    result,
                    result);

                if (maxDelta < minDelta)
                {
                    return true;
                }
            }

            return false;
        }

        public override bool SolveWithLimits(
            SparseMatrix J,
            Matrix W,
            Matrix right,
            Matrix limits,
            Matrix result,
            Matrix previous)
        {
            int n = right.GetHeight();
            if (result.GetHeight() != n)
            {
                result.Initialize(1, n);
            }

            if (previous != null && previous.GetHeight() == n)
            {
                result.Set(previous);
            }

            J.RightScale(W, reg);
            reg.MultiplyTranspose(J, M);

            for (int i = 0; i < maxIterations; ++i)
            {
                double maxDelta = SolveIteration(
                    M,
                    right,
                    limits,
                    result,
                    result);

                if (maxDelta < minDelta)
                {
                    return true;
                }
            }

            return false;
        }

        // k_next and k are the same matrix in every call: an in-place Gauss-Seidel sweep. Each k[i] is read before it is
        // overwritten, as upstream.
        double SolveIteration(
            Matrix left,
            Matrix right,
            Matrix k_next,
            Matrix k)
        {
            double maxDifference = 0.0;
            int n = k.GetHeight();

            for (int i = 0; i < n; ++i)
            {
                double s0 = 0.0, s1 = 0.0;
                for (int j = 0; j < i; ++j)
                {
                    s0 += left.Get(j, i) * k_next.Get(0, j);
                }

                for (int j = i + 1; j < n; ++j)
                {
                    s1 += left.Get(j, i) * k.Get(0, j);
                }

                double k_next_i =
                    (1 / left.Get(i, i)) * (right.Get(0, i) - s0 - s1);

                double min_k = Utilities.Fmax(1E-3, k.Get(0, i));
                double delta = (Math.Abs(k_next_i) - min_k) / min_k;
                maxDifference = (delta > maxDifference)
                    ? delta
                    : maxDifference;

                k_next.Set(0, i, k_next_i);
            }

            return maxDifference;
        }

        double SolveIteration(
            Matrix left,
            Matrix right,
            Matrix limits,
            Matrix k_next,
            Matrix k)
        {
            double maxDifference = 0.0;
            int n = k.GetHeight();

            for (int i = 0; i < n; ++i)
            {
                double s0 = 0.0, s1 = 0.0;
                for (int j = 0; j < i; ++j)
                {
                    s0 += left.Get(j, i) * k_next.Get(0, j);
                }

                for (int j = i + 1; j < n; ++j)
                {
                    s1 += left.Get(j, i) * k.Get(0, j);
                }

                double k_next_i =
                    (1 / left.Get(i, i)) * (right.Get(0, i) - s0 - s1);

                double limitMin = limits.Get(0, i);
                double limitMax = limits.Get(1, i);
                double x = Utilities.Fmax(limitMin, Utilities.Fmin(limitMax, k_next_i));

                double min_k = Utilities.Fmax(1E-3, Math.Abs(k.Get(0, i)));
                double delta = Math.Abs(x - k.Get(0, i)) / min_k;
                maxDifference = (delta > maxDifference)
                    ? delta
                    : maxDifference;

                k_next.Set(0, i, x);
            }

            return maxDifference;
        }
    }
}
