// Port of simple-2d-constraint-solver include/rigid_body_system.h and src/rigid_body_system.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>
    /// Bodies, constraints and force generators, and the system state they are solved in. Upstream's wall-clock profiling
    /// (solve and evaluation microseconds per frame) is not ported: it never feeds the simulation.
    /// </summary>
    public class RigidBodySystem
    {
        protected readonly List<RigidBody> rigidBodies = new List<RigidBody>();
        protected readonly List<Constraint> constraints = new List<Constraint>();
        protected readonly List<ForceGenerator> forceGenerators = new List<ForceGenerator>();

        protected readonly SystemState state = new SystemState();

        public virtual void Reset()
        {
            rigidBodies.Clear();
            constraints.Clear();
            forceGenerators.Clear();
        }

        public virtual void Process(double dt, int steps = 1)
        {
            /* void */
        }

        public void AddRigidBody(RigidBody body)
        {
            rigidBodies.Add(body);
            body.index = rigidBodies.Count - 1;
        }

        public void RemoveRigidBody(RigidBody body)
        {
            rigidBodies[body.index] = rigidBodies[rigidBodies.Count - 1];
            rigidBodies[body.index].index = body.index;
            rigidBodies.RemoveAt(rigidBodies.Count - 1);
        }

        public RigidBody GetRigidBody(int i)
        {
            return rigidBodies[i];
        }

        /// <summary>Read access for the dense core's snapshot.</summary>
        internal Constraint GetConstraint(int i) => constraints[i];

        /// <summary>Read access for the dense core's snapshot.</summary>
        internal ForceGenerator GetForceGenerator(int i) => forceGenerators[i];

        public void AddConstraint(Constraint constraint)
        {
            constraints.Add(constraint);
            constraint.index = constraints.Count - 1;
        }

        public void RemoveConstraint(Constraint constraint)
        {
            constraints[constraint.index] = constraints[constraints.Count - 1];
            constraints[constraint.index].index = constraint.index;
            constraints.RemoveAt(constraints.Count - 1);
        }

        public void AddForceGenerator(ForceGenerator forceGenerator)
        {
            forceGenerators.Add(forceGenerator);
            forceGenerator.index = forceGenerators.Count - 1;
        }

        public void RemoveForceGenerator(ForceGenerator forceGenerator)
        {
            forceGenerators[forceGenerator.index] = forceGenerators[forceGenerators.Count - 1];
            forceGenerators[forceGenerator.index].index = forceGenerator.index;
            forceGenerators.RemoveAt(forceGenerators.Count - 1);
        }

        public int GetRigidBodyCount() => rigidBodies.Count;
        public int GetConstraintCount() => constraints.Count;
        public int GetForceGeneratorCount() => forceGenerators.Count;

        public int GetFullConstraintCount()
        {
            int count = 0;
            for (int i = 0; i < constraints.Count; ++i)
            {
                count += constraints[i].GetConstraintCount();
            }

            return count;
        }

        protected void PopulateSystemState()
        {
            int n = GetRigidBodyCount();
            int n_c = GetFullConstraintCount();
            int m = GetConstraintCount();

            state.Resize(n, n_c);

            for (int i = 0; i < n; ++i)
            {
                state.a_x[i] = 0;
                state.a_y[i] = 0;

                state.v_x[i] = rigidBodies[i].v_x;
                state.v_y[i] = rigidBodies[i].v_y;

                state.p_x[i] = rigidBodies[i].p_x;
                state.p_y[i] = rigidBodies[i].p_y;

                state.a_theta[i] = 0;
                state.v_theta[i] = rigidBodies[i].v_theta;
                state.theta[i] = rigidBodies[i].theta;

                state.m[i] = rigidBodies[i].m;
            }

            for (int i = 0, j_f = 0; i < m; ++i)
            {
                state.indexMap[i] = j_f;
                j_f += constraints[i].GetConstraintCount();
            }
        }

        protected void PopulateMassMatrices(Matrix M, Matrix M_inv)
        {
            int n = GetRigidBodyCount();

            M.Initialize(1, 3 * n);
            M_inv.Initialize(1, 3 * n);

            for (int i = 0; i < n; ++i)
            {
                M.Set(0, i * 3 + 0, rigidBodies[i].m);
                M.Set(0, i * 3 + 1, rigidBodies[i].m);
                M.Set(0, i * 3 + 2, rigidBodies[i].I);

                M_inv.Set(0, i * 3 + 0, 1 / rigidBodies[i].m);
                M_inv.Set(0, i * 3 + 1, 1 / rigidBodies[i].m);
                M_inv.Set(0, i * 3 + 2, 1 / rigidBodies[i].I);
            }
        }

        protected void ProcessForces()
        {
            int n_f = GetForceGeneratorCount();
            int n = GetRigidBodyCount();

            for (int i = 0; i < n; ++i)
            {
                state.f_x[i] = 0.0;
                state.f_y[i] = 0.0;
                state.t[i] = 0.0;
            }

            for (int i = 0; i < n_f; ++i)
            {
                forceGenerators[i].Apply(state);
            }
        }
    }
}
