// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
//
// The dense synthesizer's code: Runtime/Synthesis/Synthesizer.cs and its filters
// (Filters.cs, ButterworthLowPassFilter.cs, ConvolutionFilter.cs,
// PartitionedConvolutionFilter.cs), RingBuffer.cs and DeterministicRandom.cs, line for line over
// the structs in SynthesizerData.cs. See DenseEngine.cs for the rules.
using System;
using Nexstead.EngineBench.Scs;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Dense
{
    public static unsafe class SynthOps
    {
        const int BlockSize = PartitionedConvolutionFilter.BlockSize;
        const int FftSize = 2 * BlockSize;
        const int Bins = BlockSize + 1;

        // ---- Synthesizer ----

        public static void WriteInput(DenseSynthesizer* s, double* data)
        {
            s->inputWriteOffset += (double)s->audioSampleRate / s->inputSampleRate;
            if (s->inputWriteOffset >= (double)s->inputBufferSize)
            {
                s->inputWriteOffset -= (double)s->inputBufferSize;
            }

            int totalChannelCount = s->inputChannelCount + s->auxChannelCount;
            for (int i = 0; i < totalChannelCount; ++i)
            {
                SynthChannel* channel = s->channels + i;
                double lastInputSample = channel->lastInputSample;
                bool aux = (i >= s->inputChannelCount);
                int baseIndex = channel->data.writeIndex;
                double distance = InputDistance(s, s->inputWriteOffset, s->lastInputSampleOffset);
                double sOffset = InputDistance(s, baseIndex, s->lastInputSampleOffset);
                for (; sOffset <= distance; sOffset += 1.0)
                {
                    if (sOffset >= s->inputBufferSize) sOffset -= s->inputBufferSize;

                    double f = sOffset / distance;
                    double sample = lastInputSample * (1 - f) + data[i] * f;

                    Write(ref channel->data, aux
                        ? (float)sample
                        : (float)FastF(ref channel->antialiasing, (float)sample));
                }

                channel->lastInputSample = data[i];
            }

            s->lastInputSampleOffset = s->inputWriteOffset;
        }

        public static void EndInputBlock(DenseSynthesizer* s)
        {
            for (int i = 0; i < s->inputChannelCount + s->auxChannelCount; ++i)
            {
                RemoveBeginning(ref s->channels[i].data, s->inputSamplesRead);
            }

            if (s->inputChannelCount != 0)
            {
                s->latency = Size(ref s->channels[0].data);
            }

            s->inputSamplesRead = 0;
        }

        public static void RenderAudio(DenseSynthesizer* s)
        {
            int n = Math.Min(
                Math.Max(0, Synthesizer.RenderLimit - Size(ref s->audioBuffer)),
                Size(ref s->channels[0].data));

            for (int i = 0; i < s->inputChannelCount + s->auxChannelCount; ++i)
            {
                Read(ref s->channels[i].data, n, s->channels[i].transferBuffer);
            }

            s->inputSamplesRead = n;

            for (int i = 0; i < s->inputChannelCount; ++i)
            {
                SetCutoffFrequency(ref s->channels[i].airNoiseLowPass, s->audioParameters.airNoiseFrequencyCutoff, s->audioSampleRate);
                s->channels[i].jitter.jitterScale = s->audioParameters.inputSampleNoise;
            }

            for (int i = 0; i < n; ++i)
            {
                RenderSample(s, i);
            }
        }

        /// <summary><see cref="Synthesizer.ReadAudioOutput"/>: a null <paramref name="forcedInduction"/> or <paramref name="mix"/> drops those samples.</summary>
        public static int ReadAudioOutput(DenseSynthesizer* s, int samples, short* buffer, float* forcedInduction, short* mix)
        {
            int newDataLength = Size(ref s->audioBuffer);
            int taken = Math.Min(samples, newDataLength);
            Take(ref s->audioBuffer, samples, buffer, newDataLength);
            if (forcedInduction != null) Take(ref s->forcedInductionBuffer, samples, forcedInduction, newDataLength);
            else RemoveBeginning(ref s->forcedInductionBuffer, taken);
            if (mix != null) Take(ref s->mixBuffer, samples, mix, newDataLength);
            else RemoveBeginning(ref s->mixBuffer, taken);

            return taken;
        }

        static void Take<T>(ref SynthRingBuffer<T> source, int samples, T* buffer, int newDataLength) where T : unmanaged
        {
            if (newDataLength >= samples)
            {
                ReadAndRemove(ref source, samples, buffer);
            }
            else
            {
                ReadAndRemove(ref source, newDataLength, buffer);
                for (int i = newDataLength; i < samples; ++i) buffer[i] = default;
            }
        }

        public static void SetInputSampleRate(DenseSynthesizer* s, double sampleRate)
        {
            if (sampleRate != s->inputSampleRate)
            {
                s->inputSampleRate = (float)sampleRate;
            }
        }

        public static int GetInputSize(DenseSynthesizer* s) => Size(ref s->channels[0].data);
        public static int GetOutputSize(DenseSynthesizer* s) => Size(ref s->audioBuffer);
        public static double GetLatency(DenseSynthesizer* s) => (double)s->latency / s->audioSampleRate;

        static double InputDistance(DenseSynthesizer* s, double s1, double s0)
        {
            return (s1 < s0)
                ? (double)s->inputBufferSize - s0 + s1
                : s1 - s0;
        }

        /// <summary>The managed synthesizer's <c>RenderSample</c>: one sample of each output.</summary>
        static void RenderSample(DenseSynthesizer* s, int inputSample)
        {
            float airNoise = s->audioParameters.airNoise;
            float dF_F_mix = s->audioParameters.dF_F_mix;
            float convAmount = s->audioParameters.convolution;

            float signal = 0;
            for (int i = 0; i < s->inputChannelCount; ++i)
            {
                SynthChannel* f = s->channels + i;

                float jitteredSample = FastF(ref f->jitter, f->transferBuffer[inputSample]);

                float f_in = jitteredSample;
                float f_dc = FastF(ref f->inputDcFilter, f_in);
                float f_ac = f_in - f_dc;
                float f_p = F(ref f->derivative, f_in);

                float noise = (float)(2.0 * NextDouble(ref s->random) - 1.0);
                // Upstream filters every channel's air noise through channel 0's low-pass. Kept for parity.
                float r = FastF(ref s->channels[0].airNoiseLowPass, noise);
                float r_mixed = airNoise * r + (1 - airNoise);

                float v_in = f_p * dF_F_mix + f_ac * r_mixed * (1 - dF_F_mix);
                if (IsSubnormal(v_in))
                {
                    v_in = 0;
                }

                float v = convAmount * Convolve(s, ref f->convolution, v_in) + (1 - convAmount) * v_in;

                signal += v;
            }

            float forcedInduction = RenderForcedInduction(s, inputSample, signal);
            if (s->forcedInductionCapture != null && s->forcedInductionCaptureCount < s->forcedInductionCaptureCapacity)
            {
                s->forcedInductionCapture[s->forcedInductionCaptureCount++] = forcedInduction;
            }

            signal = FastF(ref s->antialiasing, signal);

            s->leveler.p_target = s->audioParameters.levelerTarget;
            s->leveler.p_ceiling = Synthesizer.OutputCeiling / s->audioParameters.volume;
            float v_leveled = F(ref s->leveler, signal) * s->audioParameters.volume;
            short engineSample = ToPcm16(v_leveled);
            Write(ref s->audioBuffer, engineSample);

            if (s->auxChannelCount == 0)
            {
                Write(ref s->forcedInductionBuffer, 0.0f);
                Write(ref s->mixBuffer, engineSample);
                return;
            }

            float fi = FastF(ref s->forcedInductionAntialiasing, forcedInduction);
            Write(ref s->forcedInductionBuffer, fi * s->leveler.attenuation * s->audioParameters.volume);

            s->mixLeveler.p_target = s->audioParameters.levelerTarget;
            s->mixLeveler.p_ceiling = Synthesizer.OutputCeiling / s->audioParameters.volume;
            Write(ref s->mixBuffer, ToPcm16(F(ref s->mixLeveler, signal + fi) * s->audioParameters.volume));
        }

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

        // ---- Forced induction sounds ----

        static void Set(ref SynthBandpass b, float f, float q, float fs)
        {
            float w0 = 2.0f * 3.14159265f * Utilities.Clamp(f, 20.0f, 0.45f * fs) / fs;
            float alpha = MathF.Sin(w0) / (2.0f * q);
            float a0 = 1.0f + alpha;
            b.b0 = alpha / a0;
            b.b2 = -alpha / a0;
            b.a1 = -2.0f * MathF.Cos(w0) / a0;
            b.a2 = (1.0f - alpha) / a0;
        }

        static float Process(ref SynthBandpass b, float x)
        {
            float y = b.b0 * x + b.b2 * b.x2 - b.a1 * b.y1 - b.a2 * b.y2;
            b.x2 = b.x1; b.x1 = x;
            b.y2 = b.y1; b.y1 = y;
            if (IsSubnormal(b.y1)) b.y1 = 0;
            return y;
        }

        static float FiNoise(ref SynthForcedInduction fi)
        {
            fi.noiseState ^= fi.noiseState << 13;
            fi.noiseState ^= fi.noiseState >> 17;
            fi.noiseState ^= fi.noiseState << 5;
            return (float)(fi.noiseState / 4294967295.0 * 2.0 - 1.0);
        }

        static float Max(float a, float b) => (a < b) ? b : a;

        /// <summary>The managed synthesizer's <c>RenderForcedInduction</c>.</summary>
        static float RenderForcedInduction(DenseSynthesizer* s, int inputSample, float signal)
        {
            if (s->auxChannelCount < Synthesizer.ForcedInductionChannelCount) return 0.0f;

            SynthChannel* aux = s->channels + s->inputChannelCount;
            int mode = (int)Utilities.Round(aux[6].transferBuffer[inputSample]);
            int type = mode / 10;
            int blowoffType = mode % 10;
            if (type == 0) return 0.0f;

            ref SynthForcedInduction fi = ref s->forcedInduction;
            float volume = s->audioParameters.forcedInductionVolume;
            float fs = s->audioSampleRate;
            float envelopeDecay = 1.0f - 1.0f / (0.3f * fs);
            float floor = 0.08f * s->audioParameters.levelerTarget;
            const double twoPi = 2 * 3.14159265358979323846;

            float a = Math.Abs(signal);
            fi.mixEnvelope = Max(a * (1 - envelopeDecay) + fi.mixEnvelope * envelopeDecay, 0.0f);
            float reference = 0.8f * fi.mixEnvelope + floor;

            float shaft = Max(aux[0].transferBuffer[inputSample], 0.0f);
            float whine = Max(aux[1].transferBuffer[inputSample], 0.0f);
            float blowoff = Max(aux[2].transferBuffer[inputSample], 0.0f);
            float flow = Max(aux[3].transferBuffer[inputSample], 0.0f);
            float surge = Max(aux[4].transferBuffer[inputSample], 0.0f);
            float wastegate = Max(aux[5].transferBuffer[inputSample], 0.0f);

            float tonal = 0;
            if (type == 1 || type == 4)
            {
                float pitch = Utilities.Clamp(400.0f + ((type == 1) ? 2.4f : 2.0f) * shaft, 300.0f, 9000.0f);
                if ((fi.counter++ & 31) == 0)
                {
                    Set(ref fi.whistleBand, pitch, 18.0f, fs);
                    Set(ref fi.whooshBand, 500.0f + 0.35f * pitch, 0.9f, fs);
                }

                fi.vibratoPhase += twoPi * 5.3 / fs;
                if (fi.vibratoPhase > twoPi) fi.vibratoPhase -= twoPi;
                fi.whinePhase += twoPi * pitch * (1.0 + 0.003 * Math.Sin(fi.vibratoPhase)) / fs;
                if (fi.whinePhase > twoPi) fi.whinePhase -= twoPi;

                float tone = (float)Math.Sin(fi.whinePhase);
                float air = 5.0f * Process(ref fi.whistleBand, FiNoise(ref fi));
                float toneShare = (type == 4) ? 0.65f : 0.45f;
                tonal += whine * (toneShare * tone + (1.0f - toneShare) * air);
                tonal += 0.45f * flow * flow * Process(ref fi.whooshBand, FiNoise(ref fi));
            }
            else
            {
                double lobes = (type == 2) ? 6.0 : 5.0;
                double f = Utilities.StdMin(shaft * lobes, 0.2 * fs);
                fi.whinePhase += twoPi * f / fs;
                if (fi.whinePhase > twoPi) fi.whinePhase -= twoPi;
                double p = fi.whinePhase;
                float tone;
                if (type == 2)
                {
                    tone = (float)(Math.Sin(p) + 0.55 * Math.Sin(2 * p) + 0.35 * Math.Sin(3 * p) + 0.2 * Math.Sin(4 * p));
                }
                else
                {
                    tone = (float)(Math.Sin(p) + 0.45 * Math.Sin(3 * p) + 0.25 * Math.Sin(5 * p));
                }

                if ((fi.counter++ & 31) == 0)
                {
                    Set(ref fi.whistleBand, (float)(2.0 * f + 200.0), 6.0f, fs);
                }
                tonal += whine * (0.45f * tone + 1.5f * Process(ref fi.whistleBand, FiNoise(ref fi)));
            }

            float bursts = 0;
            if (surge > 0.01f)
            {
                fi.flutterPhase += twoPi * (11.0 + 7.0 * surge) / fs;
                if (fi.flutterPhase > twoPi) fi.flutterPhase -= twoPi;
                float gate = (float)Math.Pow(Utilities.StdMax(Math.Sin(fi.flutterPhase), 0.0), 3.0);
                if ((fi.counter & 31) == 1) Set(ref fi.flutterBand, 380.0f, 1.6f, fs);
                bursts += 2.0f * surge * gate * Process(ref fi.flutterBand, FiNoise(ref fi));
            }

            if (blowoff > 0.001f)
            {
                if ((fi.counter & 31) == 2)
                {
                    Set(ref fi.blowoffBand1, blowoffType == 1 ? 1100.0f : 2600.0f, blowoffType == 1 ? 1.2f : 0.7f, fs);
                    Set(ref fi.blowoffBand2, 6500.0f, 1.3f, fs);
                }
                float r = FiNoise(ref fi);
                bursts += (blowoffType == 1)
                    ? 0.7f * blowoff * Process(ref fi.blowoffBand1, r)
                    : blowoff * (1.6f * Process(ref fi.blowoffBand1, r) + 0.8f * Process(ref fi.blowoffBand2, r));
            }

            if (wastegate > 0.01f)
            {
                if ((fi.counter & 31) == 3) Set(ref fi.wastegateBand, 1500.0f, 2.5f, fs);
                bursts += 0.6f * wastegate * Process(ref fi.wastegateBand, FiNoise(ref fi));
            }

            return volume * (tonal * (1.1f * fi.mixEnvelope + 0.4f * floor) + bursts * reference * 1.5f);
        }

        /// <summary><c>float.IsSubnormal</c>, by its bits: zero exponent, non-zero mantissa.</summary>
        static bool IsSubnormal(float v)
        {
            int bits = *(int*)&v & 0x7FFFFFFF;
            return bits < 0x00800000 && bits != 0;
        }

        // ---- Ring buffer ----

        static void Write<T>(ref SynthRingBuffer<T> b, T data) where T : unmanaged
        {
            b.buffer[b.writeIndex] = data;

            if (++b.writeIndex >= b.capacity)
            {
                b.writeIndex = 0;
            }
        }

        static T Read<T>(ref SynthRingBuffer<T> b, int index) where T : unmanaged
        {
            return (b.start + index) >= b.capacity
                ? b.buffer[b.start + index - b.capacity]
                : b.buffer[b.start + index];
        }

        static void Read<T>(ref SynthRingBuffer<T> b, int n, T* target) where T : unmanaged
        {
            if (b.start + n < b.capacity)
            {
                for (int i = 0; i < n; ++i) target[i] = b.buffer[b.start + i];
            }
            else
            {
                int first = b.capacity - b.start;
                for (int i = 0; i < first; ++i) target[i] = b.buffer[b.start + i];
                for (int i = 0; i < n - first; ++i) target[first + i] = b.buffer[i];
            }
        }

        static void ReadAndRemove<T>(ref SynthRingBuffer<T> b, int n, T* target) where T : unmanaged
        {
            Read(ref b, n, target);

            b.start += n;
            if (b.start >= b.capacity)
            {
                b.start -= b.capacity;
            }
        }

        static void RemoveBeginning<T>(ref SynthRingBuffer<T> b, int n) where T : unmanaged
        {
            b.start += n;
            if (b.start >= b.capacity)
            {
                b.start -= b.capacity;
            }
        }

        static int Size<T>(ref SynthRingBuffer<T> b) where T : unmanaged
        {
            return (b.writeIndex < b.start)
                ? b.writeIndex + (b.capacity - b.start)
                : b.writeIndex - b.start;
        }

        // ---- Random ----

        static ulong Next(ref SynthRandom r)
        {
            unchecked
            {
                r.state += 0x9E3779B97F4A7C15ul;
                ulong z = r.state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }

        static double NextDouble(ref SynthRandom r) => (double)(Next(ref r) >> 11) * (1.0 / 9007199254740992.0);

        // ---- Butterworth ----

        static float FastF(ref SynthButterworthFloat b, float sample)
        {
            float y_prev0 = Read(ref b.y, 3), y_prev1 = Read(ref b.y, 2), y_prev2 = Read(ref b.y, 1), y_prev3 = Read(ref b.y, 0);
            float x_prev0 = Read(ref b.x, 3), x_prev1 = Read(ref b.x, 2), x_prev2 = Read(ref b.x, 1), x_prev3 = Read(ref b.x, 0);

            float n = b.f_4 / b.a0 * (sample + 4 * x_prev0 + 6 * x_prev1 + 4 * x_prev2 + x_prev3);
            float d = -b.a1 * y_prev0 - b.a2 * y_prev1 - b.a3 * y_prev2 - b.a4 * y_prev3;
            float result = n + d;

            RemoveBeginning(ref b.x, 1);
            Write(ref b.x, sample);

            RemoveBeginning(ref b.y, 1);
            Write(ref b.y, result);

            return result;
        }

        static void SetCutoffFrequency(ref SynthButterworthFloat b, float f_c, float sampleRate)
        {
            float f = MathF.Tan((float)Constants.pi * f_c / sampleRate);
            float f_2 = f * f;
            float f_3 = f_2 * f;
            float f_4 = f_2 * f_2;
            float m = (float)(-2.0 * Math.Cos(5.0 * Constants.pi / 8.0));
            float n = (float)(-2.0 * Math.Cos(7.0 * Constants.pi / 8.0));

            b.a0 = (float)(1.0 + (m + n) * f + (2.0 + n * m) * f_2 + (m + n) * f_3 + f_4);
            b.a1 = (float)((-4.0 - 2.0 * (n + m) * f + 2.0 * (m + n) * f_3 + 4.0 * f_4) / b.a0);
            b.a2 = (float)((6.0 - 2.0 * (2.0 + m * n) * f_2 + 6.0 * f_4) / b.a0);
            b.a3 = (float)((-4.0 + 2.0 * (m + n) * f - 2.0 * (m + n) * f_3 + 4.0 * f_4) / b.a0);
            b.a4 = (float)((1.0 - (n + m) * f + (2.0 + m * n) * f_2 - (m + n) * f_3 + f_4) / b.a0);
            b.f_4 = f_4;
        }

        static double FastF(ref SynthButterworthDouble b, double sample)
        {
            double y_prev0 = Read(ref b.y, 3), y_prev1 = Read(ref b.y, 2), y_prev2 = Read(ref b.y, 1), y_prev3 = Read(ref b.y, 0);
            double x_prev0 = Read(ref b.x, 3), x_prev1 = Read(ref b.x, 2), x_prev2 = Read(ref b.x, 1), x_prev3 = Read(ref b.x, 0);

            double n = b.f_4 / b.a0 * (sample + 4 * x_prev0 + 6 * x_prev1 + 4 * x_prev2 + x_prev3);
            double d = -b.a1 * y_prev0 - b.a2 * y_prev1 - b.a3 * y_prev2 - b.a4 * y_prev3;
            double result = n + d;

            RemoveBeginning(ref b.x, 1);
            Write(ref b.x, sample);

            RemoveBeginning(ref b.y, 1);
            Write(ref b.y, result);

            return result;
        }

        // ---- Other filters ----

        static float FastF(ref SynthLowPass l, float sample)
        {
            float alpha = l.dt / (l.rc + l.dt);
            l.y = alpha * sample + (1 - alpha) * l.y;
            return l.y;
        }

        static float F(ref SynthDerivative d, float sample)
        {
            float temp = d.previous;
            d.previous = sample;

            return (sample - temp) / d.dt;
        }

        static float F(ref SynthLeveler l, float sample)
        {
            l.peak = 0.999f * l.peak;
            if (Math.Abs(sample) > l.peak)
            {
                l.peak = Math.Abs(sample);
            }

            if (l.peak == 0) return 0;

            float raw_attenuation = l.p_target / l.peak;

            float attenuation = raw_attenuation;
            if (attenuation < l.p_minLevel) attenuation = l.p_minLevel;
            else if (attenuation > l.p_maxLevel) attenuation = l.p_maxLevel;

            l.attenuation = (float)(0.9 * l.attenuation + 0.1 * attenuation);
            if (Math.Abs(sample) * l.attenuation > l.p_ceiling)
            {
                l.attenuation = l.p_ceiling / Math.Abs(sample);
            }

            return sample * l.attenuation;
        }

        static float FastF(ref SynthJitter j, float sample, float jitterScale = 1.0f)
        {
            j.history[j.offset] = sample;
            ++j.offset;

            if (j.offset >= j.maxJitter)
            {
                j.offset = 0;
            }

            float jitter = (float)(NextDouble(ref j.random) * (j.maxJitter - 1));
            float s = FastF(ref j.noiseFilter, jitter * j.jitterScale * jitterScale);
            float s_i_0 = Utilities.Clamp(MathF.Floor(s), 0.0f, (float)(j.maxJitter - 1));
            float s_i_1 = Utilities.Clamp(MathF.Ceiling(s), 0.0f, (float)(j.maxJitter - 1));

            float s_frac = (s - s_i_0);

            int i_0 = (int)s_i_0 + j.offset;
            int i_1 = (int)s_i_1 + j.offset;

            float v0 = j.history[i_0 >= j.maxJitter ? i_0 - j.maxJitter : i_0];
            float v1 = j.history[i_1 >= j.maxJitter ? i_1 - j.maxJitter : i_1];

            return v1 * s_frac + v0 * (1 - s_frac);
        }

        // ---- Convolution ----

        static float Convolve(DenseSynthesizer* s, ref SynthConvolution c, float sample)
        {
            return c.kind == SynthConvolution.Direct
                ? DirectF(ref c, sample)
                : PartitionedF(&s->fft, ref c, sample);
        }

        static float DirectF(ref SynthConvolution c, float sample)
        {
            c.shiftRegister[c.shiftOffset] = sample;

            float result = 0;
            for (int i = 0; i < c.sampleCount - c.shiftOffset; ++i)
            {
                result += c.impulseResponse[i] * c.shiftRegister[i + c.shiftOffset];
            }

            for (int i = c.sampleCount - c.shiftOffset; i < c.sampleCount; ++i)
            {
                result += c.impulseResponse[i] * c.shiftRegister[i - (c.sampleCount - c.shiftOffset)];
            }

            c.shiftOffset = (c.shiftOffset - 1 + c.sampleCount) % c.sampleCount;

            return result;
        }

        static float PartitionedF(SynthFft* fft, ref SynthConvolution c, float sample)
        {
            int p = c.position;
            c.history[p] = sample;
            c.history[p + BlockSize] = sample;
            c.currentBlock[p] = sample;

            float result = 0;
            for (int j = 0; j < BlockSize; ++j)
            {
                result += c.head[j] * c.history[p + BlockSize - j];
            }

            result += c.tail[p];

            if (++c.position == BlockSize)
            {
                c.position = 0;
                if (c.partitions > 0) NextTail(fft, ref c);

                float* swap = c.previousBlock;
                c.previousBlock = c.currentBlock;
                c.currentBlock = swap;
            }

            return result;
        }

        static void NextTail(SynthFft* fft, ref SynthConvolution c)
        {
            c.newest = c.newest == 0 ? c.partitions - 1 : c.newest - 1;

            for (int i = 0; i < BlockSize; ++i) c.workRe[i] = c.previousBlock[i];
            for (int i = 0; i < BlockSize; ++i) c.workRe[BlockSize + i] = c.currentBlock[i];
            for (int i = 0; i < FftSize; ++i) c.workIm[i] = 0;
            Forward(fft, c.workRe, c.workIm);
            float* newestRe = c.delayRe + c.newest * Bins;
            float* newestIm = c.delayIm + c.newest * Bins;
            for (int b = 0; b < Bins; ++b)
            {
                newestRe[b] = c.workRe[b];
                newestIm[b] = c.workIm[b];
            }

            for (int b = 0; b < Bins; ++b)
            {
                c.sumRe[b] = 0;
                c.sumIm[b] = 0;
            }

            for (int k = 0; k < c.partitions; ++k)
            {
                int d = c.newest + k;
                if (d >= c.partitions) d -= c.partitions;

                float* hr = c.partitionRe + k * Bins, hi = c.partitionIm + k * Bins;
                float* xr = c.delayRe + d * Bins, xi = c.delayIm + d * Bins;
                for (int b = 0; b < Bins; ++b)
                {
                    c.sumRe[b] += hr[b] * xr[b] - hi[b] * xi[b];
                    c.sumIm[b] += hr[b] * xi[b] + hi[b] * xr[b];
                }
            }

            // Real signal: the upper half of the spectrum is the conjugate mirror of the lower.
            for (int b = 0; b < Bins; ++b)
            {
                c.workRe[b] = c.sumRe[b];
                c.workIm[b] = c.sumIm[b];
            }
            for (int b = Bins; b < FftSize; ++b)
            {
                c.workRe[b] = c.sumRe[FftSize - b];
                c.workIm[b] = -c.sumIm[FftSize - b];
            }

            Inverse(fft, c.workRe, c.workIm);
            for (int i = 0; i < BlockSize; ++i) c.tail[i] = c.workRe[BlockSize + i];
        }

        // ---- FFT ----

        static void Forward(SynthFft* fft, float* re, float* im) => Transform(fft, re, im, 1.0f);

        static void Inverse(SynthFft* fft, float* re, float* im)
        {
            Transform(fft, re, im, -1.0f);
            float scale = 1.0f / fft->size;
            for (int i = 0; i < fft->size; ++i)
            {
                re[i] *= scale;
                im[i] *= scale;
            }
        }

        static void Transform(SynthFft* fft, float* re, float* im, float direction)
        {
            int size = fft->size;
            for (int i = 0; i < size; ++i)
            {
                int j = fft->bitReverse[i];
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
                        float wr = fft->cos[k * stride];
                        float wi = direction * fft->sin[k * stride];

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

        // ---- Snapshot (managed) ----

        /// <summary>Copies a managed synthesizer's whole state (after its impulse responses are loaded and it is seeded).</summary>
        public static void Snapshot(Synthesizer synthesizer, DenseMemory memory, DenseSynthesizer* synth)
        {
            synth->inputChannelCount = synthesizer.InputChannelCount;
            synth->auxChannelCount = synthesizer.AuxChannelCount;
            synth->inputBufferSize = synthesizer.InputBufferSize;
            synth->inputSamplesRead = synthesizer.InputSamplesRead;
            synth->latency = synthesizer.Latency;
            synth->inputWriteOffset = synthesizer.InputWriteOffset;
            synth->lastInputSampleOffset = synthesizer.LastInputSampleOffset;
            synth->audioBufferSize = synthesizer.AudioBufferSize;
            synth->inputSampleRate = synthesizer.InputSampleRateValue;
            synth->audioSampleRate = synthesizer.GetAudioSampleRate();
            synth->audioParameters = synthesizer.GetAudioParameters();

            Copy(ref synth->antialiasing, synthesizer.Antialiasing, memory);
            Copy(ref synth->leveler, synthesizer.Leveler);
            synth->random.state = synthesizer.Random.State;
            Copy(ref synth->audioBuffer, synthesizer.AudioBuffer, memory);

            Copy(ref synth->forcedInductionAntialiasing, synthesizer.ForcedInductionAntialiasing, memory);
            Copy(ref synth->forcedInductionBuffer, synthesizer.ForcedInductionBuffer, memory);
            Copy(ref synth->mixLeveler, synthesizer.MixLeveler);
            Copy(ref synth->mixBuffer, synthesizer.MixBuffer, memory);
            SynthForcedInduction* fi = &synth->forcedInduction;
            fi->whinePhase = synthesizer.WhinePhase;
            fi->vibratoPhase = synthesizer.VibratoPhase;
            fi->flutterPhase = synthesizer.FlutterPhase;
            fi->counter = synthesizer.FiCounter;
            fi->mixEnvelope = synthesizer.MixEnvelope;
            fi->noiseState = synthesizer.FiNoiseState;
            Synthesizer.Bandpass[] bands = synthesizer.Bands;
            fi->whistleBand = Band(bands[0]);
            fi->whooshBand = Band(bands[1]);
            fi->flutterBand = Band(bands[2]);
            fi->blowoffBand1 = Band(bands[3]);
            fi->blowoffBand2 = Band(bands[4]);
            fi->wastegateBand = Band(bands[5]);

            synth->channels = memory.Alloc<SynthChannel>(synth->inputChannelCount + synth->auxChannelCount);
            for (int i = synth->inputChannelCount; i < synth->inputChannelCount + synth->auxChannelCount; ++i)
            {
                SynthChannel* channel = synth->channels + i;
                Copy(ref channel->data, synthesizer.InputData(i), memory);
                channel->transferBuffer = memory.Copy(synthesizer.TransferBuffer(i));
                channel->lastInputSample = synthesizer.LastInputSample(i);
            }

            for (int i = 0; i < synth->inputChannelCount; ++i)
            {
                SynthChannel* channel = synth->channels + i;
                Copy(ref channel->data, synthesizer.InputData(i), memory);
                channel->transferBuffer = memory.Copy(synthesizer.TransferBuffer(i));
                channel->lastInputSample = synthesizer.LastInputSample(i);

                Copy(synth, ref channel->convolution, synthesizer.Convolution(i), memory);

                DerivativeFilter derivative = synthesizer.Derivative(i);
                channel->derivative.dt = derivative.dt;
                channel->derivative.previous = derivative.Previous;

                JitterFilter jitter = synthesizer.Jitter(i);
                Copy(ref channel->jitter.noiseFilter, jitter.NoiseFilter, memory);
                channel->jitter.random.state = jitter.Random.State;
                channel->jitter.jitterScale = jitter.GetJitterScale();
                channel->jitter.maxJitter = jitter.MaxJitter;
                channel->jitter.offset = jitter.Offset;
                channel->jitter.history = memory.Copy(jitter.History);

                Copy(ref channel->airNoiseLowPass, synthesizer.AirNoiseLowPass(i), memory);

                LowPassFilter dc = synthesizer.InputDcFilter(i);
                channel->inputDcFilter.dt = dc.dt;
                channel->inputDcFilter.y = dc.Y;
                channel->inputDcFilter.rc = dc.Rc;

                Copy(ref channel->antialiasing, synthesizer.InputAntialiasing(i), memory);
            }
        }

        static SynthBandpass Band(Synthesizer.Bandpass b) => new SynthBandpass
        {
            b0 = b.b0, b2 = b.b2, a1 = b.a1, a2 = b.a2,
            x1 = b.x1, x2 = b.x2, y1 = b.y1, y2 = b.y2,
        };

        static void Copy<T>(ref SynthRingBuffer<T> target, RingBuffer<T> source, DenseMemory memory) where T : unmanaged
        {
            target.buffer = memory.Copy(source.Buffer);
            target.capacity = source.Capacity;
            target.writeIndex = source.WriteIndex();
            target.start = source.Start();
        }

        static void Copy(ref SynthButterworthFloat target, ButterworthLowPassFilterFloat source, DenseMemory memory)
        {
            Copy(ref target.y, source.YHistory, memory);
            Copy(ref target.x, source.XHistory, memory);
            float[] a = source.Coefficients;
            target.a0 = a[0];
            target.a1 = a[1];
            target.a2 = a[2];
            target.a3 = a[3];
            target.a4 = a[4];
            target.f_4 = source.F4;
        }

        static void Copy(ref SynthButterworthDouble target, ButterworthLowPassFilterDouble source, DenseMemory memory)
        {
            Copy(ref target.y, source.YHistory, memory);
            Copy(ref target.x, source.XHistory, memory);
            double[] a = source.Coefficients;
            target.a0 = a[0];
            target.a1 = a[1];
            target.a2 = a[2];
            target.a3 = a[3];
            target.a4 = a[4];
            target.f_4 = source.F4;
        }

        static void Copy(ref SynthLeveler target, LevelingFilter source)
        {
            target.peak = source.Peak;
            target.attenuation = source.GetAttenuation();
            target.p_maxLevel = source.p_maxLevel;
            target.p_minLevel = source.p_minLevel;
            target.p_target = source.p_target;
            target.p_ceiling = source.p_ceiling;
        }

        static void Copy(DenseSynthesizer* synth, ref SynthConvolution target, ImpulseResponseFilter source, DenseMemory memory)
        {
            switch (source)
            {
                case null:
                    throw new InvalidOperationException("A synthesizer channel has no impulse response: load one before the dense snapshot.");

                case ConvolutionFilter direct:
                    target.kind = SynthConvolution.Direct;
                    target.sampleCount = direct.GetSampleCount();
                    target.shiftRegister = memory.Copy(direct.ShiftRegister);
                    target.shiftOffset = direct.ShiftOffset;
                    target.impulseResponse = memory.Copy(direct.GetImpulseResponse());
                    break;

                case PartitionedConvolutionFilter partitioned:
                    target.kind = SynthConvolution.Partitioned;
                    target.sampleCount = partitioned.GetSampleCount();
                    target.head = memory.Copy(partitioned.Head);
                    target.history = memory.Copy(partitioned.History);
                    target.position = partitioned.Position;
                    target.partitions = partitioned.Partitions;
                    target.partitionRe = CopyRows(partitioned.PartitionRe, memory);
                    target.partitionIm = CopyRows(partitioned.PartitionIm, memory);
                    target.delayRe = CopyRows(partitioned.DelayRe, memory);
                    target.delayIm = CopyRows(partitioned.DelayIm, memory);
                    target.newest = partitioned.Newest;
                    target.previousBlock = memory.Copy(partitioned.PreviousBlock);
                    target.currentBlock = memory.Copy(partitioned.CurrentBlock);
                    target.tail = memory.Copy(partitioned.Tail);
                    target.workRe = memory.Alloc<float>(FftSize);
                    target.workIm = memory.Alloc<float>(FftSize);
                    target.sumRe = memory.Alloc<float>(Bins);
                    target.sumIm = memory.Alloc<float>(Bins);

                    if (synth->fft.size == 0)
                    {
                        Fft transform = PartitionedConvolutionFilter.SharedTransform;
                        synth->fft.size = transform.Size;
                        synth->fft.bitReverse = memory.Copy(transform.BitReverse);
                        synth->fft.cos = memory.Copy(transform.Cos);
                        synth->fft.sin = memory.Copy(transform.Sin);
                    }
                    break;

                default:
                    throw new ArgumentException($"Unknown convolution filter {source.GetType().Name}.", nameof(source));
            }
        }

        /// <summary>Rows of equal length (one per partition or delay slot) into one block, row k at [k × bins].</summary>
        static float* CopyRows(float[][] rows, DenseMemory memory)
        {
            float* block = memory.Alloc<float>(rows.Length * Bins);
            for (int k = 0; k < rows.Length; ++k)
            {
                if (rows[k].Length != Bins) throw new ArgumentException("Unexpected spectrum length.", nameof(rows));
                for (int b = 0; b < Bins; ++b) block[k * Bins + b] = rows[k][b];
            }

            return block;
        }
    }
}
