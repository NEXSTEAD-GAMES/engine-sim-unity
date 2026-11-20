// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense solver step: Runtime/Scs's OptimizedNsvRigidBodySystem.Process with its NSV
// integrator, Gauss-Seidel solver, block-sparse and dense matrix operations, and the constraints'
// Calculate, line for line. The matrices keep fixed sizes (bodies and constraints don't change
// after loading); every buffer the managed code fills before reading is filled here the same way,
// and the one it carries between steps, lambda (the warm start), is carried here too.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public static unsafe partial class SolverOps
    {
        const int Stride = 3;
        const int Entries = 2;
        const int RowSize = Stride * Entries;
        const byte EmptyBlock = 0xFF;

        // ---- Called by the other parts ----

        /// <summary>OptimizedNsvRigidBodySystem.Process.</summary>
        public static void Process(DenseEngine* e, double dt, int steps)
        {
            DenseSolver* s = &e->solver;

            PopulateSystemState(s);
            PopulateMassMatrices(s);

            for (int i = 0; i < steps; ++i)
            {
                // NsvOdeSolver.Start
                s->odeDt = dt / steps;

                while (true)
                {
                    // NsvOdeSolver.Step
                    s->state.dt = s->odeDt;
                    bool done = true;

                    ProcessForces(e, s);
                    ProcessConstraints(s, dt / steps);
                    OdeSolve(s);

                    if (done) break;
                }
            }

            PropagateResults(s);
        }

        public static void SetDyno(DenseEngine* e, bool enabled, bool hold, double rotationSpeed)
        {
            ConstraintData* dyno = &e->solver.constraints[e->solver.dynoConstraint];
            dyno->enabled = enabled;
            dyno->hold = hold;
            dyno->rotationSpeed = rotationSpeed;
        }

        /// <summary>Dynamometer.GetTorque.</summary>
        public static double DynoTorque(DenseEngine* e)
        {
            DenseSolver* s = &e->solver;
            ConstraintData* dyno = &s->constraints[s->dynoConstraint];
            return (s->bodies[dyno->body0].v_theta > 0)
                ? -dyno->F_t[0]
                : dyno->F_t[0];
        }

        public static void SetStarter(DenseEngine* e, bool enabled)
        {
            e->solver.constraints[e->solver.starterConstraint].enabled = enabled;
        }

        /// <summary>What Transmission.Update sets on its clutch.</summary>
        public static void SetClutchTorqueLimits(DenseEngine* e, double minTorque, double maxTorque)
        {
            ConstraintData* clutch = &e->solver.constraints[e->solver.clutchConstraint];
            clutch->minTorque = minTorque;
            clutch->maxTorque = maxTorque;
        }

        // ---- RigidBodySystem ----

        static void PopulateSystemState(DenseSolver* s)
        {
            int n = s->bodyCount;
            int m = s->constraintCount;
            SystemStateData* state = &s->state;

            for (int i = 0; i < n; ++i)
            {
                RigidBodyData* body = &s->bodies[i];

                state->a_x[i] = 0;
                state->a_y[i] = 0;

                state->v_x[i] = body->v_x;
                state->v_y[i] = body->v_y;

                state->p_x[i] = body->p_x;
                state->p_y[i] = body->p_y;

                state->a_theta[i] = 0;
                state->v_theta[i] = body->v_theta;
                state->theta[i] = body->theta;

                state->m[i] = body->m;
            }

            for (int i = 0, j_f = 0; i < m; ++i)
            {
                state->indexMap[i] = j_f;
                j_f += s->constraints[i].constraintCount;
            }
        }

        static void PopulateMassMatrices(DenseSolver* s)
        {
            int n = s->bodyCount;
            for (int i = 0; i < n; ++i)
            {
                RigidBodyData* body = &s->bodies[i];

                s->M[i * 3 + 0] = body->m;
                s->M[i * 3 + 1] = body->m;
                s->M[i * 3 + 2] = body->I;

                s->M_inv[i * 3 + 0] = 1 / body->m;
                s->M_inv[i * 3 + 1] = 1 / body->m;
                s->M_inv[i * 3 + 2] = 1 / body->I;
            }
        }

        static void ProcessForces(DenseEngine* e, DenseSolver* s)
        {
            int n = s->bodyCount;
            SystemStateData* state = &s->state;

            for (int i = 0; i < n; ++i)
            {
                state->f_x[i] = 0.0;
                state->f_y[i] = 0.0;
                state->t[i] = 0.0;
            }

            for (int i = 0; i < s->forceGeneratorCount; ++i)
            {
                int chamber = s->forceGeneratorChambers[i];
                if (chamber >= 0) PartsOps.ChamberApplyForce(e, chamber, state);
                else ForcedInductionOps.Apply(e, state);
            }
        }

        // ---- NsvOdeSolver ----

        static void OdeSolve(DenseSolver* s)
        {
            SystemStateData* system = &s->state;
            double dt = s->odeDt;
            system->dt = dt;

            for (int i = 0; i < system->n; ++i)
            {
                system->v_x[i] += system->a_x[i] * dt;
                system->v_y[i] += system->a_y[i] * dt;
                system->v_theta[i] += system->a_theta[i] * dt;

                system->p_x[i] += system->v_x[i] * dt;
                system->p_y[i] += system->v_y[i] * dt;
                system->theta[i] += system->v_theta[i] * dt;
            }
        }

        // ---- OptimizedNsvRigidBodySystem ----

        static void PropagateResults(DenseSolver* s)
        {
            int n = s->bodyCount;
            SystemStateData* state = &s->state;
            for (int i = 0; i < n; ++i)
            {
                RigidBodyData* body = &s->bodies[i];

                body->v_x = state->v_x[i];
                body->v_y = state->v_y[i];

                body->p_x = state->p_x[i];
                body->p_y = state->p_y[i];

                body->v_theta = state->v_theta[i];
                body->theta = state->theta[i];
            }

            int m = s->constraintCount;
            for (int i = 0, i_f = 0; i < m; ++i)
            {
                ConstraintData* constraint = &s->constraints[i];

                for (int j = 0; j < constraint->constraintCount; ++j, ++i_f)
                {
                    for (int k = 0; k < constraint->bodyCount; ++k)
                    {
                        constraint->F_x[j * 2 + k] = state->r_x[i_f * 2 + k];
                        constraint->F_y[j * 2 + k] = state->r_y[i_f * 2 + k];
                        constraint->F_t[j * 2 + k] = state->r_t[i_f * 2 + k];
                    }
                }
            }
        }

        static void ProcessConstraints(DenseSolver* s, double dt)
        {
            int n = s->bodyCount;
            int m_f = s->fullConstraintCount;
            int m = s->constraintCount;
            SystemStateData* state = &s->state;

            // J_sparse.Initialize(3 * n, m_f): block indices reset (values kept); v_bias, C, limits zeroed.
            for (int i = 0; i < Entries * m_f; ++i) s->J_sparseBlocks[i] = EmptyBlock;
            for (int i = 0; i < m_f; ++i)
            {
                s->v_bias[i] = 0;
                s->C[i] = 0;
                s->limits[i * 2 + 0] = 0;
                s->limits[i * 2 + 1] = 0;
            }

            ConstraintOutputData* output = &s->output;
            for (int j = 0, j_f = 0; j < m; ++j)
            {
                ConstraintData* constraint = &s->constraints[j];
                Calculate(s, constraint, output, state);

                int n_f = constraint->constraintCount;
                for (int k = 0; k < n_f; ++k, ++j_f)
                {
                    for (int i = 0; i < constraint->bodyCount; ++i)
                    {
                        int index = i == 0 ? constraint->body0 : constraint->body1;

                        if (index == -1) continue;

                        // Upstream's block index is a uint8_t; the int converts by truncation.
                        s->J_sparseBlocks[j_f * Entries + i] = unchecked((byte)index);
                    }

                    for (int i = 0; i < constraint->bodyCount * 3; ++i)
                    {
                        int index = i / 3 == 0 ? constraint->body0 : constraint->body1;

                        if (index == -1) continue;

                        s->J_sparse[j_f * RowSize + (i / 3) * Stride + i % 3] = output->J[k * 6 + i];
                    }

                    s->v_bias[j_f] = output->v_bias[k];
                    s->C[j_f] = output->C[k];
                    s->limits[j_f * 2 + 0] = output->limits[k * 2 + 0] * dt;
                    s->limits[j_f * 2 + 1] = output->limits[k * 2 + 1] * dt;
                }
            }

            int n3 = n * 3;
            for (int i = 0; i < n; ++i)
            {
                s->q_dot[i * 3 + 0] = state->v_x[i];
                s->q_dot[i * 3 + 1] = state->v_y[i];
                s->q_dot[i * 3 + 2] = state->v_theta[i];
            }

            for (int i = 0; i < n; ++i)
            {
                s->F_ext[i * 3 + 0] = state->f_x[i];
                s->F_ext[i * 3 + 1] = state->f_y[i];
                s->F_ext[i * 3 + 2] = state->t[i];
            }

            // q_dot_prime = q_dot + M_inv * F_ext * dt
            for (int i = 0; i < n3; ++i) s->reg0[i] = dt * s->F_ext[i];                  // F_ext.Scale(dt, reg0)
            for (int i = 0; i < n3; ++i) s->reg1[i] = s->M_inv[i] * s->reg0[i];          // reg0.LeftScale(M_inv, reg1)
            for (int i = 0; i < n3; ++i) s->q_dot_prime[i] = s->reg1[i] + s->q_dot[i];   // reg1.Add(q_dot, q_dot_prime)

            // b_err = (bias_factor / dt) * C
            double biasScale = s->biasFactor / dt;
            for (int i = 0; i < m_f; ++i) s->b_err[i] = biasScale * s->C[i];

            // -(J * q_dot_prime + v_bias + b_err)
            SparseMultiplyVector(s->J_sparse, s->J_sparseBlocks, m_f, s->q_dot_prime, s->reg0);
            for (int i = 0; i < m_f; ++i) s->reg1[i] = s->reg0[i] + s->v_bias[i];         // reg0.Add(v_bias, reg1)
            for (int i = 0; i < m_f; ++i) s->reg0[i] = s->reg1[i] + s->b_err[i];          // reg1.Add(b_err, reg0)
            for (int i = 0; i < m_f; ++i) s->right[i] = -s->reg0[i];                      // reg0.Negate(right)

            // The Gauss-Seidel solver supports limits: SolveWithLimits(J, M_inv, right, limits, lambda, lambda).
            SolveWithLimits(s);

            // R = transpose(J.leftScale(lambda / dt))
            double inverseDt = 1 / dt;
            for (int i = 0; i < m_f; ++i) s->reg0[i] = inverseDt * s->lambda[i];          // lambda.Scale(1 / dt, reg0)
            SparseLeftScale(s->J_sparse, s->J_sparseBlocks, m_f, s->reg0, s->sreg0, s->sreg0Blocks);

            // For a one-body constraint, entry 1 of sreg0 is an empty block whose value is stale; it lands in a
            // reaction slot that is never read (both readers stop at the constraint's body count), as upstream.
            for (int i = 0; i < m_f; ++i)
            {
                for (int j = 0; j < 2; ++j)
                {
                    state->r_x[i * 2 + j] = s->sreg0[i * RowSize + j * Stride + 0];
                    state->r_y[i * 2 + j] = s->sreg0[i * RowSize + j * Stride + 1];
                    state->r_t[i * 2 + j] = s->sreg0[i * RowSize + j * Stride + 2];
                }
            }

            for (int i = 0; i < n; ++i)
            {
                state->a_x[i] = s->F_ext[i * 3 + 0];
                state->a_y[i] = s->F_ext[i * 3 + 1];
                state->a_theta[i] = s->F_ext[i * 3 + 2];
            }

            for (int i = 0, j_f = 0; i < m; ++i)
            {
                ConstraintData* constraint = &s->constraints[i];

                int n_f = constraint->constraintCount;
                for (int j = 0; j < n_f; ++j, ++j_f)
                {
                    for (int k = 0; k < constraint->bodyCount; ++k)
                    {
                        int body = k == 0 ? constraint->body0 : constraint->body1;
                        state->a_x[body] += state->r_x[j_f * 2 + k];
                        state->a_y[body] += state->r_y[j_f * 2 + k];
                        state->a_theta[body] += state->r_t[j_f * 2 + k];
                    }
                }
            }

            for (int i = 0; i < n; ++i)
            {
                double invMass = s->M_inv[i * 3 + 0];
                double invInertia = s->M_inv[i * 3 + 2];

                state->a_x[i] *= invMass;
                state->a_y[i] *= invMass;
                state->a_theta[i] *= invInertia;
            }
        }

        // ---- GaussSeidelSleSolver.SolveWithLimits (result and previous are both lambda) ----

        static void SolveWithLimits(DenseSolver* s)
        {
            int n = s->fullConstraintCount;

            // result.GetHeight() == n after the first step (the snapshot starts lambda as the managed one is), and
            // result.Set(previous) copies lambda onto itself: the warm start is lambda as it stands.

            SparseRightScale(s->J_sparse, s->J_sparseBlocks, n, s->M_inv, s->solverReg, s->solverRegBlocks);
            SparseMultiplyTranspose(s->solverReg, s->solverRegBlocks, n, s->J_sparse, s->J_sparseBlocks, n, s->solverM);

            for (int i = 0; i < s->maxIterations; ++i)
            {
                double maxDelta = SolveIteration(s->solverM, s->right, s->limits, s->lambda, n);

                if (maxDelta < s->minDelta)
                {
                    return;
                }
            }
        }

        // k_next and k are the same vector: an in-place Gauss-Seidel sweep. Each k[i] is read before it is
        // overwritten, as upstream.
        static double SolveIteration(double* left, double* right, double* limits, double* k, int n)
        {
            double maxDifference = 0.0;

            for (int i = 0; i < n; ++i)
            {
                double s0 = 0.0, s1 = 0.0;
                double* row = left + i * n;

                for (int j = 0; j < i; ++j)
                {
                    s0 += row[j] * k[j];
                }

                for (int j = i + 1; j < n; ++j)
                {
                    s1 += row[j] * k[j];
                }

                double k_next_i =
                    (1 / row[i]) * (right[i] - s0 - s1);

                double limitMin = limits[i * 2 + 0];
                double limitMax = limits[i * 2 + 1];

                double x = Utilities.Fmax(limitMin, Utilities.Fmin(limitMax, k_next_i));

                double min_k = Utilities.Fmax(1E-3, Math.Abs(k[i]));
                double delta = Math.Abs(x - k[i]) / min_k;
                maxDifference = (delta > maxDifference)
                    ? delta
                    : maxDifference;

                k[i] = x;
            }

            return maxDifference;
        }

        // ---- SparseMatrix<3, 2> on row-major blocks; vectors are the width-1 matrices ----

        /// <summary>SparseMatrix.Multiply(b, target) with b a column vector: target is cleared, then each row's dot product.</summary>
        static void SparseMultiplyVector(double* data, byte* blocks, int height, double* b, double* target)
        {
            for (int i = 0; i < height; ++i) target[i] = 0;

            for (int i = 0; i < height; ++i)
            {
                double v = 0.0;
                for (int k = 0; k < Entries; ++k)
                {
                    int offset = k * Stride;
                    byte block = blocks[i * Entries + k];
                    if (block == EmptyBlock) continue;

                    for (int l = 0; l < Stride; ++l)
                    {
                        v += data[i * RowSize + offset + l] * b[block * Stride + l];
                    }
                }

                target[i] = v;
            }
        }

        /// <summary>SparseMatrix.RightScale(scale, target): target's blocks reset, then each block scaled per column.</summary>
        static void SparseRightScale(double* data, byte* blocks, int height, double* scale, double* target, byte* targetBlocks)
        {
            for (int i = 0; i < Entries * height; ++i) targetBlocks[i] = EmptyBlock;

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < Entries; ++j)
                {
                    byte index = blocks[i * Entries + j];
                    if (index == EmptyBlock) continue;

                    targetBlocks[i * Entries + j] = index;

                    for (int k = 0; k < Stride; ++k)
                    {
                        target[i * RowSize + j * Stride + k] = scale[index * Stride + k] * data[i * RowSize + j * Stride + k];
                    }
                }
            }
        }

        /// <summary>SparseMatrix.LeftScale(scale, target): target's blocks reset, then each row scaled.</summary>
        static void SparseLeftScale(double* data, byte* blocks, int height, double* scale, double* target, byte* targetBlocks)
        {
            for (int i = 0; i < Entries * height; ++i) targetBlocks[i] = EmptyBlock;

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < Entries; ++j)
                {
                    byte index = blocks[i * Entries + j];
                    if (index == EmptyBlock) continue;

                    targetBlocks[i * Entries + j] = index;

                    for (int k = 0; k < Stride; ++k)
                    {
                        target[i * RowSize + j * Stride + k] = scale[i] * data[i * RowSize + j * Stride + k];
                    }
                }
            }
        }

        /// <summary>
        /// SparseMatrix.MultiplyTranspose(b_T, target): target (b_T height wide, height high, row-major) is cleared, then
        /// entry (row i, column j) is row i of this dotted with row j of b_T over matching blocks.
        /// </summary>
        static void SparseMultiplyTranspose(
            double* data, byte* blocks, int height,
            double* b_T, byte* b_TBlocks, int b_THeight,
            double* target)
        {
            for (int i = 0; i < b_THeight * height; ++i) target[i] = 0;

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < b_THeight; ++j)
                {
                    double dot = 0;
                    for (int k = 0; k < Entries; ++k)
                    {
                        byte block0 = blocks[i * Entries + k];
                        if (block0 == EmptyBlock) continue;

                        for (int l = 0; l < Entries; ++l)
                        {
                            byte block1 = b_TBlocks[j * Entries + l];
                            if (block0 == block1)
                            {
                                for (int m = 0; m < Stride; ++m)
                                {
                                    dot +=
                                        data[i * RowSize + k * Stride + m]
                                        * b_T[j * RowSize + l * Stride + m];
                                }
                            }
                        }
                    }

                    target[i * b_THeight + j] = dot;
                }
            }
        }
    }
}
