// Checks of what keeps designs buildable: firing orders, shared crank pins, reshaping.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System.Collections.Generic;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public sealed class DesignChecksTests
    {
        /// <summary>The design read from a preset.</summary>
        static EngineDesign PresetDesign(string name)
        {
            EngineDesign design = DesignExtraction.FromDescription(TestEngines.Preset(name).Description, out string reason);
            Assert.IsNotNull(design, reason);
            return design;
        }

        /// <summary>A design of this layout, cylinder count and bank angle, from the inline-4 superbike's parts.</summary>
        static EngineDesign Shaped(EngineLayout layout, int cylinders, double bankAngle)
        {
            EngineDesign design = PresetDesign("inline-4-superbike");
            design.bankAngle = bankAngle;
            design.Reshape(layout, cylinders);
            return design;
        }

        /// <summary>Every common firing order offered for a layout builds an engine the simulation can build.</summary>
        [TestCase(EngineLayout.Inline, 1, 0)]
        [TestCase(EngineLayout.Inline, 2, 0)]
        [TestCase(EngineLayout.Inline, 3, 0)]
        [TestCase(EngineLayout.Inline, 4, 0)]
        [TestCase(EngineLayout.Inline, 5, 0)]
        [TestCase(EngineLayout.Inline, 6, 0)]
        [TestCase(EngineLayout.Inline, 8, 0)]
        [TestCase(EngineLayout.Flat, 2, 0)]
        [TestCase(EngineLayout.Flat, 4, 0)]
        [TestCase(EngineLayout.Flat, 6, 0)]
        [TestCase(EngineLayout.V, 6, 120)]
        [TestCase(EngineLayout.V, 8, 90)]
        [TestCase(EngineLayout.V, 12, 60)]
        public void CommonFiringOrdersBuild(EngineLayout layout, int cylinders, double bankAngle)
        {
            EngineDesign design = Shaped(layout, cylinders, bankAngle);
            List<int[]> orders = FiringOrders.For(design);
            Assert.IsNotEmpty(orders, "no common order offered");
            foreach (int[] order in orders)
            {
                design.firingOrder = order;
                CollectionAssert.IsEmpty(EngineGenerator.Problems(design), FiringOrders.Format(order));
                EngineAssembly assembly = EngineBuilder.Build(EngineGenerator.Describe(design));
                Assert.AreEqual(cylinders, assembly.engine.GetCylinderCount(), FiringOrders.Format(order));
            }
        }

        /// <summary>
        /// A V's paired cylinders share a crank pin, so a firing order that needs the pin at two angles is refused, by name:
        /// the cross-plane V8 order at 75° between the banks.
        /// </summary>
        [Test]
        public void SharedPinConflictsAreNamed()
        {
            EngineDesign design = PresetDesign(TestEngines.V8);
            Assert.IsFalse(design.splitPins);
            design.bankAngle = 75;
            List<string> problems = EngineGenerator.Problems(design);
            Assert.IsNotEmpty(problems);
            StringAssert.Contains("share a crank pin", problems[0]);
            Assert.IsEmpty(FiringOrders.For(design), "an order offered that can't be built");
        }

        /// <summary>Reshaping keeps a design buildable: an inline-4 becomes a V8, a flat-6, an inline-3.</summary>
        [Test]
        public void ReshapedDesignsBuild()
        {
            EngineDesign design = PresetDesign("inline-4-superbike");
            (EngineLayout layout, int cylinders)[] shapes = { (EngineLayout.V, 8), (EngineLayout.Flat, 6), (EngineLayout.Inline, 3) };
            foreach ((EngineLayout layout, int cylinders) in shapes)
            {
                design.Reshape(layout, cylinders);
                CollectionAssert.IsEmpty(EngineGenerator.Problems(design), $"{layout} {cylinders}");
                EngineDescription description = EngineGenerator.Describe(design);
                Assert.AreEqual(cylinders, description.engine.pistons.Length, $"{layout} {cylinders}");
            }
        }
    }
}
