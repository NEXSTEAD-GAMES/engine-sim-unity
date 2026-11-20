// Port of simple-2d-constraint-solver include/constraint.h, src/constraint.cpp,
// include/force_generator.h and src/force_generator.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    public class Constraint
    {
        public const int MaxConstraintCount = 3;
        public const int MaxBodyCount = 2;

        /// <summary>Filled by <see cref="Calculate"/>. One instance is reused for every constraint.</summary>
        public sealed class Output
        {
            public readonly double[] C = new double[MaxConstraintCount];
            public readonly double[,] J = new double[MaxConstraintCount, 3 * MaxBodyCount];
            public readonly double[,] J_dot = new double[MaxConstraintCount, 3 * MaxBodyCount];
            public readonly double[] v_bias = new double[MaxConstraintCount];
            public readonly double[,] limits = new double[MaxConstraintCount, 2];
            public readonly double[] ks = new double[MaxConstraintCount];
            public readonly double[] kd = new double[MaxConstraintCount];
        }

        public int index = -1;
        public int bodyCount;
        public readonly RigidBody[] bodies = new RigidBody[MaxBodyCount];

        public readonly double[,] F_x = new double[MaxConstraintCount, MaxBodyCount];
        public readonly double[,] F_y = new double[MaxConstraintCount, MaxBodyCount];
        public readonly double[,] F_t = new double[MaxConstraintCount, MaxBodyCount];

        protected int constraintCount;

        public Constraint(int constraintCount, int bodyCount)
        {
            this.constraintCount = constraintCount;
            this.bodyCount = bodyCount;
        }

        public virtual void Calculate(Output output, SystemState state)
        {
            /* void */
        }

        public int GetConstraintCount() => constraintCount;

        protected static void NoLimits(Output output)
        {
            for (int i = 0; i < MaxConstraintCount; ++i)
            {
                output.limits[i, 0] = -double.MaxValue;
                output.limits[i, 1] = double.MaxValue;
            }
        }
    }

    public abstract class ForceGenerator
    {
        public int index;

        public abstract void Apply(SystemState system);
    }
}
