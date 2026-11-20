// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// THE DENSE CORE
//
// The managed port (Runtime/Simulation, Runtime/Scs, Runtime/Synthesis) mirrors upstream class by
// class and is the oracle. The dense core is its per-step code again, data-oriented, so Burst can
// compile it: an engine's whole state lives in unmanaged structs, and the steps are static methods
// over them. A dense engine is not built from a description: it is a snapshot of a managed
// PistonEngineSimulator taken after loading (and impulse responses and seeding), so all of
// upstream's set-up stays in one place. From there, stepping the dense engine must give exactly
// what stepping the managed one gives (bit for bit under .NET, checked on every golden run of the
// C++ reference).
//
// Files (namespace Nexstead.EngineBench.Dense):
//   DenseParts        Parts*.cs          engine parts, functions, gas systems, chambers, intakes,
//                                        exhausts, heads, camshafts, valvetrains, fuel, ignition,
//                                        throttle (PartsOps)
//   DenseSolver       Solver*.cs         rigid bodies, every constraint kind (the solver's own and
//                                        Dynamometer, StarterMotor, VehicleDragConstraint,
//                                        ClutchConstraint), the optimised NSV system, Gauss-Seidel
//                                        (SolverOps)
//   DenseSynthesizer  Synthesizer*.cs    the synthesizer, its filters, both convolutions (SynthOps)
//   DenseSimulator    SimulatorDense.cs  step orchestration, vehicle, transmission, dyno samples,
//                                        delay filters, synthesizer input, block rendering
//                                        (SimulatorOps); DenseCore, which takes the snapshot and
//                                        owns the memory
// A cylinder is the global piston and chamber index; rigid bodies are indexed as the managed
// RigidBody.index.
//
// Rules for per-step code (what Burst compiles):
//   - unsafe static methods over the structs here and in each part; arrays are pointers into memory
//     from DenseMemory, allocated at snapshot time; small fixed arrays may be `fixed` buffers.
//   - no managed objects, arrays, strings, delegates, interfaces, virtual calls, LINQ, foreach over
//     collections or exceptions; where the managed port dispatches virtually, store an int kind and
//     switch on it.
//   - no static mutable state (static readonly scalars and consts are fine).
//   - System.Math / System.MathF and Simulation.Utilities may be called.
//   - the arithmetic is the managed port's, operation for operation, in the same order: the dense
//     core is checked bit for bit against it.
//   - nothing allocates per step.
//
// Snapshot code is ordinary managed code and may read anything. Where a managed class keeps what a
// snapshot needs private, add an internal read accessor to it (never change its behaviour).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    /// <summary>One engine's whole per-step state. Lives in unmanaged memory (<see cref="DenseMemory"/>).</summary>
    public unsafe struct DenseEngine
    {
        public DenseParts parts;
        public DenseSolver solver;
        public DenseSimulator simulator;
        public DenseSynthesizer synthesizer;
    }

    /// <summary>Mirror of <see cref="Scs.RigidBody"/>.</summary>
    public struct RigidBodyData
    {
        public double p_x;
        public double p_y;
        public double v_x;
        public double v_y;
        public double theta;
        public double v_theta;
        public double m;
        public double I;
        public int index;
    }

    /// <summary>Mirror of <see cref="Scs.SystemState"/>: arrays sized once at snapshot time.</summary>
    public unsafe struct SystemStateData
    {
        public int* indexMap;

        public double* a_theta;
        public double* v_theta;
        public double* theta;

        public double* a_x;
        public double* a_y;
        public double* v_x;
        public double* v_y;
        public double* p_x;
        public double* p_y;

        public double* f_x;
        public double* f_y;
        public double* t;

        public double* r_x;
        public double* r_y;
        public double* r_t;

        public double* m;

        public int n;
        public int n_c;
        public double dt;
    }

    /// <summary>Mirror of <see cref="Scs.SystemState"/>'s methods.</summary>
    public static unsafe class SystemStateOps
    {
        public static void LocalToWorld(SystemStateData* s, double x, double y, out double x_t, out double y_t, int body)
        {
            double x0 = s->p_x[body];
            double y0 = s->p_y[body];
            double theta = s->theta[body];

            double cos_theta = Math.Cos(theta);
            double sin_theta = Math.Sin(theta);

            x_t = cos_theta * x - sin_theta * y + x0;
            y_t = sin_theta * x + cos_theta * y + y0;
        }

        public static void VelocityAtPoint(SystemStateData* s, double x, double y, out double v_x, out double v_y, int body)
        {
            LocalToWorld(s, x, y, out double w_x, out double w_y, body);

            double v_theta = s->v_theta[body];
            double angularToLinear_x = -v_theta * (w_y - s->p_y[body]);
            double angularToLinear_y = v_theta * (w_x - s->p_x[body]);

            v_x = s->v_x[body] + angularToLinear_x;
            v_y = s->v_y[body] + angularToLinear_y;
        }

        public static void ApplyForce(SystemStateData* s, double x_l, double y_l, double f_x, double f_y, int body)
        {
            LocalToWorld(s, x_l, y_l, out double w_x, out double w_y, body);

            s->f_x[body] += f_x;
            s->f_y[body] += f_y;

            s->t[body] +=
                (w_y - s->p_y[body]) * -f_x +
                (w_x - s->p_x[body]) * f_y;
        }

        /// <summary>Allocates the arrays for <paramref name="bodies"/> bodies and <paramref name="constraints"/> constraint rows, zeroed.</summary>
        public static void Allocate(SystemStateData* s, DenseMemory memory, int bodies, int constraints)
        {
            s->n = bodies;
            s->n_c = constraints;
            s->indexMap = memory.Alloc<int>(constraints);
            s->a_theta = memory.Alloc<double>(bodies);
            s->v_theta = memory.Alloc<double>(bodies);
            s->theta = memory.Alloc<double>(bodies);
            s->a_x = memory.Alloc<double>(bodies);
            s->a_y = memory.Alloc<double>(bodies);
            s->v_x = memory.Alloc<double>(bodies);
            s->v_y = memory.Alloc<double>(bodies);
            s->p_x = memory.Alloc<double>(bodies);
            s->p_y = memory.Alloc<double>(bodies);
            s->f_x = memory.Alloc<double>(bodies);
            s->f_y = memory.Alloc<double>(bodies);
            s->t = memory.Alloc<double>(bodies);
            s->m = memory.Alloc<double>(bodies);
            s->r_x = memory.Alloc<double>(constraints * 2);
            s->r_y = memory.Alloc<double>(constraints * 2);
            s->r_t = memory.Alloc<double>(constraints * 2);
        }
    }

    /// <summary>
    /// The unmanaged memory of one dense engine: every allocation is zeroed and lives until <see cref="Dispose"/> frees them all.
    /// Snapshot-time only; per-step code never allocates.
    /// </summary>
    public sealed unsafe class DenseMemory : IDisposable
    {
        readonly List<IntPtr> blocks = new List<IntPtr>();

        public T* Alloc<T>(int count) where T : unmanaged
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            long bytes = Math.Max(1L, (long)count * sizeof(T));
            IntPtr block = Marshal.AllocHGlobal(new IntPtr(bytes));
            byte* p = (byte*)block;
            for (long i = 0; i < bytes; ++i) p[i] = 0;
            blocks.Add(block);
            return (T*)block;
        }

        /// <summary>Copies a managed array into new unmanaged memory.</summary>
        public T* Copy<T>(T[] source) where T : unmanaged
        {
            T* target = Alloc<T>(source.Length);
            for (int i = 0; i < source.Length; ++i) target[i] = source[i];
            return target;
        }

        public void Dispose()
        {
            foreach (IntPtr block in blocks) Marshal.FreeHGlobal(block);
            blocks.Clear();
        }
    }
}
