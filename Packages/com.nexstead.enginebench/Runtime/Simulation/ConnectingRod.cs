// Port of engine-sim include/connecting_rod.h and src/connecting_rod.cpp, without upstream's journals on a rod: the port has
// no master and slave rods (radial engines), so every rod runs on a crankshaft journal.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class ConnectingRod : Part
    {
        public sealed class Parameters
        {
            public double mass = 0.0;
            public double momentOfInertia = 0.0;
            public double centerOfMass = 0.0;
            public double length = 0.0;
            public Piston piston = null;
            public Crankshaft crankshaft = null;
            public int journal = 0;
        }

        double centerOfMass;
        double length;
        double m;
        double I;
        int journal;
        Crankshaft crankshaft;
        Piston piston;

        public void Initialize(Parameters @params)
        {
            centerOfMass = @params.centerOfMass;
            length = @params.length;
            m = @params.mass;
            I = @params.momentOfInertia;
            journal = @params.journal;
            crankshaft = @params.crankshaft;
            piston = @params.piston;
        }

        public double GetBigEndLocal()
        {
            return -(length / 2) + centerOfMass;
        }

        public double GetLittleEndLocal()
        {
            return (length / 2) - centerOfMass;
        }

        public double GetCenterOfMass() => centerOfMass;
        public double GetLength() => length;
        public double GetMass() => m;
        public double GetMomentOfInertia() => I;
        public int GetJournal() => journal;
        public Crankshaft GetCrankshaft() => crankshaft;
        public Piston GetPiston() => piston;
    }
}
