// Port of engine-sim include/piston.h and src/piston.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class Piston : Part
    {
        public sealed class Parameters
        {
            public ConnectingRod Rod;
            public CylinderBank Bank;
            public int CylinderIndex;
            public double BlowbyFlowCoefficient;
            public double CompressionHeight;
            public double WristPinPosition;
            public double Displacement;
            public double mass;
        }

        ConnectingRod rod;
        CylinderBank bank;
        LineConstraint cylinderConstraint;
        int cylinderIndex = -1;
        double compressionHeight;
        double displacement;
        double wristPinLocation;
        double mass;
        double blowby_k;

        public void Initialize(Parameters @params)
        {
            rod = @params.Rod;
            bank = @params.Bank;
            cylinderIndex = @params.CylinderIndex;
            compressionHeight = @params.CompressionHeight;
            displacement = @params.Displacement;
            wristPinLocation = @params.WristPinPosition;
            mass = @params.mass;
            blowby_k = @params.BlowbyFlowCoefficient;
        }

        public void SetCylinderConstraint(LineConstraint constraint)
        {
            cylinderConstraint = constraint;
        }

        public double RelativeX()
        {
            return body.p_x - bank.GetX();
        }

        public double RelativeY()
        {
            return body.p_y - bank.GetY();
        }

        public double CalculateCylinderWallForce()
        {
            return Math.Sqrt(
                cylinderConstraint.F_x[0, 0] * cylinderConstraint.F_x[0, 0]
                + cylinderConstraint.F_y[0, 0] * cylinderConstraint.F_y[0, 0]);
        }

        public ConnectingRod GetRod() => rod;
        public CylinderBank GetCylinderBank() => bank;
        public int GetCylinderIndex() => cylinderIndex;
        public double GetCompressionHeight() => compressionHeight;
        public double GetDisplacement() => displacement;
        public double GetWristPinLocation() => wristPinLocation;
        public double GetMass() => mass;
        public double GetBlowbyK() => blowby_k;

        // Read access for the dense core's snapshot.
        internal LineConstraint GetCylinderConstraint() => cylinderConstraint;
    }
}
