// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense core's gas systems: GasSystem's per-step methods over GasData, line for line.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    /// <summary>Mirror of <see cref="GasSystem"/>'s methods (the ones a step uses).</summary>
    public static unsafe class GasOps
    {
        /// <summary><see cref="GasSystem.Mix.Default"/>: all inert.</summary>
        public static MixData DefaultMix() => new MixData { p_fuel = 0.0, p_inert = 1.0, p_o2 = 0.0 };

        public static void Reset(GasData* g, double P, double T, MixData mix)
        {
            g->n_mol = P * Volume(g) / (Constants.R * T);
            g->E_k = T * (0.5 * g->degreesOfFreedom * g->n_mol * Constants.R);
            g->mix = mix;
            g->momentum_x = g->momentum_y = 0;
        }

        public static void SetVolume(GasData* g, double V)
        {
            ChangeVolume(g, V - g->V);
        }

        public static void ChangeVolume(GasData* g, double dV)
        {
            double V = Volume(g);
            double L = Math.Pow(V + dV, 1 / 3.0);
            double surfaceArea = (L * L);
            double dL = -dV / surfaceArea;
            double W = dL * Pressure(g) * surfaceArea;

            g->V += dV;
            g->E_k += W;
        }

        public static void ChangeEnergy(GasData* g, double dE)
        {
            g->E_k += dE;
        }

        public static double React(GasData* g, double n, MixData mix)
        {
            double l_n_fuel = mix.p_fuel * n;
            double l_n_o2 = mix.p_o2 * n;

            double system_n_fuel = N_fuel(g);
            double system_n_o2 = N_o2(g);
            double system_n_inert = N_inert(g);
            double system_n = N(g);

            // Assuming the following reaction:
            // 25[O2] + 2[C8H16] -> 16[CO2] + 18[H2O]
            const double ideal_o2_ratio = 25.0 / 2;
            const double ideal_fuel_ratio = 2.0 / 25;
            const double output_input_ratio = (16.0 + 18.0) / (25 + 2);

            double ideal_fuel_n = ideal_fuel_ratio * l_n_o2;
            double ideal_o2_n = ideal_o2_ratio * l_n_fuel;

            double a_n_fuel = Utilities.Fmin(
                Utilities.Fmin(system_n_fuel, l_n_fuel),
                ideal_fuel_n);
            double a_n_o2 = Utilities.Fmin(
                Utilities.Fmin(system_n_o2, l_n_o2),
                ideal_o2_n);

            double reactants_n = a_n_fuel + a_n_o2;
            double products_n = output_input_ratio * reactants_n;
            double dn = products_n - reactants_n;

            g->n_mol += dn;

            // Adjust mix
            double new_system_n_fuel = system_n_fuel - a_n_fuel;
            double new_system_n_o2 = system_n_o2 - a_n_o2;
            double new_system_n_inert = system_n_inert + products_n;
            double new_system_n = system_n + dn;

            if (new_system_n != 0)
            {
                g->mix.p_fuel = new_system_n_fuel / new_system_n;
                g->mix.p_inert = new_system_n_inert / new_system_n;
                g->mix.p_o2 = new_system_n_o2 / new_system_n;
            }
            else
            {
                g->mix.p_fuel = g->mix.p_inert = g->mix.p_o2 = 0;
            }

            return a_n_fuel;
        }

        public static double FlowRate(
            double k_flow,
            double P0,
            double P1,
            double T0,
            double T1,
            double hcr,
            double chokedFlowLimit,
            double chokedFlowRateCached)
        {
            if (k_flow == 0) return 0;

            double direction;
            double T_0;
            double p_0, p_T; // p_0 = upstream pressure
            if (P0 > P1)
            {
                direction = 1.0;
                T_0 = T0;
                p_0 = P0;
                p_T = P1;
            }
            else
            {
                direction = -1.0;
                T_0 = T1;
                p_0 = P1;
                p_T = P0;
            }

            double p_ratio = p_T / p_0;
            double flowRate = 0;
            if (p_ratio <= chokedFlowLimit)
            {
                // Choked flow
                flowRate = chokedFlowRateCached;
                flowRate /= Math.Sqrt(Constants.R * T_0);
            }
            else
            {
                double s = Math.Pow(p_ratio, 1 / hcr);

                flowRate = (2 * hcr) / (hcr - 1);
                flowRate *= s * (s - p_ratio);
                flowRate = Math.Sqrt(Utilities.Fmax(flowRate, 0.0) / (Constants.R * T_0));
            }

            flowRate *= direction * p_0;

            return flowRate * k_flow;
        }

        public static double LoseN(GasData* g, double dn, double E_k_per_mol)
        {
            g->E_k -= E_k_per_mol * dn;
            g->n_mol -= dn;

            if (g->n_mol < 0)
            {
                g->n_mol = 0;
            }

            return dn;
        }

        public static double GainN(GasData* g, double dn, double E_k_per_mol, MixData mix)
        {
            double next_n = g->n_mol + dn;
            double current_n = g->n_mol;

            g->E_k += dn * E_k_per_mol;
            g->n_mol = next_n;

            if (next_n != 0)
            {
                g->mix.p_fuel = (g->mix.p_fuel * current_n + dn * mix.p_fuel) / next_n;
                g->mix.p_inert = (g->mix.p_inert * current_n + dn * mix.p_inert) / next_n;
                g->mix.p_o2 = (g->mix.p_o2 * current_n + dn * mix.p_o2) / next_n;
            }
            else
            {
                g->mix.p_fuel = g->mix.p_inert = g->mix.p_o2 = 0;
            }

            return -dn;
        }

        public static void DissipateExcessVelocity(GasData* g)
        {
            double v_x = Velocity_x(g);
            double v_y = Velocity_y(g);
            double v_squared = v_x * v_x + v_y * v_y;
            double c = C(g);
            double c_squared = c * c;

            if (c_squared >= v_squared || v_squared == 0)
            {
                return;
            }

            double k_squared = c_squared / v_squared;
            double k = Math.Sqrt(k_squared);

            g->momentum_x *= k;
            g->momentum_y *= k;

            g->E_k += 0.5 * Mass(g) * (v_squared - c_squared);

            if (g->E_k < 0) g->E_k = 0;
        }

        public static void UpdateVelocity(GasData* g, double dt, double beta)
        {
            if (N(g) == 0) return;

            double width = g->width;
            double height = g->height;
            double dx = g->dx;
            double dy = g->dy;

            double depth = Volume(g) / (width * height);

            double d_momentum_x = 0;
            double d_momentum_y = 0;

            double p0 = DynamicPressure(g, dx, dy);
            double p1 = DynamicPressure(g, -dx, -dy);
            double p2 = DynamicPressure(g, dy, dx);
            double p3 = DynamicPressure(g, -dy, -dx);

            double p_sa_0 = p0 * (height * depth);
            double p_sa_1 = p1 * (height * depth);
            double p_sa_2 = p2 * (width * depth);
            double p_sa_3 = p3 * (width * depth);

            d_momentum_x += p_sa_0 * dx;
            d_momentum_y += p_sa_0 * dy;

            d_momentum_x -= p_sa_1 * dx;
            d_momentum_y -= p_sa_1 * dy;

            d_momentum_x += p_sa_2 * dy;
            d_momentum_y += p_sa_2 * dx;

            d_momentum_x -= p_sa_3 * dy;
            d_momentum_y -= p_sa_3 * dx;

            double m = Mass(g);
            double inv_m = 1 / m;
            double v0_x = g->momentum_x * inv_m;
            double v0_y = g->momentum_y * inv_m;

            g->momentum_x -= d_momentum_x * dt * beta;
            g->momentum_y -= d_momentum_y * dt * beta;

            double v1_x = g->momentum_x * inv_m;
            double v1_y = g->momentum_y * inv_m;

            g->E_k -= 0.5 * m * (v1_x * v1_x - v0_x * v0_x);
            g->E_k -= 0.5 * m * (v1_y * v1_y - v0_y * v0_y);

            if (g->E_k < 0) g->E_k = 0;
        }

        /// <summary>
        /// <see cref="GasSystem.Flow(in GasSystem.FlowParameters)"/>: moves gas between two systems for one time step and returns
        /// the amount moved (positive: system 0 to system 1).
        /// </summary>
        public static double Flow(FlowParams* @params)
        {
            GasData* source;
            GasData* sink;
            double sourcePressure, sinkPressure;
            double dx, dy;
            double sourceCrossSection, sinkCrossSection;
            double direction;

            double P_0 =
                Pressure(@params->system_0)
                + DynamicPressure(@params->system_0, @params->direction_x, @params->direction_y);
            double P_1 =
                Pressure(@params->system_1)
                + DynamicPressure(@params->system_1, -@params->direction_x, -@params->direction_y);

            if (P_0 > P_1)
            {
                dx = @params->direction_x;
                dy = @params->direction_y;
                source = @params->system_0;
                sink = @params->system_1;
                sourcePressure = P_0;
                sinkPressure = P_1;
                sourceCrossSection = @params->crossSectionArea_0;
                sinkCrossSection = @params->crossSectionArea_1;
                direction = 1.0;
            }
            else
            {
                dx = -@params->direction_x;
                dy = -@params->direction_y;
                source = @params->system_1;
                sink = @params->system_0;
                sourcePressure = P_1;
                sinkPressure = P_0;
                sourceCrossSection = @params->crossSectionArea_1;
                sinkCrossSection = @params->crossSectionArea_0;
                direction = -1.0;
            }

            double flow = @params->dt * FlowRate(
                @params->k_flow,
                sourcePressure,
                sinkPressure,
                Temperature(source),
                Temperature(sink),
                HeatCapacityRatio(source),
                source->chokedFlowLimit,
                source->chokedFlowFactorCached);

            flow = Utilities.Clamp(flow, 0.0, 0.9 * N(source));

            double fraction = flow / N(source);
            double fractionVolume = fraction * Volume(source);
            double fractionMass = fraction * Mass(source);

            if (flow != 0)
            {
                // - Stage 1
                // Fraction flows from source to sink.

                double E_k_bulk_src0 = BulkKineticEnergy(source);
                double E_k_bulk_sink0 = BulkKineticEnergy(sink);

                double E_k_per_mol = KineticEnergyPerMol(source);
                GainN(sink, flow, E_k_per_mol, source->mix);
                LoseN(source, flow, E_k_per_mol);

                double dp_x = source->momentum_x * fraction;
                double dp_y = source->momentum_y * fraction;
                source->momentum_x -= dp_x;
                source->momentum_y -= dp_y;

                sink->momentum_x += dp_x;
                sink->momentum_y += dp_y;

                double E_k_bulk_src1 = BulkKineticEnergy(source);
                double E_k_bulk_sink1 = BulkKineticEnergy(sink);

                sink->E_k -= ((E_k_bulk_src1 + E_k_bulk_sink1) - (E_k_bulk_src0 + E_k_bulk_sink0));
            }

            double sourceMass = Mass(source);
            double invSourceMass = 1 / sourceMass;
            double sinkMass = Mass(sink);
            double invSinkMass = 1 / sinkMass;

            double c_source = C(source);
            double c_sink = C(sink);

            double sourceInitialMomentum_x = source->momentum_x;
            double sourceInitialMomentum_y = source->momentum_y;

            double sinkInitialMomentum_x = sink->momentum_x;
            double sinkInitialMomentum_y = sink->momentum_y;

            // Momentum in fraction

            if (sinkCrossSection != 0)
            {
                double sinkFractionVelocity =
                    Utilities.Clamp((fractionVolume / sinkCrossSection) / @params->dt, 0.0, c_sink);
                double sinkFractionVelocity_x = sinkFractionVelocity * dx;
                double sinkFractionVelocity_y = sinkFractionVelocity * dy;
                double sinkFractionMomentum_x = sinkFractionVelocity_x * fractionMass;
                double sinkFractionMomentum_y = sinkFractionVelocity_y * fractionMass;

                sink->momentum_x += sinkFractionMomentum_x;
                sink->momentum_y += sinkFractionMomentum_y;
            }

            if (sourceCrossSection != 0 && sourceMass != 0)
            {
                double sourceFractionVelocity =
                    Utilities.Clamp((fractionVolume / sourceCrossSection) / @params->dt, 0.0, c_source);
                double sourceFractionVelocity_x = sourceFractionVelocity * dx;
                double sourceFractionVelocity_y = sourceFractionVelocity * dy;
                double sourceFractionMomentum_x = sourceFractionVelocity_x * fractionMass;
                double sourceFractionMomentum_y = sourceFractionVelocity_y * fractionMass;

                source->momentum_x += sourceFractionMomentum_x;
                source->momentum_y += sourceFractionMomentum_y;
            }

            if (sourceMass != 0)
            {
                // Energy conservation
                double sourceVelocity0_x = sourceInitialMomentum_x * invSourceMass;
                double sourceVelocity0_y = sourceInitialMomentum_y * invSourceMass;

                double sourceVelocity1_x = source->momentum_x * invSourceMass;
                double sourceVelocity1_y = source->momentum_y * invSourceMass;

                source->E_k -=
                    0.5 * sourceMass
                    * (sourceVelocity1_x * sourceVelocity1_x - sourceVelocity0_x * sourceVelocity0_x);

                source->E_k -=
                    0.5 * sourceMass
                    * (sourceVelocity1_y * sourceVelocity1_y - sourceVelocity0_y * sourceVelocity0_y);
            }

            if (sinkMass > 0)
            {
                double sinkVelocity0_x = sinkInitialMomentum_x * invSinkMass;
                double sinkVelocity0_y = sinkInitialMomentum_y * invSinkMass;

                double sinkVelocity1_x = sink->momentum_x * invSinkMass;
                double sinkVelocity1_y = sink->momentum_y * invSinkMass;

                sink->E_k -=
                    0.5 * sinkMass
                    * (sinkVelocity1_x * sinkVelocity1_x - sinkVelocity0_x * sinkVelocity0_x);

                sink->E_k -=
                    0.5 * sinkMass
                    * (sinkVelocity1_y * sinkVelocity1_y - sinkVelocity0_y * sinkVelocity0_y);
            }

            if (sink->E_k < 0)
            {
                sink->E_k = 0;
            }

            if (source->E_k < 0)
            {
                source->E_k = 0;
            }

            return flow * direction;
        }

        /// <summary><see cref="GasSystem.Inject"/>.</summary>
        public static void Inject(GasData* g, double n, double T, MixData mix)
        {
            if (n <= 0) return;

            double bulk_E_k_0 = BulkKineticEnergy(g);
            GainN(g, n, KineticEnergyPerMol(T, g->degreesOfFreedom), mix);
            g->E_k += (BulkKineticEnergy(g) - bulk_E_k_0);
        }

        /// <summary><see cref="GasSystem.Flow(double, double, double, double, GasSystem.Mix)"/>: flow to or from an environment at a fixed pressure and temperature.</summary>
        public static double FlowToEnvironment(GasData* g, double k_flow, double dt, double P_env, double T_env, MixData mix)
        {
            double maxFlow = PressureEquilibriumMaxFlow(g, P_env, T_env);
            double flow = dt * FlowRate(
                k_flow,
                Pressure(g),
                P_env,
                Temperature(g),
                T_env,
                HeatCapacityRatio(g),
                g->chokedFlowLimit,
                g->chokedFlowFactorCached);

            if (Math.Abs(flow) > Math.Abs(maxFlow))
            {
                flow = maxFlow;
            }

            if (flow < 0)
            {
                double bulk_E_k_0 = BulkKineticEnergy(g);
                GainN(g, -flow, KineticEnergyPerMol(T_env, g->degreesOfFreedom), mix);
                double bulk_E_k_1 = BulkKineticEnergy(g);

                g->E_k += (bulk_E_k_1 - bulk_E_k_0);
            }
            else
            {
                double starting_n = N(g);
                LoseN(g, flow, KineticEnergyPerMol(g));

                g->momentum_x -= (flow / starting_n) * g->momentum_x;
                g->momentum_y -= (flow / starting_n) * g->momentum_y;
            }

            return flow;
        }

        public static double PressureEquilibriumMaxFlow(GasData* g, double P_env, double T_env)
        {
            if (Pressure(g) > P_env)
            {
                return -(P_env * (0.5 * g->degreesOfFreedom * Volume(g)) - KineticEnergy(g)) / KineticEnergyPerMol(g);
            }
            else
            {
                double E_k_per_mol_env = 0.5 * T_env * Constants.R * g->degreesOfFreedom;
                return -(P_env * (0.5 * g->degreesOfFreedom * Volume(g)) - KineticEnergy(g)) / E_k_per_mol_env;
            }
        }

        // ---- Inline accessors (gas_system.h) ----

        public static double KineticEnergyPerMol(double T, int degreesOfFreedom)
        {
            return 0.5 * T * Constants.R * degreesOfFreedom;
        }

        public static double HeatCapacityRatio(int degreesOfFreedom)
        {
            return 1.0 + (2.0 / degreesOfFreedom);
        }

        public static double ApproximateDensity(GasData* g)
        {
            return (Units.AirMolecularMass * N(g)) / Volume(g);
        }

        public static double N(GasData* g)
        {
            return g->n_mol;
        }

        public static double KineticEnergy(GasData* g)
        {
            return g->E_k;
        }

        public static double KineticEnergy(GasData* g, double n)
        {
            return (KineticEnergy(g) / N(g)) * n;
        }

        public static double KineticEnergyPerMol(GasData* g) => KineticEnergy(g, 1.0);

        public static double BulkKineticEnergy(GasData* g)
        {
            double m = Mass(g);
            if (m == 0) return 0;

            double v_x = g->momentum_x / m;
            double v_y = g->momentum_y / m;
            double v_squared = v_x * v_x + v_y * v_y;
            return 0.5 * m * v_squared;
        }

        public static double C(GasData* g)
        {
            if (N(g) == 0 || KineticEnergy(g) == 0) return 0;

            double hcr = HeatCapacityRatio(g);
            double staticPressure = Pressure(g);
            double density = ApproximateDensity(g);
            double c = Math.Sqrt(staticPressure * hcr / density);

            return c;
        }

        public static double DynamicPressure(GasData* g, double dx, double dy)
        {
            if (N(g) == 0 || KineticEnergy(g) == 0) return 0;

            double inverseMass = 1 / Mass(g);
            double v = inverseMass * (dx * g->momentum_x + dy * g->momentum_y);

            if (v <= 0)
            {
                return 0;
            }

            double hcr = HeatCapacityRatio(g);
            double staticPressure = Pressure(g);
            double density = ApproximateDensity(g);
            double c_squared = staticPressure * hcr / density;
            double machNumber_squared = v * v / c_squared;

            // Below is equivalent to:
            // staticPressure * pow(1 + ((hcr - 1) / 2) * machNumber * machNumber, hcr / (hcr - 1)) - 1)

            double x = 1 + ((hcr - 1) / 2) * machNumber_squared;
            double x_d;
            switch (g->degreesOfFreedom)
            {
                case 3:
                    x_d = x * x * x * x * x;
                    break;
                case 5:
                {
                    double x_2 = x * x;
                    double x_3 = x_2 * x;
                    x_d = x_3 * x_3 * x;
                    break;
                }
                default:
                    x_d = x;
                    break;
            }

            return staticPressure * (Math.Sqrt(x_d) - 1);
        }

        public static double Mass(GasData* g)
        {
            return Units.AirMolecularMass * N(g);
        }

        public static double Pressure(GasData* g)
        {
            double volume = Volume(g);
            return (volume != 0)
                ? KineticEnergy(g) / (0.5 * g->degreesOfFreedom * volume)
                : 0;
        }

        public static double Temperature(GasData* g)
        {
            if (N(g) == 0) return 0;
            else return KineticEnergy(g) / (0.5 * g->degreesOfFreedom * N(g) * Constants.R);
        }

        public static double Velocity_x(GasData* g)
        {
            if (N(g) == 0) return 0;
            else return g->momentum_x / Mass(g);
        }

        public static double Velocity_y(GasData* g)
        {
            if (N(g) == 0) return 0;
            else return g->momentum_y / Mass(g);
        }

        public static double Volume(GasData* g)
        {
            return g->V;
        }

        public static double N_fuel(GasData* g)
        {
            return g->mix.p_fuel * N(g);
        }

        public static double N_inert(GasData* g)
        {
            return g->mix.p_inert * N(g);
        }

        public static double N_o2(GasData* g)
        {
            return g->mix.p_o2 * N(g);
        }

        public static double HeatCapacityRatio(GasData* g)
        {
            return HeatCapacityRatio(g->degreesOfFreedom);
        }
    }
}
