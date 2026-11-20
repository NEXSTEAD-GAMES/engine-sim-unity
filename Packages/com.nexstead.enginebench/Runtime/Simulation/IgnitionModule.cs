// Port of engine-sim include/ignition_module.h and src/ignition_module.cpp, with the community fork's
// fixes: sparks kept when the crank angle wraps (Samuel, 562fa44) and sparks per cycle
// (karpuzikov, a3c1da4).
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>Fires each spark plug as the crankshaft passes its angle less the timing advance; cuts sparks over the rev limit.</summary>
    public sealed class IgnitionModule : Part
    {
        public sealed class Parameters
        {
            public int cylinderCount;
            public Crankshaft crankshaft;
            public Function timingCurve;
            public double revLimit = Units.Rpm(6000.0);
            public double limiterDuration = 0.5 * Units.sec;
            /// <summary>1 for a four-stroke; 2 fires each plug every revolution (a two-stroke, or wasted spark).</summary>
            public double sparksPerCycle = 1;
        }

        public struct SparkPlug
        {
            public double angle;
            public bool ignitionEvent;
            public bool enabled;
        }

        public bool enabled = false;

        Function timingCurve;
        SparkPlug[] plugs;
        Crankshaft crankshaft;
        int cylinderCount = 0;

        double lastCrankshaftAngle = 0.0;
        double revLimit = 0;
        double revLimitTimer = 0.0;
        double limiterDuration = 0;
        int sparksPerCycle = 1;

        public void Initialize(Parameters @params)
        {
            cylinderCount = @params.cylinderCount;
            plugs = new SparkPlug[cylinderCount];
            crankshaft = @params.crankshaft;
            timingCurve = @params.timingCurve;
            revLimit = @params.revLimit;
            limiterDuration = @params.limiterDuration;
            sparksPerCycle = Math.Max(1, (int)@params.sparksPerCycle);
        }

        public void SetFiringOrder(int cylinderIndex, double angle)
        {
            plugs[cylinderIndex].angle = angle;
            plugs[cylinderIndex].enabled = true;
        }

        public void Reset()
        {
            lastCrankshaftAngle = crankshaft.GetCycleAngle();
            ResetIgnitionEvents();
        }

        public void Update(double dt)
        {
            double cycleAngle = crankshaft.GetCycleAngle();

            if (enabled && revLimitTimer == 0)
            {
                double fourPi = 4 * Constants.pi;
                double advance = GetTimingAdvance();

                for (int spark = 0; spark < sparksPerCycle * cylinderCount; ++spark)
                {
                    int i = spark % cylinderCount;
                    double repeat = (spark / cylinderCount) * fourPi / sparksPerCycle;
                    double adjustedAngle = Utilities.PositiveMod(plugs[i].angle + repeat - advance, fourPi);
                    double r0 = lastCrankshaftAngle;
                    double r1 = cycleAngle;

                    if (crankshaft.body.v_theta < 0)
                    {
                        if (r1 < r0)
                        {
                            r1 += fourPi;
                            // Only an event after the wrap moves into the next cycle.
                            if (adjustedAngle < r0) adjustedAngle += fourPi;
                        }

                        if (adjustedAngle >= r0 && adjustedAngle < r1)
                        {
                            plugs[i].ignitionEvent = plugs[i].enabled;
                        }
                    }
                    else
                    {
                        if (r1 > r0)
                        {
                            r1 -= fourPi;
                            if (adjustedAngle >= r0) adjustedAngle -= fourPi;
                        }

                        if (adjustedAngle >= r1 && adjustedAngle < r0)
                        {
                            plugs[i].ignitionEvent = plugs[i].enabled;
                        }
                    }
                }
            }

            revLimitTimer -= dt;
            if (Math.Abs(crankshaft.body.v_theta) > revLimit)
            {
                revLimitTimer = limiterDuration;
            }

            if (revLimitTimer < 0)
            {
                revLimitTimer = 0;
            }

            lastCrankshaftAngle = cycleAngle;
        }

        public bool GetIgnitionEvent(int index) => plugs[index].ignitionEvent;

        public void ResetIgnitionEvents()
        {
            for (int i = 0; i < cylinderCount; ++i)
            {
                plugs[i].ignitionEvent = false;
            }
        }

        public double GetTimingAdvance()
        {
            return timingCurve.SampleTriangle(-crankshaft.body.v_theta);
        }

        // Read access for the dense core's snapshot.
        internal Function GetTimingCurve() => timingCurve;
        internal SparkPlug GetPlug(int i) => plugs[i];
        internal Crankshaft GetCrankshaft() => crankshaft;
        internal int GetCylinderCount() => cylinderCount;
        internal double GetLastCrankshaftAngle() => lastCrankshaftAngle;
        internal double GetRevLimit() => revLimit;
        internal double GetRevLimitTimer() => revLimitTimer;
        internal double GetLimiterDuration() => limiterDuration;
        internal int GetSparksPerCycle() => sparksPerCycle;

        // Upstream's protected getPlug(i) has no caller and is not ported.
    }
}
