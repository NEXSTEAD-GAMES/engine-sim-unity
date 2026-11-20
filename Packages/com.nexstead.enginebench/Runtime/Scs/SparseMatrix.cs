// Port of simple-2d-constraint-solver include/sparse_matrix.h (template SparseMatrix<T_Stride, T_Entries>).
// MIT licence; engine-sim's copyright and licence notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench.Scs
{
    /// <summary>
    /// A block-sparse matrix: each row holds <c>entries</c> blocks of <c>stride</c> values, and each block names the column
    /// block it belongs to (0xFF: empty). Upstream's template parameters are constructor arguments with the same defaults
    /// (3, 2); engine-sim uses only <c>SparseMatrix&lt;3&gt;</c>. As upstream, <see cref="Initialize"/> resets the block
    /// indices but not the values; values of empty blocks keep whatever they held.
    /// </summary>
    public sealed class SparseMatrix
    {
        public const byte EmptyBlock = 0xFF;

        readonly int stride;
        readonly int entries;

        double[] data;
        byte[] blockData;

        int width;
        int height;
        int capacityHeight;

        public SparseMatrix(int stride = 3, int entries = 2)
        {
            this.stride = stride;
            this.entries = entries;
        }

        public void Initialize(int width, int height)
        {
            Resize(width, height);
            for (int i = 0; i < entries * this.height; ++i)
            {
                blockData[i] = EmptyBlock;
            }
        }

        public void Resize(int width, int height)
        {
            if (width == this.width && height == this.height) return;
            else if (height > capacityHeight)
            {
                // Upstream's destroy() resets the size but not the capacity.
                if (data != null)
                {
                    data = null;
                    blockData = null;
                    this.width = this.height = 0;
                }

                capacityHeight = (height > capacityHeight)
                    ? height
                    : capacityHeight;

                data = new double[stride * entries * capacityHeight];
                blockData = new byte[capacityHeight * entries];
            }

            this.height = height;
            this.width = width;
        }

        public void Expand(Matrix matrix)
        {
            matrix.Initialize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < entries; ++j)
                {
                    byte block = blockData[i * entries + j];
                    if (block == EmptyBlock) continue;
                    else
                    {
                        for (int k = 0; k < stride; ++k)
                        {
                            matrix.Set(block * stride + k, i, data[Row(i) + j * stride + k]);
                        }
                    }
                }
            }
        }

        public void ExpandTransposed(Matrix matrix)
        {
            matrix.Initialize(height, width);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < entries; ++j)
                {
                    byte block = blockData[i * entries + j];
                    if (block == EmptyBlock) continue;
                    else
                    {
                        for (int k = 0; k < stride; ++k)
                        {
                            matrix.Set(i, block * stride + k, data[Row(i) + j * stride + k]);
                        }
                    }
                }
            }
        }

        public void SetBlock(int row, int entry, byte index)
        {
            blockData[row * entries + entry] = index;
        }

        public void Set(int row, int entry, int slice, double v)
        {
            data[Row(row) + entry * stride + slice] = v;
        }

        public double Get(int row, int entry, int slice)
        {
            return data[Row(row) + entry * stride + slice];
        }

        public void SetEmpty(int row, int col)
        {
            blockData[row * entries + col] = EmptyBlock;
            for (int i = 0; i < stride; ++i)
            {
                data[Row(row) + col * stride + i] = 0;
            }
        }

        public void MultiplyTranspose(SparseMatrix b_T, Matrix target)
        {
            target.Initialize(b_T.height, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < b_T.height; ++j)
                {
                    double dot = 0;
                    for (int k = 0; k < entries; ++k)
                    {
                        byte block0 = blockData[i * entries + k];
                        if (block0 == EmptyBlock) continue;

                        for (int l = 0; l < entries; ++l)
                        {
                            byte block1 = b_T.blockData[j * entries + l];
                            if (block0 == block1)
                            {
                                for (int m = 0; m < stride; ++m)
                                {
                                    dot +=
                                        data[Row(i) + k * stride + m]
                                        * b_T.data[b_T.Row(j) + l * stride + m];
                                }
                            }
                        }
                    }

                    target.Set(j, i, dot);
                }
            }
        }

        public void TransposeMultiplyVector(Matrix b, Matrix target)
        {
            target.Initialize(1, width);

            for (int i = 0; i < height; ++i)
            {
                for (int k = 0; k < entries; ++k)
                {
                    int offset = k * stride;
                    byte block = blockData[i * entries + k];
                    if (block == EmptyBlock) continue;

                    for (int l = 0; l < stride; ++l)
                    {
                        int j = block * stride + l;
                        target.Add(0, j, data[Row(i) + offset + l] * b.Get(0, i));
                    }
                }
            }
        }

        public void Multiply(Matrix b, Matrix target)
        {
            int b_w = b.GetWidth();

            target.Initialize(b.GetWidth(), height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < b_w; ++j)
                {
                    double v = 0.0;
                    for (int k = 0; k < entries; ++k)
                    {
                        int offset = k * stride;
                        byte block = blockData[i * entries + k];
                        if (block == EmptyBlock) continue;

                        for (int l = 0; l < stride; ++l)
                        {
                            v += data[Row(i) + offset + l] * b.Get(j, block * stride + l);
                        }
                    }

                    target.Set(j, i, v);
                }
            }
        }

        public void RightScale(Matrix scale, SparseMatrix target)
        {
            target.Initialize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < entries; ++j)
                {
                    byte index = blockData[i * entries + j];
                    if (index == EmptyBlock) continue;

                    target.SetBlock(i, j, index);

                    for (int k = 0; k < stride; ++k)
                    {
                        target.Set(
                            i,
                            j,
                            k,
                            scale.Get(0, index * stride + k) * data[Row(i) + j * stride + k]);
                    }
                }
            }
        }

        public void LeftScale(Matrix scale, SparseMatrix target)
        {
            target.Initialize(width, height);

            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < entries; ++j)
                {
                    byte index = blockData[i * entries + j];
                    if (index == EmptyBlock) continue;

                    target.SetBlock(i, j, index);

                    for (int k = 0; k < stride; ++k)
                    {
                        target.Set(
                            i,
                            j,
                            k,
                            scale.Get(0, i) * data[Row(i) + j * stride + k]);
                    }
                }
            }
        }

        public int GetWidth() => width;
        public int GetHeight() => height;

        int Row(int row) => row * entries * stride;
    }
}
