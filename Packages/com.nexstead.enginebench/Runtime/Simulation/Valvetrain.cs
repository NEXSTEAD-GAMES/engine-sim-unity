// Port of engine-sim include/valvetrain.h and src/valvetrain.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public abstract class Valvetrain
    {
        public abstract double IntakeValveLift(int cylinder);
        public abstract double ExhaustValveLift(int cylinder);

        public abstract Camshaft GetActiveIntakeCamshaft();
        public abstract Camshaft GetActiveExhaustCamshaft();
    }
}
