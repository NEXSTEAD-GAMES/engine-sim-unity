// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense core's engine parts: the managed parts' per-step methods over DenseParts, line for line.
// Rigid bodies are the solver's (DenseSolver.bodies), read and written where the managed parts read
// and write their RigidBody.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public static unsafe partial class PartsOps
    {
        // ---- Counts and simple reads ----

        public static int CylinderCount(DenseEngine* e) => e->parts.cylinderCount;
        public static int IntakeCount(DenseEngine* e) => e->parts.intakeCount;
        public static int ExhaustCount(DenseEngine* e) => e->parts.exhaustCount;
        public static int CrankshaftCount(DenseEngine* e) => e->parts.crankshaftCount;

        /// <summary>The crankshaft's rigid body index.</summary>
        public static int CrankBody(DenseEngine* e, int crankshaft) => e->parts.crankshafts[crankshaft].body;

        /// <summary><see cref="Intake.flow"/>.</summary>
        public static double IntakeFlow(DenseEngine* e, int intake) => e->parts.intakes[intake].flow;

        public static double ExhaustLength(DenseEngine* e, int exhaust) => e->parts.exhausts[exhaust].length;
        public static double ExhaustAudioVolume(DenseEngine* e, int exhaust) => e->parts.exhausts[exhaust].audioVolume;

        /// <summary><c>chamber.exhaustRunnerAndPrimary.Pressure()</c>.</summary>
        public static double ExhaustRunnerPressure(DenseEngine* e, int cylinder)
        {
            return GasOps.Pressure(&e->parts.gases[e->parts.chambers[cylinder].exhaustRunnerAndPrimary]);
        }

        /// <summary><c>chamber.exhaustRunnerAndPrimary.DynamicPressure(dx, dy)</c>.</summary>
        public static double ExhaustRunnerDynamicPressure(DenseEngine* e, int cylinder, double dx, double dy)
        {
            return GasOps.DynamicPressure(&e->parts.gases[e->parts.chambers[cylinder].exhaustRunnerAndPrimary], dx, dy);
        }

        /// <summary><c>chamber.system.Pressure()</c>.</summary>
        public static double ChamberPressure(DenseEngine* e, int cylinder)
        {
            return GasOps.Pressure(&e->parts.gases[e->parts.chambers[cylinder].system]);
        }

        /// <summary><c>chamber.system.Temperature()</c>.</summary>
        public static double ChamberTemperature(DenseEngine* e, int cylinder)
        {
            return GasOps.Temperature(&e->parts.gases[e->parts.chambers[cylinder].system]);
        }

        /// <summary><c>chamber.GetVolume()</c>.</summary>
        public static double ChamberVolume(DenseEngine* e, int cylinder)
        {
            return GetVolume(e, &e->parts.chambers[cylinder]);
        }

        /// <summary><c>chamber.intakeRunnerAndManifold.Pressure()</c>.</summary>
        public static double IntakeRunnerPressure(DenseEngine* e, int cylinder)
        {
            return GasOps.Pressure(&e->parts.gases[e->parts.chambers[cylinder].intakeRunnerAndManifold]);
        }

        /// <summary><c>intake.system.Pressure()</c>.</summary>
        public static double IntakePressure(DenseEngine* e, int intake)
        {
            return GasOps.Pressure(&e->parts.gases[e->parts.intakes[intake].system]);
        }

        /// <summary><c>exhaust.GetSystem().Pressure()</c>.</summary>
        public static double ExhaustPressure(DenseEngine* e, int exhaust)
        {
            return GasOps.Pressure(&e->parts.gases[e->parts.exhausts[exhaust].system]);
        }

        /// <summary>
        /// The head the simulator's synthesizer input reads for a cylinder: <c>engine.GetHead(piston.GetCylinderBank().GetIndex())</c>,
        /// at the piston's cylinder index.
        /// </summary>
        static HeadCylinderData* SynthesizerHeadCylinder(DenseEngine* e, int cylinder)
        {
            DenseParts* p = &e->parts;
            PistonData* piston = &p->pistons[cylinder];
            CylinderHeadData* head = &p->heads[p->banks[piston->bank].index];
            return &head->cylinders[piston->cylinderIndex];
        }

        /// <summary><c>head.GetSoundAttenuation(piston.GetCylinderIndex())</c>.</summary>
        public static double SoundAttenuation(DenseEngine* e, int cylinder) => SynthesizerHeadCylinder(e, cylinder)->soundAttenuation;

        /// <summary><c>head.GetHeaderPrimaryLength(piston.GetCylinderIndex())</c>.</summary>
        public static double HeaderPrimaryLength(DenseEngine* e, int cylinder) => SynthesizerHeadCylinder(e, cylinder)->headerPrimaryLength;

        /// <summary><c>head.GetExhaustSystem(piston.GetCylinderIndex()).GetIndex()</c>.</summary>
        public static int CylinderExhaust(DenseEngine* e, int cylinder)
        {
            return e->parts.exhausts[SynthesizerHeadCylinder(e, cylinder)->exhaustSystem].index;
        }

        // ---- Engine ----

        /// <summary><see cref="Engine.Update"/>: the throttle's update.</summary>
        public static void EngineUpdate(DenseEngine* e, double dt)
        {
            ThrottleData* throttle = &e->parts.throttle;
            switch (throttle->kind)
            {
                case ThrottleKind.DirectLinkage:
                    SetThrottle(e, throttle->throttlePosition);
                    break;
                case ThrottleKind.Governor:
                    GovernorUpdate(e, throttle, dt);
                    break;
            }
        }

        /// <summary><see cref="Engine.SetSpeedControl"/>.</summary>
        public static void SetSpeedControl(DenseEngine* e, double s)
        {
            ThrottleData* throttle = &e->parts.throttle;
            throttle->speedControl = s;
            switch (throttle->kind)
            {
                case ThrottleKind.DirectLinkage:
                    throttle->throttlePosition = 1 - Math.Pow(s, throttle->gamma);
                    break;
                case ThrottleKind.Governor:
                    throttle->targetSpeed = (1 - s) * throttle->minSpeed + s * throttle->maxSpeed;
                    break;
            }
        }

        /// <summary><see cref="Governor.Update"/>.</summary>
        static void GovernorUpdate(DenseEngine* e, ThrottleData* g, double dt)
        {
            double currentSpeed = GetSpeed(e);
            double ds = g->targetSpeed * g->targetSpeed - currentSpeed * currentSpeed;

            g->velocity += (dt * -ds * g->k_s - g->velocity * dt * g->k_d);
            g->velocity = Utilities.Clamp(g->velocity, g->minVelocity, g->maxVelocity);

            if (Math.Abs(currentSpeed) < Math.Abs(0.5 * g->minSpeed))
            {
                g->velocity = 0;
                g->currentThrottle = 1.0;
            }

            g->currentThrottle += g->velocity * dt;
            g->currentThrottle = Utilities.Clamp(g->currentThrottle);

            SetThrottle(e, 1 - Math.Pow(1 - g->currentThrottle, g->gamma));
        }

        /// <summary><see cref="Engine.SetThrottle"/>.</summary>
        static void SetThrottle(DenseEngine* e, double throttle)
        {
            DenseParts* p = &e->parts;
            for (int i = 0; i < p->intakeCount; ++i)
            {
                p->intakes[i].throttle = throttle;
            }

            p->throttleValue = throttle;
        }

        /// <summary><see cref="Engine.GetRpm"/>.</summary>
        public static double GetRpm(DenseEngine* e)
        {
            if (e->parts.crankshaftCount == 0) return 0;
            else return Math.Abs(Units.ToRpm(e->solver.bodies[e->parts.crankshafts[0].body].v_theta));
        }

        /// <summary><see cref="Engine.GetSpeed"/>.</summary>
        static double GetSpeed(DenseEngine* e)
        {
            if (e->parts.crankshaftCount == 0) return 0;
            else return Math.Abs(e->solver.bodies[e->parts.crankshafts[0].body].v_theta);
        }

        /// <summary><see cref="Engine.IsSpinningCw"/>.</summary>
        public static bool IsSpinningCw(DenseEngine* e)
        {
            return e->solver.bodies[e->parts.crankshafts[0].body].v_theta <= 0;
        }

        /// <summary><see cref="Engine.GetManifoldPressure"/>.</summary>
        static double GetManifoldPressure(DenseEngine* e)
        {
            DenseParts* p = &e->parts;
            double pressureSum = 0.0;
            for (int i = 0; i < p->intakeCount; ++i)
            {
                pressureSum += GasOps.Pressure(&p->gases[p->intakes[i].system]);
            }

            return pressureSum / p->intakeCount;
        }

        // ---- Crankshaft ----

        /// <summary><see cref="Crankshaft.ResetAngle"/>.</summary>
        public static void ResetCrankAngle(DenseEngine* e, int crankshaft)
        {
            RigidBodyData* body = &e->solver.bodies[e->parts.crankshafts[crankshaft].body];
            body->theta = body->theta % (4 * Constants.pi);
        }

        /// <summary><see cref="Crankshaft.GetCycleAngle"/> with offset 0.</summary>
        public static double CrankCycleAngle(DenseEngine* e, int crankshaft) => GetCycleAngle(e, crankshaft, 0.0);

        /// <summary><see cref="Crankshaft.GetAngle"/>.</summary>
        static double GetCrankAngle(DenseEngine* e, int crankshaft)
        {
            CrankshaftData* c = &e->parts.crankshafts[crankshaft];
            return e->solver.bodies[c->body].theta - c->tdc;
        }

        /// <summary><see cref="Crankshaft.GetCycleAngle"/>.</summary>
        static double GetCycleAngle(DenseEngine* e, int crankshaft, double offset)
        {
            double wrapped = (-GetCrankAngle(e, crankshaft) + offset) % (4 * Constants.pi);
            return (wrapped < 0)
                ? wrapped + 4 * Constants.pi
                : wrapped;
        }

        // ---- Ignition ----

        /// <summary><see cref="IgnitionModule.enabled"/>.</summary>
        public static void SetIgnitionEnabled(DenseEngine* e, bool enabled)
        {
            e->parts.ignition.enabled = enabled;
        }

        /// <summary><see cref="IgnitionModule.Update"/>.</summary>
        public static void IgnitionUpdate(DenseEngine* e, double dt)
        {
            IgnitionData* m = &e->parts.ignition;
            RigidBodyData* crankBody = &e->solver.bodies[e->parts.crankshafts[m->crankshaft].body];
            double cycleAngle = GetCycleAngle(e, m->crankshaft, 0.0);

            if (m->enabled && m->revLimitTimer == 0)
            {
                double fourPi = 4 * Constants.pi;
                double advance = GetTimingAdvance(e);

                for (int spark = 0; spark < m->sparksPerCycle * m->cylinderCount; ++spark)
                {
                    int i = spark % m->cylinderCount;
                    SparkPlugData* plug = &m->plugs[i];
                    double repeat = (spark / m->cylinderCount) * fourPi / m->sparksPerCycle;
                    double adjustedAngle = Utilities.PositiveMod(plug->angle + repeat - advance, fourPi);
                    double r0 = m->lastCrankshaftAngle;
                    double r1 = cycleAngle;

                    if (crankBody->v_theta < 0)
                    {
                        if (r1 < r0)
                        {
                            r1 += fourPi;
                            if (adjustedAngle < r0) adjustedAngle += fourPi;
                        }

                        if (adjustedAngle >= r0 && adjustedAngle < r1)
                        {
                            plug->ignitionEvent = plug->enabled;
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
                            plug->ignitionEvent = plug->enabled;
                        }
                    }
                }
            }

            m->revLimitTimer -= dt;
            if (Math.Abs(crankBody->v_theta) > m->revLimit)
            {
                m->revLimitTimer = m->limiterDuration;
            }

            if (m->revLimitTimer < 0)
            {
                m->revLimitTimer = 0;
            }

            m->lastCrankshaftAngle = cycleAngle;
        }

        /// <summary><see cref="IgnitionModule.GetIgnitionEvent"/>.</summary>
        public static bool IgnitionEvent(DenseEngine* e, int cylinder) => e->parts.ignition.plugs[cylinder].ignitionEvent;

        /// <summary><see cref="IgnitionModule.ResetIgnitionEvents"/>.</summary>
        public static void ResetIgnitionEvents(DenseEngine* e)
        {
            IgnitionData* m = &e->parts.ignition;
            for (int i = 0; i < m->cylinderCount; ++i)
            {
                m->plugs[i].ignitionEvent = false;
            }
        }

        /// <summary><see cref="IgnitionModule.GetTimingAdvance"/>.</summary>
        static double GetTimingAdvance(DenseEngine* e)
        {
            IgnitionData* m = &e->parts.ignition;
            return SampleTriangle(
                &e->parts.functions[m->timingCurve],
                -e->solver.bodies[e->parts.crankshafts[m->crankshaft].body].v_theta);
        }

        // ---- Intake and exhaust ----

        /// <summary><see cref="Intake.GetFuelAirMix"/>.</summary>
        public static MixData IntakeFuelAirMix(IntakeData* it)
        {
            double ideal_afr = 0.8 * it->molecularAfr * 4;
            double p_air = ideal_afr / (1 + ideal_afr);

            MixData fuelAirMix = GasOps.DefaultMix();
            fuelAirMix.p_fuel = 1 - p_air;
            fuelAirMix.p_inert = p_air * 0.75;
            fuelAirMix.p_o2 = p_air * 0.25;
            return fuelAirMix;
        }

        /// <summary><see cref="Intake.Process"/>.</summary>
        public static void IntakeProcess(DenseEngine* e, int intake, double dt)
        {
            DenseParts* p = &e->parts;
            IntakeData* it = &p->intakes[intake];
            GasData* system = &p->gases[it->system];
            GasData* atmosphere = &p->gases[it->atmosphere];

            MixData fuelAirMix = IntakeFuelAirMix(it);

            const double idle_afr = 2.0;
            const double p_idle_air = idle_afr / (1 + idle_afr);
            MixData fuelMix = GasOps.DefaultMix();
            fuelMix.p_fuel = (1.0 - p_idle_air);
            fuelMix.p_inert = p_idle_air * 0.75;
            fuelMix.p_o2 = p_idle_air * 0.25;

            double throttle = it->idleThrottlePlatePosition * it->throttle;
            double flowAttenuation = Math.Cos(throttle * Constants.pi / 2);

            FlowParams flowParams = default;
            flowParams.crossSectionArea_0 = Units.Area(10, Units.m2);
            flowParams.crossSectionArea_1 = it->crossSectionArea;
            flowParams.direction_x = 0.0;
            flowParams.direction_y = -1.0;
            flowParams.dt = dt;

            if (it->boosted != 0)
            {
                flowParams.crossSectionArea_0 = it->crossSectionArea;
                flowParams.system_0 = &p->gases[p->forcedInduction->charge];
                flowParams.system_1 = system;
                flowParams.k_flow = flowAttenuation * it->inputFlowK;
                it->flow = GasOps.Flow(&flowParams);

                flowParams.k_flow = it->idleFlowK;
                GasOps.Flow(&flowParams);
            }
            else
            {
                GasOps.Reset(atmosphere, it->atmospherePressure, it->atmosphereTemperature, fuelAirMix);
                flowParams.system_0 = atmosphere;
                flowParams.system_1 = system;
                flowParams.k_flow = flowAttenuation * it->inputFlowK;
                it->flow = GasOps.Flow(&flowParams);

                GasOps.Reset(atmosphere, it->atmospherePressure, it->atmosphereTemperature, fuelMix);
                flowParams.system_0 = atmosphere;
                flowParams.system_1 = system;
                flowParams.k_flow = it->idleFlowK;
                GasOps.Flow(&flowParams);
            }

            GasOps.DissipateExcessVelocity(system);
            GasOps.UpdateVelocity(system, dt, it->velocityDecay);
        }

        /// <summary><see cref="ExhaustSystem.Process"/>.</summary>
        public static void ExhaustProcess(DenseEngine* e, int exhaust, double dt)
        {
            DenseParts* p = &e->parts;
            ExhaustData* x = &p->exhausts[exhaust];
            GasData* system = &p->gases[x->system];
            GasData* atmosphere = &p->gases[x->atmosphere];

            MixData airMix = GasOps.DefaultMix();
            airMix.p_fuel = 0;
            airMix.p_inert = 1.0;
            airMix.p_o2 = 0.0;

            GasOps.Reset(atmosphere, Units.Pressure(1.0, Units.atm), Units.Celcius(25.0), airMix);

            FlowParams flowParams = default;
            flowParams.crossSectionArea_0 = x->collectorCrossSectionArea;
            flowParams.crossSectionArea_1 = Units.Area(10, Units.m2);
            flowParams.direction_x = 1.0;
            flowParams.direction_y = 0.0;
            flowParams.dt = dt;

            flowParams.system_0 = atmosphere;
            flowParams.system_1 = system;
            flowParams.k_flow = x->outletFlowRate;

            if (x->turbine != 0)
            {
                ForcedInductionData* f = p->forcedInduction;
                flowParams.k_flow = ForcedInductionOps.GetExhaustOutletFlowK(f, x->outletFlowRate);

                double P = GasOps.Pressure(system);
                double T = GasOps.Temperature(system);
                double hcr = GasOps.HeatCapacityRatio(system);
                x->flow = GasOps.Flow(&flowParams);

                if (x->flow < 0)
                {
                    ForcedInductionOps.AddExhaustFlow(f, -x->flow, T, P, hcr, x->outletFlowRate);
                }
            }
            else
            {
                x->flow = GasOps.Flow(&flowParams);
            }

            GasOps.DissipateExcessVelocity(system);
            GasOps.UpdateVelocity(system, dt, x->velocityDecay);
        }

        // ---- Heads, valvetrains, camshafts ----

        /// <summary><see cref="CylinderHead.IntakeFlowRate"/>.</summary>
        static double IntakeFlowRate(DenseEngine* e, CylinderHeadData* head, int cylinder)
        {
            return SampleTriangle(
                &e->parts.functions[head->intakePortFlow],
                IntakeValveLift(e, &e->parts.valvetrains[head->valvetrain], cylinder));
        }

        /// <summary><see cref="CylinderHead.ExhaustFlowRate"/>.</summary>
        static double ExhaustFlowRate(DenseEngine* e, CylinderHeadData* head, int cylinder)
        {
            return SampleTriangle(
                &e->parts.functions[head->exhaustPortFlow],
                ExhaustValveLift(e, &e->parts.valvetrains[head->valvetrain], cylinder));
        }

        /// <summary>Cylinder <paramref name="cylinder"/>'s intake and exhaust valve lift now, as its chamber's flow reads them (for the capture).</summary>
        internal static void ValveLifts(DenseEngine* e, int cylinder, out double intake, out double exhaust)
        {
            DenseParts* p = &e->parts;
            ChamberData* c = &p->chambers[cylinder];
            ValvetrainData* v = &p->valvetrains[p->heads[c->head].valvetrain];
            int cylinderIndex = p->pistons[c->piston].cylinderIndex;
            intake = IntakeValveLift(e, v, cylinderIndex);
            exhaust = ExhaustValveLift(e, v, cylinderIndex);
        }

        /// <summary><see cref="Valvetrain.IntakeValveLift"/>.</summary>
        static double IntakeValveLift(DenseEngine* e, ValvetrainData* v, int cylinder)
        {
            if (v->kind == ValvetrainKind.VariableLift)
            {
                return ValveLift(e, FollowedCamshaft(e, v, cylinder, v->intakeOnHighLift, v->intakeCamshaft, v->highLiftIntakeCamshaft), cylinder);
            }

            return ValveLift(e, v->intakeCamshaft, cylinder);
        }

        /// <summary><see cref="Valvetrain.ExhaustValveLift"/>.</summary>
        static double ExhaustValveLift(DenseEngine* e, ValvetrainData* v, int cylinder)
        {
            if (v->kind == ValvetrainKind.VariableLift)
            {
                return ValveLift(e, FollowedCamshaft(e, v, cylinder, v->exhaustOnHighLift, v->exhaustCamshaft, v->highLiftExhaustCamshaft), cylinder);
            }

            return ValveLift(e, v->exhaustCamshaft, cylinder);
        }

        /// <summary>VariableLiftValvetrain.FollowedCamshaft.</summary>
        static int FollowedCamshaft(DenseEngine* e, ValvetrainData* v, int cylinder, byte* onHighLift, int normal, int highLift)
        {
            double closedLift = Units.Distance(0.01, Units.mm);

            byte requested = IsHighLiftEngaged(e, v) ? (byte)1 : (byte)0;
            if (onHighLift[cylinder] != requested
                && ValveLift(e, normal, cylinder) < closedLift
                && ValveLift(e, highLift, cylinder) < closedLift)
            {
                onHighLift[cylinder] = requested;
            }

            return onHighLift[cylinder] != 0 ? highLift : normal;
        }

        /// <summary>VariableLiftValvetrain.IsHighLiftEngaged.</summary>
        static bool IsHighLiftEngaged(DenseEngine* e, ValvetrainData* v)
        {
            return
                GetManifoldPressure(e) > v->manifoldVacuum
                && GetSpeed(e) > v->minRpm
                && (1 - e->parts.throttleValue) > v->minThrottlePosition;
        }

        /// <summary><see cref="Camshaft.ValveLift"/>.</summary>
        static double ValveLift(DenseEngine* e, int camshaft, int lobe)
        {
            CamshaftData* cam = &e->parts.camshafts[camshaft];
            return SampleLobe(e, cam, GetCamAngle(e, cam) + cam->lobeAngles[lobe]);
        }

        /// <summary><see cref="Camshaft.SampleLobe"/>.</summary>
        static double SampleLobe(DenseEngine* e, CamshaftData* cam, double theta)
        {
            double clampedTheta = theta % (2 * Constants.pi);
            if (clampedTheta < 0) clampedTheta += 2 * Constants.pi;
            if (clampedTheta >= Constants.pi) clampedTheta -= 2 * Constants.pi;

            return SampleTriangle(&e->parts.functions[cam->lobeProfile], clampedTheta);
        }

        /// <summary><see cref="Camshaft.GetAngle"/>.</summary>
        static double GetCamAngle(DenseEngine* e, CamshaftData* cam)
        {
            double angle =
                ((GetCrankAngle(e, cam->crankshaft) + cam->advance) * cam->reductionRatio) % (2 * Constants.pi);

            return (angle < 0)
                ? angle + 2 * Constants.pi
                : angle;
        }

        // ---- Fuel ----

        /// <summary><see cref="Fuel.FlameSpeed"/>.</summary>
        static double FlameSpeed(
            DenseEngine* e,
            double turbulence,
            double molecularAfr,
            double T,
            double P,
            double firingPressure,
            double motoringPressure)
        {
            FuelData* fuel = &e->parts.fuel;
            double S_L = LaminarBurningVelocity(fuel, molecularAfr, T, P);
            const double p_adjustment = 1.0;

            return SampleTriangle(&e->parts.functions[fuel->turbulenceToFlameSpeedRatio], (turbulence / S_L) * p_adjustment) * S_L;
        }

        /// <summary><see cref="Fuel.LaminarBurningVelocity"/>.</summary>
        static double LaminarBurningVelocity(FuelData* fuel, double molecularAfr, double T, double P)
        {
            const double er_m = 1.21;
            double B_m = Units.Distance(30.5, Units.cm) / Units.sec;
            double B_er = -Units.Distance(54.9, Units.cm) / Units.sec;

            double er = molecularAfr / fuel->molecularAfr;
            double alpha = 2.4 - 0.271 * Math.Pow(er, 3.51);
            double beta = -0.357 + 0.14 * Math.Pow(er, 2.77);
            double S_L_0 = B_m + B_er * (er - er_m) * (er - er_m);
            double T_ratio = T / Units.Kelvin(298);
            double P_ratio = P / Units.Pressure(1.0, Units.atm);

            return S_L_0 * Math.Pow(T_ratio, alpha) * Math.Pow(P_ratio, beta);
        }

        // ---- Function ----

        /// <summary><see cref="Function.SampleTriangle"/>.</summary>
        static double SampleTriangle(FunctionData* f, double x)
        {
            x *= f->inputScale;

            int closest = ClosestSample(f, x);
            if (f->size == 0) return 0;
            else if (x >= f->x[f->size - 1]) return f->y[f->size - 1] * f->outputScale;
            else if (x <= f->x[0]) return f->y[0] * f->outputScale;

            double sum = 0;
            double totalWeight = 0;

            for (int i = closest; i >= 0; --i)
            {
                if (f->x[i] > x) continue;
                if (Math.Abs(x - f->x[i]) > f->filterRadius) break;

                double w = Triangle(f, f->x[i] - x);
                sum += w * f->y[i];
                totalWeight += w;
            }

            for (int i = closest; i < f->size; ++i)
            {
                if (f->x[i] <= x) continue;
                if (Math.Abs(f->x[i] - x) > f->filterRadius) break;

                double w = Triangle(f, f->x[i] - x);
                sum += w * f->y[i];
                totalWeight += w;
            }

            return (totalWeight != 0)
                ? sum * f->outputScale / totalWeight
                : 0;
        }

        /// <summary><see cref="Function.Triangle"/>.</summary>
        static double Triangle(FunctionData* f, double x)
        {
            return (f->filterRadius - Math.Abs(x)) / f->filterRadius;
        }

        /// <summary><see cref="Function.ClosestSample"/>.</summary>
        static int ClosestSample(FunctionData* f, double x)
        {
            if (double.IsNaN(x))
            {
                return 0;
            }

            int l = 0;
            int r = f->size - 1;

            if (f->size == 0) return -1;
            else if (x <= f->x[l]) return l;
            else if (x >= f->x[r]) return r;

            while (l + 1 < r)
            {
                int m = (l + r) / 2;

                if (x > f->x[m])
                {
                    l = m;
                }
                else if (x < f->x[m])
                {
                    r = m;
                }
                else if (x == f->x[m])
                {
                    return m;
                }
            }

            return (x - f->x[l] < f->x[r] - x)
                ? l
                : r;
        }

        // ---- DeterministicRandom ----

        /// <summary><see cref="DeterministicRandom.NextDouble"/>.</summary>
        static double NextDouble(ulong* state) => (double)(NextRandom(state) >> 11) * (1.0 / 9007199254740992.0);

        /// <summary><see cref="DeterministicRandom.Next"/>.</summary>
        static ulong NextRandom(ulong* state)
        {
            unchecked
            {
                *state += 0x9E3779B97F4A7C15ul;
                return MixRandom(*state);
            }
        }

        static ulong MixRandom(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }
    }
}
