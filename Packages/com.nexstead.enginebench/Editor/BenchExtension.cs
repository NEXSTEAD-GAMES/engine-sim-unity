// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// A section another package adds to the Bench window: a tab in its top bar, after the Bench's own sections. For example
    /// an exporter that renders the selected engine with <see cref="EngineRenderer"/>. Derive a public, non-abstract class
    /// with a public parameterless constructor; the Bench finds it (<see cref="BenchExtensions.Types"/>), makes one instance
    /// per window, builds its content once (<see cref="CreateGUI"/>) and shows it while its tab is selected, and disposes it
    /// when the window closes or the scripts reload. The Bench itself references no extension. Part of the extension API
    /// (stable within a major version: members may be added, as virtual ones with defaults).
    /// </summary>
    public abstract class BenchExtension : IDisposable
    {
        /// <summary>The section's tab.</summary>
        public abstract string Title { get; }

        /// <summary>Tabs come by rising order, then by type name.</summary>
        public virtual int Order => 0;

        /// <summary>
        /// Builds the section's content (UI Toolkit), once per window. <see cref="BenchContext.Engine"/> is null here: the
        /// engine the window starts with arrives through <see cref="BenchContext.EngineChanged"/> right after, as every later
        /// one does. The element stays in the window while it is open, hidden while another tab is selected, so its scheduler
        /// keeps running: use it for work in progress.
        /// </summary>
        public abstract VisualElement CreateGUI(BenchContext context);

        /// <summary>
        /// The window closed, or the scripts are about to reload (it runs before every assembly reload): stop any work and let
        /// go of what the extension holds.
        /// </summary>
        public virtual void Dispose()
        {
        }
    }

    /// <summary>What the Bench window hands its extensions.</summary>
    public sealed class BenchContext
    {
        readonly Action<EngineAsset> putOnBench;

        /// <param name="putOnBench">The window's own, behind <see cref="PutOnBench"/>.</param>
        internal BenchContext(Action<EngineAsset> putOnBench)
        {
            this.putOnBench = putOnBench;
        }

        /// <summary>
        /// The engine on the Bench; null while none is. Always the Bench's in-memory copy of the engine picked
        /// (<see cref="EnginePresets.TemporaryCopy"/>), with the user's changes: they reach the project only when saved.
        /// </summary>
        public EngineAsset Engine { get; private set; }

        /// <summary>The Bench picked another engine, or none (null).</summary>
        public event Action<EngineAsset> EngineChanged;

        /// <summary>
        /// The saved engine <see cref="Engine"/> is a copy of: the preset or project engine picked, or the engine a preset's
        /// copy was saved as; null while no engine is on the Bench. For the package's development project, which saves a
        /// preset's copy over its preset.
        /// </summary>
        internal EngineAsset Source { get; private set; }

        /// <summary><see cref="Source"/> is another saved engine, or none (null).</summary>
        internal event Action<EngineAsset> SourceChanged;

        /// <summary>
        /// Puts a copy of <paramref name="saved"/> on the Bench in place of its engine, without asking about changes not saved:
        /// for a caller that has just saved them in <paramref name="saved"/> itself.
        /// </summary>
        internal void PutOnBench(EngineAsset saved) => putOnBench(saved);

        internal void SetEngine(EngineAsset engine, EngineAsset source)
        {
            bool engineChanged = engine != Engine, sourceChanged = source != Source;
            Engine = engine;
            Source = source;
            if (engineChanged) EngineChanged?.Invoke(engine);
            if (sourceChanged) SourceChanged?.Invoke(source);
        }
    }

    /// <summary>The <see cref="BenchExtension"/> types the Bench finds.</summary>
    public static class BenchExtensions
    {
        /// <summary>Public, non-abstract types with a public parameterless constructor, by full name.</summary>
        public static IReadOnlyList<Type> Types => TypeCache.GetTypesDerivedFrom<BenchExtension>()
            .Where(t => (t.IsPublic || t.IsNestedPublic) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        /// <summary>One instance of each type, by <see cref="BenchExtension.Order"/>, then type name.</summary>
        public static List<BenchExtension> CreateAll() => Types
            .Select(t => (BenchExtension)Activator.CreateInstance(t))
            .OrderBy(e => e.Order)
            .ThenBy(e => e.GetType().FullName, StringComparer.Ordinal)
            .ToList();
    }
}
