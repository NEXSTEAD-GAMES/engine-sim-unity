// Port of engine-sim include/transmission.h and src/transmission.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class Transmission
    {
        public sealed class Parameters
        {
            public int GearCount;
            public double[] GearRatios;
            public double MaxClutchTorque;
        }

        readonly ClutchConstraint clutchConstraint = new ClutchConstraint();
        RigidBody rotatingMass;
        Vehicle vehicle;

        int gear = -1;
        int gearCount = 0;
        double[] gearRatios;
        double maxClutchTorque = Units.Torque(1000.0, Units.ft_lb);
        double clutchPressure = 0.0;

        public void Initialize(Parameters @params)
        {
            gearCount = @params.GearCount;
            maxClutchTorque = @params.MaxClutchTorque;
            gearRatios = new double[@params.GearCount];
            Array.Copy(@params.GearRatios, gearRatios, gearCount);
        }

        public void Update(double dt)
        {
            if (gear == -1)
            {
                clutchConstraint.minTorque = 0;
                clutchConstraint.maxTorque = 0;
            }
            else
            {
                clutchConstraint.minTorque = -maxClutchTorque * clutchPressure;
                clutchConstraint.maxTorque = maxClutchTorque * clutchPressure;
            }
        }

        public void AddToSystem(RigidBodySystem system, RigidBody rotatingMass, Vehicle vehicle, Engine engine)
        {
            this.rotatingMass = rotatingMass;
            this.vehicle = vehicle;

            clutchConstraint.SetBody1(engine.GetOutputCrankshaft().body);
            clutchConstraint.SetBody2(this.rotatingMass);

            system.AddConstraint(clutchConstraint);
        }

        public void ChangeGear(int newGear)
        {
            if (newGear < -1 || newGear >= gearCount) return;
            else if (newGear != -1)
            {
                double m_car = vehicle.GetMass();
                double gear_ratio = gearRatios[newGear];
                double diff_ratio = vehicle.GetDiffRatio();
                double tire_radius = vehicle.GetTireRadius();
                double f = tire_radius / (diff_ratio * gear_ratio);

                double new_I = m_car * f * f;
                double E_r =
                    0.5 * rotatingMass.I * rotatingMass.v_theta * rotatingMass.v_theta;
                double new_v_theta = rotatingMass.v_theta < 0
                    ? -Math.Sqrt(E_r * 2 / new_I)
                    : Math.Sqrt(E_r * 2 / new_I);

                rotatingMass.I = new_I;
                rotatingMass.p_x = rotatingMass.p_y = 0;
                rotatingMass.m = m_car;
                rotatingMass.v_theta = new_v_theta;
            }

            gear = newGear;
        }

        public int GetGear() => gear;

        /// <summary>Read access for the dense core's snapshot.</summary>
        internal ClutchConstraint Clutch => clutchConstraint;
        internal double MaxClutchTorque => maxClutchTorque;
        public void SetClutchPressure(double pressure) => clutchPressure = pressure;
        public double GetClutchPressure() => clutchPressure;
    }
}
