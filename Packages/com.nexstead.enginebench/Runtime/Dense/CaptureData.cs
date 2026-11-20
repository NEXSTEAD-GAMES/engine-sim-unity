// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    /// <summary>
    /// The engine's state, step by step, for the Bench's visuals: rings of <see cref="capacity"/> steps that the stepping
    /// thread fills (<see cref="CaptureOps.Record"/>) and another thread reads (EngineCapture). Step <c>n</c> sits at index
    /// <c>n % capacity</c>; per-cylinder and per-exhaust values are laid out step by step. Reads only: recording changes no
    /// value the simulation computes.
    /// </summary>
    public unsafe struct DenseCapture
    {
        public int capacity;
        public int cylinders;
        public int exhausts;
        /// <summary>Steps recorded so far.</summary>
        public long written;

        /// <summary>The output crankshaft's angle within the engine cycle (rad, 0 to 4π from top dead centre, rising as it turns).</summary>
        public float* crankAngle;
        /// <summary>Each piston's distance from its bank's origin along the bank (m).</summary>
        public float* pistonTravel;
        /// <summary>Each cylinder's connecting rod: its body's position (m) and angle (rad), its ends being along its axis.</summary>
        public float* rodX;
        public float* rodY;
        public float* rodAngle;
        /// <summary>Each cylinder's intake and exhaust valve lift (m).</summary>
        public float* intakeLift;
        public float* exhaustLift;
        /// <summary>1 where a spark lit the cylinder's charge in that step.</summary>
        public byte* fired;
        /// <summary>Each exhaust's input to the synthesizer in that step: the pressure signal the sound is made from.</summary>
        public float* exhaustInput;
        /// <summary>Each chamber's ignition count at the last step recorded, to tell new ignitions.</summary>
        public int* lastIgnitions;
    }

    public static unsafe class CaptureOps
    {
        /// <summary>Appends this step's state (after <see cref="SimulatorOps.SimulateStep"/>).</summary>
        public static void Record(DenseEngine* e, DenseCapture* c)
        {
            DenseParts* p = &e->parts;
            RigidBodyData* bodies = e->solver.bodies;
            int index = (int)(c->written % c->capacity);

            c->crankAngle[index] = (float)PartsOps.CrankCycleAngle(e, 0);

            int row = index * c->cylinders;
            for (int i = 0; i < c->cylinders; ++i)
            {
                ChamberData* chamber = &p->chambers[i];
                PistonData* piston = &p->pistons[chamber->piston];
                CylinderBankData* bank = &p->banks[piston->bank];
                RigidBodyData* body = &bodies[piston->body];
                c->pistonTravel[row + i] = (float)((body->p_x - bank->x) * bank->dx + (body->p_y - bank->y) * bank->dy);

                RigidBodyData* rod = &bodies[piston->rodBody];
                c->rodX[row + i] = (float)rod->p_x;
                c->rodY[row + i] = (float)rod->p_y;
                c->rodAngle[row + i] = (float)rod->theta;

                PartsOps.ValveLifts(e, i, out double intake, out double exhaust);
                c->intakeLift[row + i] = (float)intake;
                c->exhaustLift[row + i] = (float)exhaust;

                c->fired[row + i] = (byte)(chamber->ignitions != c->lastIgnitions[i] ? 1 : 0);
                c->lastIgnitions[i] = chamber->ignitions;
            }

            int exhaustRow = index * c->exhausts;
            for (int j = 0; j < c->exhausts; ++j)
            {
                c->exhaustInput[exhaustRow + j] = (float)e->simulator.exhaustFlowStagingBuffer[j];
            }

            ++c->written;
        }

        /// <summary>
        /// The same record from the managed simulator (the managed core), so both cores capture alike: the values are the
        /// managed parts' own, which the dense core mirrors bit for bit.
        /// </summary>
        public static void Record(PistonEngineSimulator simulator, DenseCapture* c)
        {
            Engine engine = simulator.GetEngine();
            int index = (int)(c->written % c->capacity);

            c->crankAngle[index] = (float)engine.GetOutputCrankshaft().GetCycleAngle();

            int row = index * c->cylinders;
            for (int i = 0; i < c->cylinders; ++i)
            {
                CombustionChamber chamber = engine.GetChamber(i);
                Piston piston = chamber.GetPiston();
                CylinderBank bank = piston.GetCylinderBank();
                c->pistonTravel[row + i] = (float)(piston.RelativeX() * bank.GetDx() + piston.RelativeY() * bank.GetDy());

                RigidBody rod = piston.GetRod().body;
                c->rodX[row + i] = (float)rod.p_x;
                c->rodY[row + i] = (float)rod.p_y;
                c->rodAngle[row + i] = (float)rod.theta;

                CylinderHead head = chamber.GetHead();
                c->intakeLift[row + i] = (float)head.IntakeValveLift(piston.GetCylinderIndex());
                c->exhaustLift[row + i] = (float)head.ExhaustValveLift(piston.GetCylinderIndex());

                c->fired[row + i] = (byte)(chamber.ignitions != c->lastIgnitions[i] ? 1 : 0);
                c->lastIgnitions[i] = chamber.ignitions;
            }

            double[] staging = simulator.ExhaustFlowStagingBuffer;
            int exhaustRow = index * c->exhausts;
            for (int j = 0; j < c->exhausts; ++j)
            {
                c->exhaustInput[exhaustRow + j] = (float)staging[j];
            }

            ++c->written;
        }
    }
}
