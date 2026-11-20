// Ported from engine-sim (https://github.com/ange-yaghi/engine-sim): es/sound-library/impulse_responses.mr.
// MIT licence, Copyright (c) 2022 Ange Yaghi; see Third Party Notices.md.
namespace Nexstead.EngineBench.Design
{
    /// <summary>
    /// engine-sim's exhaust impulse responses (impulse_response_library) that the package ships, as engine descriptions name
    /// their files, at engine-sim's volumes, for engines written as engine-sim's scripts write them. The package's presets
    /// hold the same recordings under <see cref="ExhaustFilters"/>' names; engine-sim's real_engine_0, real_engine_1 and
    /// sharp_0 are left out (near-duplicates, and one a copy, of minimal_muffling_01 and _03).
    /// </summary>
    public static class ImpulseResponseLibrary
    {
        const string Folder = "es/sound-library/";

        public static readonly ImpulseResponseNode Default = new ImpulseResponseNode(Folder + "smooth/smooth_39.wav", 0.001);
        public static readonly ImpulseResponseNode RealEngine2 = new ImpulseResponseNode(Folder + "archive/test_engine_16_eq_adjusted_16.wav", 0.001);
        public static readonly ImpulseResponseNode MildExhaust0 = new ImpulseResponseNode(Folder + "new/mild_exhaust.wav", 0.01);
        public static readonly ImpulseResponseNode MildExhaust0Reverb = new ImpulseResponseNode(Folder + "new/mild_exhaust_reverb.wav", 0.01);
        public static readonly ImpulseResponseNode MinimalMuffling01 = new ImpulseResponseNode(Folder + "new/minimal_muffling_01.wav", 0.01);
        public static readonly ImpulseResponseNode MinimalMuffling02 = new ImpulseResponseNode(Folder + "new/minimal_muffling_02.wav", 0.01);
        public static readonly ImpulseResponseNode MinimalMuffling03 = new ImpulseResponseNode(Folder + "new/minimal_muffling_03.wav", 0.01);
    }
}
