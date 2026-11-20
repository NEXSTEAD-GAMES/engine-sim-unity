// Port of engine-sim include/delay_filter.h.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>Delays a signal by a whole number of samples; silent until the delay has filled.</summary>
    public sealed class DelayFilter : Filter
    {
        int latencySamples;
        readonly RingBuffer<double> history = new RingBuffer<double>();

        public void Initialize(double delay, double audioFrequency)
        {
            int samples = (int)Utilities.Round(delay * audioFrequency);
            int capacity = samples + 32;

            history.Initialize(capacity);
            latencySamples = samples;
        }

        // Read access for the dense core's snapshot.
        internal int LatencySamples => latencySamples;
        internal RingBuffer<double> History => history;

        public override float F(float sample)
        {
            return (float)FastF((double)sample);
        }

        public double FastF(double sample)
        {
            history.Write(sample);

            if (history.Size() <= latencySamples)
            {
                return 0;
            }
            else
            {
                return history.ReadAndRemove();
            }
        }
    }
}
