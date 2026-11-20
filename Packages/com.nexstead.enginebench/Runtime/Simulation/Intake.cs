// Port of engine-sim include/intake.h and src/intake.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>The intake plenum: fed from the atmosphere through the throttle and the idle circuit.</summary>
    public sealed class Intake : Part
    {
        public sealed class Parameters
        {
            // Plenum volume
            public double volume;

            // Plenum dimensions
            public double CrossSectionArea;

            // Input flow constant
            public double InputFlowK;

            // Idle-circuit flow constant
            public double IdleFlowK;

            // Flow rate from plenum to runner
            public double RunnerFlowRate;

            // Molecular air fuel ratio (defaults to ideal for octane)
            public double MolecularAfr = (25.0 / 2.0);

            // Throttle plate position at idle
            public double IdleThrottlePlatePosition = 0.975;

            // Runner volume
            public double RunnerLength = Units.Distance(4.0, Units.inch);

            // Velocity decay factor
            public double VelocityDecay = 0.5;

            // The air the intake draws from: pressure and temperature (upstream PR #309, Alexey Egorov, as the community
            // fork merged it, 72a7683): altitude. A forced induction draws from it too, and its boost is measured against it.
            public double AtmospherePressure = Units.Pressure(1.0, Units.atm);
            public double AtmosphereTemperature = Units.Celcius(25.0);
        }

        public readonly GasSystem system = new GasSystem();
        public double throttle = 1.0;
        public double flow = 0;
        public double flowRate = 0;
        public double totalFuelInjected = 0;

        double crossSectionArea = 0.0;
        double inputFlowK = 0;
        double idleFlowK = 0;
        double runnerFlowRate;
        double molecularAfr = 0;
        double idleThrottlePlatePosition = 0.0;
        double runnerLength = 0;
        double velocityDecay;
        double atmospherePressure = Units.Pressure(1.0, Units.atm);
        double atmosphereTemperature = Units.Celcius(25.0);
        readonly GasSystem atmosphere = new GasSystem();
        ForcedInduction forcedInduction;

        public void Initialize(Parameters @params)
        {
            double width = Math.Sqrt(@params.CrossSectionArea);
            system.Initialize(
                Units.Pressure(1.0, Units.atm),
                @params.volume,
                Units.Celcius(25.0));
            system.SetGeometry(
                width,
                @params.volume / @params.CrossSectionArea,
                1.0,
                0.0);

            atmosphere.Initialize(
                @params.AtmospherePressure,
                Units.Volume(1000.0, Units.m3),
                @params.AtmosphereTemperature);
            atmosphere.SetGeometry(
                Units.Distance(100.0, Units.m),
                Units.Distance(100.0, Units.m),
                1.0,
                0.0);

            inputFlowK = @params.InputFlowK;
            molecularAfr = @params.MolecularAfr;
            idleFlowK = @params.IdleFlowK;
            idleThrottlePlatePosition = @params.IdleThrottlePlatePosition;
            runnerLength = @params.RunnerLength;
            crossSectionArea = @params.CrossSectionArea;
            velocityDecay = @params.VelocityDecay;
            runnerFlowRate = @params.RunnerFlowRate;
            atmospherePressure = @params.AtmospherePressure;
            atmosphereTemperature = @params.AtmosphereTemperature;
        }

        /// <summary>The mixture of air and fuel drawn through the throttle.</summary>
        public GasSystem.Mix GetFuelAirMix()
        {
            double ideal_afr = 0.8 * molecularAfr * 4;
            double p_air = ideal_afr / (1 + ideal_afr);

            GasSystem.Mix fuelAirMix = GasSystem.Mix.Default;
            fuelAirMix.p_fuel = 1 - p_air;
            fuelAirMix.p_inert = p_air * 0.75;
            fuelAirMix.p_o2 = p_air * 0.25;
            return fuelAirMix;
        }

        public void Process(double dt)
        {
            GasSystem.Mix fuelAirMix = GetFuelAirMix();

            const double idle_afr = 2.0;
            const double p_idle_air = idle_afr / (1 + idle_afr);
            GasSystem.Mix fuelMix = GasSystem.Mix.Default;
            fuelMix.p_fuel = (1.0 - p_idle_air);
            fuelMix.p_inert = p_idle_air * 0.75;
            fuelMix.p_o2 = p_idle_air * 0.25;

            double throttle = GetThrottlePlatePosition();
            double flowAttenuation = Math.Cos(throttle * Constants.pi / 2);

            GasSystem.FlowParameters flowParams = default;
            flowParams.crossSectionArea_0 = Units.Area(10, Units.m2);
            flowParams.crossSectionArea_1 = crossSectionArea;
            flowParams.direction_x = 0.0;
            flowParams.direction_y = -1.0;
            flowParams.dt = dt;

            double idleCircuitFlow;
            if (forcedInduction != null)
            {
                // Boosted (community fork, forced induction): the throttle and the idle circuit draw from the charge air,
                // which already carries the mixture
                flowParams.crossSectionArea_0 = crossSectionArea;
                flowParams.system_0 = forcedInduction.GetChargeSystem();
                flowParams.system_1 = system;
                flowParams.k_flow = flowAttenuation * inputFlowK;
                flow = GasSystem.Flow(flowParams);

                flowParams.k_flow = idleFlowK;
                idleCircuitFlow = GasSystem.Flow(flowParams);
                fuelMix = fuelAirMix;
            }
            else
            {
                atmosphere.Reset(atmospherePressure, atmosphereTemperature, fuelAirMix);
                flowParams.system_0 = atmosphere;
                flowParams.system_1 = system;
                flowParams.k_flow = flowAttenuation * inputFlowK;
                flow = GasSystem.Flow(flowParams);

                atmosphere.Reset(atmospherePressure, atmosphereTemperature, fuelMix);
                flowParams.system_0 = atmosphere;
                flowParams.system_1 = system;
                flowParams.k_flow = idleFlowK;
                idleCircuitFlow = GasSystem.Flow(flowParams);
            }

            system.DissipateExcessVelocity();
            system.UpdateVelocity(dt, velocityDecay);

            if (flow > 0)
            {
                totalFuelInjected += fuelAirMix.p_fuel * flow;
            }

            if (idleCircuitFlow > 0)
            {
                totalFuelInjected += fuelMix.p_fuel * idleCircuitFlow;
            }
        }

        public double GetRunnerFlowRate() => runnerFlowRate;
        public double GetThrottlePlatePosition() => idleThrottlePlatePosition * throttle;
        public double GetRunnerLength() => runnerLength;
        public double GetPlenumCrossSectionArea() => crossSectionArea;
        public double GetVelocityDecay() => velocityDecay;
        public double GetAtmospherePressure() => atmospherePressure;
        public double GetAtmosphereTemperature() => atmosphereTemperature;

        /// <summary>The compressor whose charge air the throttle draws from instead of the atmosphere; null if not boosted.</summary>
        public ForcedInduction GetForcedInduction() => forcedInduction;
        internal void SetForcedInduction(ForcedInduction forcedInduction) => this.forcedInduction = forcedInduction;

        // Read access for the dense core's snapshot.
        internal double GetInputFlowK() => inputFlowK;
        internal double GetIdleFlowK() => idleFlowK;
        internal double GetMolecularAfr() => molecularAfr;
        internal double GetIdleThrottlePlatePosition() => idleThrottlePlatePosition;
        internal GasSystem GetAtmosphere() => atmosphere;
    }
}
