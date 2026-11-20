// Port of simple-2d-constraint-solver include/optimized_nsv_rigid_body_system.h and
// src/optimized_nsv_rigid_body_system.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>
    /// Semi-implicit Euler with a sparse constraint solve each substep. Upstream's wall-clock profiling is not ported.
    /// </summary>
    public class OptimizedNsvRigidBodySystem : RigidBodySystem
    {
        public double biasFactor = 1.0;

        protected readonly NsvOdeSolver odeSolver = new NsvOdeSolver();
        protected SleSolver sleSolver;

        protected sealed class IntermediateValues
        {
            public readonly SparseMatrix J_sparse = new SparseMatrix(3), sreg0 = new SparseMatrix(3);
            public readonly Matrix C = new Matrix();
            public readonly Matrix M = new Matrix(), M_inv = new Matrix();
            public readonly Matrix b_err = new Matrix(), v_bias = new Matrix();
            public readonly Matrix limits = new Matrix();

            public readonly Matrix q_dot = new Matrix(), q_dot_prime = new Matrix();

            public readonly Matrix reg0 = new Matrix(), reg1 = new Matrix(), reg2 = new Matrix();

            public readonly Matrix right = new Matrix();
            public readonly Matrix F_ext = new Matrix(), F_C = new Matrix(), R = new Matrix();

            // Results
            public readonly Matrix lambda = new Matrix();
        }

        protected readonly IntermediateValues iv = new IntermediateValues();

        // Upstream declares the output on the stack in every ProcessConstraints call without initialising it. Each
        // constraint writes every field the solve reads (J for its bodies, v_bias, C, limits), so one reused instance is
        // equivalent and keeps the step allocation-free.
        readonly Constraint.Output constraintOutput = new Constraint.Output();

        public void Initialize(SleSolver sleSolver)
        {
            this.sleSolver = sleSolver;
        }

        /// <summary>Read access for the dense core's snapshot: the solver, and the constraint impulses kept as its warm start.</summary>
        internal SleSolver Solver => sleSolver;
        internal Matrix Lambda => iv.lambda;

        public override void Process(double dt, int steps = 1)
        {
            PopulateSystemState();
            PopulateMassMatrices(iv.M, iv.M_inv);

            for (int i = 0; i < steps; ++i)
            {
                odeSolver.Start(state, dt / steps);

                while (true)
                {
                    bool done = odeSolver.Step(state);

                    ProcessForces();
                    ProcessConstraints(dt / steps);
                    odeSolver.Solve(state);

                    if (done) break;
                }

                odeSolver.End();
            }

            PropagateResults();
        }

        protected void PropagateResults()
        {
            int n = GetRigidBodyCount();
            for (int i = 0; i < n; ++i)
            {
                rigidBodies[i].v_x = state.v_x[i];
                rigidBodies[i].v_y = state.v_y[i];

                rigidBodies[i].p_x = state.p_x[i];
                rigidBodies[i].p_y = state.p_y[i];

                rigidBodies[i].v_theta = state.v_theta[i];
                rigidBodies[i].theta = state.theta[i];
            }

            int m = GetConstraintCount();
            for (int i = 0, i_f = 0; i < m; ++i)
            {
                Constraint constraint = constraints[i];

                for (int j = 0; j < constraint.GetConstraintCount(); ++j, ++i_f)
                {
                    for (int k = 0; k < constraint.bodyCount; ++k)
                    {
                        constraint.F_x[j, k] = state.r_x[i_f * 2 + k];
                        constraint.F_y[j, k] = state.r_y[i_f * 2 + k];
                        constraint.F_t[j, k] = state.r_t[i_f * 2 + k];
                    }
                }
            }
        }

        protected void ProcessConstraints(double dt)
        {
            int n = GetRigidBodyCount();
            int m_f = GetFullConstraintCount();
            int m = GetConstraintCount();

            iv.J_sparse.Initialize(3 * n, m_f);
            iv.v_bias.Initialize(1, m_f);
            iv.C.Initialize(1, m_f);
            iv.limits.Initialize(2, m_f);

            Constraint.Output output = constraintOutput;
            for (int j = 0, j_f = 0; j < m; ++j)
            {
                constraints[j].Calculate(output, state);

                int n_f = constraints[j].GetConstraintCount();
                for (int k = 0; k < n_f; ++k, ++j_f)
                {
                    for (int i = 0; i < constraints[j].bodyCount; ++i)
                    {
                        int index = constraints[j].bodies[i].index;

                        if (index == -1) continue;

                        // Upstream's block index is a uint8_t; the int converts by truncation.
                        iv.J_sparse.SetBlock(j_f, i, unchecked((byte)index));
                    }

                    for (int i = 0; i < constraints[j].bodyCount * 3; ++i)
                    {
                        int index = constraints[j].bodies[i / 3].index;

                        if (index == -1) continue;

                        iv.J_sparse.Set(j_f, i / 3, i % 3,
                            output.J[k, i]);
                    }

                    iv.v_bias.Set(0, j_f, output.v_bias[k]);
                    iv.C.Set(0, j_f, output.C[k]);
                    iv.limits.Set(0, j_f, output.limits[k, 0] * dt);
                    iv.limits.Set(1, j_f, output.limits[k, 1] * dt);
                }
            }

            iv.q_dot.Resize(1, n * 3);
            for (int i = 0; i < n; ++i)
            {
                iv.q_dot.Set(0, i * 3 + 0, state.v_x[i]);
                iv.q_dot.Set(0, i * 3 + 1, state.v_y[i]);
                iv.q_dot.Set(0, i * 3 + 2, state.v_theta[i]);
            }

            iv.F_ext.Initialize(1, 3 * n, 0.0);
            for (int i = 0; i < n; ++i)
            {
                iv.F_ext.Set(0, i * 3 + 0, state.f_x[i]);
                iv.F_ext.Set(0, i * 3 + 1, state.f_y[i]);
                iv.F_ext.Set(0, i * 3 + 2, state.t[i]);
            }

            // Calculate q_dot_prime
            //  q_dot_prime = q_dot + M_inv * F_ext * dt
            iv.F_ext.Scale(dt, iv.reg0);
            iv.reg0.LeftScale(iv.M_inv, iv.reg1);
            iv.reg1.Add(iv.q_dot, iv.q_dot_prime);

            // Calculate b_err
            //  b_err = (bias_factor / dt) * C
            iv.C.Scale(biasFactor / dt, iv.b_err);

            // Calculate right side of linear equation
            //  -(J * q_dot_prime + v_bias + b_err)
            iv.J_sparse.Multiply(iv.q_dot_prime, iv.reg0);
            iv.reg0.Add(iv.v_bias, iv.reg1);
            iv.reg1.Add(iv.b_err, iv.reg0);
            iv.reg0.Negate(iv.right);

            if (!sleSolver.SupportsLimits())
            {
                sleSolver.Solve(
                    iv.J_sparse,
                    iv.M_inv,
                    iv.right,
                    iv.lambda,
                    iv.lambda);
            }
            else
            {
                sleSolver.SolveWithLimits(
                    iv.J_sparse,
                    iv.M_inv,
                    iv.right,
                    iv.limits,
                    iv.lambda,
                    iv.lambda);
            }

            // Constraint force derivation
            //  R = J_T * lambda_scale
            //  => transpose(J) * transpose(transpose(lambda_scale)) = R
            //  => transpose(lambda_scale * J) = R
            //  => transpose(J.leftScale(lambda_scale)) = R

            iv.lambda.Scale(1 / dt, iv.reg0);
            iv.J_sparse.LeftScale(iv.reg0, iv.sreg0);

            // For a one-body constraint, entry 1 of sreg0 is an empty block whose value is stale; it lands in a reaction slot
            // that is never read (both readers stop at the constraint's body count), as upstream.
            for (int i = 0; i < m_f; ++i)
            {
                for (int j = 0; j < 2; ++j)
                {
                    state.r_x[i * 2 + j] = iv.sreg0.Get(i, j, 0);
                    state.r_y[i * 2 + j] = iv.sreg0.Get(i, j, 1);
                    state.r_t[i * 2 + j] = iv.sreg0.Get(i, j, 2);
                }
            }

            for (int i = 0; i < n; ++i)
            {
                state.a_x[i] = iv.F_ext.Get(0, i * 3 + 0);
                state.a_y[i] = iv.F_ext.Get(0, i * 3 + 1);
                state.a_theta[i] = iv.F_ext.Get(0, i * 3 + 2);
            }

            for (int i = 0, j_f = 0; i < m; ++i)
            {
                Constraint constraint = constraints[i];

                int n_f = constraint.GetConstraintCount();
                for (int j = 0; j < n_f; ++j, ++j_f)
                {
                    for (int k = 0; k < constraint.bodyCount; ++k)
                    {
                        int body = constraint.bodies[k].index;
                        state.a_x[body] += state.r_x[j_f * 2 + k];
                        state.a_y[body] += state.r_y[j_f * 2 + k];
                        state.a_theta[body] += state.r_t[j_f * 2 + k];
                    }
                }
            }

            for (int i = 0; i < n; ++i)
            {
                double invMass = iv.M_inv.Get(0, i * 3 + 0);
                double invInertia = iv.M_inv.Get(0, i * 3 + 2);

                state.a_x[i] *= invMass;
                state.a_y[i] *= invMass;
                state.a_theta[i] *= invInertia;
            }
        }
    }
}
