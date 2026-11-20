// Port of engine-sim include/standard_valvetrain.h and src/standard_valvetrain.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class StandardValvetrain : Valvetrain
    {
        public sealed class Parameters
        {
            public Camshaft intakeCamshaft;
            public Camshaft exhaustCamshaft;
        }

        Camshaft intakeCamshaft;
        Camshaft exhaustCamshaft;

        public void Initialize(Parameters parameters)
        {
            intakeCamshaft = parameters.intakeCamshaft;
            exhaustCamshaft = parameters.exhaustCamshaft;
        }

        public override double IntakeValveLift(int cylinder)
        {
            return intakeCamshaft.ValveLift(cylinder);
        }

        public override double ExhaustValveLift(int cylinder)
        {
            return exhaustCamshaft.ValveLift(cylinder);
        }

        public override Camshaft GetActiveIntakeCamshaft() => intakeCamshaft;
        public override Camshaft GetActiveExhaustCamshaft() => exhaustCamshaft;
    }
}
