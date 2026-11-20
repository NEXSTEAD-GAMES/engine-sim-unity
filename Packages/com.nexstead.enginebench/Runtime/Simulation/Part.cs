// Port of engine-sim include/part.h and src/part.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.

using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>A part with a rigid body in the constraint solver.</summary>
    public abstract class Part
    {
        public readonly RigidBody body = new RigidBody();
    }
}
