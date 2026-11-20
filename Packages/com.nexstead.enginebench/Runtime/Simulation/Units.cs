// Port of engine-sim include/units.h and include/constants.h.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>Upstream's physical constants. <c>pi</c> is upstream's truncated value, used as is.</summary>
    public static class Constants
    {
        public const double pi = 3.14159265359;
        public const double R = 8.31446261815324;
        public const double root_2 = 1.41421356237309504880168872420969807856967187537694807317667973799;
        public const double e = 2.71828182845904523536028747135266249775724709369995;
    }

    /// <summary>Upstream's unit system: SI underneath, each unit a factor.</summary>
    public static class Units
    {
        // Force
        public const double N = 1.0;
        public const double lbf = N * 4.44822;

        // Mass
        public const double kg = 1.0;
        public const double g = kg / 1000.0;
        public const double lb = 0.45359237 * kg;

        // Distance
        public const double m = 1.0;
        public const double cm = m / 100.0;
        public const double mm = m / 1000.0;
        public const double km = m * 1000.0;
        public const double inch = cm * 2.54;
        public const double foot = inch * 12.0;
        public const double thou = inch / 1000.0;
        public const double mile = m * 1609.344;

        // Time
        public const double sec = 1.0;
        public const double minute = 60 * sec;
        public const double hour = 60 * minute;

        // Torque
        public const double Nm = N * m;
        public const double ft_lb = foot * lbf;

        // Power
        public const double W = Nm / sec;
        public const double kW = W * 1000.0;
        public const double hp = 745.699872 * W;

        // Volume
        public const double m3 = 1.0;
        public const double cc = cm * cm * cm;
        public const double mL = cc;
        public const double L = mL * 1000.0;
        public const double cubic_feet = foot * foot * foot;
        public const double cubic_inches = inch * inch * inch;
        public const double gal = 3.785411784 * L;

        // Molecular
        public const double mol = 1.0;
        public const double kmol = mol / 1000.0;
        public const double mmol = mol / 1000000.0;
        public const double lbmol = mol * 453.59237;

        // Flow-rate (moles)
        public const double mol_per_sec = mol / sec;
        public const double scfm = 0.002641 * lbmol / minute;

        // Area
        public const double m2 = 1.0;
        public const double cm2 = cm * cm;

        // Pressure
        public const double Pa = 1.0;
        public const double kPa = Pa * 1000.0;
        public const double MPa = Pa * 1000000.0;
        public const double atm = 101.325 * kPa;
        public const double mbar = Pa * 100.0;
        public const double bar = mbar * 1000.0;
        public const double psi = lbf / (inch * inch);
        public const double psig = psi;
        public const double inHg = Pa * 3386.3886666666713;
        public const double inH2O = inHg * 0.0734824;

        // Temperature
        public const double K = 1.0;
        public const double K0 = 273.15;
        public const double C = K;
        public const double F = (5.0 / 9.0) * K;
        public const double F0 = -459.67;

        // Energy
        public const double J = 1.0;
        public const double kJ = J * 1000;
        public const double MJ = J * 1000000;

        // Angles
        public const double rad = 1.0;
        public const double deg = rad * (Constants.pi / 180);

        // Physical constants
        public const double AirMolecularMass = 28.97 * g / mol;

        // Conversions
        public static double Distance(double v, double unit) => v * unit;
        public static double Area(double v, double unit) => v * unit;
        public static double Torque(double v, double unit) => v * unit;
        public static double Rpm(double rpm) => rpm * 0.104719755;
        public static double ToRpm(double rad_s) => rad_s / 0.104719755;
        public static double Pressure(double v, double unit) => v * unit;
        public static double Psia(double p) => Pressure(p, psig) - Pressure(1.0, atm);
        public static double ToPsia(double p) => (p + Pressure(1.0, atm)) / psig;
        public static double Mass(double v, double unit) => v * unit;
        public static double Force(double v, double unit) => v * unit;
        public static double Volume(double v, double unit) => v * unit;
        public static double Flow(double v, double unit) => v * unit;
        public static double Convert(double v, double unit0, double unit1) => v * (unit0 / unit1);
        public static double Convert(double v, double unit) => v / unit;
        public static double Celcius(double T_C) => T_C * C + K0;
        public static double Kelvin(double T) => T * K;
        public static double Fahrenheit(double T_F) => F * (T_F - F0);
        public static double ToAbsoluteFahrenheit(double T) => T / F;
        public static double Angle(double v, double unit) => v * unit;
        public static double Energy(double v, double unit) => v * unit;
    }
}
