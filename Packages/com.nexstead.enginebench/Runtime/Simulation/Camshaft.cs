// Port of engine-sim include/camshaft.h and src/camshaft.cpp, with the camshaft reduction ratio of
// upstream PR #309 (Alexey Egorov), as the community fork merged it (72a7683).
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class Camshaft : Part
    {
        public sealed class Parameters
        {
            // Number of lobes
            public int lobes;

            // Camshaft advance in camshaft degrees
            public double advance = 0;

            // Camshaft turns per crankshaft turn: 0.5 for a four-stroke, 1 for a two-stroke
            public double reductionRatio = 0.5;

            // Corresponding crankshaft
            public Crankshaft crankshaft;

            // Lobe profile
            public Function lobeProfile;

            // Base radius
            public double baseRadius = Units.Distance(600, Units.thou);
        }

        Crankshaft crankshaft;
        Function lobeProfile;
        double[] lobeAngles;
        double advance = 0;
        double baseRadius = 0;
        double reductionRatio = 0.5;
        int lobes = 0;

        public void Initialize(Parameters @params)
        {
            lobeAngles = new double[@params.lobes];

            lobes = @params.lobes;
            crankshaft = @params.crankshaft;
            lobeProfile = @params.lobeProfile;
            advance = @params.advance;
            baseRadius = @params.baseRadius;
            reductionRatio = @params.reductionRatio;
        }

        public double ValveLift(int lobe)
        {
            return SampleLobe(GetAngle() + lobeAngles[lobe]);
        }

        public double SampleLobe(double theta)
        {
            double clampedTheta = theta % (2 * Constants.pi);
            if (clampedTheta < 0) clampedTheta += 2 * Constants.pi;
            if (clampedTheta >= Constants.pi) clampedTheta -= 2 * Constants.pi;

            return lobeProfile.SampleTriangle(clampedTheta);
        }

        public void SetLobeCenterline(int lobe, double crankAngle) { lobeAngles[lobe] = crankAngle / 2; }
        public double GetLobeCenterline(int lobe) => lobeAngles[lobe];

        public double GetAngle()
        {
            double angle =
                ((crankshaft.GetAngle() + advance) * reductionRatio) % (2 * Constants.pi);

            return (angle < 0)
                ? angle + 2 * Constants.pi
                : angle;
        }

        public Function GetLobeProfile() => lobeProfile;
        public double GetAdvance() => advance;
        public double GetBaseRadius() => baseRadius;
        public double GetReductionRatio() => reductionRatio;

        // Read access for the dense core's snapshot.
        internal Crankshaft GetCrankshaft() => crankshaft;
        internal int GetLobeCount() => lobes;
    }
}
