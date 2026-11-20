// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// An engine captured for rendering: its tuned description, impulse responses and mix, and its figures, as plain data
    /// that no longer touches the <see cref="EngineAsset"/>. Capture it on the main thread; it is immutable, so any number of
    /// <see cref="EngineRenderer"/>s may be built from it on any thread. Part of the render API (stable within a major
    /// version).
    /// </summary>
    public sealed class EngineRenderSource
    {
        readonly EngineDescription description;
        readonly Dictionary<string, short[]> impulseResponses;

        EngineRenderSource(EngineAsset engine)
        {
            Name = engine.name;
            // As tuned (EngineAsset.TunedDescription), read here: renderers build their engines from this data only.
            description = engine.TunedDescription();
            Mix = engine.TunedMix();
            impulseResponses = new Dictionary<string, short[]>(StringComparer.Ordinal);
            foreach (ImpulseResponseDescription response in description.impulseResponses)
            {
                if (!impulseResponses.ContainsKey(response.file)) impulseResponses.Add(response.file, engine.ImpulseResponseSamples(response.file));
            }

            EngineParametersDescription e = description.engine;
            RedlineRpm = Units.ToRpm(e.redline);
            RevLimitRpm = Units.ToRpm(e.ignitionModule.revLimit);
            StarterRpm = Math.Abs(Units.ToRpm(e.starterSpeed));
            foreach (CylinderBankDescription bank in e.cylinderBanks) Cylinders += bank.cylinderCount;
            ForcedInductionKind = e.HasForcedInduction
                ? ForcedInductionSound.KindOf(EngineBuilder.ForcedInductionParameters(e.forcedInduction).type)
                : ForcedInductionKind.None;
        }

        // The same engine, with another description: its forced induction removed.
        EngineRenderSource(EngineRenderSource from, EngineDescription description)
        {
            this.description = description;
            impulseResponses = from.impulseResponses;
            Name = from.Name;
            Mix = from.Mix;
            RedlineRpm = from.RedlineRpm;
            RevLimitRpm = from.RevLimitRpm;
            StarterRpm = from.StarterRpm;
            Cylinders = from.Cylinders;
            ForcedInductionKind = ForcedInductionKind.None;
        }

        /// <summary>Captures <paramref name="engine"/> as tuned now. Main thread only: it reads the asset.</summary>
        public static EngineRenderSource Capture(EngineAsset engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            return new EngineRenderSource(engine);
        }

        /// <summary>
        /// The same engine with its turbocharger or supercharger taken off: its intake breathes the atmosphere, and everything
        /// else is as captured (for an exporter's bank of the engine without it). This source itself when the engine has none.
        /// </summary>
        public EngineRenderSource WithoutForcedInduction()
        {
            if (ForcedInductionKind == ForcedInductionKind.None) return this;
            EngineDescription without = description.ShallowCopy();
            without.engine = description.engine.ShallowCopy();
            without.engine.forcedInduction = null;
            return new EngineRenderSource(this, without);
        }

        /// <summary>The engine asset's name.</summary>
        public string Name { get; }
        /// <summary>The redline, in rpm.</summary>
        public double RedlineRpm { get; }
        /// <summary>The revs where the ignition module's rev limiter cuts, in rpm.</summary>
        public double RevLimitRpm { get; }
        /// <summary>How fast the starter turns the crank, in rpm (unsigned): an engine idling slower than this hasn't started.</summary>
        public double StarterRpm { get; }
        public int Cylinders { get; }
        /// <summary>The synthesizer's mix, as tuned.</summary>
        public EngineSoundMix Mix { get; }
        /// <summary>The engine's turbocharger or supercharger, by how it sounds; <see cref="ForcedInductionKind.None"/> without one.</summary>
        public ForcedInductionKind ForcedInductionKind { get; }

        /// <summary>A fresh engine, vehicle and transmission (each renderer needs its own).</summary>
        internal EngineAssembly Build() => EngineBuilder.Build(description);

        internal short[] ImpulseResponse(string file) => impulseResponses[file];
    }
}
