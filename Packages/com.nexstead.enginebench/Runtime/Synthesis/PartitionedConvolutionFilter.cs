// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>
    /// The same convolution as <see cref="ConvolutionFilter"/>, one output per input sample with no added latency, at a fraction
    /// of the cost. The first <see cref="BlockSize"/> taps are convolved directly. The rest are split into partitions of
    /// <see cref="BlockSize"/> taps and applied in the frequency domain (uniformly partitioned overlap-save): once per block,
    /// from input blocks that are already complete, the tail of the next block's output is computed.
    /// </summary>
    public sealed class PartitionedConvolutionFilter : ImpulseResponseFilter
    {
        public const int BlockSize = 128;
        const int FftSize = 2 * BlockSize;
        const int Bins = BlockSize + 1;

        static readonly Fft Transform = new Fft(FftSize);

        int sampleCount;
        float[] head;

        // Last BlockSize inputs, written twice so the window is contiguous: hist[p + 1 .. p + BlockSize].
        readonly float[] history = new float[2 * BlockSize];
        int position;

        // Tail partitions: spectra of the zero-padded partitions, and a delay line of input-segment spectra.
        int partitions;
        float[][] partitionRe, partitionIm;
        float[][] delayRe, delayIm;
        int newest;

        float[] previousBlock = new float[BlockSize];
        float[] currentBlock = new float[BlockSize];
        readonly float[] tail = new float[BlockSize];

        readonly float[] workRe = new float[FftSize];
        readonly float[] workIm = new float[FftSize];
        readonly float[] sumRe = new float[Bins];
        readonly float[] sumIm = new float[Bins];

        public override void SetImpulseResponse(float[] impulseResponse, int length)
        {
            sampleCount = length;
            head = new float[BlockSize];
            Array.Copy(impulseResponse, head, Math.Min(length, BlockSize));

            partitions = (length + BlockSize - 1) / BlockSize - 1;
            partitionRe = new float[partitions][];
            partitionIm = new float[partitions][];
            delayRe = new float[partitions][];
            delayIm = new float[partitions][];
            for (int k = 0; k < partitions; ++k)
            {
                Array.Clear(workRe, 0, FftSize);
                Array.Clear(workIm, 0, FftSize);
                int start = (k + 1) * BlockSize;
                Array.Copy(impulseResponse, start, workRe, 0, Math.Min(BlockSize, length - start));
                Transform.Forward(workRe, workIm);

                partitionRe[k] = new float[Bins];
                partitionIm[k] = new float[Bins];
                Array.Copy(workRe, partitionRe[k], Bins);
                Array.Copy(workIm, partitionIm[k], Bins);

                delayRe[k] = new float[Bins];
                delayIm[k] = new float[Bins];
            }

            Array.Clear(history, 0, history.Length);
            Array.Clear(previousBlock, 0, BlockSize);
            Array.Clear(currentBlock, 0, BlockSize);
            Array.Clear(tail, 0, BlockSize);
            position = 0;
            newest = 0;
        }

        public override int GetSampleCount() => sampleCount;

        // Read access for the dense core's snapshot.
        internal static Fft SharedTransform => Transform;
        internal float[] Head => head;
        internal float[] History => history;
        internal int Position => position;
        internal int Partitions => partitions;
        internal float[][] PartitionRe => partitionRe;
        internal float[][] PartitionIm => partitionIm;
        internal float[][] DelayRe => delayRe;
        internal float[][] DelayIm => delayIm;
        internal int Newest => newest;
        internal float[] PreviousBlock => previousBlock;
        internal float[] CurrentBlock => currentBlock;
        internal float[] Tail => tail;

        public override float F(float sample)
        {
            int p = position;
            history[p] = sample;
            history[p + BlockSize] = sample;
            currentBlock[p] = sample;

            float result = 0;
            for (int j = 0; j < BlockSize; ++j)
            {
                result += head[j] * history[p + BlockSize - j];
            }

            result += tail[p];

            if (++position == BlockSize)
            {
                position = 0;
                if (partitions > 0) NextTail();

                float[] swap = previousBlock;
                previousBlock = currentBlock;
                currentBlock = swap;
            }

            return result;
        }

        /// <summary>
        /// At the end of a block: transforms the segment [previous block, this block], adds it to the delay line, and computes
        /// the next block's tail as the sum over partitions k ≥ 1 of partition k times the segment k − 1 blocks back.
        /// </summary>
        void NextTail()
        {
            newest = newest == 0 ? partitions - 1 : newest - 1;

            Array.Copy(previousBlock, 0, workRe, 0, BlockSize);
            Array.Copy(currentBlock, 0, workRe, BlockSize, BlockSize);
            Array.Clear(workIm, 0, FftSize);
            Transform.Forward(workRe, workIm);
            Array.Copy(workRe, delayRe[newest], Bins);
            Array.Copy(workIm, delayIm[newest], Bins);

            Array.Clear(sumRe, 0, Bins);
            Array.Clear(sumIm, 0, Bins);
            for (int k = 0; k < partitions; ++k)
            {
                int d = newest + k;
                if (d >= partitions) d -= partitions;

                float[] hr = partitionRe[k], hi = partitionIm[k];
                float[] xr = delayRe[d], xi = delayIm[d];
                for (int b = 0; b < Bins; ++b)
                {
                    sumRe[b] += hr[b] * xr[b] - hi[b] * xi[b];
                    sumIm[b] += hr[b] * xi[b] + hi[b] * xr[b];
                }
            }

            // Real signal: the upper half of the spectrum is the conjugate mirror of the lower.
            for (int b = 0; b < Bins; ++b)
            {
                workRe[b] = sumRe[b];
                workIm[b] = sumIm[b];
            }
            for (int b = Bins; b < FftSize; ++b)
            {
                workRe[b] = sumRe[FftSize - b];
                workIm[b] = -sumIm[FftSize - b];
            }

            Transform.Inverse(workRe, workIm);
            Array.Copy(workRe, BlockSize, tail, 0, BlockSize);
        }
    }

    /// <summary>In-place radix-2 complex FFT of a fixed power-of-two size, single precision.</summary>
    public sealed class Fft
    {
        readonly int size;
        readonly int[] bitReverse;
        readonly float[] cos;
        readonly float[] sin;

        public Fft(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("FFT size must be a power of two.", nameof(size));

            this.size = size;
            int bits = 0;
            while ((1 << bits) < size) ++bits;

            bitReverse = new int[size];
            for (int i = 0; i < size; ++i)
            {
                int r = 0;
                for (int b = 0; b < bits; ++b) r |= ((i >> b) & 1) << (bits - 1 - b);
                bitReverse[i] = r;
            }

            cos = new float[size / 2];
            sin = new float[size / 2];
            for (int i = 0; i < size / 2; ++i)
            {
                double angle = -2.0 * Math.PI * i / size;
                cos[i] = (float)Math.Cos(angle);
                sin[i] = (float)Math.Sin(angle);
            }
        }

        public int Size => size;

        // Read access for the dense core's snapshot.
        internal int[] BitReverse => bitReverse;
        internal float[] Cos => cos;
        internal float[] Sin => sin;

        /// <summary>X[k] = Σ x[n] e^(−2πikn/N).</summary>
        public void Forward(float[] re, float[] im) => Transform(re, im, 1.0f);

        /// <summary>x[n] = (1/N) Σ X[k] e^(2πikn/N).</summary>
        public void Inverse(float[] re, float[] im)
        {
            Transform(re, im, -1.0f);
            float scale = 1.0f / size;
            for (int i = 0; i < size; ++i)
            {
                re[i] *= scale;
                im[i] *= scale;
            }
        }

        void Transform(float[] re, float[] im, float direction)
        {
            for (int i = 0; i < size; ++i)
            {
                int j = bitReverse[i];
                if (j > i)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            for (int length = 2; length <= size; length <<= 1)
            {
                int half = length >> 1;
                int stride = size / length;
                for (int start = 0; start < size; start += length)
                {
                    for (int k = 0; k < half; ++k)
                    {
                        float wr = cos[k * stride];
                        float wi = direction * sin[k * stride];

                        int a = start + k;
                        int b = a + half;
                        float xr = re[b] * wr - im[b] * wi;
                        float xi = re[b] * wi + im[b] * wr;

                        re[b] = re[a] - xr;
                        im[b] = im[a] - xi;
                        re[a] += xr;
                        im[a] += xi;
                    }
                }
            }
        }
    }
}
