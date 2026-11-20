// Measures exhaust filters as the synthesizer plays them: tone (octave-band shares) and level.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Tests
{
    /// <summary>
    /// An exhaust filter's tone and level, over the samples the synthesizer keeps (<see cref="Synthesizer.KeptLength"/>):
    /// its energy in the octave bands from 63 Hz to 8 kHz (band edges 45 to 11360 Hz, a 65536-point spectrum), as shares of
    /// their sum, and its level at a volume.
    /// </summary>
    public static class ExhaustFilterMeasure
    {
        /// <summary>The octave bands' edges (Hz), 63 Hz to 8 kHz.</summary>
        public static readonly double[] BandEdges = { 45, 90, 180, 355, 710, 1420, 2840, 5680, 11360 };
        const int SpectrumSize = 1 << 16;

        /// <summary>Each octave band's share of the energy in all of them.</summary>
        public static double[] BandShares(short[] samples, int sampleRate)
        {
            int kept = Synthesizer.KeptLength(samples, samples.Length);
            var re = new float[SpectrumSize];
            var im = new float[SpectrumSize];
            for (int i = 0; i < kept; ++i) re[i] = samples[i] / 32767f;
            new Fft(SpectrumSize).Forward(re, im);

            var bands = new double[BandEdges.Length - 1];
            for (int k = 0; k <= SpectrumSize / 2; ++k)
            {
                double frequency = (double)k * sampleRate / SpectrumSize;
                for (int b = 0; b < bands.Length; ++b)
                {
                    if (frequency >= BandEdges[b] && frequency < BandEdges[b + 1]) bands[b] += (double)re[k] * re[k] + (double)im[k] * im[k];
                }
            }

            double total = 0;
            foreach (double band in bands) total += band;
            for (int b = 0; b < bands.Length; ++b) bands[b] /= total;
            return bands;
        }

        /// <summary>The share of the energy at 2 kHz and up (1420 to 11360 Hz): how much rasp gets through.</summary>
        public static double Top(double[] shares) => shares[5] + shares[6] + shares[7];

        /// <summary>The share of the energy at 125 Hz and down (45 to 180 Hz): how much boom.</summary>
        public static double Boom(double[] shares) => shares[0] + shares[1];

        /// <summary>
        /// The filter's level (dB) at <paramref name="volume"/>: the gain it gives a broadband signal, the root of its kept
        /// samples' energy (each scaled to ±1) times the volume.
        /// </summary>
        public static double LevelDb(short[] samples, double volume)
        {
            int kept = Synthesizer.KeptLength(samples, samples.Length);
            double energy = 0;
            for (int i = 0; i < kept; ++i)
            {
                double v = samples[i] / 32767.0;
                energy += v * v;
            }

            return 20 * Math.Log10(volume * Math.Sqrt(energy));
        }
    }
}
