// Port of engine-sim include/convolution_filter.h and src/convolution_filter.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>A filter that convolves its input with an impulse response, one output per input sample.</summary>
    public abstract class ImpulseResponseFilter : Filter
    {
        /// <summary>Takes the first <paramref name="length"/> values of <paramref name="impulseResponse"/>; resets the filter's state.</summary>
        public abstract void SetImpulseResponse(float[] impulseResponse, int length);

        public abstract int GetSampleCount();
    }

    /// <summary>
    /// Direct-form convolution: upstream's arithmetic exactly, at a cost of one multiply-add per tap per sample. The reference
    /// for <see cref="PartitionedConvolutionFilter"/>.
    /// </summary>
    public sealed class ConvolutionFilter : ImpulseResponseFilter
    {
        float[] shiftRegister;
        int shiftOffset;
        float[] impulseResponse;
        int sampleCount;

        public void Initialize(int samples)
        {
            sampleCount = samples;
            shiftOffset = 0;
            shiftRegister = new float[samples];
            impulseResponse = new float[samples];
        }

        public override float F(float sample)
        {
            shiftRegister[shiftOffset] = sample;

            float result = 0;
            for (int i = 0; i < sampleCount - shiftOffset; ++i)
            {
                result += impulseResponse[i] * shiftRegister[i + shiftOffset];
            }

            for (int i = sampleCount - shiftOffset; i < sampleCount; ++i)
            {
                result += impulseResponse[i] * shiftRegister[i - (sampleCount - shiftOffset)];
            }

            shiftOffset = (shiftOffset - 1 + sampleCount) % sampleCount;

            return result;
        }

        public override void SetImpulseResponse(float[] impulseResponse, int length)
        {
            Initialize(length);
            Array.Copy(impulseResponse, this.impulseResponse, length);
        }

        public override int GetSampleCount() => sampleCount;
        public float[] GetImpulseResponse() => impulseResponse;

        // Read access for the dense core's snapshot.
        internal float[] ShiftRegister => shiftRegister;
        internal int ShiftOffset => shiftOffset;
    }
}
