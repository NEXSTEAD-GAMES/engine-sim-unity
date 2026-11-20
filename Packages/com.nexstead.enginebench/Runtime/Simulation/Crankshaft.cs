// Port of engine-sim include/crankshaft.h and src/crankshaft.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class Crankshaft : Part
    {
        public sealed class Parameters
        {
            public double mass;
            public double flywheelMass;
            public double momentOfInertia;
            public double crankThrow;
            public double pos_x = 0;
            public double pos_y = 0;
            public double tdc = 0;
            public double frictionTorque = 0;
            public int rodJournals;
        }

        double[] rodJournalAngles;
        int rodJournalCount;
        double tdc;
        double @throw;
        double m;
        double I;
        double flywheelMass;
        double p_x;
        double p_y;
        double frictionTorque;

        public void Initialize(Parameters @params)
        {
            m = @params.mass;
            flywheelMass = @params.flywheelMass;
            I = @params.momentOfInertia;
            @throw = @params.crankThrow;

            rodJournalCount = @params.rodJournals;
            rodJournalAngles = new double[rodJournalCount];

            p_x = @params.pos_x;
            p_y = @params.pos_y;
            tdc = @params.tdc;
            frictionTorque = @params.frictionTorque;
        }

        public int GetRodJournalCount() => rodJournalCount;

        public void SetRodJournalAngle(int i, double angle)
        {
            rodJournalAngles[i] = angle;
        }

        public void GetRodJournalPositionLocal(int i, out double x, out double y)
        {
            double theta = rodJournalAngles[i];

            x = Math.Cos(theta) * @throw;
            y = Math.Sin(theta) * @throw;
        }

        public void GetRodJournalPositionGlobal(int i, out double x, out double y)
        {
            GetRodJournalPositionLocal(i, out double lx, out double ly);

            x = lx + body.p_x;
            y = ly + body.p_y;
        }

        public double GetRodJournalAngle(int i) => rodJournalAngles[i];

        public void ResetAngle()
        {
            body.theta = body.theta % (4 * Constants.pi);
        }

        public double GetAngle()
        {
            return body.theta - tdc;
        }

        public double GetCycleAngle(double offset = 0.0)
        {
            double wrapped = (-GetAngle() + offset) % (4 * Constants.pi);
            return (wrapped < 0)
                ? wrapped + 4 * Constants.pi
                : wrapped;
        }

        public double GetTdc() => tdc;
        public double GetThrow() => @throw;
        public double GetMass() => m;
        public double GetMomentOfInertia() => I;
        public double GetFlywheelMass() => flywheelMass;
        public double GetPosX() => p_x;
        public double GetPosY() => p_y;
        public double GetFrictionTorque() => frictionTorque;
    }
}
