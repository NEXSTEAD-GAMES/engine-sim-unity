// Checks of the Bench's extension hook: which BenchExtension types it finds and how it orders them.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System.Linq;
using Nexstead.EngineBench.Editor;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Tests
{
    /// <summary>Shown in the Bench while the package's tests are compiled (the package's own project).</summary>
    public sealed class TestBenchExtension : BenchExtension
    {
        public override string Title => "Test extension (package tests)";
        public override int Order => -100;

        public override VisualElement CreateGUI(BenchContext context)
        {
            var label = new Label("No engine.");
            context.EngineChanged += engine => label.text = engine != null ? $"Engine: {engine.name}." : "No engine.";
            return label;
        }
    }

    /// <summary>Found, but drawn after <see cref="TestBenchExtension"/>: a higher order.</summary>
    public sealed class LaterTestBenchExtension : BenchExtension
    {
        public override string Title => "Later test extension (package tests)";
        public override int Order => 100;

        public override VisualElement CreateGUI(BenchContext context) => new Label("Later test extension.");
    }

    public abstract class AbstractTestBenchExtension : BenchExtension
    {
    }

    internal sealed class InternalTestBenchExtension : BenchExtension
    {
        public override string Title => "Never shown";

        public override VisualElement CreateGUI(BenchContext context) => new Label("Later test extension.");
    }

    public sealed class NoDefaultConstructorTestBenchExtension : BenchExtension
    {
        public NoDefaultConstructorTestBenchExtension(int unused)
        {
        }

        public override string Title => "Never shown";

        public override VisualElement CreateGUI(BenchContext context) => new Label("Later test extension.");
    }

    public sealed class BenchExtensionTests
    {
        /// <summary>Public, non-abstract types with a parameterless constructor are found; the others aren't.</summary>
        [Test]
        public void FindsPublicConcreteExtensions()
        {
            var types = BenchExtensions.Types;
            CollectionAssert.Contains(types, typeof(TestBenchExtension));
            CollectionAssert.Contains(types, typeof(LaterTestBenchExtension));
            CollectionAssert.DoesNotContain(types, typeof(AbstractTestBenchExtension));
            CollectionAssert.DoesNotContain(types, typeof(InternalTestBenchExtension));
            CollectionAssert.DoesNotContain(types, typeof(NoDefaultConstructorTestBenchExtension));
        }

        /// <summary>
        /// The context tells extensions about every change of engine, none included, and nothing when it stays the same; and
        /// apart, of the saved engine it is a copy of, which changes alone when a preset's copy is saved as a new engine.
        /// </summary>
        [Test]
        public void ContextReportsEngineChanges()
        {
            var context = new BenchContext(_ => { });
            var seen = new System.Collections.Generic.List<EngineAsset>();
            var sources = new System.Collections.Generic.List<EngineAsset>();
            context.EngineChanged += seen.Add;
            context.SourceChanged += sources.Add;
            EngineAsset v8 = TestEngines.Preset(TestEngines.V8);
            EngineAsset saved = TestEngines.Preset("boxer-4-turbo");
            context.SetEngine(v8, v8);
            context.SetEngine(v8, v8);
            context.SetEngine(v8, saved);
            context.SetEngine(null, null);
            CollectionAssert.AreEqual(new[] { v8, null }, seen);
            CollectionAssert.AreEqual(new[] { v8, saved, null }, sources);
            Assert.IsNull(context.Engine);
            Assert.IsNull(context.Source);
        }

        /// <summary>One instance of each, drawn by rising order.</summary>
        [Test]
        public void CreatesOneOfEachByOrder()
        {
            var created = BenchExtensions.CreateAll();
            try
            {
                Assert.AreEqual(1, created.Count(e => e is TestBenchExtension));
                int first = created.FindIndex(e => e is TestBenchExtension);
                int later = created.FindIndex(e => e is LaterTestBenchExtension);
                Assert.Less(first, later);
                for (int i = 1; i < created.Count; ++i) Assert.LessOrEqual(created[i - 1].Order, created[i].Order);
            }
            finally
            {
                foreach (BenchExtension extension in created) extension.Dispose();
            }
        }
    }
}
