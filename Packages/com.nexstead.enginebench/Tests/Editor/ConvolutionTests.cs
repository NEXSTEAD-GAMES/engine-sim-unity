// Checks of the partitioned convolution against upstream's direct form and the exact result.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Tests
{
    public sealed class ConvolutionTests
    {
        // Worst relative error measured on 2026-11-07 over engine-sim's 10 library impulse responses and these lengths: 9.1e-7 of the
        // output's peak (the direct form: up to 7.6e-6). The bound leaves twice that.
        const double RelativeErrorBound = 2e-6;

        static IEnumerable<TestCaseData> Cases()
        {
            foreach (string response in new[] { ExhaustFilters.Stock.AssetName, ExhaustFilters.MuffledRoomy.AssetName, ExhaustFilters.StraightPipe.AssetName })
            {
                foreach (int length in new[] { 1, 100, 128, 129, 1000, int.MaxValue })
                {
                    string name = length == int.MaxValue ? "full" : length.ToString();
                    yield return new TestCaseData(response, length).SetName($"PartitionedMatchesExact({response},{name})");
                }
            }
        }

        /// <summary>
        /// On a preset impulse response cut to a length (as the synthesizer cuts it), and an exhaust-like input, the
        /// partitioned convolution stays within <see cref="RelativeErrorBound"/> of the exact result and is no less accurate
        /// than upstream's direct form.
        /// </summary>
        [TestCaseSource(nameof(Cases))]
        public void PartitionedMatchesExact(string response, int length)
        {
            short[] samples = TestEngines.ImpulseResponse(response).Samples;
            int n = Math.Min(Synthesizer.KeptLength(samples, samples.Length), length);

            var h = new float[n];
            for (int i = 0; i < n; ++i) h[i] = 0.001f * samples[i] / short.MaxValue;

            var direct = new ConvolutionFilter();
            direct.SetImpulseResponse(h, n);
            var partitioned = new PartitionedConvolutionFilter();
            partitioned.SetImpulseResponse(h, n);

            var random = new DeterministicRandom();
            random.Seed(7, 0);
            const int inputLength = 12000;
            var x = new float[inputLength];
            double directError = 0, partitionedError = 0, peak = 0;
            for (int t = 0; t < inputLength; ++t)
            {
                // Pressure pulses, noise and a slow offset, at the synthesizer input's scale.
                x[t] = (float)(2000 * Math.Exp(-(t % 300 / 15.0)) + 300 * (random.NextDouble() - 0.5) + 500 * Math.Sin(t / 4000.0));

                double exact = 0;
                for (int j = 0; j < n && j <= t; ++j) exact += (double)h[j] * x[t - j];

                directError = Math.Max(directError, Math.Abs(direct.F(x[t]) - exact));
                partitionedError = Math.Max(partitionedError, Math.Abs(partitioned.F(x[t]) - exact));
                peak = Math.Max(peak, Math.Abs(exact));
            }

            Assert.LessOrEqual(partitionedError / peak, RelativeErrorBound, "partitioned error relative to the output peak");
            Assert.LessOrEqual(partitionedError, directError, "partitioned error against the direct form's");
        }
    }
}
