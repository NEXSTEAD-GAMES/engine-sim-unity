// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;
using UnityEngine;

namespace Nexstead.EngineBench
{
    /// <summary>A value that replaces the script's while <see cref="overridden"/> is set.</summary>
    [Serializable]
    public struct TunedValue
    {
        public bool overridden;
        public double value;

        public double Or(double script) => overridden ? value : script;
    }

    /// <summary>
    /// Sound tuning on top of an engine's script (its <see cref="EngineDescription"/>): every value set here replaces the
    /// script's, and every value not set keeps it, so a re-import of the script keeps the tuning. The <see cref="mixer"/>
    /// applies while the engine runs; the <see cref="build"/> values when it is built (the Bench's Restart). Values are in the
    /// description's units (SI).
    /// </summary>
    [Serializable]
    public sealed class EngineSoundTuning
    {
        /// <summary>The synthesizer's mix (<see cref="EngineSoundMix"/>).</summary>
        [Serializable]
        public sealed class Mixer
        {
            public TunedValue convolution;
            public TunedValue highFrequencyGain;
            public TunedValue lowFrequencyNoise;
            public TunedValue lowFrequencyNoiseCutoff;
            public TunedValue highFrequencyNoise;
        }

        /// <summary>
        /// The engine as built. Exhaust systems and cylinder heads are by index; an entry the arrays don't reach, or a null one,
        /// is not tuned.
        /// </summary>
        [Serializable]
        public sealed class Build
        {
            public TunedValue simulationFrequency;
            public ExhaustSystem[] exhaustSystems = Array.Empty<ExhaustSystem>();
            public CylinderHead[] cylinderHeads = Array.Empty<CylinderHead>();
        }

        [Serializable]
        public sealed class ExhaustSystem
        {
            [Tooltip("Replaces the script's impulse response; none keeps the script's.")]
            public ImpulseResponseAsset impulseResponse;
            public TunedValue impulseResponseVolume;
            public TunedValue audioVolume;
            public TunedValue length;
            public TunedValue collectorCrossSectionArea;
            public TunedValue outletFlowRate;
            public TunedValue primaryTubeLength;
            public TunedValue primaryFlowRate;
            public TunedValue velocityDecay;

            internal bool TunesImpulseResponse => impulseResponse != null || impulseResponseVolume.overridden;

            internal bool Any => TunesImpulseResponse || audioVolume.overridden || length.overridden
                                 || collectorCrossSectionArea.overridden || outletFlowRate.overridden || primaryTubeLength.overridden
                                 || primaryFlowRate.overridden || velocityDecay.overridden;
        }

        [Serializable]
        public sealed class CylinderHead
        {
            public Cylinder[] cylinders = Array.Empty<Cylinder>();
        }

        [Serializable]
        public sealed class Cylinder
        {
            public TunedValue headerPrimaryLength;
            public TunedValue soundAttenuation;

            internal bool Any => headerPrimaryLength.overridden || soundAttenuation.overridden;
        }

        public Mixer mixer = new Mixer();
        public Build build = new Build();

        /// <summary>The mix: the script's, as upstream's application loads it, with the tuned values in place.</summary>
        public EngineSoundMix Mix(EngineParametersDescription script)
        {
            EngineSoundMix loaded = EngineSoundMix.AsLoaded(script.highFrequencyGain, script.noise, script.jitter);
            return new EngineSoundMix
            {
                convolution = (float)mixer.convolution.Or(loaded.convolution),
                highFrequencyGain = (float)mixer.highFrequencyGain.Or(loaded.highFrequencyGain),
                lowFrequencyNoise = (float)mixer.lowFrequencyNoise.Or(loaded.lowFrequencyNoise),
                lowFrequencyNoiseCutoff = (float)mixer.lowFrequencyNoiseCutoff.Or(loaded.lowFrequencyNoiseCutoff),
                highFrequencyNoise = (float)mixer.highFrequencyNoise.Or(loaded.highFrequencyNoise),
            };
        }

        /// <summary>
        /// The description to build: the script's with the tuned values in place. A copy, sharing what the tuning doesn't
        /// change; the script's itself when nothing is tuned. An exhaust system with a tuned impulse response or volume gets an
        /// impulse response entry of its own, appended to the list, so no other exhaust system shares the change.
        /// </summary>
        public EngineDescription Apply(EngineDescription script)
        {
            Check(script);
            if (!TunesBuild()) return script;

            EngineDescription tuned = script.ShallowCopy();
            EngineParametersDescription engine = tuned.engine = script.engine.ShallowCopy();
            engine.simulationFrequency = build.simulationFrequency.Or(script.engine.simulationFrequency);

            var responses = new List<ImpulseResponseDescription>(script.impulseResponses);
            engine.exhaustSystems = new ExhaustSystemDescription[script.engine.exhaustSystems.Length];
            for (int i = 0; i < engine.exhaustSystems.Length; ++i)
            {
                ExhaustSystemDescription from = script.engine.exhaustSystems[i];
                ExhaustSystem x = i < build.exhaustSystems.Length ? build.exhaustSystems[i] : null;
                if (x == null || !x.Any)
                {
                    engine.exhaustSystems[i] = from;
                    continue;
                }

                ExhaustSystemDescription to = from.ShallowCopy();
                to.audioVolume = x.audioVolume.Or(from.audioVolume);
                to.length = x.length.Or(from.length);
                to.collectorCrossSectionArea = x.collectorCrossSectionArea.Or(from.collectorCrossSectionArea);
                to.outletFlowRate = x.outletFlowRate.Or(from.outletFlowRate);
                to.primaryTubeLength = x.primaryTubeLength.Or(from.primaryTubeLength);
                to.primaryFlowRate = x.primaryFlowRate.Or(from.primaryFlowRate);
                to.velocityDecay = x.velocityDecay.Or(from.velocityDecay);
                if (x.TunesImpulseResponse)
                {
                    ImpulseResponseDescription scripted = script.impulseResponses[from.impulseResponse];
                    responses.Add(new ImpulseResponseDescription
                    {
                        file = x.impulseResponse != null ? x.impulseResponse.SourceFile : scripted.file,
                        volume = x.impulseResponseVolume.Or(scripted.volume),
                    });
                    to.impulseResponse = responses.Count - 1;
                }

                engine.exhaustSystems[i] = to;
            }

            tuned.impulseResponses = responses.ToArray();

            engine.cylinderHeads = new CylinderHeadDescription[script.engine.cylinderHeads.Length];
            for (int h = 0; h < engine.cylinderHeads.Length; ++h)
            {
                CylinderHeadDescription from = script.engine.cylinderHeads[h];
                CylinderHead head = h < build.cylinderHeads.Length ? build.cylinderHeads[h] : null;
                if (head == null || !Array.Exists(head.cylinders, c => c != null && c.Any))
                {
                    engine.cylinderHeads[h] = from;
                    continue;
                }

                CylinderHeadDescription to = from.ShallowCopy();
                to.cylinders = new HeadCylinderDescription[from.cylinders.Length];
                for (int c = 0; c < to.cylinders.Length; ++c)
                {
                    HeadCylinderDescription scripted = from.cylinders[c];
                    Cylinder tunedCylinder = c < head.cylinders.Length ? head.cylinders[c] : null;
                    if (tunedCylinder == null || !tunedCylinder.Any)
                    {
                        to.cylinders[c] = scripted;
                        continue;
                    }

                    HeadCylinderDescription cylinder = scripted.ShallowCopy();
                    cylinder.headerPrimaryLength = tunedCylinder.headerPrimaryLength.Or(scripted.headerPrimaryLength);
                    cylinder.soundAttenuation = tunedCylinder.soundAttenuation.Or(scripted.soundAttenuation);
                    to.cylinders[c] = cylinder;
                }

                engine.cylinderHeads[h] = to;
            }

            return tuned;
        }

        /// <summary>Whether any value that applies when the engine is built is tuned.</summary>
        public bool TunesBuild()
        {
            if (build.simulationFrequency.overridden) return true;
            foreach (ExhaustSystem x in build.exhaustSystems)
            {
                if (x != null && x.Any) return true;
            }

            foreach (CylinderHead head in build.cylinderHeads)
            {
                if (head != null && Array.Exists(head.cylinders, c => c != null && c.Any)) return true;
            }

            return false;
        }

        /// <summary>The impulse responses the tuning picks, for their samples.</summary>
        public IEnumerable<ImpulseResponseAsset> ImpulseResponses()
        {
            foreach (ExhaustSystem x in build.exhaustSystems)
            {
                if (x != null && x.impulseResponse != null) yield return x.impulseResponse;
            }
        }

        /// <summary>Exhaust system <paramref name="index"/>'s entry, the array grown to reach it.</summary>
        public ExhaustSystem Exhaust(int index)
        {
            if (build.exhaustSystems.Length <= index) Array.Resize(ref build.exhaustSystems, index + 1);
            return build.exhaustSystems[index] ??= new ExhaustSystem();
        }

        /// <summary>Cylinder <paramref name="cylinder"/> of head <paramref name="head"/>'s entry, the arrays grown to reach it.</summary>
        public Cylinder HeadCylinder(int head, int cylinder)
        {
            if (build.cylinderHeads.Length <= head) Array.Resize(ref build.cylinderHeads, head + 1);
            CylinderHead h = build.cylinderHeads[head] ??= new CylinderHead();
            if (h.cylinders.Length <= cylinder) Array.Resize(ref h.cylinders, cylinder + 1);
            return h.cylinders[cylinder] ??= new Cylinder();
        }

        /// <summary>Tunes nothing: every value back to the script's.</summary>
        public void Clear()
        {
            mixer = new Mixer();
            build = new Build();
        }

        /// <summary>Drops entries the description has no exhaust system, head or cylinder for (after a re-import changed it).</summary>
        public void Fit(EngineDescription script)
        {
            EngineParametersDescription e = script.engine;
            if (build.exhaustSystems.Length > e.exhaustSystems.Length) Array.Resize(ref build.exhaustSystems, e.exhaustSystems.Length);
            if (build.cylinderHeads.Length > e.cylinderHeads.Length) Array.Resize(ref build.cylinderHeads, e.cylinderHeads.Length);
            for (int h = 0; h < build.cylinderHeads.Length; ++h)
            {
                CylinderHead head = build.cylinderHeads[h];
                int count = e.cylinderHeads[h].cylinders.Length;
                if (head != null && head.cylinders.Length > count) Array.Resize(ref head.cylinders, count);
            }
        }

        void Check(EngineDescription script)
        {
            EngineParametersDescription e = script.engine;
            if (build.exhaustSystems.Length > e.exhaustSystems.Length || build.cylinderHeads.Length > e.cylinderHeads.Length)
            {
                throw new InvalidOperationException("The sound tuning names more exhaust systems or heads than the script has.");
            }

            for (int h = 0; h < build.cylinderHeads.Length; ++h)
            {
                CylinderHead head = build.cylinderHeads[h];
                if (head != null && head.cylinders.Length > e.cylinderHeads[h].cylinders.Length)
                {
                    throw new InvalidOperationException($"The sound tuning names more cylinders in head {h + 1} than the script has.");
                }
            }
        }
    }
}
