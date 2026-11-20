// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense solver's state: Runtime/Scs's OptimizedNsvRigidBodySystem (with its NSV integrator and
// Gauss-Seidel solver) and the constraints engine-sim uses, as plain data. See DenseEngine.cs.
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    /// <summary>The managed constraint type a <see cref="ConstraintData"/> stands for.</summary>
    public enum ConstraintKind
    {
        FixedPosition,
        Line,
        Link,
        Clutch,
        RotationFriction,
        Dynamometer,
        StarterMotor,
        VehicleDrag,
    }

    /// <summary>
    /// One constraint: its kind, bodies (indices into <see cref="DenseSolver.bodies"/>; -1 for none), reaction forces and the
    /// parameters its kind reads. Fields carry the managed field names; each kind uses its own.
    /// </summary>
    public unsafe struct ConstraintData
    {
        public ConstraintKind kind;
        public int index;
        public int constraintCount;
        public int bodyCount;
        public int body0;
        public int body1;

        /// <summary>Reaction forces, [row * 2 + body] (managed <c>F_x[row, body]</c>).</summary>
        public fixed double F_x[Scs.Constraint.MaxConstraintCount * Scs.Constraint.MaxBodyCount];
        public fixed double F_y[Scs.Constraint.MaxConstraintCount * Scs.Constraint.MaxBodyCount];
        public fixed double F_t[Scs.Constraint.MaxConstraintCount * Scs.Constraint.MaxBodyCount];

        // FixedPosition (local, world); Link body 1 is local_x / local_y (managed local_x_1, local_y_1)
        public double local_x;
        public double local_y;
        public double world_x;
        public double world_y;

        // Link body 2
        public double local_x_2;
        public double local_y_2;

        // Line
        public double p0_x;
        public double p0_y;
        public double dx;
        public double dy;

        public double ks;
        public double kd;

        // Clutch, RotationFriction (min, max); Dynamometer, StarterMotor (max)
        public double minTorque;
        public double maxTorque;

        // Dynamometer, StarterMotor
        public double rotationSpeed;
        public bool hold;
        public bool enabled;

        // VehicleDrag: the vehicle it drags (its rotating mass is body0)
        public double vehicleMass;
        public double dragCoefficient;
        public double crossSectionArea;
        public double rollingResistance;
    }

    /// <summary>
    /// Mirror of <see cref="Scs.Constraint.Output"/>, the scratch every constraint writes before its rows are copied into the
    /// system. Only what the optimised system reads: J (row-major, [row * 6 + column]), C, v_bias and limits ([row * 2 + 0/1]).
    /// J_dot, ks and kd are written by the managed constraints but never read by this system, so they are not kept.
    /// </summary>
    public unsafe struct ConstraintOutputData
    {
        public fixed double C[Scs.Constraint.MaxConstraintCount];
        public fixed double J[Scs.Constraint.MaxConstraintCount * 3 * Scs.Constraint.MaxBodyCount];
        public fixed double v_bias[Scs.Constraint.MaxConstraintCount];
        public fixed double limits[Scs.Constraint.MaxConstraintCount * 2];
    }

    public unsafe struct DenseSolver
    {
        /// <summary>The rigid bodies, indexed as the managed <c>RigidBody.index</c>.</summary>
        public RigidBodyData* bodies;
        public int bodyCount;

        public ConstraintData* constraints;
        public int constraintCount;

        /// <summary>Sum of the constraints' row counts (managed <c>GetFullConstraintCount</c>): the size of the solve.</summary>
        public int fullConstraintCount;

        /// <summary>The force generators, in order: each is the combustion chamber of this cylinder, or -1 for the forced induction's drive.</summary>
        public int* forceGeneratorChambers;
        public int forceGeneratorCount;

        public SystemStateData state;
        public ConstraintOutputData output;

        public double biasFactor;

        // NSV integrator
        public double odeDt;

        // Gauss-Seidel
        public int maxIterations;
        public double minDelta;

        // Intermediate values (managed OptimizedNsvRigidBodySystem.IntermediateValues). Column vectors of
        // length 3 × bodies (M, M_inv, q_dot, q_dot_prime, F_ext) or rows (v_bias, C, b_err, right, lambda);
        // reg0 / reg1 hold either, so they have the larger length. limits is [row * 2 + 0/1].
        public double* M;
        public double* M_inv;
        public double* q_dot;
        public double* q_dot_prime;
        public double* F_ext;
        public double* reg0;
        public double* reg1;
        public double* v_bias;
        public double* C;
        public double* b_err;
        public double* right;
        public double* limits;

        /// <summary>The constraint impulses: the solve's result, and its warm start in the next step.</summary>
        public double* lambda;

        // Block-sparse matrices (stride 3, 2 entries per row): values [row * 6 + entry * 3 + slice],
        // block indices [row * 2 + entry] (0xFF: empty).
        public double* J_sparse;
        public byte* J_sparseBlocks;
        public double* sreg0;
        public byte* sreg0Blocks;
        public double* solverReg;
        public byte* solverRegBlocks;

        /// <summary>Gauss-Seidel's J W Jᵀ, [row * fullConstraintCount + column].</summary>
        public double* solverM;

        public int dynoConstraint;
        public int starterConstraint;
        public int clutchConstraint;
    }
}
