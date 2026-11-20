// Port of engine-sim include/throttle.h and src/throttle.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    /// <summary>Turns the speed control (0..1) into a throttle plate position each step.</summary>
    public class Throttle
    {
        protected double speedControl = 0;

        public virtual void SetSpeedControl(double s)
        {
            speedControl = s;
        }

        public virtual void Update(double dt, Engine engine)
        {
            /* void */
        }

        public double GetSpeedControl() => speedControl;
    }
}
