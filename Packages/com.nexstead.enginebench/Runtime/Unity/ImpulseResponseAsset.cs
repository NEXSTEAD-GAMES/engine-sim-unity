// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;
using UnityEngine;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// An exhaust impulse response as the synthesizer reads it: 16-bit samples at 44.1 kHz. Kept as raw samples, not an
    /// AudioClip: the synthesizer scales them by 1/32767, as upstream, and must see them unchanged by any import setting.
    /// </summary>
    public sealed class ImpulseResponseAsset : ScriptableObject
    {
        [SerializeField] short[] samples = System.Array.Empty<short>();
        [SerializeField] int sampleRate = 44100;
        [Tooltip("The file engine descriptions name it by: engine-sim's recording it holds, or the package's filter derived from one.")]
        [SerializeField] string sourceFile = "";

        public short[] Samples => samples;
        public int SampleRate => sampleRate;
        public string SourceFile => sourceFile;

        /// <summary>
        /// The sum of the squared samples the synthesizer keeps (<see cref="Synthesizer.KeptLength"/>), each scaled to ±1: how
        /// much energy the filter passes from a broadband signal, per unit of volume squared.
        /// </summary>
        public double Energy()
        {
            int kept = Synthesizer.KeptLength(samples, samples.Length);
            double sum = 0;
            for (int i = 0; i < kept; ++i)
            {
                double v = samples[i] / 32767.0;
                sum += v * v;
            }

            return sum;
        }

        public void Set(short[] samples, int sampleRate, string sourceFile)
        {
            this.samples = samples;
            this.sampleRate = sampleRate;
            this.sourceFile = sourceFile;
        }
    }
}
