// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System.Collections.Generic;

namespace Nexstead.EngineBench.Design
{
    /// <summary>
    /// Common even-firing orders by layout and cylinder count, in the designs' numbering (bank by bank: a V8's or a flat
    /// engine's first bank is cylinders 1 to 4). Makers number their banks either way round, so on two banks each order is
    /// also tried with the banks' numbers swapped. <see cref="For"/> offers those a design can build: a V's pairs share crank
    /// pins, so its orders depend on the bank angle.
    /// </summary>
    public static class FiringOrders
    {
        static readonly Dictionary<(EngineLayout, int), int[][]> Common = new Dictionary<(EngineLayout, int), int[][]>
        {
            [(EngineLayout.Inline, 1)] = new[] { new[] { 1 } },
            [(EngineLayout.Inline, 2)] = new[] { new[] { 1, 2 } },
            [(EngineLayout.Inline, 3)] = new[] { new[] { 1, 2, 3 }, new[] { 1, 3, 2 } },
            [(EngineLayout.Inline, 4)] = new[] { new[] { 1, 3, 4, 2 }, new[] { 1, 2, 4, 3 } },
            [(EngineLayout.Inline, 5)] = new[] { new[] { 1, 2, 4, 5, 3 } },
            [(EngineLayout.Inline, 6)] = new[] { new[] { 1, 5, 3, 6, 2, 4 }, new[] { 1, 4, 2, 6, 3, 5 } },
            [(EngineLayout.Inline, 8)] = new[] { new[] { 1, 6, 2, 5, 8, 3, 7, 4 } },
            [(EngineLayout.Flat, 2)] = new[] { new[] { 1, 2 } },
            [(EngineLayout.Flat, 4)] = new[] { new[] { 1, 2, 3, 4 }, new[] { 1, 4, 3, 2 } },
            [(EngineLayout.Flat, 6)] = new[] { new[] { 1, 6, 2, 4, 3, 5 } },
            [(EngineLayout.V, 6)] = new[] { new[] { 1, 4, 2, 5, 3, 6 } },
            [(EngineLayout.V, 8)] = new[]
            {
                new[] { 1, 8, 4, 5, 7, 3, 6, 2 }, new[] { 1, 8, 6, 2, 7, 3, 4, 5 }, new[] { 1, 5, 3, 7, 4, 8, 2, 6 },
            },
            [(EngineLayout.V, 12)] = new[] { new[] { 1, 7, 5, 11, 3, 9, 6, 12, 2, 8, 4, 10 } },
        };

        /// <summary>The common orders a design of this layout, cylinder count and bank angle can build.</summary>
        public static List<int[]> For(EngineDesign design)
        {
            var orders = new List<int[]>();
            if (!Common.TryGetValue((design.layout, design.cylinders), out int[][] candidates)) return orders;

            int[] kept = design.firingOrder;
            var offered = new HashSet<string>();
            try
            {
                foreach (int[] common in candidates)
                {
                    int[][] tries = design.Banks == 2 ? new[] { common, BanksSwapped(common) } : new[] { common };
                    foreach (int[] order in tries)
                    {
                        design.firingOrder = order;
                        if (EngineGenerator.FiringProblems(design).Count == 0 && offered.Add(Format(order))) orders.Add(order);
                    }
                }
            }
            finally
            {
                design.firingOrder = kept;
            }

            return orders;
        }

        /// <summary>The order with the two banks' cylinder numbers swapped (1 for 1 + half, and back), starting from cylinder 1.</summary>
        static int[] BanksSwapped(int[] order)
        {
            int half = order.Length / 2;
            var swapped = new int[order.Length];
            int start = 0;
            for (int k = 0; k < order.Length; ++k)
            {
                swapped[k] = order[k] <= half ? order[k] + half : order[k] - half;
                if (swapped[k] == 1) start = k;
            }

            var rotated = new int[order.Length];
            for (int k = 0; k < order.Length; ++k) rotated[k] = swapped[(start + k) % order.Length];
            return rotated;
        }

        /// <summary>An order written as its cylinders joined by dashes: "1-3-4-2".</summary>
        public static string Format(int[] order) => order == null ? "" : string.Join("-", order);

        /// <summary>An order from cylinder numbers separated by dashes, spaces or commas; null if it holds anything else.</summary>
        public static int[] Parse(string text)
        {
            var order = new List<int>();
            foreach (string part in (text ?? "").Split(new[] { '-', ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out int cylinder)) return null;
                order.Add(cylinder);
            }

            return order.ToArray();
        }
    }
}
