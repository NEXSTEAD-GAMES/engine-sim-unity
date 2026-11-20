// Port of engine-sim include/ring_buffer.h.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class RingBuffer<T>
    {
        T[] buffer;
        int capacity;
        int writeIndex;
        int start;

        public void Initialize(int capacity)
        {
            buffer = new T[capacity];
            this.capacity = capacity;
            writeIndex = 0;
            start = 0;
        }

        public void Write(T data)
        {
            buffer[writeIndex] = data;

            if (++writeIndex >= capacity)
            {
                writeIndex = 0;
            }
        }

        public void Overwrite(T data, int index)
        {
            if (start + index < capacity)
            {
                buffer[start + index] = data;
            }
            else
            {
                buffer[start + index - capacity] = data;
            }
        }

        public int Index(int @base, int offset)
        {
            if (offset == 0) return @base;
            else if (offset < 0)
            {
                int offset_u = -offset;
                if (offset_u <= @base) return @base - offset_u;
                else return (@base + capacity) - offset_u;
            }
            else
            {
                int rawOffset = @base + offset;
                if (rawOffset >= capacity) return rawOffset - capacity;
                else return rawOffset;
            }
        }

        public T Read(int index)
        {
            return (start + index) >= capacity
                ? buffer[start + index - capacity]
                : buffer[start + index];
        }

        public void Read(int n, T[] target)
        {
            if (start + n < capacity)
            {
                Array.Copy(buffer, start, target, 0, n);
            }
            else
            {
                Array.Copy(buffer, start, target, 0, capacity - start);
                Array.Copy(buffer, 0, target, capacity - start, n - (capacity - start));
            }
        }

        public void ReadAndRemove(int n, T[] target)
        {
            Read(n, target);

            start += n;
            if (start >= capacity)
            {
                start -= capacity;
            }
        }

        /// <summary>Removes and returns the oldest value (upstream's <c>readAndRemove(1, &amp;v)</c>).</summary>
        public T ReadAndRemove()
        {
            T v = buffer[start];

            start += 1;
            if (start >= capacity)
            {
                start -= capacity;
            }

            return v;
        }

        public void SetWriteIndex(int writeIndex) => this.writeIndex = writeIndex;

        public void RemoveBeginning(int n)
        {
            start += n;
            if (start >= capacity)
            {
                start -= capacity;
            }
        }

        public void SetStartIndex(int startIndex) => start = startIndex;

        public int Size()
        {
            return (writeIndex < start)
                ? writeIndex + (capacity - start)
                : writeIndex - start;
        }

        public int WriteIndex() => writeIndex;
        public int Start() => start;

        // Read access for the dense core's snapshot.
        internal T[] Buffer => buffer;
        internal int Capacity => capacity;
    }
}
