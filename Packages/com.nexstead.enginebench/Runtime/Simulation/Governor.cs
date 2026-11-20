// Port of engine-sim include/governor.h and src/governor.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>A throttle that holds a target speed between its minimum and maximum, as on small stationary engines.</summary>
    public sealed class Governor : Throttle
    {
        public sealed class Parameters
        {
            public double minSpeed;
            public double maxSpeed;
            public double minVelocity;
            public double maxVelocity;
            public double k_s;
            public double k_d;
            public double gamma;
        }

        double minSpeed = 0;
        double maxSpeed = 0;
        double minVelocity = 0.0;
        double maxVelocity = 0.0;
        double k_s = 0.0;
        double k_d = 0.0;
        double gamma = 1.0;

        double targetSpeed = 0;
        double currentThrottle = 1.0;
        double velocity = 0.0;

        public void Initialize(Parameters @params)
        {
            minSpeed = @params.minSpeed;
            maxSpeed = @params.maxSpeed;
            minVelocity = @params.minVelocity;
            maxVelocity = @params.maxVelocity;
            k_s = @params.k_s;
            k_d = @params.k_d;
            gamma = @params.gamma;
        }

        public override void SetSpeedControl(double s)
        {
            base.SetSpeedControl(s);

            targetSpeed = (1 - s) * minSpeed + s * maxSpeed;
        }

        public override void Update(double dt, Engine engine)
        {
            double currentSpeed = engine.GetSpeed();
            double ds = targetSpeed * targetSpeed - currentSpeed * currentSpeed;

            velocity += (dt * -ds * k_s - velocity * dt * k_d);
            velocity = Utilities.Clamp(velocity, minVelocity, maxVelocity);

            if (Math.Abs(currentSpeed) < Math.Abs(0.5 * minSpeed))
            {
                velocity = 0;
                currentThrottle = 1.0;
            }

            currentThrottle += velocity * dt;
            currentThrottle = Utilities.Clamp(currentThrottle);

            engine.SetThrottle(1 - Math.Pow(1 - currentThrottle, gamma));
        }

        // Read access for the dense core's snapshot.
        internal double GetMinSpeed() => minSpeed;
        internal double GetMaxSpeed() => maxSpeed;
        internal double GetMinVelocity() => minVelocity;
        internal double GetMaxVelocity() => maxVelocity;
        internal double GetKs() => k_s;
        internal double GetKd() => k_d;
        internal double GetGamma() => gamma;
        internal double GetTargetSpeed() => targetSpeed;
        internal double GetCurrentThrottle() => currentThrottle;
        internal double GetVelocity() => velocity;
    }
}
