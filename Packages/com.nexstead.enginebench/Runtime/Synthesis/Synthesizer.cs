// Port of engine-sim include/synthesizer.h and src/synthesizer.cpp (with the reference's
// seeded random patch), and of the community fork's forced induction sounds (cf96419,
// kyan0s-git's renderForcedInduction) with their noise seeded as the reference seeds it.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
//
// Upstream renders on its own thread behind a mutex and condition variable. This port is the
// synthesizer's state and arithmetic with a single-threaded API: WriteInput per simulation
// step, EndInputBlock, RenderAudio and ReadAudioOutput are called in that order by whoever
// owns it. Which thread that is, and how blocks are paced, is the playback layer's job
// (EngineSoundGenerator, EngineSoundStream).
//
// The package's addition: the forced induction sounds are an output of their own. The fork adds
// them to the engine's signal before the antialiasing and the leveler. Here each has its own
// antialiasing, and both go through the gain the leveler (and its ceiling) takes from the engine
// signal alone, so the engine output is the same whether the forced induction sounds or not. A
// third output plays the two together as the fork does: their sum through a leveler of its own,
// under the same ceiling (for an engine without forced induction, the engine output itself). The
// forced induction sound before its antialiasing is the fork's sample for sample
// (ForcedInductionCapture).
using System;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class Synthesizer
    {
        /// <summary>
        /// Control channels a simulation writes after its exhaust channels to drive the turbocharger and supercharger sounds:
        /// [0] shaft or rotor frequency (Hz, times the whistle pitch), [1] whine level, [2] blow-off level, [3] compressor flow,
        /// [4] surge, [5] external wastegate venting, [6] mode (<see cref="ForcedInduction.GetSoundMode"/>).
        /// </summary>
        public const int ForcedInductionChannelCount = 7;

        public enum ConvolutionMode
        {
            /// <summary>Upstream's direct form (<see cref="ConvolutionFilter"/>): bit-identical to the reference, up to 10,000 multiply-adds per sample per channel.</summary>
            Direct,
            /// <summary><see cref="PartitionedConvolutionFilter"/>: the same convolution in a fraction of the time, equal to the direct form up to float rounding.</summary>
            Partitioned,
        }

        public struct AudioParameters
        {
            public float volume;
            public float convolution;
            public float dF_F_mix;
            public float inputSampleNoise;
            public float inputSampleNoiseFrequencyCutoff;
            public float airNoise;
            public float airNoiseFrequencyCutoff;
            public float levelerTarget;
            public float levelerMaxGain;
            public float levelerMinGain;
            /// <summary>The forced induction sounds' level (the fork's forcedInductionVolume).</summary>
            public float forcedInductionVolume;

            /// <summary>Upstream's defaults.</summary>
            public static AudioParameters Default => new AudioParameters
            {
                volume = 1.0f,
                convolution = 1.0f,
                dF_F_mix = 0.01f,
                inputSampleNoise = 0.5f,
                inputSampleNoiseFrequencyCutoff = 10000.0f,
                airNoise = 1.0f,
                airNoiseFrequencyCutoff = 2000.0f,
                levelerTarget = 30000.0f,
                levelerMaxGain = 1.9f,
                levelerMinGain = 0.00001f,
                forcedInductionVolume = 1.0f,
            };
        }

        public sealed class Parameters
        {
            public int inputChannelCount = 1;
            /// <summary>Control channels written after the exhaust channels: 0, or <see cref="ForcedInductionChannelCount"/>.</summary>
            public int auxChannelCount = 0;
            public int inputBufferSize = 1024;
            public int audioBufferSize = 44100;
            public float inputSampleRate = 10000;
            public float audioSampleRate = 44100;
            public AudioParameters initialAudioParameters = AudioParameters.Default;
        }

        sealed class InputChannel
        {
            public readonly RingBuffer<float> data = new RingBuffer<float>();
            public float[] transferBuffer;
            public double lastInputSample = 0.0;
        }

        sealed class ProcessingFilters
        {
            public ImpulseResponseFilter convolution;
            public readonly DerivativeFilter derivative = new DerivativeFilter();
            public readonly JitterFilter jitterFilter = new JitterFilter();
            public readonly ButterworthLowPassFilterFloat airNoiseLowPass = new ButterworthLowPassFilterFloat();
            public readonly LowPassFilter inputDcFilter = new LowPassFilter();
            public readonly ButterworthLowPassFilterDouble antialiasing = new ButterworthLowPassFilterDouble();
        }

        /// <summary>Upstream renders at most this many samples per <see cref="RenderAudio()"/> call.</summary>
        public const int RenderLimit = 2000;

        /// <summary>
        /// No output sample's magnitude passes it (16-bit scale, after the volume): where the leveler would overshoot past it
        /// (its gain rises between pulses faster than it falls on the next), it cuts its gain to fit (the package's addition,
        /// <see cref="LevelingFilter.p_ceiling"/>).
        /// </summary>
        public const float OutputCeiling = 32000.0f;

        readonly ButterworthLowPassFilterFloat antialiasing = new ButterworthLowPassFilterFloat();
        readonly LevelingFilter levelingFilter = new LevelingFilter();
        readonly DeterministicRandom random = new DeterministicRandom();
        readonly RingBuffer<short> audioBuffer = new RingBuffer<short>();
        readonly ButterworthLowPassFilterFloat forcedInductionAntialiasing = new ButterworthLowPassFilterFloat();
        /// <summary>The forced induction output: floats on the 16-bit scale, neither rounded nor clamped.</summary>
        readonly RingBuffer<float> forcedInductionBuffer = new RingBuffer<float>();
        /// <summary>The engine and its forced induction leveled together (the fork's output).</summary>
        readonly LevelingFilter mixLevelingFilter = new LevelingFilter();
        readonly RingBuffer<short> mixBuffer = new RingBuffer<short>();

        InputChannel[] inputChannels;
        ProcessingFilters[] filters;
        AudioParameters audioParameters;
        int inputChannelCount;
        int auxChannelCount;
        int totalChannelCount;
        int inputBufferSize;
        int inputSamplesRead;
        int latency;
        double inputWriteOffset;
        double lastInputSampleOffset;

        int audioBufferSize;

        float inputSampleRate;
        float audioSampleRate;

        // The forced induction sounds' state
        double whinePhase;
        double vibratoPhase;
        double flutterPhase;
        uint fiCounter;
        float mixEnvelope;
        uint fiNoiseState = DefaultNoiseState;
        Bandpass whistleBand, whooshBand, flutterBand;
        Bandpass blowoffBand1, blowoffBand2, wastegateBand;

        const uint DefaultNoiseState = 0x9E3779B9u;

        /// <summary>
        /// For parity checks: when set, receives each rendered sample's forced induction sound before its antialiasing and gain
        /// (the reference's <c>.fi.f32</c>).
        /// </summary>
        public System.Collections.Generic.List<float> ForcedInductionCapture { get; set; }

        /// <summary>The convolution the next <see cref="InitializeImpulseResponse"/> sets up.</summary>
        public ConvolutionMode convolutionMode = ConvolutionMode.Partitioned;

        public void Initialize(Parameters p)
        {
            inputChannelCount = p.inputChannelCount;
            auxChannelCount = p.auxChannelCount;
            totalChannelCount = p.inputChannelCount + p.auxChannelCount;
            inputBufferSize = p.inputBufferSize;
            audioBufferSize = p.audioBufferSize;
            inputSampleRate = p.inputSampleRate;
            audioSampleRate = p.audioSampleRate;
            audioParameters = p.initialAudioParameters;

            inputSamplesRead = 0;
            inputWriteOffset = 0;

            audioBuffer.Initialize(p.audioBufferSize);
            forcedInductionBuffer.Initialize(p.audioBufferSize);
            mixBuffer.Initialize(p.audioBufferSize);
            inputChannels = new InputChannel[totalChannelCount];
            for (int i = 0; i < totalChannelCount; ++i)
            {
                inputChannels[i] = new InputChannel { transferBuffer = new float[p.inputBufferSize] };
                inputChannels[i].data.Initialize(p.inputBufferSize);
            }

            filters = new ProcessingFilters[p.inputChannelCount];
            for (int i = 0; i < p.inputChannelCount; ++i)
            {
                var f = new ProcessingFilters();
                f.airNoiseLowPass.SetCutoffFrequency(audioParameters.airNoiseFrequencyCutoff, audioSampleRate);

                f.derivative.dt = 1 / audioSampleRate;

                f.inputDcFilter.SetCutoffFrequency(10.0f);
                f.inputDcFilter.dt = 1 / audioSampleRate;

                f.jitterFilter.Initialize(10, audioParameters.inputSampleNoiseFrequencyCutoff, audioSampleRate);

                f.antialiasing.SetCutoffFrequency(1900.0f, audioSampleRate);

                filters[i] = f;
            }

            levelingFilter.p_target = audioParameters.levelerTarget;
            levelingFilter.p_maxLevel = audioParameters.levelerMaxGain;
            levelingFilter.p_minLevel = audioParameters.levelerMinGain;
            mixLevelingFilter.p_target = audioParameters.levelerTarget;
            mixLevelingFilter.p_maxLevel = audioParameters.levelerMaxGain;
            mixLevelingFilter.p_minLevel = audioParameters.levelerMinGain;
            antialiasing.SetCutoffFrequency(audioSampleRate * 0.45f, audioSampleRate);
            forcedInductionAntialiasing.SetCutoffFrequency(audioSampleRate * 0.45f, audioSampleRate);

            whinePhase = 0;
            vibratoPhase = 0;
            flutterPhase = 0;
            fiCounter = 0;
            mixEnvelope = 0;
            fiNoiseState = DefaultNoiseState;
            whistleBand = whooshBand = flutterBand = default;
            blowoffBand1 = blowoffBand2 = wastegateBand = default;

            // Upstream fills the ring buffer once round, which leaves it empty (write index back
            // at the start).
            for (int i = 0; i < audioBufferSize; ++i)
            {
                audioBuffer.Write(0);
                forcedInductionBuffer.Write(0);
                mixBuffer.Write(0);
            }
        }

        /// <summary>
        /// How many of an impulse response's first <paramref name="samples"/> samples the synthesizer keeps: up to the last
        /// louder than 100 in magnitude, at most 10000; 0 if none is.
        /// </summary>
        public static int KeptLength(short[] impulseResponse, int samples)
        {
            int clippedLength = 0;
            for (int i = 0; i < samples; ++i)
            {
                if (Math.Abs((int)impulseResponse[i]) > 100)
                {
                    clippedLength = i + 1;
                }
            }

            return Math.Min(10000, clippedLength);
        }

        /// <summary>
        /// Loads an exhaust channel's impulse response (16-bit samples) into a convolution of <see cref="convolutionMode"/>:
        /// the samples it keeps (<see cref="KeptLength"/>). Every channel needs one before <see cref="RenderAudio()"/>.
        /// </summary>
        public void InitializeImpulseResponse(short[] impulseResponse, int samples, float volume, int index)
        {
            int sampleCount = KeptLength(impulseResponse, samples);
            if (sampleCount == 0) throw new ArgumentException("The impulse response has no sample louder than 100.", nameof(impulseResponse));

            var values = new float[sampleCount];
            for (int i = 0; i < sampleCount; ++i)
            {
                values[i] = volume * impulseResponse[i] / short.MaxValue;
            }

            ImpulseResponseFilter convolution = convolutionMode == ConvolutionMode.Direct
                ? new ConvolutionFilter()
                : (ImpulseResponseFilter)new PartitionedConvolutionFilter();
            convolution.SetImpulseResponse(values, sampleCount);
            filters[index].convolution = convolution;
        }

        public void SeedRandom(ulong seed)
        {
            random.Seed(seed, DeterministicRandom.SynthesizerNoiseStream);

            // The forced induction noise (xorshift32): a nonzero 32-bit state from its own stream
            var forcedInductionSeed = new DeterministicRandom();
            forcedInductionSeed.Seed(seed, DeterministicRandom.ForcedInductionNoiseStream);
            fiNoiseState = (uint)(forcedInductionSeed.Next() >> 32);
            if (fiNoiseState == 0) fiNoiseState = DefaultNoiseState;
            for (int i = 0; i < inputChannelCount; ++i)
            {
                filters[i].jitterFilter.SeedRandom(seed, i);
            }
        }

        /// <summary>
        /// Takes up to <paramref name="samples"/> rendered samples of the engine output into <paramref name="buffer"/>, and the same
        /// samples of the forced induction output into <paramref name="forcedInduction"/> (on the 16-bit scale, neither rounded
        /// nor clamped, as the engine's gain can take it past full scale) and of the two leveled together into
        /// <paramref name="mix"/>; a null buffer drops its samples, so the outputs stay in step. Pads with silence when fewer are
        /// ready. Returns how many were real.
        /// </summary>
        public int ReadAudioOutput(int samples, short[] buffer, float[] forcedInduction = null, short[] mix = null)
        {
            int newDataLength = audioBuffer.Size();
            int taken = Math.Min(samples, newDataLength);
            Take(audioBuffer, samples, buffer, newDataLength);
            if (forcedInduction != null) Take(forcedInductionBuffer, samples, forcedInduction, newDataLength);
            else forcedInductionBuffer.RemoveBeginning(taken);
            if (mix != null) Take(mixBuffer, samples, mix, newDataLength);
            else mixBuffer.RemoveBeginning(taken);

            return taken;
        }

        static void Take<T>(RingBuffer<T> source, int samples, T[] buffer, int newDataLength)
        {
            if (newDataLength >= samples)
            {
                source.ReadAndRemove(samples, buffer);
            }
            else
            {
                source.ReadAndRemove(newDataLength, buffer);
                Array.Clear(buffer, newDataLength, samples - newDataLength);
            }
        }

        /// <summary>One simulation step's value per exhaust channel, then per control channel, resampled to the audio rate.</summary>
        public void WriteInput(double[] data)
        {
            inputWriteOffset += (double)audioSampleRate / inputSampleRate;
            if (inputWriteOffset >= (double)inputBufferSize)
            {
                inputWriteOffset -= (double)inputBufferSize;
            }

            for (int i = 0; i < totalChannelCount; ++i)
            {
                RingBuffer<float> buffer = inputChannels[i].data;
                double lastInputSample = inputChannels[i].lastInputSample;

                // Control channels are interpolated only; the exhaust channels are also low-pass filtered
                bool aux = (i >= inputChannelCount);
                int baseIndex = buffer.WriteIndex();
                double distance = InputDistance(inputWriteOffset, lastInputSampleOffset);
                double s = InputDistance(baseIndex, lastInputSampleOffset);
                for (; s <= distance; s += 1.0)
                {
                    if (s >= inputBufferSize) s -= inputBufferSize;

                    double f = s / distance;
                    double sample = lastInputSample * (1 - f) + data[i] * f;

                    buffer.Write(aux
                        ? (float)sample
                        : (float)filters[i].antialiasing.FastF((float)sample));
                }

                inputChannels[i].lastInputSample = data[i];
            }

            lastInputSampleOffset = inputWriteOffset;
        }

        /// <summary>Releases the input the last <see cref="RenderAudio()"/> consumed and records the remaining input as latency.</summary>
        public void EndInputBlock()
        {
            for (int i = 0; i < totalChannelCount; ++i)
            {
                inputChannels[i].data.RemoveBeginning(inputSamplesRead);
            }

            if (inputChannelCount != 0)
            {
                latency = inputChannels[0].data.Size();
            }

            inputSamplesRead = 0;
        }

        /// <summary>
        /// Renders the waiting input, up to <see cref="RenderLimit"/> samples less what is already buffered. Upstream's
        /// rendering thread runs this once per input block.
        /// </summary>
        public void RenderAudio()
        {
            int n = Math.Min(
                Math.Max(0, RenderLimit - audioBuffer.Size()),
                inputChannels[0].data.Size());

            for (int i = 0; i < totalChannelCount; ++i)
            {
                inputChannels[i].data.Read(n, inputChannels[i].transferBuffer);
            }

            inputSamplesRead = n;

            for (int i = 0; i < inputChannelCount; ++i)
            {
                filters[i].airNoiseLowPass.SetCutoffFrequency(audioParameters.airNoiseFrequencyCutoff, audioSampleRate);
                filters[i].jitterFilter.SetJitterScale(audioParameters.inputSampleNoise);
            }

            for (int i = 0; i < n; ++i)
            {
                RenderSample(i);
            }
        }

        public double GetLatency() => (double)latency / audioSampleRate;

        public int InputDelta(int s1, int s0)
        {
            return (s1 < s0)
                ? inputBufferSize - s0 + s1
                : s1 - s0;
        }

        public double InputDistance(double s1, double s0)
        {
            return (s1 < s0)
                ? (double)inputBufferSize - s0 + s1
                : s1 - s0;
        }

        public void SetInputSampleRate(double sampleRate)
        {
            if (sampleRate != inputSampleRate)
            {
                inputSampleRate = (float)sampleRate;
            }
        }

        public double GetInputSampleRate() => inputSampleRate;
        public float GetAudioSampleRate() => audioSampleRate;

        /// <summary>Samples waiting to be rendered (per channel).</summary>
        public int GetInputSize() => inputChannels[0].data.Size();

        /// <summary>Rendered samples waiting to be read.</summary>
        public int GetOutputSize() => audioBuffer.Size();

        /// <summary>Renders one sample of each output: the engine into the audio buffer, the forced induction into its own.</summary>
        void RenderSample(int inputSample)
        {
            float airNoise = audioParameters.airNoise;
            float dF_F_mix = audioParameters.dF_F_mix;
            float convAmount = audioParameters.convolution;

            float signal = 0;
            for (int i = 0; i < inputChannelCount; ++i)
            {
                ProcessingFilters f = filters[i];

                float jitteredSample = f.jitterFilter.FastF(inputChannels[i].transferBuffer[inputSample]);

                float f_in = jitteredSample;
                float f_dc = f.inputDcFilter.FastF(f_in);
                float f_ac = f_in - f_dc;
                float f_p = f.derivative.F(f_in);

                float noise = (float)(2.0 * random.NextDouble() - 1.0);
                // Upstream filters every channel's air noise through channel 0's low-pass
                // (`m_filters->airNoiseLowPass`). Kept for parity.
                float r = filters[0].airNoiseLowPass.FastF(noise);
                float r_mixed = airNoise * r + (1 - airNoise);

                float v_in = f_p * dF_F_mix + f_ac * r_mixed * (1 - dF_F_mix);
                if (float.IsSubnormal(v_in))
                {
                    v_in = 0;
                }

                float v = convAmount * f.convolution.F(v_in) + (1 - convAmount) * v_in;

                signal += v;
            }

            float forcedInduction = RenderForcedInduction(inputSample, signal);
            ForcedInductionCapture?.Add(forcedInduction);

            signal = antialiasing.FastF(signal);

            levelingFilter.p_target = audioParameters.levelerTarget;
            levelingFilter.p_ceiling = OutputCeiling / audioParameters.volume;
            float v_leveled = levelingFilter.F(signal) * audioParameters.volume;
            short engineSample = ToPcm16(v_leveled);
            audioBuffer.Write(engineSample);

            if (auxChannelCount == 0)
            {
                // Without forced induction: silence, and the engine alone (which a leveler of its own would level the same)
                forcedInductionBuffer.Write(0.0f);
                mixBuffer.Write(engineSample);
                return;
            }

            // The same gain as the engine's sample
            float fi = forcedInductionAntialiasing.FastF(forcedInduction);
            forcedInductionBuffer.Write(fi * levelingFilter.GetAttenuation() * audioParameters.volume);

            // The two together, leveled as the fork levels them
            mixLevelingFilter.p_target = audioParameters.levelerTarget;
            mixLevelingFilter.p_ceiling = OutputCeiling / audioParameters.volume;
            mixBuffer.Write(ToPcm16(mixLevelingFilter.F(signal + fi) * audioParameters.volume));
        }

        /// <summary>Upstream's output conversion: rounded half away from zero, clamped to 16 bits.</summary>
        static short ToPcm16(float v)
        {
            long r_long = (long)Utilities.Round(v);
            int r_int = unchecked((int)r_long);
            if (r_int > short.MaxValue)
            {
                r_int = short.MaxValue;
            }
            else if (r_int < short.MinValue)
            {
                r_int = short.MinValue;
            }

            return (short)r_int;
        }

        /// <summary>A band-pass biquad (the fork's <c>Synthesizer::Bandpass</c>).</summary>
        internal struct Bandpass
        {
            public float b0, b2, a1, a2;
            public float x1, x2, y1, y2;

            public void Set(float f, float q, float fs)
            {
                float w0 = 2.0f * 3.14159265f * Utilities.Clamp(f, 20.0f, 0.45f * fs) / fs;
                float alpha = MathF.Sin(w0) / (2.0f * q);
                float a0 = 1.0f + alpha;
                b0 = alpha / a0;
                b2 = -alpha / a0;
                a1 = -2.0f * MathF.Cos(w0) / a0;
                a2 = (1.0f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                if (float.IsSubnormal(y1)) y1 = 0;
                return y;
            }
        }

        /// <summary>xorshift32, uniform in [-1, 1].</summary>
        float FiNoise()
        {
            fiNoiseState ^= fiNoiseState << 13;
            fiNoiseState ^= fiNoiseState >> 17;
            fiNoiseState ^= fiNoiseState << 5;
            return (float)(fiNoiseState / 4294967295.0 * 2.0 - 1.0);
        }

        static float Max(float a, float b) => (a < b) ? b : a;

        /// <summary>
        /// The turbocharger and supercharger sounds for one output sample (kyan0s-git's, as the community fork has them), from the
        /// control channels (<see cref="ForcedInductionChannelCount"/>). <paramref name="signal"/> is the engine's signal of that
        /// sample, whose loudness they follow.
        /// </summary>
        float RenderForcedInduction(int inputSample, float signal)
        {
            if (auxChannelCount < ForcedInductionChannelCount) return 0.0f;

            float Aux(int k) => inputChannels[inputChannelCount + k].transferBuffer[inputSample];

            int mode = (int)Utilities.Round(Aux(6));
            int type = mode / 10;
            int blowoffType = mode % 10;
            if (type == 0) return 0.0f;

            float volume = audioParameters.forcedInductionVolume;
            float fs = audioSampleRate;
            float envelopeDecay = 1.0f - 1.0f / (0.3f * fs);
            float floor = 0.08f * audioParameters.levelerTarget;
            const double twoPi = 2 * 3.14159265358979323846;

            // The sounds follow the loudness of the engine (with a floor so that a blow-off on a quiet overrun is still heard)
            float a = Math.Abs(signal);
            mixEnvelope = Max(a * (1 - envelopeDecay) + mixEnvelope * envelopeDecay, 0.0f);
            float reference = 0.8f * mixEnvelope + floor;

            float shaft = Max(Aux(0), 0.0f);
            float whine = Max(Aux(1), 0.0f);
            float blowoff = Max(Aux(2), 0.0f);
            float flow = Max(Aux(3), 0.0f);
            float surge = Max(Aux(4), 0.0f);
            float wastegate = Max(Aux(5), 0.0f);

            // Filter centres follow the shaft speed; updated every 32 samples
            float tonal = 0;
            if (type == 1 || type == 4)
            {
                float pitch = Utilities.Clamp(400.0f + ((type == 1) ? 2.4f : 2.0f) * shaft, 300.0f, 9000.0f);
                if ((fiCounter++ & 31) == 0)
                {
                    whistleBand.Set(pitch, 18.0f, fs);
                    whooshBand.Set(500.0f + 0.35f * pitch, 0.9f, fs);
                }

                // A slightly wavering tone inside a narrow band of air noise
                vibratoPhase += twoPi * 5.3 / fs;
                if (vibratoPhase > twoPi) vibratoPhase -= twoPi;
                whinePhase += twoPi * pitch * (1.0 + 0.003 * Math.Sin(vibratoPhase)) / fs;
                if (whinePhase > twoPi) whinePhase -= twoPi;

                float tone = (float)Math.Sin(whinePhase);
                float air = 5.0f * whistleBand.Process(FiNoise());
                float toneShare = (type == 4) ? 0.65f : 0.45f;
                tonal += whine * (toneShare * tone + (1.0f - toneShare) * air);
                tonal += 0.45f * flow * flow * whooshBand.Process(FiNoise());
            }
            else
            {
                // Supercharger rotor whine: lobe passing frequency with harmonics
                double lobes = (type == 2) ? 6.0 : 5.0;
                double f = Utilities.StdMin(shaft * lobes, 0.2 * fs);
                whinePhase += twoPi * f / fs;
                if (whinePhase > twoPi) whinePhase -= twoPi;
                double p = whinePhase;
                float tone;
                if (type == 2)
                {
                    tone = (float)(Math.Sin(p) + 0.55 * Math.Sin(2 * p) + 0.35 * Math.Sin(3 * p) + 0.2 * Math.Sin(4 * p));
                }
                else
                {
                    tone = (float)(Math.Sin(p) + 0.45 * Math.Sin(3 * p) + 0.25 * Math.Sin(5 * p));
                }

                if ((fiCounter++ & 31) == 0)
                {
                    whistleBand.Set((float)(2.0 * f + 200.0), 6.0f, fs);
                }
                tonal += whine * (0.45f * tone + 1.5f * whistleBand.Process(FiNoise()));
            }

            // Compressor surge without a blow-off valve: a fluttering chop
            float bursts = 0;
            if (surge > 0.01f)
            {
                flutterPhase += twoPi * (11.0 + 7.0 * surge) / fs;
                if (flutterPhase > twoPi) flutterPhase -= twoPi;
                float gate = (float)Math.Pow(Utilities.StdMax(Math.Sin(flutterPhase), 0.0), 3.0);
                if ((fiCounter & 31) == 1) flutterBand.Set(380.0f, 1.6f, fs);
                bursts += 2.0f * surge * gate * flutterBand.Process(FiNoise());
            }

            // Blow-off: a sharp vented "pssh" or a soft recirculating whoosh
            if (blowoff > 0.001f)
            {
                if ((fiCounter & 31) == 2)
                {
                    blowoffBand1.Set(blowoffType == 1 ? 1100.0f : 2600.0f, blowoffType == 1 ? 1.2f : 0.7f, fs);
                    blowoffBand2.Set(6500.0f, 1.3f, fs);
                }
                float r = FiNoise();
                bursts += (blowoffType == 1)
                    ? 0.7f * blowoff * blowoffBand1.Process(r)
                    : blowoff * (1.6f * blowoffBand1.Process(r) + 0.8f * blowoffBand2.Process(r));
            }

            // External wastegate venting through its own pipe
            if (wastegate > 0.01f)
            {
                if ((fiCounter & 31) == 3) wastegateBand.Set(1500.0f, 2.5f, fs);
                bursts += 0.6f * wastegate * wastegateBand.Process(FiNoise());
            }

            return volume * (tonal * (1.1f * mixEnvelope + 0.4f * floor) + bursts * reference * 1.5f);
        }

        public double GetLevelerGain() => levelingFilter.GetAttenuation();
        public AudioParameters GetAudioParameters() => audioParameters;
        public void SetAudioParameters(AudioParameters parameters) => audioParameters = parameters;

        // Read access for the dense core's snapshot.
        internal int InputChannelCount => inputChannelCount;
        internal int InputBufferSize => inputBufferSize;
        internal int InputSamplesRead => inputSamplesRead;
        internal int Latency => latency;
        internal double InputWriteOffset => inputWriteOffset;
        internal double LastInputSampleOffset => lastInputSampleOffset;
        internal int AudioBufferSize => audioBufferSize;
        internal float InputSampleRateValue => inputSampleRate;
        internal ButterworthLowPassFilterFloat Antialiasing => antialiasing;
        internal LevelingFilter Leveler => levelingFilter;
        internal DeterministicRandom Random => random;
        internal RingBuffer<short> AudioBuffer => audioBuffer;
        internal RingBuffer<float> ForcedInductionBuffer => forcedInductionBuffer;
        internal LevelingFilter MixLeveler => mixLevelingFilter;
        internal RingBuffer<short> MixBuffer => mixBuffer;
        internal ButterworthLowPassFilterFloat ForcedInductionAntialiasing => forcedInductionAntialiasing;
        internal int AuxChannelCount => auxChannelCount;
        internal double WhinePhase => whinePhase;
        internal double VibratoPhase => vibratoPhase;
        internal double FlutterPhase => flutterPhase;
        internal uint FiCounter => fiCounter;
        internal float MixEnvelope => mixEnvelope;
        internal uint FiNoiseState => fiNoiseState;
        internal Bandpass[] Bands => new[] { whistleBand, whooshBand, flutterBand, blowoffBand1, blowoffBand2, wastegateBand };
        internal RingBuffer<float> InputData(int channel) => inputChannels[channel].data;
        internal float[] TransferBuffer(int channel) => inputChannels[channel].transferBuffer;
        internal double LastInputSample(int channel) => inputChannels[channel].lastInputSample;
        internal ImpulseResponseFilter Convolution(int channel) => filters[channel].convolution;
        internal DerivativeFilter Derivative(int channel) => filters[channel].derivative;
        internal JitterFilter Jitter(int channel) => filters[channel].jitterFilter;
        internal ButterworthLowPassFilterFloat AirNoiseLowPass(int channel) => filters[channel].airNoiseLowPass;
        internal LowPassFilter InputDcFilter(int channel) => filters[channel].inputDcFilter;
        internal ButterworthLowPassFilterDouble InputAntialiasing(int channel) => filters[channel].antialiasing;
    }
}
