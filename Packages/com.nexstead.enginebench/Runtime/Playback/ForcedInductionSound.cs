// Part of Nx Engine Bench, based on engine-sim and its community fork's forced induction (not an upstream file).
// MIT licence: LICENSE.md; the notices: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>An engine's turbocharger or supercharger, by how it sounds. Part of the render API.</summary>
    public enum ForcedInductionKind
    {
        None,
        /// <summary>A whistle that spools with the exhaust flow (single, twin or sequential turbos).</summary>
        Turbocharger,
        /// <summary>A rotor whine at 6 lobes per rotor turn, locked to the revs.</summary>
        Roots,
        /// <summary>A rotor whine at 5 lobes per rotor turn, locked to the revs.</summary>
        TwinScrew,
        /// <summary>A whistle locked to the revs through the drive ratio.</summary>
        Centrifugal,
    }

    /// <summary>Where a blow-off valve vents, or none. Part of the render API.</summary>
    public enum BlowoffValveKind
    {
        /// <summary>A sharp "pssh".</summary>
        Atmospheric,
        /// <summary>A soft whoosh.</summary>
        Recirculating,
        /// <summary>
        /// No blow-off sound: no valve, or a Roots or twin-screw blower, whose bypass valve makes none. Without a valve the
        /// charge stays trapped on a lift. The model's compressor surge (an 11–18 Hz flutter, <see cref="ForcedInductionTelemetry.surgeLevel"/>)
        /// needs reverse flow through the compressor, which a lift hardly makes: the wheel keeps spinning faster than the
        /// trapped charge bleeds away.
        /// </summary>
        None,
    }

    /// <summary>
    /// The forced induction's state after a block: what its sounds follow. The levels are the synthesizer's control channels at
    /// the block's last step, each 0 to 1. Part of the render API.
    /// </summary>
    public readonly struct ForcedInductionTelemetry
    {
        /// <summary>The (first) compressor's shaft or rotor speed (Hz).</summary>
        public readonly double shaftHz;
        /// <summary>The whistle's or the rotor whine's tone at that speed (<see cref="ForcedInductionSound.ToneHz"/>).</summary>
        public readonly double toneHz;
        /// <summary>Charge pressure above the intake's atmosphere (kPa).</summary>
        public readonly double boostKpa;
        public readonly double whineLevel;
        public readonly double blowoffLevel;
        /// <summary>Compressor flow against its capacity.</summary>
        public readonly double flowLevel;
        public readonly double surgeLevel;
        /// <summary>An external wastegate's venting.</summary>
        public readonly double wastegateNoise;

        public ForcedInductionTelemetry(double shaftHz, double toneHz, double boostKpa, double whineLevel, double blowoffLevel,
            double flowLevel, double surgeLevel, double wastegateNoise)
        {
            this.shaftHz = shaftHz;
            this.toneHz = toneHz;
            this.boostKpa = boostKpa;
            this.whineLevel = whineLevel;
            this.blowoffLevel = blowoffLevel;
            this.flowLevel = flowLevel;
            this.surgeLevel = surgeLevel;
            this.wastegateNoise = wastegateNoise;
        }
    }

    /// <summary>How the synthesizer pitches the forced induction's sounds. Part of the render API.</summary>
    public static class ForcedInductionSound
    {
        /// <summary>
        /// The tone the synthesizer makes at a shaft speed: a turbocharger's whistle is 400 + 2.4 × shaft Hz × pitch, a centrifugal
        /// supercharger's 400 + 2.0 × shaft Hz × pitch, both within 300–9000 Hz (so the pitch is not proportional to the shaft
        /// speed); a Roots or twin-screw whine is the lobe frequency, 6 or 5 × rotor Hz × pitch, up to a fifth of the sample rate.
        /// 0 for <see cref="ForcedInductionKind.None"/>.
        /// </summary>
        public static double ToneHz(ForcedInductionKind kind, double shaftHz, double whistlePitch)
        {
            double shaft = Math.Max(shaftHz * whistlePitch, 0.0);
            switch (kind)
            {
                case ForcedInductionKind.Turbocharger: return Utilities.Clamp(400.0 + 2.4 * shaft, 300.0, 9000.0);
                case ForcedInductionKind.Centrifugal: return Utilities.Clamp(400.0 + 2.0 * shaft, 300.0, 9000.0);
                case ForcedInductionKind.Roots: return Math.Min(shaft * 6.0, 0.2 * EngineSoundGenerator.AudioSampleRate);
                case ForcedInductionKind.TwinScrew: return Math.Min(shaft * 5.0, 0.2 * EngineSoundGenerator.AudioSampleRate);
                default: return 0.0;
            }
        }

        internal static ForcedInductionKind KindOf(ForcedInduction forcedInduction) =>
            forcedInduction == null ? ForcedInductionKind.None : KindOf(forcedInduction.GetForcedInductionType());

        internal static ForcedInductionKind KindOf(ForcedInduction.Type type)
        {
            switch (type)
            {
                case ForcedInduction.Type.Roots: return ForcedInductionKind.Roots;
                case ForcedInduction.Type.TwinScrew: return ForcedInductionKind.TwinScrew;
                case ForcedInduction.Type.Centrifugal: return ForcedInductionKind.Centrifugal;
                default: return ForcedInductionKind.Turbocharger;
            }
        }

        internal static BlowoffValveKind BlowoffOf(ForcedInduction forcedInduction)
        {
            if (forcedInduction == null || forcedInduction.IsPositiveDisplacement()) return BlowoffValveKind.None;
            switch (forcedInduction.GetBlowoffType())
            {
                case ForcedInduction.BlowoffType.Atmospheric: return BlowoffValveKind.Atmospheric;
                case ForcedInduction.BlowoffType.Recirculating: return BlowoffValveKind.Recirculating;
                default: return BlowoffValveKind.None;
            }
        }
    }
}
