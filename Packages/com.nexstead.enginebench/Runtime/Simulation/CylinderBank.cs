// Port of engine-sim include/cylinder_bank.h and src/cylinder_bank.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class CylinderBank
    {
        public sealed class Parameters
        {
            public Crankshaft crankshaft;
            public double positionX;
            public double positionY;
            public double angle;
            public double bore;
            public double deckHeight;
            public double displayDepth;
            public int cylinderCount;
            public int index;
        }

        double angle;
        double bore;
        double deckHeight;
        double displayDepth = 0.4;
        int cylinderCount;
        int index = -1;

        double dx;
        double dy;
        double x;
        double y;

        public void Initialize(Parameters @params)
        {
            angle = @params.angle;
            bore = @params.bore;
            deckHeight = @params.deckHeight;
            cylinderCount = @params.cylinderCount;

            dx = Math.Cos(angle + Constants.pi / 2);
            dy = Math.Sin(angle + Constants.pi / 2);

            x = @params.positionX;
            y = @params.positionY;

            displayDepth = @params.displayDepth;

            index = @params.index;
        }

        public void GetPositionAboveDeck(double h, out double x, out double y)
        {
            x = dx * (deckHeight + h) + this.x;
            y = dy * (deckHeight + h) + this.y;
        }

        public double BoreSurfaceArea()
        {
            return Constants.pi * bore * bore / 4.0;
        }

        public double GetAngle() => angle;
        public double GetBore() => bore;
        public double GetDeckHeight() => deckHeight;
        public int GetCylinderCount() => cylinderCount;
        public int GetIndex() => index;
        public double GetDx() => dx;
        public double GetDy() => dy;
        public double GetX() => x;
        public double GetY() => y;
        public double GetDisplayDepth() => displayDepth;
    }
}
