// Port of engine-sim include/function.h, src/function.cpp, include/gaussian_filter.h and
// src/gaussian_filter.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Scs;

namespace Nexstead.EngineBench.Simulation
{
    public sealed class GaussianFilter
    {
        double[] cache;
        int cacheSteps;
        double radius;
        double alpha;
        double exp_s;
        double inv_r;

        public void Initialize(double alpha, double radius, int cacheSteps = 1024)
        {
            this.cacheSteps = cacheSteps;
            this.alpha = alpha;
            this.radius = radius;
            exp_s = Math.Exp(-this.alpha * this.radius * this.radius);
            inv_r = 1 / this.radius;

            GenerateCache();
        }

        public double Evaluate(double s)
        {
            int actualSteps = cacheSteps - 32;
            double s_sample = actualSteps * Math.Abs(s) * inv_r;
            // Beyond the radius the kernel is 0, the cache's last steps: clamp rather than read past the cache (the
            // community fork's fix, Axxiant Ltd, e070a8e).
            double s_clamped = Math.Min(s_sample, actualSteps);
            double s0 = Math.Floor(s_clamped);
            double s1 = Math.Ceiling(s_clamped);
            double d = s_clamped - s0;

            return
                (1 - d) * cache[(int)s0]
                + d * cache[(int)s1];
        }

        public double GetRadius() => radius;
        public double GetAlpha() => alpha;

        double Calculate(double s)
        {
            return Utilities.StdMax(
                0.0,
                Math.Exp(-alpha * s * s) - exp_s);
        }

        void GenerateCache()
        {
            int actualSteps = cacheSteps - 32;
            double step = 1.0 / actualSteps;
            cache = new double[cacheSteps];
            for (int i = 0; i <= actualSteps; ++i)
            {
                double s = i * step * radius;
                cache[i] = Calculate(s);
            }

            for (int i = actualSteps + 1; i < cacheSteps; ++i)
            {
                cache[i] = 0.0;
            }
        }
    }

    /// <summary>A sampled function of one variable, kept sorted by x.</summary>
    public sealed class Function
    {
        // Upstream creates this on the first Function's construction; it is immutable afterwards.
        static readonly GaussianFilter DefaultGaussianFilter = CreateDefaultGaussianFilter();

        double[] x;
        double[] y;
        double yMin;
        double yMax;
        double inputScale = 1.0;
        double outputScale = 1.0;
        double filterRadius;
        int capacity;
        int size;
        GaussianFilter gaussianFilter;

        static GaussianFilter CreateDefaultGaussianFilter()
        {
            var filter = new GaussianFilter();
            filter.Initialize(1.0, 3.0, 1024);
            return filter;
        }

        public void Initialize(int size, double filterRadius, GaussianFilter filter = null)
        {
            Resize(size);
            this.size = 0;
            this.filterRadius = filterRadius;

            gaussianFilter = filter ?? DefaultGaussianFilter;
        }

        public void Resize(int newCapacity)
        {
            var new_x = new double[newCapacity];
            var new_y = new double[newCapacity];

            if (size > 0)
            {
                Array.Copy(x, new_x, size);
                Array.Copy(y, new_y, size);
            }

            x = new_x;
            y = new_y;

            capacity = newCapacity;
        }

        public void SetInputScale(double s) => inputScale = s;
        public void SetOutputScale(double s) => outputScale = s;

        public void AddSample(double x, double y)
        {
            if (size + 1 > capacity)
            {
                Resize(capacity * 2 + 1);
            }

            yMin = Utilities.Fmin(yMin, y);
            yMax = Utilities.Fmax(yMax, y);

            int closest = ClosestSample(x);
            if (closest == -1)
            {
                size = 1;

                this.x[0] = x;
                this.y[0] = y;
                return;
            }

            int index = x < this.x[closest]
                ? closest
                : closest + 1;

            ++size;

            int sizeToCopy = size - index - 1;
            if (sizeToCopy > 0)
            {
                Array.Copy(this.x, index, this.x, index + 1, sizeToCopy);
                Array.Copy(this.y, index, this.y, index + 1, sizeToCopy);
            }

            this.x[index] = x;
            this.y[index] = y;
        }

        public double SampleTriangle(double x)
        {
            x *= inputScale;

            int closest = ClosestSample(x);
            if (size == 0) return 0;
            else if (x >= this.x[size - 1]) return y[size - 1] * outputScale;
            else if (x <= this.x[0]) return y[0] * outputScale;

            double sum = 0;
            double totalWeight = 0;

            for (int i = closest; i >= 0; --i)
            {
                if (this.x[i] > x) continue;
                if (Math.Abs(x - this.x[i]) > filterRadius) break;

                double w = Triangle(this.x[i] - x);
                sum += w * y[i];
                totalWeight += w;
            }

            for (int i = closest; i < size; ++i)
            {
                if (this.x[i] <= x) continue;
                if (Math.Abs(this.x[i] - x) > filterRadius) break;

                double w = Triangle(this.x[i] - x);
                sum += w * y[i];
                totalWeight += w;
            }

            return (totalWeight != 0)
                ? sum * outputScale / totalWeight
                : 0;
        }

        public double SampleGaussian(double x)
        {
            x *= inputScale;

            int closest = ClosestSample(x);
            double filterRadius = this.filterRadius * gaussianFilter.GetRadius();

            double sum = 0;
            double totalWeight = 0;

            if (size == 0) return 0;
            else if (x > this.x[size - 1])
            {
                double w = gaussianFilter.Evaluate(0);
                sum += w * y[size - 1];
                totalWeight += w;
            }
            else if (x < this.x[0])
            {
                double w = gaussianFilter.Evaluate(0);
                sum += w * y[0];
                totalWeight += w;
            }

            for (int i = closest; i >= 0; --i)
            {
                if (Math.Abs(x - this.x[i]) > filterRadius) break;

                double w = gaussianFilter.Evaluate((this.x[i] - x) / this.filterRadius);
                sum += w * y[i];
                totalWeight += w;
            }

            for (int i = closest + 1; i < size; ++i)
            {
                if (Math.Abs(this.x[i] - x) > filterRadius) break;

                double w = gaussianFilter.Evaluate((this.x[i] - x) / this.filterRadius);
                sum += w * y[i];
                totalWeight += w;
            }

            return (totalWeight != 0)
                ? sum * outputScale / totalWeight
                : 0;
        }

        public double Triangle(double x)
        {
            return (filterRadius - Math.Abs(x)) / filterRadius;
        }

        public int ClosestSample(double x)
        {
            if (double.IsNaN(x))
            {
                return 0;
            }

            int l = 0;
            int r = size - 1;

            if (size == 0) return -1;
            else if (x <= this.x[l]) return l;
            else if (x >= this.x[r]) return r;

            while (l + 1 < r)
            {
                int m = (l + r) / 2;

                if (x > this.x[m])
                {
                    l = m;
                }
                else if (x < this.x[m])
                {
                    r = m;
                }
                else if (x == this.x[m])
                {
                    return m;
                }
            }

            return (x - this.x[l] < this.x[r] - x)
                ? l
                : r;
        }

        public bool IsOrdered()
        {
            for (int i = 0; i < size - 1; ++i)
            {
                if (x[i] > x[i + 1]) return false;
            }

            return true;
        }

        public void GetDomain(out double x0, out double x1)
        {
            if (size == 0)
            {
                x0 = x1 = 0;
            }
            else
            {
                x0 = x[0];
                x1 = x[size - 1];
            }
        }

        public void GetRange(out double y0, out double y1)
        {
            y0 = yMin;
            y1 = yMax;
        }

        public int Size => size;
        public double X(int i) => x[i];
        public double Y(int i) => y[i];
        public double FilterRadius => filterRadius;
        public double InputScale => inputScale;
        public double OutputScale => outputScale;
    }
}
