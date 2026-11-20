// Port of engine-sim include/vehicle.h and src/vehicle.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>The car the transmission drives, as one rotating mass with drag and rolling resistance.</summary>
    public sealed class Vehicle
    {
        public sealed class Parameters
        {
            public double mass;
            public double dragCoefficient;
            public double crossSectionArea;
            public double diffRatio;
            public double tireRadius;
            public double rollingResistance;
        }

        RigidBody rotatingMass;
        double mass;
        double dragCoefficient;
        double crossSectionArea;
        double diffRatio;
        double tireRadius;
        double travelledDistance;
        double rollingResistance;

        public void Initialize(Parameters @params)
        {
            mass = @params.mass;
            dragCoefficient = @params.dragCoefficient;
            crossSectionArea = @params.crossSectionArea;
            diffRatio = @params.diffRatio;
            tireRadius = @params.tireRadius;
            rollingResistance = @params.rollingResistance;
        }

        public void Update(double dt)
        {
            travelledDistance += GetSpeed() * dt;
        }

        public void AddToSystem(RigidBodySystem system, RigidBody rotatingMass)
        {
            this.rotatingMass = rotatingMass;
        }

        public double GetMass() => mass;
        public double GetRollingResistance() => rollingResistance;
        public double GetDragCoefficient() => dragCoefficient;
        public double GetCrossSectionArea() => crossSectionArea;
        public double GetDiffRatio() => diffRatio;
        public double GetTireRadius() => tireRadius;

        public double GetSpeed()
        {
            double E_r = 0.5 * rotatingMass.I * rotatingMass.v_theta * rotatingMass.v_theta;
            double vehicleSpeed = Math.Sqrt(2 * E_r / mass);

            return vehicleSpeed;
        }

        public double GetTravelledDistance() => travelledDistance;
        public void ResetTravelledDistance() => travelledDistance = 0;

        public double LinearForceToVirtualTorque(double force)
        {
            double rotationToKineticRatio =
                Math.Sqrt(rotatingMass.I / mass);

            return rotationToKineticRatio * force;
        }
    }
}
