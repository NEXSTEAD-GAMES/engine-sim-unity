// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// What the front view draws an engine with, from its description: the crank, the banks and, per cylinder, its bank and
    /// its piston and rod sizes. Positions are the simulation's (metres, y up). Also gives a still pose, when no engine runs.
    /// </summary>
    sealed class EngineGeometry
    {
        public readonly double crankX, crankY, crankThrow;
        /// <summary>The crank angle at which the first cylinder is at top dead centre (rad).</summary>
        public readonly double tdc;
        public readonly Bank[] banks;
        public readonly Cylinder[] cylinders;

        public sealed class Bank
        {
            public double x, y, dx, dy, bore, deckHeight;
            public int cylinderCount;
        }

        public sealed class Cylinder
        {
            public int bank;
            /// <summary>Its index within its bank: 0 is drawn in front.</summary>
            public int depth;
            public double rodLength, rodCentreOfMass, compressionHeight, journalAngle;
        }

        public EngineGeometry(EngineDescription description)
        {
            EngineParametersDescription e = description.engine;
            CrankshaftDescription crank = e.crankshafts[0];
            crankX = crank.positionX;
            crankY = crank.positionY;
            crankThrow = crank.@throw;
            tdc = crank.tdc;

            banks = new Bank[e.cylinderBanks.Length];
            int count = 0;
            for (int b = 0; b < banks.Length; ++b)
            {
                CylinderBankDescription d = e.cylinderBanks[b];
                banks[b] = new Bank
                {
                    x = d.positionX, y = d.positionY, dx = Math.Cos(d.angle + Constants.pi / 2), dy = Math.Sin(d.angle + Constants.pi / 2),
                    bore = d.bore, deckHeight = d.deckHeight, cylinderCount = d.cylinderCount,
                };
                count += d.cylinderCount;
            }

            // Pistons and rods are listed bank by bank.
            cylinders = new Cylinder[count];
            for (int b = 0, c = 0; b < banks.Length; ++b)
            {
                for (int k = 0; k < banks[b].cylinderCount; ++k, ++c)
                {
                    ConnectingRodDescription rod = e.connectingRods[c];
                    double[] journals = e.crankshafts[Math.Max(0, rod.crankshaft)].rodJournalAngles;
                    cylinders[c] = new Cylinder
                    {
                        bank = b, depth = k, rodLength = rod.length, rodCentreOfMass = rod.centerOfMass,
                        compressionHeight = e.pistons[c].compressionHeight,
                        journalAngle = journals.Length > 0 ? journals[Math.Min(Math.Max(0, rod.journal), journals.Length - 1)] : 0,
                    };
                }
            }
        }

        /// <summary>
        /// A still pose at crank angle <paramref name="theta"/>, by the slider-crank geometry: for an engine that isn't
        /// running.
        /// </summary>
        public void StillPose(double theta, EnginePose pose)
        {
            pose.crankAngle = theta;
            for (int c = 0; c < cylinders.Length; ++c)
            {
                Cylinder cyl = cylinders[c];
                Bank b = banks[cyl.bank];
                double px = crankX + Math.Cos(theta + cyl.journalAngle) * crankThrow;
                double py = crankY + Math.Sin(theta + cyl.journalAngle) * crankThrow;
                double ox = px - b.x, oy = py - b.y;
                double along = ox * b.dx + oy * b.dy;
                double across = ox * -b.dy + oy * b.dx;
                double s = along + Math.Sqrt(Math.Max(0, cyl.rodLength * cyl.rodLength - across * across));
                pose.Set(c, s, px, py, b.x + b.dx * s, b.y + b.dy * s, 0, 0, 0);
            }
        }
    }

    /// <summary>One moment of an engine, as the front view draws it.</summary>
    sealed class EnginePose
    {
        public double crankAngle;
        public readonly double[] pistonTravel, bigEndX, bigEndY, littleEndX, littleEndY, intakeLift, exhaustLift, flash;

        public EnginePose(int cylinders)
        {
            pistonTravel = new double[cylinders];
            bigEndX = new double[cylinders];
            bigEndY = new double[cylinders];
            littleEndX = new double[cylinders];
            littleEndY = new double[cylinders];
            intakeLift = new double[cylinders];
            exhaustLift = new double[cylinders];
            flash = new double[cylinders];
        }

        public int Cylinders => pistonTravel.Length;

        public void Set(int c, double travel, double bigX, double bigY, double littleX, double littleY, double intake, double exhaust, double burn)
        {
            pistonTravel[c] = travel;
            bigEndX[c] = bigX;
            bigEndY[c] = bigY;
            littleEndX[c] = littleX;
            littleEndY[c] = littleY;
            intakeLift[c] = intake;
            exhaustLift[c] = exhaust;
            flash[c] = burn;
        }
    }
}
