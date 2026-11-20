// Port of simple-2d-constraint-solver include/matrix.h and src/matrix.cpp.
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>
    /// A dense matrix stored row-major in one array. As upstream, a row's stride is the current width, not the capacity, so a
    /// resize within capacity reinterprets the existing values without moving them. Upstream's <c>fastRowSwap</c> (row
    /// pointer swap, used only by the Gaussian elimination solver) is not ported.
    /// </summary>
    public sealed class Matrix
    {
        double[] data;
        int width;
        int height;
        int capacityWidth;
        int capacityHeight;

        public Matrix()
        {
            /* void */
        }

        public Matrix(int width, int height, double value = 0.0)
        {
            Initialize(width, height, value);
        }

        public void Initialize(int width, int height, double value)
        {
            Resize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    data[i * this.width + j] = value;
                }
            }
        }

        public void Initialize(int width, int height)
        {
            Resize(width, height);
            Array.Clear(data, 0, width * height);
        }

        public void Resize(int width, int height)
        {
            if (width == this.width && height == this.height) return;
            else if (width > capacityWidth || height > capacityHeight)
            {
                // Upstream's destroy() runs first and zeroes the capacities, so the new capacity is the new size.
                if (data != null)
                {
                    data = null;
                    this.width = this.height = 0;
                    capacityWidth = capacityHeight = 0;
                }

                capacityWidth = (width > capacityWidth)
                    ? width
                    : capacityWidth;

                capacityHeight = (height > capacityHeight)
                    ? height
                    : capacityHeight;

                data = new double[capacityWidth * capacityHeight];
            }

            this.height = height;
            this.width = width;
        }

        public void Set(double[] data)
        {
            Array.Copy(data, this.data, width * height);
        }

        public void Set(int column, int row, double value)
        {
            data[row * width + column] = value;
        }

        public void Add(int column, int row, double value)
        {
            data[row * width + column] += value;
        }

        public double Get(int column, int row)
        {
            return data[row * width + column];
        }

        public void Set(Matrix reference)
        {
            Resize(reference.width, reference.height);

            for (int i = 0; i < reference.height; ++i)
            {
                for (int j = 0; j < reference.width; ++j)
                {
                    data[i * width + j] = reference.data[i * reference.width + j];
                }
            }
        }

        public void Multiply(Matrix b, Matrix target)
        {
            target.Resize(b.width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < b.width; ++j)
                {
                    double v = 0.0;
                    for (int ii = 0; ii < width; ++ii)
                    {
                        v += data[i * width + ii] * b.data[ii * b.width + j];
                    }

                    target.data[i * target.width + j] = v;
                }
            }
        }

        public void ComponentMultiply(Matrix b, Matrix target)
        {
            target.Resize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    target.Set(j, i, Get(j, i) * b.Get(j, i));
                }
            }
        }

        public void TransposeMultiply(Matrix b, Matrix target)
        {
            target.Resize(b.width, width);

            for (int i = 0; i < width; ++i)
            {
                for (int j = 0; j < b.width; ++j)
                {
                    double v = 0.0;
                    for (int ii = 0; ii < height; ++ii)
                    {
                        v += data[ii * width + i] * b.data[ii * b.width + j];
                    }

                    target.data[i * target.width + j] = v;
                }
            }
        }

        public void LeftScale(Matrix scale, Matrix target)
        {
            target.Resize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    target.data[i * target.width + j] = scale.data[i * scale.width + 0] * data[i * width + j];
                }
            }
        }

        public void RightScale(Matrix scale, Matrix target)
        {
            target.Resize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    target.data[i * target.width + j] = scale.data[j * scale.width + 0] * data[i * width + j];
                }
            }
        }

        public void Scale(double s, Matrix target)
        {
            target.Resize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    target.data[i * target.width + j] = s * data[i * width + j];
                }
            }
        }

        public void Subtract(Matrix b, Matrix target)
        {
            target.Resize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    target.data[i * target.width + j] = data[i * width + j] - b.data[i * b.width + j];
                }
            }
        }

        public void Add(Matrix b, Matrix target)
        {
            target.Resize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    target.data[i * target.width + j] = data[i * width + j] + b.data[i * b.width + j];
                }
            }
        }

        public void Negate(Matrix target)
        {
            target.Resize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    target.data[i * target.width + j] = -data[i * width + j];
                }
            }
        }

        public bool Equals(Matrix b, double err = 1e-6)
        {
            if (GetWidth() != b.GetWidth()) return false;
            if (GetHeight() != b.GetHeight()) return false;

            for (int i = 0; i < GetHeight(); ++i)
            {
                for (int j = 0; j < GetWidth(); ++j)
                {
                    if (Math.Abs(Get(j, i) - b.Get(j, i)) > err)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Upstream indexes these column vectors as m_matrix[0][i], which walks the contiguous storage: element i.
        public double VectorMagnitudeSquared()
        {
            double mag = 0;
            for (int i = 0; i < height; ++i)
            {
                mag += data[i] * data[i];
            }

            return mag;
        }

        public double Dot(Matrix b)
        {
            double result = 0;
            for (int i = 0; i < height; ++i)
            {
                result += data[i] * b.data[i];
            }

            return result;
        }

        public void Madd(Matrix b, double s)
        {
            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    data[i * width + j] += b.data[i * b.width + j] * s;
                }
            }
        }

        public void Pmadd(Matrix b, double s)
        {
            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    data[i * width + j] = s * data[i * width + j] + b.data[i * b.width + j];
                }
            }
        }

        public void Transpose(Matrix target)
        {
            target.Resize(height, width);

            for (int i = 0; i < width; ++i)
            {
                for (int j = 0; j < height; ++j)
                {
                    target.data[i * target.width + j] = data[j * width + i];
                }
            }
        }

        public int GetWidth() => width;
        public int GetHeight() => height;
    }
}
