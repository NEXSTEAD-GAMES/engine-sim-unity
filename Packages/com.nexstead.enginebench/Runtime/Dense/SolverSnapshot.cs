// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// Copies a loaded managed simulator's rigid body system into a DenseSolver. Managed code, run once.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public static unsafe partial class SolverOps
    {
        public static void Snapshot(PistonEngineSimulator simulator, DenseMemory memory, DenseSolver* solver)
        {
            if (!(simulator.GetSystem() is OptimizedNsvRigidBodySystem system))
            {
                throw new ArgumentException("The dense solver mirrors the optimised NSV rigid body system only.", nameof(simulator));
            }

            if (!(system.Solver is GaussSeidelSleSolver gaussSeidel))
            {
                throw new ArgumentException("The dense solver mirrors the Gauss-Seidel solver only.", nameof(simulator));
            }

            Engine engine = simulator.GetEngine();

            int n = system.GetRigidBodyCount();
            int m = system.GetConstraintCount();
            int m_f = system.GetFullConstraintCount();

            // Bodies
            solver->bodyCount = n;
            solver->bodies = memory.Alloc<RigidBodyData>(n);
            for (int i = 0; i < n; ++i)
            {
                RigidBody body = system.GetRigidBody(i);
                if (body.index != i) throw new InvalidOperationException($"Body {i} has index {body.index}.");

                solver->bodies[i] = new RigidBodyData
                {
                    p_x = body.p_x,
                    p_y = body.p_y,
                    v_x = body.v_x,
                    v_y = body.v_y,
                    theta = body.theta,
                    v_theta = body.v_theta,
                    m = body.m,
                    I = body.I,
                    index = body.index,
                };
            }

            // Constraints
            solver->constraintCount = m;
            solver->fullConstraintCount = m_f;
            solver->constraints = memory.Alloc<ConstraintData>(m);
            for (int i = 0; i < m; ++i)
            {
                solver->constraints[i] = Describe(system.GetConstraint(i));
            }

            solver->dynoConstraint = simulator.dyno.index;
            solver->starterConstraint = simulator.starterMotor.index;
            solver->clutchConstraint = simulator.GetTransmission().Clutch.index;

            // Force generators: the combustion chambers, and a supercharger's drive (-1)
            int generators = system.GetForceGeneratorCount();
            solver->forceGeneratorCount = generators;
            solver->forceGeneratorChambers = memory.Alloc<int>(generators);
            for (int i = 0; i < generators; ++i)
            {
                solver->forceGeneratorChambers[i] = ChamberIndex(engine, system.GetForceGenerator(i));
            }

            SystemStateOps.Allocate(&solver->state, memory, n, m_f);

            solver->biasFactor = system.biasFactor;
            solver->maxIterations = gaussSeidel.maxIterations;
            solver->minDelta = gaussSeidel.minDelta;

            // Intermediate values, at the sizes the first Process gives them.
            int n3 = 3 * n;
            int vector = Math.Max(n3, m_f);
            solver->M = memory.Alloc<double>(n3);
            solver->M_inv = memory.Alloc<double>(n3);
            solver->q_dot = memory.Alloc<double>(n3);
            solver->q_dot_prime = memory.Alloc<double>(n3);
            solver->F_ext = memory.Alloc<double>(n3);
            solver->reg0 = memory.Alloc<double>(vector);
            solver->reg1 = memory.Alloc<double>(vector);
            solver->v_bias = memory.Alloc<double>(m_f);
            solver->C = memory.Alloc<double>(m_f);
            solver->b_err = memory.Alloc<double>(m_f);
            solver->right = memory.Alloc<double>(m_f);
            solver->limits = memory.Alloc<double>(2 * m_f);
            solver->lambda = memory.Alloc<double>(m_f);

            solver->J_sparse = memory.Alloc<double>(m_f * RowSize);
            solver->J_sparseBlocks = memory.Alloc<byte>(m_f * Entries);
            solver->sreg0 = memory.Alloc<double>(m_f * RowSize);
            solver->sreg0Blocks = memory.Alloc<byte>(m_f * Entries);
            solver->solverReg = memory.Alloc<double>(m_f * RowSize);
            solver->solverRegBlocks = memory.Alloc<byte>(m_f * Entries);
            solver->solverM = memory.Alloc<double>(m_f * m_f);

            // The warm start: the managed solve starts from zeros (its lambda is still unsized before the first
            // step) and from its last result afterwards.
            Matrix lambda = system.Lambda;
            if (lambda.GetHeight() == m_f)
            {
                for (int i = 0; i < m_f; ++i) solver->lambda[i] = lambda.Get(0, i);
            }
        }

        static ConstraintData Describe(Constraint constraint)
        {
            var c = new ConstraintData
            {
                index = constraint.index,
                constraintCount = constraint.GetConstraintCount(),
                bodyCount = constraint.bodyCount,
                body0 = constraint.bodyCount > 0 ? BodyIndex(constraint.bodies[0]) : -1,
                body1 = constraint.bodyCount > 1 ? BodyIndex(constraint.bodies[1]) : -1,
            };

            for (int j = 0; j < Constraint.MaxConstraintCount; ++j)
            {
                for (int k = 0; k < Constraint.MaxBodyCount; ++k)
                {
                    c.F_x[j * 2 + k] = constraint.F_x[j, k];
                    c.F_y[j * 2 + k] = constraint.F_y[j, k];
                    c.F_t[j * 2 + k] = constraint.F_t[j, k];
                }
            }

            switch (constraint)
            {
                case FixedPositionConstraint f:
                    c.kind = ConstraintKind.FixedPosition;
                    c.local_x = f.local_x;
                    c.local_y = f.local_y;
                    c.world_x = f.world_x;
                    c.world_y = f.world_y;
                    c.ks = f.ks;
                    c.kd = f.kd;
                    break;
                case LineConstraint l:
                    c.kind = ConstraintKind.Line;
                    c.local_x = l.local_x;
                    c.local_y = l.local_y;
                    c.p0_x = l.p0_x;
                    c.p0_y = l.p0_y;
                    c.dx = l.dx;
                    c.dy = l.dy;
                    c.ks = l.ks;
                    c.kd = l.kd;
                    break;
                case LinkConstraint l:
                    c.kind = ConstraintKind.Link;
                    c.local_x = l.local_x_1;
                    c.local_y = l.local_y_1;
                    c.local_x_2 = l.local_x_2;
                    c.local_y_2 = l.local_y_2;
                    c.ks = l.ks;
                    c.kd = l.kd;
                    break;
                case ClutchConstraint cl:
                    c.kind = ConstraintKind.Clutch;
                    c.minTorque = cl.minTorque;
                    c.maxTorque = cl.maxTorque;
                    c.ks = cl.ks;
                    c.kd = cl.kd;
                    break;
                case RotationFrictionConstraint r:
                    c.kind = ConstraintKind.RotationFriction;
                    c.minTorque = r.minTorque;
                    c.maxTorque = r.maxTorque;
                    c.ks = r.ks;
                    c.kd = r.kd;
                    break;
                case Dynamometer d:
                    c.kind = ConstraintKind.Dynamometer;
                    c.rotationSpeed = d.rotationSpeed;
                    c.maxTorque = d.maxTorque;
                    c.hold = d.hold;
                    c.enabled = d.enabled;
                    c.ks = d.ks;
                    c.kd = d.kd;
                    break;
                case StarterMotor st:
                    c.kind = ConstraintKind.StarterMotor;
                    c.rotationSpeed = st.rotationSpeed;
                    c.maxTorque = st.maxTorque;
                    c.enabled = st.enabled;
                    c.ks = st.ks;
                    c.kd = st.kd;
                    break;
                case VehicleDragConstraint v:
                    c.kind = ConstraintKind.VehicleDrag;
                    Vehicle vehicle = v.DraggedVehicle;
                    c.vehicleMass = vehicle.GetMass();
                    c.dragCoefficient = vehicle.GetDragCoefficient();
                    c.crossSectionArea = vehicle.GetCrossSectionArea();
                    c.rollingResistance = vehicle.GetRollingResistance();
                    c.ks = v.ks;
                    c.kd = v.kd;
                    break;
                default:
                    throw new ArgumentException($"No dense form for constraint type {constraint.GetType().Name}.", nameof(constraint));
            }

            return c;
        }

        static int BodyIndex(RigidBody body) => body == null ? -1 : body.index;

        static int ChamberIndex(Engine engine, ForceGenerator generator)
        {
            if (ReferenceEquals(engine.GetForcedInduction(), generator)) return -1;

            for (int i = 0; i < engine.GetCylinderCount(); ++i)
            {
                if (ReferenceEquals(engine.GetChamber(i), generator)) return i;
            }

            throw new ArgumentException($"Force generator {generator.index} is not one of the engine's combustion chambers.", nameof(generator));
        }
    }
}
