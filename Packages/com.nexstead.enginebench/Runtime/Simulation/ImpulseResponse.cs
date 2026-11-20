// Port of engine-sim include/impulse_response.h and src/impulse_response.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>An exhaust system's impulse response: a sound file and its volume. The synthesizer loads the samples.</summary>
    public sealed class ImpulseResponse
    {
        string filename;
        double volume = 1.0;

        public void Initialize(string filename, double volume)
        {
            this.filename = filename;
            this.volume = volume;
        }

        public string GetFilename() => filename;
        public double GetVolume() => volume;
    }
}
