// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// Streaming mono sample-rate converter: the synthesizer's rate in, the audio device's rate out. Band-limited
    /// interpolation with a Kaiser-windowed sinc, tabulated at <see cref="PhaseCount"/> fractional positions and interpolated
    /// linearly between them. Equal rates pass samples through unchanged, with no latency.
    /// </summary>
    /// <remarks>
    /// <para><b>Filter.</b> The cutoff sits at half the lower of the two rates, so upsampling rejects the input's images and
    /// downsampling rejects what would alias. The window is <see cref="BaseTapsPerSide"/> input samples each side when
    /// upsampling, scaled by input rate ÷ output rate when downsampling (the cutoff is narrower). Kaiser β =
    /// <see cref="KaiserBeta"/>. At 44.1 kHz in, the transition band runs from about 20 kHz to 24 kHz. Each phase's
    /// coefficients are normalised to sum to 1, so the DC gain is exactly 1 at every fractional position.</para>
    /// <para><b>Timing.</b> Output sample m is the input signal at input time m × inputRate ÷ outputRate: the filter is
    /// symmetric, so it adds no delay to the signal. To compute it, the input must have reached <see cref="Latency"/> samples
    /// beyond that time, which is the converter's processing latency. Before the first input sample the signal is taken as
    /// silence.</para>
    /// <para><b>Position.</b> The input position is kept exactly as an integer index and a remainder in units of
    /// 1 ÷ outputRate, so it never drifts, however long the stream runs.</para>
    /// </remarks>
    public sealed class Resampler
    {
        /// <summary>Fractional positions tabulated between two input samples.</summary>
        public const int PhaseCount = 512;

        /// <summary>Window half-width in input samples when upsampling.</summary>
        public const int BaseTapsPerSide = 32;

        public const double KaiserBeta = 9.0;

        readonly int inputRate;
        readonly int outputRate;
        readonly int tapsPerSide;
        readonly int taps;

        // PhaseCount + 1 rows of taps coefficients, and each row's difference to the next.
        readonly float[] coefficients;
        readonly float[] deltas;

        // Input history: history[j] is input sample historyBase + j.
        readonly float[] history;
        long historyBase;
        int historyCount;

        // The next output's input time: inputIndex + remainder / outputRate.
        long inputIndex;
        int remainder;

        /// <param name="maxInputPerCall">The most input one <see cref="Process"/> call may pass.</param>
        public Resampler(int inputRate, int outputRate, int maxInputPerCall = 4096)
        {
            if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
            if (outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputRate));
            if (maxInputPerCall <= 0) throw new ArgumentOutOfRangeException(nameof(maxInputPerCall));

            this.inputRate = inputRate;
            this.outputRate = outputRate;

            if (inputRate == outputRate)
            {
                tapsPerSide = 0;
                taps = 0;
            }
            else
            {
                int lowerRate = Math.Min(inputRate, outputRate);
                tapsPerSide = (int)Math.Ceiling(BaseTapsPerSide * (double)inputRate / lowerRate);
                taps = 2 * tapsPerSide;

                // Cutoff in cycles per input sample.
                double cutoff = 0.5 * lowerRate / inputRate;
                coefficients = new float[(PhaseCount + 1) * taps];
                deltas = new float[PhaseCount * taps];
                BuildTable(cutoff);
            }

            history = new float[2 * maxInputPerCall + taps + 2];
            Reset();
        }

        public int InputRate => inputRate;
        public int OutputRate => outputRate;
        public bool IsPassThrough => taps == 0;

        /// <summary>Input samples needed beyond an output sample's time before it can be produced (0 for a pass-through).</summary>
        public int Latency => tapsPerSide;

        /// <summary>The most output a call passing <paramref name="inputCount"/> samples can produce, when no output was left pending.</summary>
        public int MaxOutputCount(int inputCount)
        {
            return (int)(((long)inputCount * outputRate + inputRate - 1) / inputRate) + 1;
        }

        /// <summary>Back to silence at input time 0.</summary>
        public void Reset()
        {
            // The window reaches tapsPerSide − 1 samples before the first output's time: they start as silence.
            int lead = Math.Max(0, tapsPerSide - 1);
            Array.Clear(history, 0, history.Length);
            historyBase = -lead;
            historyCount = lead;
            inputIndex = 0;
            remainder = 0;
        }

        /// <summary>
        /// Takes all of <paramref name="input"/> and writes every output sample it makes available, up to
        /// <paramref name="outputCapacity"/>; returns how many. Output left over for lack of capacity comes out on the next call.
        /// Giving at least <see cref="MaxOutputCount"/>(<paramref name="inputCount"/>) of capacity keeps nothing pending.
        /// </summary>
        public int Process(float[] input, int inputOffset, int inputCount, float[] output, int outputOffset, int outputCapacity)
        {
            Append(input, inputOffset, inputCount);

            int written = 0;
            if (taps == 0)
            {
                // Pass-through: output sample m is input sample m.
                int available = (int)(historyBase + historyCount - inputIndex);
                written = Math.Min(available, outputCapacity);
                Array.Copy(history, (int)(inputIndex - historyBase), output, outputOffset, written);
                inputIndex += written;
                return written;
            }

            long end = historyBase + historyCount;
            while (written < outputCapacity && inputIndex + tapsPerSide < end)
            {
                double phasePosition = (double)remainder * PhaseCount / outputRate;
                int phase = (int)phasePosition;
                float t = (float)(phasePosition - phase);

                int row = phase * taps;
                int first = (int)(inputIndex - tapsPerSide + 1 - historyBase);
                float sum = 0;
                for (int j = 0; j < taps; ++j)
                {
                    sum += (coefficients[row + j] + deltas[row + j] * t) * history[first + j];
                }

                output[outputOffset + written++] = sum;

                remainder += inputRate;
                if (remainder >= outputRate)
                {
                    inputIndex += remainder / outputRate;
                    remainder %= outputRate;
                }
            }

            return written;
        }

        void Append(float[] input, int offset, int count)
        {
            if (count <= 0) return;

            if (historyCount + count > history.Length)
            {
                // Drop the samples no output needs any more: everything before the next output's window.
                long keepFrom = taps == 0 ? inputIndex : inputIndex - tapsPerSide + 1;
                int drop = (int)(keepFrom - historyBase);
                if (drop > 0)
                {
                    Array.Copy(history, drop, history, 0, historyCount - drop);
                    historyBase += drop;
                    historyCount -= drop;
                }

                if (historyCount + count > history.Length)
                {
                    throw new InvalidOperationException(
                        "Resampler input exceeds its history: pass at most maxInputPerCall per call and take the output.");
                }
            }

            Array.Copy(input, offset, history, historyCount, count);
            historyCount += count;
        }

        /// <summary>Row p holds h(p / PhaseCount − k) for taps k = −tapsPerSide + 1 … tapsPerSide, normalised to sum to 1.</summary>
        void BuildTable(double cutoff)
        {
            double i0Beta = BesselI0(KaiserBeta);
            for (int p = 0; p <= PhaseCount; ++p)
            {
                double fraction = (double)p / PhaseCount;
                double sum = 0;
                var row = new double[taps];
                for (int j = 0; j < taps; ++j)
                {
                    int k = j - tapsPerSide + 1;
                    double tau = fraction - k;
                    row[j] = WindowedSinc(tau, cutoff, i0Beta);
                    sum += row[j];
                }

                for (int j = 0; j < taps; ++j)
                {
                    coefficients[p * taps + j] = (float)(row[j] / sum);
                }
            }

            for (int p = 0; p < PhaseCount; ++p)
            {
                for (int j = 0; j < taps; ++j)
                {
                    deltas[p * taps + j] = coefficients[(p + 1) * taps + j] - coefficients[p * taps + j];
                }
            }
        }

        double WindowedSinc(double tau, double cutoff, double i0Beta)
        {
            double u = tau / tapsPerSide;
            if (u <= -1.0 || u >= 1.0) return 0.0;

            double x = 2.0 * cutoff * tau;
            double sinc = x == 0.0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
            double window = BesselI0(KaiserBeta * Math.Sqrt(1.0 - u * u)) / i0Beta;
            return 2.0 * cutoff * sinc * window;
        }

        /// <summary>The modified Bessel function of the first kind, order 0 (power series).</summary>
        static double BesselI0(double x)
        {
            double sum = 1.0, term = 1.0, half = 0.5 * x;
            for (int k = 1; k < 200; ++k)
            {
                term *= half / k;
                double square = term * term;
                sum += square;
                if (square < 1e-17 * sum) break;
            }

            return sum;
        }
    }
}
