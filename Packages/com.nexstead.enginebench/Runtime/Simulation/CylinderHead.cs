// Port of engine-sim include/cylinder_head.h and src/cylinder_head.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class CylinderHead : Part
    {
        public sealed class Parameters
        {
            public CylinderBank Bank;

            public Function ExhaustPortFlow;
            public Function IntakePortFlow;
            public Valvetrain Valvetrain;

            public double CombustionChamberVolume;
            public double IntakeRunnerVolume;
            public double IntakeRunnerCrossSectionArea;
            public double ExhaustRunnerVolume;
            public double ExhaustRunnerCrossSectionArea;

            public bool FlipDisplay = false;
        }

        public sealed class Cylinder
        {
            public ExhaustSystem exhaustSystem = null;
            public Intake intake = null;
            public double soundAttenuation = 1.0;
            public double headerPrimaryLength = 0.0;
        }

        Cylinder[] cylinders;
        CylinderBank bank;
        Valvetrain valvetrain;

        Function exhaustPortFlow;
        Function intakePortFlow;

        double intakeRunnerVolume = 0.0;
        double intakeRunnerCrossSectionArea = 0.0;
        double exhaustRunnerVolume = 0.0;
        double exhaustRunnerCrossSectionArea = 0.0;

        double combustionChamberVolume = 0.0;

        bool flipDisplay = false;

        public void Initialize(Parameters @params)
        {
            cylinders = new Cylinder[@params.Bank.GetCylinderCount()];
            for (int i = 0; i < cylinders.Length; ++i)
            {
                cylinders[i] = new Cylinder();
            }

            bank = @params.Bank;
            valvetrain = @params.Valvetrain;
            exhaustPortFlow = @params.ExhaustPortFlow;
            intakePortFlow = @params.IntakePortFlow;
            combustionChamberVolume = @params.CombustionChamberVolume;
            flipDisplay = @params.FlipDisplay;

            intakeRunnerVolume = @params.IntakeRunnerVolume;
            intakeRunnerCrossSectionArea = @params.IntakeRunnerCrossSectionArea;
            exhaustRunnerVolume = @params.ExhaustRunnerVolume;
            exhaustRunnerCrossSectionArea = @params.ExhaustRunnerCrossSectionArea;
        }

        public double IntakeFlowRate(int cylinder)
        {
            return intakePortFlow.SampleTriangle(
                IntakeValveLift(cylinder));
        }

        public double ExhaustFlowRate(int cylinder)
        {
            return exhaustPortFlow.SampleTriangle(
                ExhaustValveLift(cylinder));
        }

        public double IntakeValveLift(int cylinder)
        {
            return valvetrain.IntakeValveLift(cylinder);
        }

        public double ExhaustValveLift(int cylinder)
        {
            return valvetrain.ExhaustValveLift(cylinder);
        }

        public ExhaustSystem GetExhaustSystem(int cylinderIndex) => cylinders[cylinderIndex].exhaustSystem;

        public void SetAllExhaustSystems(ExhaustSystem system)
        {
            for (int i = 0; i < bank.GetCylinderCount(); ++i)
            {
                cylinders[i].exhaustSystem = system;
            }
        }

        public void SetExhaustSystem(int i, ExhaustSystem system)
        {
            cylinders[i].exhaustSystem = system;
        }

        public double GetSoundAttenuation(int cylinderIndex) => cylinders[cylinderIndex].soundAttenuation;

        public void SetSoundAttenuation(int i, double soundAttenuation)
        {
            cylinders[i].soundAttenuation = soundAttenuation;
        }

        public Intake GetIntake(int cylinderIndex) => cylinders[cylinderIndex].intake;

        public void SetAllIntakes(Intake intake)
        {
            for (int i = 0; i < bank.GetCylinderCount(); ++i)
            {
                cylinders[i].intake = intake;
            }
        }

        public void SetIntake(int i, Intake intake)
        {
            cylinders[i].intake = intake;
        }

        public double GetHeaderPrimaryLength(int cylinderIndex) => cylinders[cylinderIndex].headerPrimaryLength;

        public void SetAllHeaderPrimaryLengths(double length)
        {
            for (int i = 0; i < bank.GetCylinderCount(); ++i)
            {
                cylinders[i].headerPrimaryLength = length;
            }
        }

        public void SetHeaderPrimaryLength(int i, double length)
        {
            cylinders[i].headerPrimaryLength = length;
        }

        public bool GetFlipDisplay() => flipDisplay;
        public double GetCombustionChamberVolume() => combustionChamberVolume;
        public CylinderBank GetCylinderBank() => bank;

        public double GetIntakeRunnerVolume() => intakeRunnerVolume;
        public double GetIntakeRunnerCrossSectionArea() => intakeRunnerCrossSectionArea;
        public double GetExhaustRunnerVolume() => exhaustRunnerVolume;
        public double GetExhaustRunnerCrossSectionArea() => exhaustRunnerCrossSectionArea;

        public Camshaft GetExhaustCamshaft()
        {
            return valvetrain.GetActiveExhaustCamshaft();
        }

        public Camshaft GetIntakeCamshaft()
        {
            return valvetrain.GetActiveIntakeCamshaft();
        }

        // Read access for the dense core's snapshot.
        internal Valvetrain GetValvetrain() => valvetrain;
        internal Function GetIntakePortFlow() => intakePortFlow;
        internal Function GetExhaustPortFlow() => exhaustPortFlow;
    }
}
