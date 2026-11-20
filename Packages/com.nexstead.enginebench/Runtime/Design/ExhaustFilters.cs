// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;

namespace Nexstead.EngineBench.Design
{
    /// <summary>
    /// One of the package's exhaust filters: an impulse response the exhaust's pulses go through (the muffler, and the space
    /// to the listener), with its place on the ladder, its name and what it sounds like.
    /// </summary>
    public sealed class ExhaustFilter
    {
        /// <summary>Its place on the ladder from least to most muffled, 1 to 7; 0 for a variant off the ladder.</summary>
        public readonly int step;
        public readonly string name;
        /// <summary>What it sounds like, in a few words.</summary>
        public readonly string character;
        /// <summary>The impulse response as engine descriptions name it, at the volume that plays it as loud as the others.</summary>
        public readonly ImpulseResponseNode response;
        /// <summary>Where its samples come from: engine-sim's recording, or how it is derived from one.</summary>
        public readonly string origin;

        public ExhaustFilter(int step, string name, string character, string file, double volume, string origin)
        {
            this.step = step;
            this.name = name;
            this.character = character;
            response = new ImpulseResponseNode(file, volume);
            this.origin = origin;
        }

        /// <summary>How pickers show it: "5 · Stock (smooth, factory muffler)"; a variant without the number.</summary>
        public string Label => step > 0 ? $"{step} · {name} ({character})" : $"{name} ({character})";

        /// <summary>Its preset asset's name: "5-stock", "muffled-roomy".</summary>
        public string AssetName
        {
            get
            {
                string slug = name.ToLowerInvariant().Replace(",", "").Replace(' ', '-');
                return step > 0 ? $"{step}-{slug}" : slug;
            }
        }
    }

    /// <summary>
    /// The package's exhaust filters: a ladder of seven from least to most muffled (each lets less of the top end through:
    /// less of the energy at 2 kHz and up), and two variants of another character. Their volumes play them equally loud
    /// (the root of the energy they pass from a broadband signal), Stock at engine-sim's default volume, so moving along the
    /// ladder changes the tone, not the loudness. Seven are engine-sim's recordings, named by its files; Street and Comfort
    /// are derived from Sport and Stock through a gentle low-pass, as engine-sim's own equalised filters were made.
    /// </summary>
    public static class ExhaustFilters
    {
        /// <summary>Where the derived filters' names sit: not files of engine-sim's sound library.</summary>
        public const string DerivedFolder = "derived/";

        /// <summary>
        /// The low-pass that derives Street and Comfort: a one-pole filter, y += k (x − y), with this k (about 2.2 kHz at
        /// 44.1 kHz), over every sample of its source, rounded to 16 bits.
        /// </summary>
        public const double DerivingLowPass = 0.27;

        public static readonly ExhaustFilter StraightPipe = new ExhaustFilter(1, "Straight pipe", "raw, all rasp",
            ImpulseResponseLibrary.MinimalMuffling03.file, 0.002695, "engine-sim's new/minimal_muffling_03.wav");
        public static readonly ExhaustFilter Race = new ExhaustFilter(2, "Race", "loud rasp, light body",
            ImpulseResponseLibrary.MinimalMuffling01.file, 0.001224, "engine-sim's new/minimal_muffling_01.wav");
        public static readonly ExhaustFilter Sport = new ExhaustFilter(3, "Sport", "bright, with body",
            ImpulseResponseLibrary.RealEngine2.file, 0.0009414, "engine-sim's archive/test_engine_16_eq_adjusted_16.wav");
        public static readonly ExhaustFilter Street = new ExhaustFilter(4, "Street", "balanced, mild rasp",
            DerivedFolder + "street.wav", 0.001167, "Sport through the deriving low-pass");
        public static readonly ExhaustFilter Stock = new ExhaustFilter(5, "Stock", "smooth, factory muffler",
            ImpulseResponseLibrary.Default.file, 0.001, "engine-sim's smooth/smooth_39.wav (its default)");
        public static readonly ExhaustFilter Comfort = new ExhaustFilter(6, "Comfort", "quiet, soft edges",
            DerivedFolder + "comfort.wav", 0.001051, "Stock through the deriving low-pass");
        public static readonly ExhaustFilter Muffled = new ExhaustFilter(7, "Muffled", "deep boom, no rasp",
            ImpulseResponseLibrary.MildExhaust0.file, 0.001129, "engine-sim's new/mild_exhaust.wav");

        public static readonly ExhaustFilter Warm = new ExhaustFilter(0, "Warm", "stock, fuller body",
            ImpulseResponseLibrary.MinimalMuffling02.file, 0.00167, "engine-sim's new/minimal_muffling_02.wav");
        public static readonly ExhaustFilter MuffledRoomy = new ExhaustFilter(0, "Muffled, roomy", "deep boom, short tail",
            ImpulseResponseLibrary.MildExhaust0Reverb.file, 0.001505, "engine-sim's new/mild_exhaust_reverb.wav");

        /// <summary>The ladder, from least to most muffled.</summary>
        public static readonly ExhaustFilter[] Ladder = { StraightPipe, Race, Sport, Street, Stock, Comfort, Muffled };

        /// <summary>The variants off the ladder.</summary>
        public static readonly ExhaustFilter[] Variants = { Warm, MuffledRoomy };

        /// <summary>Every filter: the ladder, then the variants.</summary>
        public static readonly ExhaustFilter[] All =
        {
            StraightPipe, Race, Sport, Street, Stock, Comfort, Muffled, Warm, MuffledRoomy,
        };

        /// <summary>The filter engine descriptions name <paramref name="file"/>; null if none is.</summary>
        public static ExhaustFilter Of(string file) => Array.Find(All, filter => filter.response.file == file);
    }
}
