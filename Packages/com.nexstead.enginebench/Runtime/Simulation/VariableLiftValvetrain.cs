// Port of engine-sim's two-profile valvetrain, under a generic name,
// with the community fork's fix that switches a valve's cam only while it is closed (sillysigmabollocks, 83a121c).
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>Variable valve lift: switches to the high-lift camshafts above a speed, a manifold pressure and a throttle opening.</summary>
    public sealed class VariableLiftValvetrain : Valvetrain
    {
        public sealed class Parameters
        {
            public double minRpm;
            public double minSpeed;
            public double manifoldVacuum;
            public double minThrottlePosition;

            public Camshaft intakeCamshaft;
            public Camshaft exhaustCamshaft;

            public Camshaft highLiftIntakeCamshaft;
            public Camshaft highLiftExhaustCamshaft;

            public Engine engine;
        }

        Camshaft intakeCamshaft;
        Camshaft exhaustCamshaft;

        Camshaft highLiftIntakeCamshaft;
        Camshaft highLiftExhaustCamshaft;

        Engine engine;

        double minRpm = 0.0;
        double minSpeed = 0.0;
        double manifoldVacuum = 0.0;
        double minThrottlePosition = 0.0;

        // Whether each cylinder's intake and exhaust valve follows its high-lift cam.
        bool[] intakeOnHighLift;
        bool[] exhaustOnHighLift;

        public void Initialize(Parameters parameters)
        {
            intakeCamshaft = parameters.intakeCamshaft;
            exhaustCamshaft = parameters.exhaustCamshaft;
            highLiftIntakeCamshaft = parameters.highLiftIntakeCamshaft;
            highLiftExhaustCamshaft = parameters.highLiftExhaustCamshaft;
            minRpm = parameters.minRpm;
            minSpeed = parameters.minSpeed;
            minThrottlePosition = parameters.minThrottlePosition;
            manifoldVacuum = parameters.manifoldVacuum;
            engine = parameters.engine;

            intakeOnHighLift = new bool[intakeCamshaft.GetLobeCount()];
            exhaustOnHighLift = new bool[exhaustCamshaft.GetLobeCount()];
        }

        public override double IntakeValveLift(int cylinder)
        {
            return FollowedCamshaft(cylinder, intakeOnHighLift, intakeCamshaft, highLiftIntakeCamshaft).ValveLift(cylinder);
        }

        public override double ExhaustValveLift(int cylinder)
        {
            return FollowedCamshaft(cylinder, exhaustOnHighLift, exhaustCamshaft, highLiftExhaustCamshaft).ValveLift(cylinder);
        }

        /// <summary>
        /// The cam a cylinder's valve follows. It changes only while the valve is closed on both cams, so the lift never jumps
        /// in the middle of an opening.
        /// </summary>
        Camshaft FollowedCamshaft(int cylinder, bool[] onHighLift, Camshaft normal, Camshaft highLift)
        {
            // Lobe profiles end on a sampled value rather than exactly zero, so anything below valve lash counts as closed.
            double closedLift = Units.Distance(0.01, Units.mm);

            bool requested = IsHighLiftEngaged();
            if (onHighLift[cylinder] != requested
                && normal.ValveLift(cylinder) < closedLift
                && highLift.ValveLift(cylinder) < closedLift)
            {
                onHighLift[cylinder] = requested;
            }

            return onHighLift[cylinder] ? highLift : normal;
        }

        public override Camshaft GetActiveIntakeCamshaft()
        {
            return IsHighLiftEngaged()
                ? highLiftIntakeCamshaft
                : intakeCamshaft;
        }

        public override Camshaft GetActiveExhaustCamshaft()
        {
            return IsHighLiftEngaged()
                ? highLiftExhaustCamshaft
                : exhaustCamshaft;
        }

        // minRpm holds rad/s: scripts give it through units.rpm, and it is compared with the engine speed.
        bool IsHighLiftEngaged()
        {
            return
                engine.GetManifoldPressure() > manifoldVacuum
                && engine.GetSpeed() > minRpm
                && (1 - engine.GetThrottle()) > minThrottlePosition;
        }

        // Read access for the dense core's snapshot.
        internal Camshaft GetIntakeCamshaft() => intakeCamshaft;
        internal Camshaft GetExhaustCamshaft() => exhaustCamshaft;
        internal Camshaft GetHighLiftIntakeCamshaft() => highLiftIntakeCamshaft;
        internal Camshaft GetHighLiftExhaustCamshaft() => highLiftExhaustCamshaft;
        internal Engine GetEngine() => engine;
        internal double GetMinRpm() => minRpm;
        internal double GetManifoldVacuum() => manifoldVacuum;
        internal double GetMinThrottlePosition() => minThrottlePosition;
        internal bool[] IntakeOnHighLift => intakeOnHighLift;
        internal bool[] ExhaustOnHighLift => exhaustOnHighLift;
    }
}
