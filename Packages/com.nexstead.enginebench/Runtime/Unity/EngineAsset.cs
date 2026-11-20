// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Simulation;
using UnityEngine;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// An engine, its vehicle and transmission as an engine description, with the impulse responses it names, and its sound
    /// tuning on top (the Bench's Sound section). The description is either imported (a preset: the C++ reference's export
    /// of an engine-sim script, with the design read from it) or built from the asset's design (<see cref="SetDesign"/>).
    /// Everything that plays or renders it builds it as tuned. The package's
    /// presets are read-only, and may carry sound tuning of their own: the Bench works on a copy, which can be designed, tuned
    /// and saved.
    /// </summary>
    public sealed class EngineAsset : ScriptableObject
    {
        [SerializeField] EngineDescription description = new EngineDescription();
        [Tooltip("One per entry of the description's impulse responses, in the same order.")]
        [SerializeField] ImpulseResponseAsset[] impulseResponses = Array.Empty<ImpulseResponseAsset>();
        [SerializeField] EngineSoundTuning tuning = new EngineSoundTuning();
        // Unity keeps a nested object even when it was null, so whether there is a design is kept apart.
        [SerializeField] bool hasDesign;
        [SerializeField] EngineDesign design = new EngineDesign();
        [SerializeField] int revision;

        /// <summary>The engine's description: imported, or built from its design.</summary>
        public EngineDescription Description => description;

        /// <summary>The design the description is built from; null for an imported engine.</summary>
        public EngineDesign Design => hasDesign ? design : null;

        /// <summary>Counts the descriptions set: a running engine built at another revision plays an older description.</summary>
        public int Revision => revision;
        public ImpulseResponseAsset[] ImpulseResponses => impulseResponses;
        public EngineSoundTuning Tuning => tuning;

        /// <summary>The description as tuned (<see cref="EngineSoundTuning.Apply"/>).</summary>
        public EngineDescription TunedDescription()
        {
            try
            {
                return tuning.Apply(description);
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException($"{name}: {e.Message}", e);
            }
        }

        /// <summary>The synthesizer's mix as tuned.</summary>
        public EngineSoundMix TunedMix() => tuning.Mix(description.engine);

        /// <summary>A fresh engine, vehicle and transmission, as tuned (each simulation needs its own).</summary>
        public EngineAssembly Build() => EngineBuilder.Build(TunedDescription());

        /// <summary>The samples of an impulse response file named in the description or picked by the tuning.</summary>
        public short[] ImpulseResponseSamples(string file)
        {
            ImpulseResponseDescription[] named = description.impulseResponses;
            for (int i = 0; i < named.Length; ++i)
            {
                if (named[i].file != file) continue;

                ImpulseResponseAsset response = i < impulseResponses.Length ? impulseResponses[i] : null;
                if (response == null) throw new InvalidOperationException($"{name}: impulse response {i} ('{file}') is missing.");
                return response.Samples;
            }

            foreach (ImpulseResponseAsset response in tuning.ImpulseResponses())
            {
                if (response.SourceFile == file) return response.Samples;
            }

            throw new ArgumentException($"{name}: neither the description nor the tuning names an impulse response '{file}'.", nameof(file));
        }

        /// <summary>
        /// Sets an imported description and its impulse responses, with the design read from it (<see cref="ReadDesign"/>);
        /// no design when none can hold the engine.
        /// </summary>
        public void Set(EngineDescription description, ImpulseResponseAsset[] impulseResponses)
        {
            Assign(description, impulseResponses);
            hasDesign = false;
            design = new EngineDesign();
            ReadDesign(out _);
        }

        /// <summary>
        /// Gives an imported engine the design read from its description (<see cref="DesignExtraction"/>), keeping the
        /// description itself: the design builds an engine that runs as this one does, and is what the engine is built from
        /// once it is changed. False, with the reason, when no design can hold the engine (<see cref="DesignExtraction"/>).
        /// </summary>
        public bool ReadDesign(out string reason)
        {
            EngineDesign read = DesignExtraction.FromDescription(description, out reason);
            if (read == null) return false;
            design = read;
            hasDesign = true;
            return true;
        }

        /// <summary>
        /// Sets the design and the description built from it (<see cref="EngineGenerator"/>), with each exhaust's impulse
        /// response from <paramref name="impulseResponseFor"/> (by the file the design names it with). A running engine
        /// plays it from its next restart.
        /// </summary>
        /// <exception cref="ArgumentException">The design has problems (<see cref="EngineGenerator.Problems"/>), or an impulse response is missing.</exception>
        public void SetDesign(EngineDesign design, Func<string, ImpulseResponseAsset> impulseResponseFor)
        {
            if (design == null) throw new ArgumentNullException(nameof(design));
            EngineDescription built = EngineGenerator.Describe(design);
            var responses = new ImpulseResponseAsset[built.impulseResponses.Length];
            for (int i = 0; i < responses.Length; ++i)
            {
                string file = built.impulseResponses[i].file;
                responses[i] = impulseResponseFor(file) ?? throw new ArgumentException($"No impulse response for '{file}'.", nameof(impulseResponseFor));
            }

            Assign(built, responses);
            this.design = design;
            hasDesign = true;
        }

        void Assign(EngineDescription description, ImpulseResponseAsset[] impulseResponses)
        {
            if (impulseResponses.Length != description.impulseResponses.Length)
            {
                throw new ArgumentException("One impulse response asset per description entry expected.", nameof(impulseResponses));
            }

            this.description = description;
            this.impulseResponses = impulseResponses;
            tuning.Fit(description);
            ++revision;
        }
    }
}
