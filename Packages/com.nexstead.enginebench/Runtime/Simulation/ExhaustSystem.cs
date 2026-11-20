// Port of engine-sim include/exhaust_system.h and src/exhaust_system.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>An exhaust collector venting to the atmosphere through its outlet.</summary>
    public sealed class ExhaustSystem : Part
    {
        public sealed class Parameters
        {
            public double length;
            public double collectorCrossSectionArea;
            public double outletFlowRate;
            public double primaryTubeLength;
            public double primaryFlowRate;
            public double velocityDecay;
            public double audioVolume;
            public ImpulseResponse impulseResponse;
        }

        readonly GasSystem atmosphere = new GasSystem();
        readonly GasSystem system = new GasSystem();

        ImpulseResponse impulseResponse;
        ForcedInduction forcedInduction;
        double length = 0;
        double primaryTubeLength = 0;
        double collectorCrossSectionArea = 0;
        double primaryFlowRate = 0;
        double outletFlowRate = 0;
        double audioVolume = 0;
        double velocityDecay = 0;

        // Set by Engine.Initialize (upstream: friend class Engine).
        internal int index = -1;

        double flow = 0;

        public void Initialize(Parameters @params)
        {
            double systemWidth = Math.Sqrt(@params.collectorCrossSectionArea);
            double volume = @params.collectorCrossSectionArea * @params.length;
            double systemLength = @params.length;
            system.Initialize(
                Units.Pressure(1.0, Units.atm),
                volume,
                Units.Celcius(25.0));
            system.SetGeometry(
                systemLength,
                systemWidth,
                1.0,
                0.0);

            atmosphere.Initialize(
                Units.Pressure(1.0, Units.atm),
                Units.Volume(1000.0, Units.m3),
                Units.Celcius(25.0));
            atmosphere.SetGeometry(
                Units.Distance(10.0, Units.m),
                Units.Distance(10.0, Units.m),
                1.0,
                0.0);

            primaryFlowRate = @params.primaryFlowRate;
            audioVolume = @params.audioVolume;
            outletFlowRate = @params.outletFlowRate;
            collectorCrossSectionArea = @params.collectorCrossSectionArea;
            velocityDecay = @params.velocityDecay;
            impulseResponse = @params.impulseResponse;
            length = @params.length;
            primaryTubeLength = @params.primaryTubeLength;
        }

        public void Process(double dt)
        {
            GasSystem.Mix airMix = GasSystem.Mix.Default;
            airMix.p_fuel = 0;
            airMix.p_inert = 1.0;
            airMix.p_o2 = 0.0;

            atmosphere.Reset(Units.Pressure(1.0, Units.atm), Units.Celcius(25.0), airMix);

            GasSystem.FlowParameters flowParams = default;
            flowParams.crossSectionArea_0 = collectorCrossSectionArea;
            flowParams.crossSectionArea_1 = Units.Area(10, Units.m2);
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.dt = dt;

            flowParams.system_0 = atmosphere;
            flowParams.system_1 = system;
            flowParams.k_flow = outletFlowRate;

            if (forcedInduction != null)
            {
                flowParams.k_flow = forcedInduction.GetExhaustOutletFlowK(outletFlowRate);

                double P = system.Pressure();
                double T = system.Temperature();
                double hcr = system.HeatCapacityRatio();
                flow = GasSystem.Flow(flowParams);

                // Gas leaving through the outlet drives the turbine
                if (flow < 0)
                {
                    forcedInduction.AddExhaustFlow(-flow, T, P, hcr, outletFlowRate);
                }
            }
            else
            {
                flow = GasSystem.Flow(flowParams);
            }

            system.DissipateExcessVelocity();
            system.UpdateVelocity(dt, velocityDecay);
        }

        public int GetIndex() => index;
        public double GetLength() => length;
        public double GetFlow() => flow;
        public double GetAudioVolume() => audioVolume;
        public double GetPrimaryFlowRate() => primaryFlowRate;
        public double GetCollectorCrossSectionArea() => collectorCrossSectionArea;
        public double GetPrimaryTubeLength() => primaryTubeLength;
        public double GetVelocityDecay() => velocityDecay;
        public ImpulseResponse GetImpulseResponse() => impulseResponse;

        /// <summary>The outlet flows through the turbine(s) of a turbocharger (community fork, forced induction).</summary>
        internal void SetForcedInduction(ForcedInduction forcedInduction) => this.forcedInduction = forcedInduction;
        internal ForcedInduction GetForcedInduction() => forcedInduction;
        public GasSystem GetSystem() => system;

        // Read access for the dense core's snapshot.
        internal GasSystem GetAtmosphere() => atmosphere;
        internal double GetOutletFlowRate() => outletFlowRate;
    }
}
