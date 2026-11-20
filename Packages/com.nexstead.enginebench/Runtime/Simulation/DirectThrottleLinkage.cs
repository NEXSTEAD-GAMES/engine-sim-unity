// Port of engine-sim include/direct_throttle_linkage.h and src/direct_throttle_linkage.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class DirectThrottleLinkage : Throttle
    {
        public sealed class Parameters
        {
            public double gamma;
        }

        double gamma = 1.0;
        double throttlePosition = 1.0;

        public void Initialize(Parameters @params)
        {
            gamma = @params.gamma;
        }

        public override void SetSpeedControl(double s)
        {
            base.SetSpeedControl(s);

            throttlePosition = 1 - Math.Pow(s, gamma);
        }

        public override void Update(double dt, Engine engine)
        {
            base.Update(dt, engine);

            engine.SetThrottle(throttlePosition);
        }

        // Read access for the dense core's snapshot.
        internal double GetGamma() => gamma;
        internal double GetThrottlePosition() => throttlePosition;
    }
}
