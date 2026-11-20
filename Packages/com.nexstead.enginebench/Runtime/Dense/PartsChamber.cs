// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense core's combustion chambers: CombustionChamber's per-step methods over ChamberData, line
// for line. A cylinder is the global piston/chamber index.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public static unsafe partial class PartsOps
    {
        /// <summary><see cref="CombustionChamber.Ignite"/>.</summary>
        public static void ChamberIgnite(DenseEngine* e, int cylinder)
        {
            DenseParts* p = &e->parts;
            ChamberData* c = &p->chambers[cylinder];
            GasData* system = &p->gases[c->system];
            FuelData* fuel = &p->fuel;

            if (!c->lit)
            {
                if (system->mix.p_fuel == 0) return;

                double afr = system->mix.p_o2 / system->mix.p_fuel;
                double equivalenceRatio = afr / fuel->molecularAfr;
                if (equivalenceRatio < 0.5) return;
                else if (equivalenceRatio > 1.9) return;

                double idealInert = system->mix.p_o2 / 0.7;
                double dilution = (system->mix.p_inert / idealInert) - 1;

                c->flameEvent.lastVolume = GetVolume(e, c);
                c->flameEvent.travel_x = 0;
                c->flameEvent.travel_y = 0;
                c->flameEvent.globalMix = system->mix;
                c->lit = true;
                ++c->ignitions;

                double randomness =
                    fuel->burningEfficiencyRandomness;
                double lowEfficiencyAttenuation =
                    fuel->lowEfficiencyAttenuation;
                double maxBurningEfficiency =
                    fuel->maxBurningEfficiency;
                double maxTurbulenceEffect =
                    fuel->maxTurbulenceEffect;
                double maxDilutionEffect =
                    fuel->maxDilutionEffect;

                double turbulence =
                    SampleTriangle(
                        &p->functions[c->meanPistonSpeedToTurbulence],
                        CalculateMeanPistonSpeed(c));
                double mixingFactor =
                    1.0 - (
                        Utilities.Clamp(turbulence / maxTurbulenceEffect)
                        * Utilities.Clamp(1 - dilution / maxDilutionEffect));
                double rand_s =
                    lowEfficiencyAttenuation
                    * ((1 - randomness) + randomness * NextDouble(&c->random));
                double efficiencyAttenuation =
                    (mixingFactor * rand_s + (1 - mixingFactor));
                c->flameEvent.efficiency =
                    efficiencyAttenuation * maxBurningEfficiency;
                c->flameEvent.flameSpeed = FlameSpeed(
                    e,
                    turbulence,
                    afr,
                    GasOps.Temperature(system),
                    GasOps.Pressure(system),
                    CalculateFiringPressure(c),
                    Units.Pressure(160, Units.psi));
            }
        }

        /// <summary><see cref="CombustionChamber.Update"/>.</summary>
        public static void ChamberUpdate(DenseEngine* e, int cylinder, double dt)
        {
            DenseParts* p = &e->parts;
            ChamberData* c = &p->chambers[cylinder];
            CylinderHeadData* head = &p->heads[c->head];
            int cylinderIndex = p->pistons[c->piston].cylinderIndex;

            GasOps.SetVolume(&p->gases[c->system], GetVolume(e, c));

            UpdateCycleStates(e, c);

            c->intakeFlowRate = IntakeFlowRate(e, head, cylinderIndex);
            c->exhaustFlowRate = ExhaustFlowRate(e, head, cylinderIndex);
        }

        /// <summary><see cref="CombustionChamber.ResetLastTimestepExhaustFlow"/> and <see cref="CombustionChamber.ResetLastTimestepIntakeFlow"/>.</summary>
        public static void ChamberResetTimestepFlows(DenseEngine* e, int cylinder)
        {
            ChamberData* c = &e->parts.chambers[cylinder];
            c->lastTimestepTotalExhaustFlow = 0;
            c->lastTimestepTotalIntakeFlow = 0;
        }

        /// <summary><see cref="CombustionChamber.Flow"/>.</summary>
        public static void ChamberFlow(DenseEngine* e, int cylinder, double dt)
        {
            DenseParts* p = &e->parts;
            ChamberData* c = &p->chambers[cylinder];
            PistonData* piston = &p->pistons[c->piston];
            CylinderHeadData* head = &p->heads[c->head];
            CylinderBankData* headBank = &p->banks[head->bank];
            GasData* system = &p->gases[c->system];
            GasData* intakeRunnerAndManifold = &p->gases[c->intakeRunnerAndManifold];
            GasData* exhaustRunnerAndPrimary = &p->gases[c->exhaustRunnerAndPrimary];

            double volume = GetVolume(e, c);
            double cylinderHeight = volume / c->cylinderCrossSectionSurfaceArea;
            double cylinderSurfaceArea =
                cylinderHeight * Constants.pi * headBank->bore
                + c->cylinderCrossSectionSurfaceArea * 2;

            double dT = Units.Celcius(90.0) - GasOps.Temperature(system);

            GasOps.ChangeEnergy(system, dT * cylinderSurfaceArea * 100 * dt);
            GasOps.FlowToEnvironment(system, piston->blowby_k, dt, c->crankcasePressure, Units.Celcius(25.0), GasOps.DefaultMix());

            HeadCylinderData* headCylinder = &head->cylinders[piston->cylinderIndex];
            IntakeData* intake = &p->intakes[headCylinder->intake];
            ExhaustData* exhaust = &p->exhausts[headCylinder->exhaustSystem];

            FlowParams flowParams = default;
            flowParams.dt = dt;

            flowParams.k_flow = c->manifoldToRunnerFlowRate;
            flowParams.crossSectionArea_0 = intake->crossSectionArea;
            flowParams.crossSectionArea_1 = head->intakeRunnerCrossSectionArea;
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.system_0 = &p->gases[intake->system];
            flowParams.system_1 = intakeRunnerAndManifold;
            GasOps.Flow(&flowParams);

            GasOps.DissipateExcessVelocity(intakeRunnerAndManifold);

            flowParams.k_flow = c->intakeFlowRate;
            flowParams.crossSectionArea_0 = head->intakeRunnerCrossSectionArea;
            flowParams.crossSectionArea_1 = volume / cylinderHeight;
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.system_0 = intakeRunnerAndManifold;
            flowParams.system_1 = system;
            double intakeFlow = GasOps.Flow(&flowParams);

            GasOps.DissipateExcessVelocity(intakeRunnerAndManifold);
            GasOps.DissipateExcessVelocity(system);

            flowParams.k_flow = c->exhaustFlowRate;
            flowParams.crossSectionArea_0 = volume / cylinderHeight;
            flowParams.crossSectionArea_1 = head->exhaustRunnerCrossSectionArea;
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.system_0 = system;
            flowParams.system_1 = exhaustRunnerAndPrimary;
            double exhaustFlow = GasOps.Flow(&flowParams);

            GasOps.DissipateExcessVelocity(system);
            GasOps.DissipateExcessVelocity(exhaustRunnerAndPrimary);

            flowParams.k_flow = c->primaryToCollectorFlowRate;
            flowParams.crossSectionArea_0 = head->exhaustRunnerCrossSectionArea;
            flowParams.crossSectionArea_1 = exhaust->collectorCrossSectionArea;
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.system_0 = exhaustRunnerAndPrimary;
            flowParams.system_1 = &p->gases[exhaust->system];
            GasOps.Flow(&flowParams);

            GasOps.UpdateVelocity(intakeRunnerAndManifold, dt, intake->velocityDecay);
            GasOps.UpdateVelocity(system, dt, 0.5);
            GasOps.UpdateVelocity(exhaustRunnerAndPrimary, dt, exhaust->velocityDecay);

            if (Math.Abs(intakeFlow) > 1E-9 && c->lit)
            {
                c->lit = false;
            }

            c->exhaustFlow = exhaustFlow;
            c->lastTimestepTotalExhaustFlow += exhaustFlow;
            c->lastTimestepTotalIntakeFlow += intakeFlow;

            if (c->lit)
            {
                double totalTravel_x = headBank->bore / 2;
                double totalTravel_y = volume / BoreSurfaceArea(headBank);
                double expansion = volume / c->flameEvent.lastVolume;
                double lastTravel_x = c->flameEvent.travel_x;
                double lastTravel_y = c->flameEvent.travel_y * expansion;
                double flameSpeed = c->flameEvent.flameSpeed;

                c->flameEvent.travel_x =
                    Utilities.Fmin(lastTravel_x + dt * flameSpeed, totalTravel_x);
                c->flameEvent.travel_y =
                    Utilities.Fmin(lastTravel_y + dt * flameSpeed, totalTravel_y);

                if (lastTravel_x < c->flameEvent.travel_x || lastTravel_y < c->flameEvent.travel_y)
                {
                    double burnedVolume =
                        c->flameEvent.travel_x * c->flameEvent.travel_x
                        * Constants.pi * c->flameEvent.travel_y;
                    double prevBurnedVolume =
                        lastTravel_x * lastTravel_x * Constants.pi * lastTravel_y;
                    double litVolume = burnedVolume - prevBurnedVolume;
                    double n = (litVolume / volume) * GasOps.N(system);

                    double fuelBurned =
                        GasOps.React(system, n * c->flameEvent.efficiency, c->flameEvent.globalMix);
                    double massFuelBurned = fuelBurned * p->fuel.molecularMass;
                    GasOps.ChangeEnergy(
                        system,
                        massFuelBurned * p->fuel.energyDensity);
                }
                else
                {
                    c->lit = false;
                }

                c->flameEvent.lastVolume = volume;
            }
        }

        /// <summary><see cref="CombustionChamber.Apply"/>: the gas force and ring friction on the piston.</summary>
        public static void ChamberApplyForce(DenseEngine* e, int cylinder, SystemStateData* state)
        {
            DenseParts* p = &e->parts;
            ChamberData* c = &p->chambers[cylinder];
            PistonData* piston = &p->pistons[c->piston];
            CylinderBankData* bank = &p->banks[p->heads[c->head].bank];

            double area = (bank->bore * bank->bore / 4.0) * Constants.pi;
            double v_x = state->v_x[piston->body];
            double v_y = state->v_y[piston->body];

            double v_s =
                v_x * bank->dx + v_y * bank->dy;

            double pressureDifferential = GasOps.Pressure(&p->gases[c->system]) - c->crankcasePressure;
            double force = -area * pressureDifferential;

            const double limit = 1E-3;
            double abs_v_s = Utilities.Fmin(Math.Abs(v_s), limit);
            double attenuation = abs_v_s / limit;

            double F = CalculateFrictionForce(e, c, piston, v_s) * attenuation;
            double F_fric = (v_s > 0)
                ? -F
                : F;

            SystemStateOps.ApplyForce(
                state,
                0.0,
                0.0,
                (force + F_fric) * bank->dx,
                (force + F_fric) * bank->dy,
                piston->body);
        }

        /// <summary><see cref="CombustionChamber.GetVolume"/>.</summary>
        static double GetVolume(DenseEngine* e, ChamberData* c)
        {
            DenseParts* p = &e->parts;
            CylinderHeadData* head = &p->heads[c->head];
            PistonData* piston = &p->pistons[c->piston];

            double combustionPortVolume = head->combustionChamberVolume;
            CylinderBankData* bank = &p->banks[head->bank];

            double area = BoreSurfaceArea(bank);
            double s =
                RelativeX(e, piston) * bank->dx
                + RelativeY(e, piston) * bank->dy;
            double sweep =
                area * (bank->deckHeight - s - piston->compressionHeight);

            return sweep + combustionPortVolume - piston->displacement;
        }

        /// <summary><see cref="CombustionChamber.PistonSpeed"/>.</summary>
        static double PistonSpeed(DenseEngine* e, ChamberData* c)
        {
            DenseParts* p = &e->parts;
            CylinderBankData* bank = &p->banks[p->heads[c->head].bank];
            RigidBodyData* body = &e->solver.bodies[p->pistons[c->piston].body];
            return
                body->v_x * bank->dx
                + body->v_y * bank->dy;
        }

        /// <summary><see cref="CombustionChamber.CalculateMeanPistonSpeed"/>.</summary>
        static double CalculateMeanPistonSpeed(ChamberData* c)
        {
            double avg = 0;
            for (int i = 0; i < ChamberData.StateSamples; ++i)
            {
                avg += c->pistonSpeed[i];
            }

            avg /= ChamberData.StateSamples;
            return avg;
        }

        /// <summary><see cref="CombustionChamber.CalculateFiringPressure"/>.</summary>
        static double CalculateFiringPressure(ChamberData* c)
        {
            double firingPressure = 0;
            for (int i = 0; i < ChamberData.StateSamples; ++i)
            {
                if (c->pressure[i] > firingPressure)
                {
                    firingPressure = c->pressure[i];
                }
            }

            return firingPressure;
        }

        /// <summary>CombustionChamber.CalculateFrictionForce.</summary>
        static double CalculateFrictionForce(DenseEngine* e, ChamberData* c, PistonData* piston, double v_s)
        {
            double cylinderWallForce = CalculateCylinderWallForce(e, piston);

            FrictionModelData* frictionModel = &c->frictionModel;
            double F_coul = frictionModel->frictionCoeff * cylinderWallForce;
            double v_st = frictionModel->breakawayFrictionVelocity * Constants.root_2;
            double v_coul = frictionModel->breakawayFrictionVelocity / 10;
            double F_brk = frictionModel->breakawayFriction;
            double v = Math.Abs(v_s);

            double F_0 = Constants.root_2 * Constants.e * (F_brk - F_coul);
            double F_1 = v / v_st;
            double F_2 = Math.Exp(-F_1 * F_1) * F_1;
            double F_3 = F_coul * Math.Tanh(v / v_coul);
            double F_4 = frictionModel->viscousFrictionCoefficient * v;

            return F_0 * F_2 + F_3 + F_4;
        }

        /// <summary>CombustionChamber.UpdateCycleStates.</summary>
        static void UpdateCycleStates(DenseEngine* e, ChamberData* c)
        {
            double crankAngle = GetCycleAngle(e, 0, 0.0);
            if (double.IsNaN(crankAngle) || double.IsInfinity(crankAngle))
            {
                crankAngle = 0.0;
            }

            int i = (int)Utilities.Round((crankAngle / (4 * Constants.pi)) * (ChamberData.StateSamples - 1.0));

            c->pistonSpeed[i] = Math.Abs(PistonSpeed(e, c));
            c->pressure[i] = GasOps.Pressure(&e->parts.gases[c->system]);
        }

        // ---- Piston and bank ----

        /// <summary><see cref="Piston.RelativeX"/>.</summary>
        static double RelativeX(DenseEngine* e, PistonData* piston)
        {
            return e->solver.bodies[piston->body].p_x - e->parts.banks[piston->bank].x;
        }

        /// <summary><see cref="Piston.RelativeY"/>.</summary>
        static double RelativeY(DenseEngine* e, PistonData* piston)
        {
            return e->solver.bodies[piston->body].p_y - e->parts.banks[piston->bank].y;
        }

        /// <summary><see cref="Piston.CalculateCylinderWallForce"/>: from its cylinder-wall constraint's reaction force (row 0, body 0).</summary>
        static double CalculateCylinderWallForce(DenseEngine* e, PistonData* piston)
        {
            ConstraintData* cylinderConstraint = &e->solver.constraints[piston->cylinderConstraint];
            return Math.Sqrt(
                cylinderConstraint->F_x[0] * cylinderConstraint->F_x[0]
                + cylinderConstraint->F_y[0] * cylinderConstraint->F_y[0]);
        }

        /// <summary><see cref="CylinderBank.BoreSurfaceArea"/>.</summary>
        static double BoreSurfaceArea(CylinderBankData* bank)
        {
            return Constants.pi * bank->bore * bank->bore / 4.0;
        }
    }
}
