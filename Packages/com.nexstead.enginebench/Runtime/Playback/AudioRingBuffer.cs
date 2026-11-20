// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Threading;
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// A lock-free ring of floats for one producer thread and one consumer thread: the simulation thread writes rendered audio,
    /// Unity's audio thread reads it. No locks, no allocation after construction.
    /// </summary>
    /// <remarks>
    /// The producer owns the write position and the consumer the read position. Each side publishes its position with
    /// <see cref="Volatile.Write(ref int, int)"/> after touching the samples, and reads the other side's with
    /// <see cref="Volatile.Read(ref int)"/> before touching them, so samples are never read before they are written or
    /// overwritten before they are read. Positions are free-running <c>int</c> counters: they wrap after 2^31 samples
    /// (13.5 hours at 44.1 kHz), and unchecked subtraction still gives the right fill level, since it never exceeds the
    /// capacity.
    /// </remarks>
    public sealed class AudioRingBuffer
    {
        readonly float[] buffer;
        readonly int mask;
        int writePosition;
        int readPosition;

        /// <param name="capacity">A power of two.</param>
        public AudioRingBuffer(int capacity)
        {
            if (capacity < 1 || (capacity & (capacity - 1)) != 0)
            {
                throw new ArgumentException("The capacity must be a power of two.", nameof(capacity));
            }

            buffer = new float[capacity];
            mask = capacity - 1;
        }

        public int Capacity => buffer.Length;

        /// <summary>Samples waiting to be read. Exact on the consumer side; a lower bound on the producer side.</summary>
        public int Count => unchecked(Volatile.Read(ref writePosition) - Volatile.Read(ref readPosition));

        /// <summary>Room to write. Exact on the producer side; a lower bound on the consumer side.</summary>
        public int Free => buffer.Length - Count;

        /// <summary>Producer only. Writes as many of <paramref name="count"/> samples as fit; returns how many.</summary>
        public int Write(float[] source, int offset, int count)
        {
            int write = writePosition;
            int read = Volatile.Read(ref readPosition);
            int free = buffer.Length - unchecked(write - read);
            int n = Math.Min(count, free);
            if (n <= 0) return 0;

            int start = write & mask;
            int first = Math.Min(n, buffer.Length - start);
            Array.Copy(source, offset, buffer, start, first);
            Array.Copy(source, offset + first, buffer, 0, n - first);

            Volatile.Write(ref writePosition, unchecked(write + n));
            return n;
        }

        /// <summary>Consumer only. Reads up to <paramref name="count"/> samples; returns how many.</summary>
        public int Read(float[] target, int offset, int count)
        {
            int read = readPosition;
            int write = Volatile.Read(ref writePosition);
            int available = unchecked(write - read);
            int n = Math.Min(count, available);
            if (n <= 0) return 0;

            int start = read & mask;
            int first = Math.Min(n, buffer.Length - start);
            Array.Copy(buffer, start, target, offset, first);
            Array.Copy(buffer, 0, target, offset + first, n - first);

            Volatile.Write(ref readPosition, unchecked(read + n));
            return n;
        }

        /// <summary>Empties the ring. Only while neither the producer nor the consumer is running.</summary>
        public void Clear()
        {
            Volatile.Write(ref writePosition, 0);
            Volatile.Write(ref readPosition, 0);
        }
    }
}
