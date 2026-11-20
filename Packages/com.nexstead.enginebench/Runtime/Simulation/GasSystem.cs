// Port of engine-sim include/gas_system.h and src/gas_system.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>A lumped volume of gas: amount, kinetic energy, volume, bulk momentum and mix.</summary>
    public sealed class GasSystem
    {
        public struct Mix
        {
            public double p_fuel;
            public double p_inert;
            public double p_o2;

            /// <summary>Upstream's default-constructed mix: all inert.</summary>
            public static readonly Mix Default = new Mix { p_fuel = 0.0, p_inert = 1.0, p_o2 = 0.0 };
        }

        /// <summary>Upstream holds this by value inside the gas system and never copies it, so a class serves.</summary>
        public sealed class State
        {
            public double n_mol = 0.0;
            public double E_k = 0.0;
            public double V = 0.0;
            public readonly double[] momentum = { 0.0, 0.0 };

            public Mix mix = Mix.Default;
        }

        /// <summary>Built on the caller's stack for each flow; passed by reference.</summary>
        public struct FlowParameters
        {
            public double k_flow;
            public double dt;
            public double direction_x, direction_y;
            public double crossSectionArea_0, crossSectionArea_1;
            public GasSystem system_0, system_1;
        }

        readonly State state = new State();

        int degreesOfFreedom = 5;

        double chokedFlowLimit = 0;
        double chokedFlowFactorCached = 0;

        double width = 0.0;
        double height = 0.0;
        double dx = 0.0;
        double dy = 0.0;

        public void SetGeometry(double width, double height, double dx, double dy)
        {
            this.width = width;
            this.height = height;
            this.dx = dx;
            this.dy = dy;
        }

        public void Initialize(double P, double V, double T) => Initialize(P, V, T, Mix.Default);

        public void Initialize(double P, double V, double T, Mix mix, int degreesOfFreedom = 5)
        {
            this.degreesOfFreedom = degreesOfFreedom;
            state.n_mol = P * V / (Constants.R * T);
            state.V = V;
            state.E_k = T * (0.5 * degreesOfFreedom * state.n_mol * Constants.R);
            state.mix = mix;
            state.momentum[0] = state.momentum[1] = 0;

            chokedFlowLimit = ChokedFlowLimit(degreesOfFreedom);
            chokedFlowFactorCached = ChokedFlowRate(degreesOfFreedom);
        }

        public void Reset(double P, double T) => Reset(P, T, Mix.Default);

        public void Reset(double P, double T, Mix mix)
        {
            state.n_mol = P * Volume() / (Constants.R * T);
            state.E_k = T * (0.5 * degreesOfFreedom * state.n_mol * Constants.R);
            state.mix = mix;
            state.momentum[0] = state.momentum[1] = 0;
        }

        public void SetVolume(double V)
        {
            ChangeVolume(V - state.V);
        }

        public void SetN(double n)
        {
            state.E_k = KineticEnergy(n);
            state.n_mol = n;
        }

        public void ChangeVolume(double dV)
        {
            double V = Volume();
            double L = Math.Pow(V + dV, 1 / 3.0);
            double surfaceArea = (L * L);
            double dL = -dV / surfaceArea;
            double W = dL * Pressure() * surfaceArea;

            state.V += dV;
            state.E_k += W;
        }

        public void ChangePressure(double dP)
        {
            state.E_k += dP * Volume() * degreesOfFreedom * 0.5;
        }

        public void ChangeTemperature(double dT)
        {
            state.E_k += dT * 0.5 * degreesOfFreedom * N() * Constants.R;
        }

        public void ChangeTemperature(double dT, double n)
        {
            state.E_k += dT * 0.5 * degreesOfFreedom * n * Constants.R;
        }

        public void ChangeEnergy(double dE)
        {
            state.E_k += dE;
        }

        public void ChangeMix(Mix mix)
        {
            state.mix = mix;
        }

        public void InjectFuel(double n)
        {
            double n_fuel = N_fuel() + n;
            double p_fuel = n_fuel / N();
            state.mix.p_fuel = p_fuel;
        }

        public double React(double n, Mix mix)
        {
            double l_n_fuel = mix.p_fuel * n;
            double l_n_o2 = mix.p_o2 * n;

            double system_n_fuel = N_fuel();
            double system_n_o2 = N_o2();
            double system_n_inert = N_inert();
            double system_n = N();

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

            state.n_mol += dn;

            // Adjust mix
            double new_system_n_fuel = system_n_fuel - a_n_fuel;
            double new_system_n_o2 = system_n_o2 - a_n_o2;
            double new_system_n_inert = system_n_inert + products_n;
            double new_system_n = system_n + dn;

            if (new_system_n != 0)
            {
                state.mix.p_fuel = new_system_n_fuel / new_system_n;
                state.mix.p_inert = new_system_n_inert / new_system_n;
                state.mix.p_o2 = new_system_n_o2 / new_system_n;
            }
            else
            {
                state.mix.p_fuel = state.mix.p_inert = state.mix.p_o2 = 0;
            }

            return a_n_fuel;
        }

        public static double FlowConstant(
            double targetFlowRate,
            double P,
            double pressureDrop,
            double T,
            double hcr)
        {
            double T_0 = T;
            double p_0 = P, p_T = P - pressureDrop; // p_0 = upstream pressure

            double chokedFlowLimit =
                Math.Pow((2.0 / (hcr + 1)), hcr / (hcr - 1));
            double p_ratio = p_T / p_0;

            double flowRate = 0;
            if (p_ratio <= chokedFlowLimit)
            {
                // Choked flow
                flowRate = Math.Sqrt(hcr);
                flowRate *= Math.Pow(2 / (hcr + 1), (hcr + 1) / (2 * (hcr - 1)));
            }
            else
            {
                flowRate = (2 * hcr) / (hcr - 1);
                flowRate *= (1 - Math.Pow(p_ratio, (hcr - 1) / hcr));
                flowRate = Math.Sqrt(flowRate);
                flowRate *= Math.Pow(p_ratio, 1 / hcr);
            }

            flowRate *= p_0 / Math.Sqrt(Constants.R * T_0);

            return targetFlowRate / flowRate;
        }

        public static double K_28inH2O(double flowRateScfm)
        {
            return FlowConstant(
                Units.Flow(flowRateScfm, Units.scfm),
                Units.Pressure(1.0, Units.atm),
                Units.Pressure(28.0, Units.inH2O),
                Units.Celcius(25),
                HeatCapacityRatio(5));
        }

        public static double K_carb(double flowRateScfm)
        {
            return FlowConstant(
                Units.Flow(flowRateScfm, Units.scfm),
                Units.Pressure(1.0, Units.atm),
                Units.Pressure(1.5, Units.inHg),
                Units.Celcius(25),
                HeatCapacityRatio(5));
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

        public double LoseN(double dn, double E_k_per_mol)
        {
            state.E_k -= E_k_per_mol * dn;
            state.n_mol -= dn;

            if (state.n_mol < 0)
            {
                state.n_mol = 0;
            }

            return dn;
        }

        public double GainN(double dn, double E_k_per_mol) => GainN(dn, E_k_per_mol, Mix.Default);

        public double GainN(double dn, double E_k_per_mol, Mix mix)
        {
            double next_n = state.n_mol + dn;
            double current_n = state.n_mol;

            state.E_k += dn * E_k_per_mol;
            state.n_mol = next_n;

            if (next_n != 0)
            {
                state.mix.p_fuel = (state.mix.p_fuel * current_n + dn * mix.p_fuel) / next_n;
                state.mix.p_inert = (state.mix.p_inert * current_n + dn * mix.p_inert) / next_n;
                state.mix.p_o2 = (state.mix.p_o2 * current_n + dn * mix.p_o2) / next_n;
            }
            else
            {
                state.mix.p_fuel = state.mix.p_inert = state.mix.p_o2 = 0;
            }

            return -dn;
        }

        public void DissipateExcessVelocity()
        {
            double v_x = Velocity_x();
            double v_y = Velocity_y();
            double v_squared = v_x * v_x + v_y * v_y;
            double c = C();
            double c_squared = c * c;

            if (c_squared >= v_squared || v_squared == 0)
            {
                return;
            }

            double k_squared = c_squared / v_squared;
            double k = Math.Sqrt(k_squared);

            state.momentum[0] *= k;
            state.momentum[1] *= k;

            state.E_k += 0.5 * Mass() * (v_squared - c_squared);

            if (state.E_k < 0) state.E_k = 0;
        }

        public void UpdateVelocity(double dt, double beta = 1.0)
        {
            if (N() == 0) return;

            double depth = Volume() / (width * height);

            double d_momentum_x = 0;
            double d_momentum_y = 0;

            double p0 = DynamicPressure(dx, dy);
            double p1 = DynamicPressure(-dx, -dy);
            double p2 = DynamicPressure(dy, dx);
            double p3 = DynamicPressure(-dy, -dx);

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

            double m = Mass();
            double inv_m = 1 / m;
            double v0_x = state.momentum[0] * inv_m;
            double v0_y = state.momentum[1] * inv_m;

            state.momentum[0] -= d_momentum_x * dt * beta;
            state.momentum[1] -= d_momentum_y * dt * beta;

            double v1_x = state.momentum[0] * inv_m;
            double v1_y = state.momentum[1] * inv_m;

            state.E_k -= 0.5 * m * (v1_x * v1_x - v0_x * v0_x);
            state.E_k -= 0.5 * m * (v1_y * v1_y - v0_y * v0_y);

            if (state.E_k < 0) state.E_k = 0;
        }

        public void DissipateVelocity(double dt, double timeConstant)
        {
            if (N() == 0) return;

            double invMass = 1.0 / Mass();
            double velocity_x = state.momentum[0] * invMass;
            double velocity_y = state.momentum[1] * invMass;
            double velocity_squared =
                velocity_x * velocity_x + velocity_y * velocity_y;

            double s = dt / (dt + timeConstant);
            state.momentum[0] = state.momentum[0] * (1 - s);
            state.momentum[1] = state.momentum[1] * (1 - s);

            double newVelocity_x = state.momentum[0] * invMass;
            double newVelocity_y = state.momentum[1] * invMass;
            double newVelocity_squared =
                newVelocity_x * newVelocity_x + newVelocity_y * newVelocity_y;

            double dE_k = 0.5 * Mass() * (velocity_squared - newVelocity_squared);
            state.E_k += dE_k;
        }

        /// <summary>
        /// Moves gas between two systems for one time step and returns the amount moved (positive: system 0 to system 1).
        /// Upstream also computes the source's pressure-equilibrium limit here and discards it; that pure call is left out.
        /// </summary>
        public static double Flow(in FlowParameters @params)
        {
            GasSystem source, sink;
            double sourcePressure, sinkPressure;
            double dx, dy;
            double sourceCrossSection, sinkCrossSection;
            double direction;

            double P_0 =
                @params.system_0.Pressure()
                + @params.system_0.DynamicPressure(@params.direction_x, @params.direction_y);
            double P_1 =
                @params.system_1.Pressure()
                + @params.system_1.DynamicPressure(-@params.direction_x, -@params.direction_y);

            if (P_0 > P_1)
            {
                dx = @params.direction_x;
                dy = @params.direction_y;
                source = @params.system_0;
                sink = @params.system_1;
                sourcePressure = P_0;
                sinkPressure = P_1;
                sourceCrossSection = @params.crossSectionArea_0;
                sinkCrossSection = @params.crossSectionArea_1;
                direction = 1.0;
            }
            else
            {
                dx = -@params.direction_x;
                dy = -@params.direction_y;
                source = @params.system_1;
                sink = @params.system_0;
                sourcePressure = P_1;
                sinkPressure = P_0;
                sourceCrossSection = @params.crossSectionArea_1;
                sinkCrossSection = @params.crossSectionArea_0;
                direction = -1.0;
            }

            double flow = @params.dt * FlowRate(
                @params.k_flow,
                sourcePressure,
                sinkPressure,
                source.Temperature(),
                sink.Temperature(),
                source.HeatCapacityRatio(),
                source.chokedFlowLimit,
                source.chokedFlowFactorCached);

            flow = Utilities.Clamp(flow, 0.0, 0.9 * source.N());

            double fraction = flow / source.N();
            double fractionVolume = fraction * source.Volume();
            double fractionMass = fraction * source.Mass();

            if (flow != 0)
            {
                // - Stage 1
                // Fraction flows from source to sink.

                double E_k_bulk_src0 = source.BulkKineticEnergy();
                double E_k_bulk_sink0 = sink.BulkKineticEnergy();

                double E_k_per_mol = source.KineticEnergyPerMol();
                sink.GainN(flow, E_k_per_mol, source.GetMix());
                source.LoseN(flow, E_k_per_mol);

                double dp_x = source.state.momentum[0] * fraction;
                double dp_y = source.state.momentum[1] * fraction;
                source.state.momentum[0] -= dp_x;
                source.state.momentum[1] -= dp_y;

                sink.state.momentum[0] += dp_x;
                sink.state.momentum[1] += dp_y;

                double E_k_bulk_src1 = source.BulkKineticEnergy();
                double E_k_bulk_sink1 = sink.BulkKineticEnergy();

                sink.state.E_k -= ((E_k_bulk_src1 + E_k_bulk_sink1) - (E_k_bulk_src0 + E_k_bulk_sink0));
            }

            double sourceMass = source.Mass();
            double invSourceMass = 1 / sourceMass;
            double sinkMass = sink.Mass();
            double invSinkMass = 1 / sinkMass;

            double c_source = source.C();
            double c_sink = sink.C();

            double sourceInitialMomentum_x = source.state.momentum[0];
            double sourceInitialMomentum_y = source.state.momentum[1];

            double sinkInitialMomentum_x = sink.state.momentum[0];
            double sinkInitialMomentum_y = sink.state.momentum[1];

            // Momentum in fraction

            if (sinkCrossSection != 0)
            {
                double sinkFractionVelocity =
                    Utilities.Clamp((fractionVolume / sinkCrossSection) / @params.dt, 0.0, c_sink);
                double sinkFractionVelocity_x = sinkFractionVelocity * dx;
                double sinkFractionVelocity_y = sinkFractionVelocity * dy;
                double sinkFractionMomentum_x = sinkFractionVelocity_x * fractionMass;
                double sinkFractionMomentum_y = sinkFractionVelocity_y * fractionMass;

                sink.state.momentum[0] += sinkFractionMomentum_x;
                sink.state.momentum[1] += sinkFractionMomentum_y;
            }

            if (sourceCrossSection != 0 && sourceMass != 0)
            {
                double sourceFractionVelocity =
                    Utilities.Clamp((fractionVolume / sourceCrossSection) / @params.dt, 0.0, c_source);
                double sourceFractionVelocity_x = sourceFractionVelocity * dx;
                double sourceFractionVelocity_y = sourceFractionVelocity * dy;
                double sourceFractionMomentum_x = sourceFractionVelocity_x * fractionMass;
                double sourceFractionMomentum_y = sourceFractionVelocity_y * fractionMass;

                source.state.momentum[0] += sourceFractionMomentum_x;
                source.state.momentum[1] += sourceFractionMomentum_y;
            }

            if (sourceMass != 0)
            {
                // Energy conservation
                double sourceVelocity0_x = sourceInitialMomentum_x * invSourceMass;
                double sourceVelocity0_y = sourceInitialMomentum_y * invSourceMass;

                double sourceVelocity1_x = source.state.momentum[0] * invSourceMass;
                double sourceVelocity1_y = source.state.momentum[1] * invSourceMass;

                source.state.E_k -=
                    0.5 * sourceMass
                    * (sourceVelocity1_x * sourceVelocity1_x - sourceVelocity0_x * sourceVelocity0_x);

                source.state.E_k -=
                    0.5 * sourceMass
                    * (sourceVelocity1_y * sourceVelocity1_y - sourceVelocity0_y * sourceVelocity0_y);
            }

            if (sinkMass > 0)
            {
                double sinkVelocity0_x = sinkInitialMomentum_x * invSinkMass;
                double sinkVelocity0_y = sinkInitialMomentum_y * invSinkMass;

                double sinkVelocity1_x = sink.state.momentum[0] * invSinkMass;
                double sinkVelocity1_y = sink.state.momentum[1] * invSinkMass;

                sink.state.E_k -=
                    0.5 * sinkMass
                    * (sinkVelocity1_x * sinkVelocity1_x - sinkVelocity0_x * sinkVelocity0_x);

                sink.state.E_k -=
                    0.5 * sinkMass
                    * (sinkVelocity1_y * sinkVelocity1_y - sinkVelocity0_y * sinkVelocity0_y);
            }

            if (sink.state.E_k < 0)
            {
                sink.state.E_k = 0;
            }

            if (source.state.E_k < 0)
            {
                source.state.E_k = 0;
            }

            return flow * direction;
        }

        /// <summary>Adds <paramref name="n"/> moles of gas at temperature <paramref name="T"/> pushed into the system (community fork, forced induction).</summary>
        public void Inject(double n, double T, Mix mix)
        {
            if (n <= 0) return;

            double bulk_E_k_0 = BulkKineticEnergy();
            GainN(n, KineticEnergyPerMol(T, degreesOfFreedom), mix);
            state.E_k += (BulkKineticEnergy() - bulk_E_k_0);
        }

        public double Flow(double k_flow, double dt, double P_env, double T_env) => Flow(k_flow, dt, P_env, T_env, Mix.Default);

        /// <summary>Flow to or from an environment at a fixed pressure and temperature.</summary>
        public double Flow(double k_flow, double dt, double P_env, double T_env, Mix mix)
        {
            double maxFlow = PressureEquilibriumMaxFlow(P_env, T_env);
            double flow = dt * FlowRate(
                k_flow,
                Pressure(),
                P_env,
                Temperature(),
                T_env,
                HeatCapacityRatio(),
                chokedFlowLimit,
                chokedFlowFactorCached);

            if (Math.Abs(flow) > Math.Abs(maxFlow))
            {
                flow = maxFlow;
            }

            if (flow < 0)
            {
                double bulk_E_k_0 = BulkKineticEnergy();
                GainN(-flow, KineticEnergyPerMol(T_env, degreesOfFreedom), mix);
                double bulk_E_k_1 = BulkKineticEnergy();

                state.E_k += (bulk_E_k_1 - bulk_E_k_0);
            }
            else
            {
                double starting_n = N();
                LoseN(flow, KineticEnergyPerMol());

                state.momentum[0] -= (flow / starting_n) * state.momentum[0];
                state.momentum[1] -= (flow / starting_n) * state.momentum[1];
            }

            return flow;
        }

        public double PressureEquilibriumMaxFlow(GasSystem b)
        {
            if (Pressure() > b.Pressure())
            {
                double maxFlow =
                        (b.Volume() * KineticEnergy() - Volume() * b.KineticEnergy()) /
                        (b.Volume() * KineticEnergyPerMol() + Volume() * KineticEnergyPerMol());
                return Utilities.Fmax(0.0, Utilities.Fmin(maxFlow, N()));
            }
            else
            {
                double maxFlow =
                        (b.Volume() * KineticEnergy() - Volume() * b.KineticEnergy()) /
                        (b.Volume() * b.KineticEnergyPerMol() + Volume() * b.KineticEnergyPerMol());
                return Utilities.Fmin(0.0, Utilities.Fmax(maxFlow, -b.N()));
            }
        }

        public double PressureEquilibriumMaxFlow(double P_env, double T_env)
        {
            if (Pressure() > P_env)
            {
                return -(P_env * (0.5 * degreesOfFreedom * Volume()) - KineticEnergy()) / KineticEnergyPerMol();
            }
            else
            {
                double E_k_per_mol_env = 0.5 * T_env * Constants.R * degreesOfFreedom;
                return -(P_env * (0.5 * degreesOfFreedom * Volume()) - KineticEnergy()) / E_k_per_mol_env;
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

        public static double ChokedFlowLimit(int degreesOfFreedom)
        {
            double hcr = HeatCapacityRatio(degreesOfFreedom);
            return Math.Pow((2.0 / (hcr + 1)), hcr / (hcr - 1));
        }

        public static double ChokedFlowRate(int degreesOfFreedom)
        {
            double hcr = HeatCapacityRatio(degreesOfFreedom);
            double flowRate =
                Math.Sqrt(hcr) * Math.Pow(2 / (hcr + 1), (hcr + 1) / (2 * (hcr - 1)));

            return flowRate;
        }

        public double ApproximateDensity()
        {
            return (Units.AirMolecularMass * N()) / Volume();
        }

        public int DegreesOfFreedom() => degreesOfFreedom;

        public double N()
        {
            return state.n_mol;
        }

        public double N(double V)
        {
            return (V / Volume()) * N();
        }

        public double KineticEnergy()
        {
            return state.E_k;
        }

        public double KineticEnergy(double n)
        {
            return (KineticEnergy() / N()) * n;
        }

        public double KineticEnergyPerMol() => KineticEnergy(1.0);

        public double TotalEnergy()
        {
            if (N() == 0) return 0;

            double invMass = 1 / Mass();
            double v_x = state.momentum[0] * invMass;
            double v_y = state.momentum[1] * invMass;
            double v_squared = v_x * v_x + v_y * v_y;

            return KineticEnergy() + 0.5 * Mass() * v_squared;
        }

        public double BulkKineticEnergy()
        {
            double m = Mass();
            if (m == 0) return 0;

            double v_x = state.momentum[0] / m;
            double v_y = state.momentum[1] / m;
            double v_squared = v_x * v_x + v_y * v_y;
            return 0.5 * m * v_squared;
        }

        public double C()
        {
            if (N() == 0 || KineticEnergy() == 0) return 0;

            double hcr = HeatCapacityRatio();
            double staticPressure = Pressure();
            double density = ApproximateDensity();
            double c = Math.Sqrt(staticPressure * hcr / density);

            return c;
        }

        public double DynamicPressure(double dx, double dy)
        {
            if (N() == 0 || KineticEnergy() == 0) return 0;

            double inverseMass = 1 / Mass();
            double v = inverseMass * (dx * state.momentum[0] + dy * state.momentum[1]);

            if (v <= 0)
            {
                return 0;
            }

            double hcr = HeatCapacityRatio();
            double staticPressure = Pressure();
            double density = ApproximateDensity();
            double c_squared = staticPressure * hcr / density;
            double machNumber_squared = v * v / c_squared;

            // Below is equivalent to:
            // staticPressure * pow(1 + ((hcr - 1) / 2) * machNumber * machNumber, hcr / (hcr - 1)) - 1)

            double x = 1 + ((hcr - 1) / 2) * machNumber_squared;
            double x_d;
            switch (degreesOfFreedom)
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

        public double Mass()
        {
            return Units.AirMolecularMass * N();
        }

        public double Pressure()
        {
            double volume = Volume();
            return (volume != 0)
                ? KineticEnergy() / (0.5 * degreesOfFreedom * volume)
                : 0;
        }

        public double Temperature()
        {
            if (N() == 0) return 0;
            else return KineticEnergy() / (0.5 * degreesOfFreedom * N() * Constants.R);
        }

        public double Velocity_x()
        {
            if (N() == 0) return 0;
            else return state.momentum[0] / Mass();
        }

        public double Velocity_y()
        {
            if (N() == 0) return 0;
            else return state.momentum[1] / Mass();
        }

        public double Volume()
        {
            return state.V;
        }

        public double Volume(double n)
        {
            return n * N() / Volume();
        }

        public double N_fuel()
        {
            return state.mix.p_fuel * N();
        }

        public double N_inert()
        {
            return state.mix.p_inert * N();
        }

        public double N_o2()
        {
            return state.mix.p_o2 * N();
        }

        public double HeatCapacityRatio()
        {
            return HeatCapacityRatio(degreesOfFreedom);
        }

        /// <summary>Upstream's <c>mix()</c>; renamed because a C# member can't share its nested type's name.</summary>
        public Mix GetMix() => state.mix;

        // Read access for the dense core's snapshot.
        internal double GetMomentum(int axis) => state.momentum[axis];
        internal double GetChokedFlowLimit() => chokedFlowLimit;
        internal double GetChokedFlowFactorCached() => chokedFlowFactorCached;
        internal double GetWidth() => width;
        internal double GetHeight() => height;
        internal double GetDx() => dx;
        internal double GetDy() => dy;
    }
}
