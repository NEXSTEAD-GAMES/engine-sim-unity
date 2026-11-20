// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense synthesizer's state: the managed Synthesizer (Runtime/Synthesis) and everything it
// owns, as unmanaged structs. See DenseEngine.cs for the rules; SynthesizerOps.cs for the code.
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public unsafe struct DenseSynthesizer
    {
        /// <summary>One per input channel (exhaust system), then one per control channel (only their input is used).</summary>
        public SynthChannel* channels;
        public int inputChannelCount;
        /// <summary>The forced induction's control channels: 0 or <see cref="Synthesizer.ForcedInductionChannelCount"/>.</summary>
        public int auxChannelCount;
        public int inputBufferSize;
        public int inputSamplesRead;
        public int latency;
        public double inputWriteOffset;
        public double lastInputSampleOffset;
        public int audioBufferSize;
        public float inputSampleRate;
        public float audioSampleRate;
        public Synthesizer.AudioParameters audioParameters;

        public SynthButterworthFloat antialiasing;
        public SynthLeveler leveler;
        public SynthRandom random;
        public SynthRingBuffer<short> audioBuffer;

        public SynthButterworthFloat forcedInductionAntialiasing;
        public SynthRingBuffer<float> forcedInductionBuffer;
        public SynthLeveler mixLeveler;
        public SynthRingBuffer<short> mixBuffer;
        public SynthForcedInduction forcedInduction;

        /// <summary>For parity checks: each rendered sample's forced induction sound before its antialiasing; null when off.</summary>
        public float* forcedInductionCapture;
        public int forcedInductionCaptureCount;
        public int forcedInductionCaptureCapacity;

        /// <summary>The partitioned convolutions' FFT tables (shared; unset if no channel is partitioned).</summary>
        public SynthFft fft;
    }

    /// <summary>The forced induction sounds' state (the managed synthesizer's).</summary>
    public struct SynthForcedInduction
    {
        public double whinePhase;
        public double vibratoPhase;
        public double flutterPhase;
        public uint counter;
        public float mixEnvelope;
        public uint noiseState;
        public SynthBandpass whistleBand, whooshBand, flutterBand;
        public SynthBandpass blowoffBand1, blowoffBand2, wastegateBand;
    }

    /// <summary><c>Synthesizer.Bandpass</c>.</summary>
    public struct SynthBandpass
    {
        public float b0, b2, a1, a2;
        public float x1, x2, y1, y2;
    }

    /// <summary>An input channel and its processing filters (<c>Synthesizer.InputChannel</c> and <c>ProcessingFilters</c>).</summary>
    public unsafe struct SynthChannel
    {
        public SynthRingBuffer<float> data;
        public float* transferBuffer;
        public double lastInputSample;

        public SynthConvolution convolution;
        public SynthDerivative derivative;
        public SynthJitter jitter;
        public SynthButterworthFloat airNoiseLowPass;
        public SynthLowPass inputDcFilter;
        public SynthButterworthDouble antialiasing;
    }

    /// <summary><see cref="RingBuffer{T}"/>.</summary>
    public unsafe struct SynthRingBuffer<T> where T : unmanaged
    {
        public T* buffer;
        public int capacity;
        public int writeIndex;
        public int start;
    }

    /// <summary><see cref="DeterministicRandom"/> (SplitMix64).</summary>
    public struct SynthRandom
    {
        public ulong state;
    }

    /// <summary><see cref="ButterworthLowPassFilterFloat"/>.</summary>
    public struct SynthButterworthFloat
    {
        public SynthRingBuffer<float> y;
        public SynthRingBuffer<float> x;
        public float a0, a1, a2, a3, a4;
        public float f_4;
    }

    /// <summary><see cref="ButterworthLowPassFilterDouble"/>.</summary>
    public struct SynthButterworthDouble
    {
        public SynthRingBuffer<double> y;
        public SynthRingBuffer<double> x;
        public double a0, a1, a2, a3, a4;
        public double f_4;
    }

    /// <summary><see cref="LowPassFilter"/>.</summary>
    public struct SynthLowPass
    {
        public float dt;
        public float y;
        public float rc;
    }

    /// <summary><see cref="DerivativeFilter"/>.</summary>
    public struct SynthDerivative
    {
        public float dt;
        public float previous;
    }

    /// <summary><see cref="LevelingFilter"/>.</summary>
    public struct SynthLeveler
    {
        public float peak;
        public float attenuation;
        public float p_maxLevel;
        public float p_minLevel;
        public float p_target;
        public float p_ceiling;
    }

    /// <summary><see cref="JitterFilter"/>.</summary>
    public unsafe struct SynthJitter
    {
        public SynthButterworthFloat noiseFilter;
        public SynthRandom random;
        public float jitterScale;
        public int maxJitter;
        public int offset;
        public float* history;
    }

    /// <summary>Tables of <see cref="Fft"/>.</summary>
    public unsafe struct SynthFft
    {
        public int size;
        public int* bitReverse;
        public float* cos;
        public float* sin;
    }

    /// <summary>
    /// A channel's convolution: <see cref="ConvolutionFilter"/> (direct) or <see cref="PartitionedConvolutionFilter"/>, as the
    /// snapshot found it. The snapshot refuses a channel with no impulse response, as the managed synthesizer can't render then.
    /// </summary>
    public unsafe struct SynthConvolution
    {
        public const int Direct = 1;
        public const int Partitioned = 2;

        public int kind;
        public int sampleCount;

        // Direct
        public float* shiftRegister;
        public int shiftOffset;
        public float* impulseResponse;

        // Partitioned (block size, FFT size and bin count are PartitionedConvolutionFilter's constants)
        public float* head;
        /// <summary>The last block size inputs, written twice: 2 × block size.</summary>
        public float* history;
        public int position;
        public int partitions;
        /// <summary>Partition k's spectrum at [k × bins].</summary>
        public float* partitionRe;
        public float* partitionIm;
        /// <summary>Delay line slot d's spectrum at [d × bins].</summary>
        public float* delayRe;
        public float* delayIm;
        public int newest;
        public float* previousBlock;
        public float* currentBlock;
        public float* tail;
        public float* workRe;
        public float* workIm;
        public float* sumRe;
        public float* sumIm;
    }
}
