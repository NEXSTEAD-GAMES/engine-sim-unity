// Port of engine-sim include/fuel.h and src/fuel.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public class Fuel
    {
        public sealed class Parameters
        {
            public string name = "Gasoline";
            public double molecularMass =
                Units.Mass(100.0, Units.g);
            public double energyDensity =
                Units.Energy(48.1, Units.kJ) / Units.Mass(1.0, Units.g);
            public double density =
                Units.Mass(0.755, Units.kg) / Units.Volume(1.0, Units.L);
            public double molecularAfr = 25 / 2.0;
            public double burningEfficiencyRandomness = 0.5;
            public double lowEfficiencyAttenuation = 0.6;
            public double maxBurningEfficiency = 0.8;
            public double maxTurbulenceEffect = 2.0;
            public double maxDilutionEffect = 50.0;
            public Function turbulenceToFlameSpeedRatio = null;
        }

        // Upstream also declares a name field that nothing sets or reads.
        protected double molecularMass;
        protected double energyDensity;
        protected double density;
        protected double molecularAfr;
        protected double maxBurningEfficiency;
        protected double burningEfficiencyRandomness;
        protected double lowEfficiencyAttenuation;
        protected double maxTurbulenceEffect;
        protected double maxDilutionEffect;
        protected Function turbulenceToFlameSpeedRatio;

        public void Initialize(Parameters @params)
        {
            molecularMass = @params.molecularMass;
            energyDensity = @params.energyDensity;
            density = @params.density;
            turbulenceToFlameSpeedRatio = @params.turbulenceToFlameSpeedRatio;
            molecularAfr = @params.molecularAfr;
            burningEfficiencyRandomness = @params.burningEfficiencyRandomness;
            maxBurningEfficiency = @params.maxBurningEfficiency;
            maxDilutionEffect = @params.maxDilutionEffect;
            maxTurbulenceEffect = @params.maxTurbulenceEffect;
            lowEfficiencyAttenuation = @params.lowEfficiencyAttenuation;
        }

        public double GetMolecularMass() => molecularMass;
        public double GetEnergyDensity() => energyDensity;
        public double GetDensity() => density;
        public double GetBurningEfficiencyRandomness() => burningEfficiencyRandomness;
        public double GetLowEfficiencyAttenuation() => lowEfficiencyAttenuation;
        public double GetMaxBurningEfficiency() => maxBurningEfficiency;
        public double GetMaxTurbulenceEffect() => maxTurbulenceEffect;
        public double GetMaxDilutionEffect() => maxDilutionEffect;
        public double GetMolecularAfr() => molecularAfr;

        public double FlameSpeed(
            double turbulence,
            double molecularAfr,
            double T,
            double P,
            double firingPressure,
            double motoringPressure)
        {
            double S_L = LaminarBurningVelocity(molecularAfr, T, P);
            const double p_adjustment = 1.0;

            return turbulenceToFlameSpeedRatio.SampleTriangle((turbulence / S_L) * p_adjustment) * S_L;
        }

        public virtual double LaminarBurningVelocity(double molecularAfr, double T, double P)
        {
            // Assuming fuel is gasoline
            const double er_m = 1.21;
            double B_m = Units.Distance(30.5, Units.cm) / Units.sec;
            double B_er = -Units.Distance(54.9, Units.cm) / Units.sec;

            double er = molecularAfr / this.molecularAfr;
            double alpha = 2.4 - 0.271 * Math.Pow(er, 3.51);
            double beta = -0.357 + 0.14 * Math.Pow(er, 2.77);
            double S_L_0 = B_m + B_er * (er - er_m) * (er - er_m);
            double T_ratio = T / Units.Kelvin(298);
            double P_ratio = P / Units.Pressure(1.0, Units.atm);

            return S_L_0 * Math.Pow(T_ratio, alpha) * Math.Pow(P_ratio, beta);
        }

        // Read access for the dense core's snapshot.
        internal Function GetTurbulenceToFlameSpeedRatio() => turbulenceToFlameSpeedRatio;
    }
}
