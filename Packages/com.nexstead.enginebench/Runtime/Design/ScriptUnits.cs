// Ported from engine-sim (https://github.com/ange-yaghi/engine-sim): es/constants/units.mr, es/constants/constants.mr.
// MIT licence, Copyright (c) 2022 Ange Yaghi; see Third Party Notices.md.
namespace Nexstead.EngineBench.Design
{
    /// <summary>
    /// engine-sim's script units and constants, each computed as its script library computes it, so a value written with
    /// them (<c>5.142 * ScriptUnits.inch</c>) is the double an engine script produces. SI base units are 1.
    /// </summary>
    public static class ScriptUnits
    {
        /// <summary>The script library's π (constants.mr), as engine-sim's C++ uses it: 3.14159265359.</summary>
        public const double pi = 3.14159265359;

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
        public const double minute = 60.0 * sec;
        public const double hour = 60.0 * minute;

        // Torque
        public const double Nm = N * m;
        public const double lb_ft = lbf * foot;

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
        public const double mmol = kmol / 1000.0;
        public const double lbmol = mol * 453.59237;

        // Flow rate
        public const double mol_per_sec = mol / sec;
        public const double scfm = 0.002641 * lbmol / minute;

        // Area
        public const double m2 = 1.0;
        public const double cm2 = cm * cm;

        // Pressure
        public const double Pa = 1.0;
        public const double kPa = Pa * 1000.0;
        public const double MPa = kPa * 1000.0;
        public const double atm = 101.325 * kPa;
        /// <summary>From pounds-force, as the C++ units: the community fork's fix of the script library, which had mass pounds.</summary>
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
        public const double kJ = J * 1000.0;
        public const double MJ = kJ * 1000.0;

        // Angles
        public const double rad = 1.0;
        public const double deg = rad * (pi / 180.0);

        // Engine speed
        public const double rpm = 0.104719755;

        // Speed
        public const double mph = mile / hour;
    }
}
