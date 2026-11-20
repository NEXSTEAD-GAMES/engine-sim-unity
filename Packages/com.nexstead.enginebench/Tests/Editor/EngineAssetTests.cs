// Checks of the engine assets: the presets, copies made from them, the sound tuning, and the block generator.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using System.Linq;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Editor;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Nexstead.EngineBench.Tests
{
    public sealed class EngineAssetTests
    {
        /// <summary>
        /// Every preset is read-only, and every impulse response it names, or its sound tuning picks, is a preset with samples;
        /// every exhaust filter (<see cref="ExhaustFilters"/>) is an impulse response preset.
        /// </summary>
        [Test]
        public void PresetsAreComplete()
        {
            string[] names = EnginePresets.EngineNames();
            Assert.IsNotEmpty(names);
            Assert.AreEqual(ExhaustFilters.All.Length,
                AssetDatabase.FindAssets($"t:{nameof(ImpulseResponseAsset)}", new[] { EnginePresets.ImpulseResponsesFolder }).Length);

            foreach (string name in names)
            {
                EngineAsset engine = TestEngines.Preset(name);
                EngineDescription description = engine.Description;
                Assert.IsTrue(EnginePresets.IsPreset(engine), name);
                Assert.IsFalse(EnginePresets.IsEditable(engine), name + " is editable");

                Assert.AreEqual(description.impulseResponses.Length, engine.ImpulseResponses.Length, name);
                for (int i = 0; i < engine.ImpulseResponses.Length; ++i)
                {
                    ImpulseResponseAsset response = engine.ImpulseResponses[i];
                    Assert.IsTrue(EnginePresets.IsPreset(response), $"{name}: impulse response {i} is not a preset");
                    Assert.AreEqual(description.impulseResponses[i].file, response.SourceFile, name);
                    Assert.IsNotNull(ExhaustFilters.Of(response.SourceFile), $"{name}: '{response.SourceFile}' is not one of the exhaust filters");
                    Assert.IsNotEmpty(response.Samples, $"{name}: {response.name} has no samples");
                }

                foreach (ImpulseResponseAsset tuned in engine.Tuning.ImpulseResponses())
                {
                    Assert.IsTrue(EnginePresets.IsPreset(tuned), $"{name}: its tuning picks {tuned}, not a preset");
                    Assert.IsNotNull(ExhaustFilters.Of(tuned.SourceFile), $"{name}: its tuning picks '{tuned.SourceFile}', not one of the exhaust filters");
                }
            }
        }

        /// <summary>
        /// A preset picked in the Bench: an editable in-memory copy, with the preset's description field for field, the same
        /// impulse responses and the design read from it, named after its engine; tuned and saved as a preset in the project,
        /// it keeps its tuning and design, while the preset stays as it was. Saving refuses an existing path and a saved engine.
        /// </summary>
        [Test]
        public void PresetCopyIsEditableAndSaves()
        {
            const string folder = "Assets/NxEngineBenchTestTemp";
            EngineAsset preset = TestEngines.Preset(TestEngines.V8);
            EngineAsset copy = EnginePresets.TemporaryCopy(preset);
            try
            {
                Assert.IsTrue(EnginePresets.IsTemporary(copy));
                Assert.IsTrue(EnginePresets.IsEditable(copy));
                Assert.IsFalse(EnginePresets.IsEditable(preset));
                Assert.AreEqual(preset.Description.engine.name, copy.name);

                var differences = new List<string>();
                StructuralComparer.Collect(copy.Description, preset.Description, "V8", differences);
                Assert.IsEmpty(differences, string.Join("\n", differences));
                CollectionAssert.AreEqual(preset.ImpulseResponses, copy.ImpulseResponses);
                Assert.IsNotNull(copy.Design, "no design read");
                Assert.AreEqual(preset.Revision, copy.Revision, "the design changed the description");

                copy.Tuning.mixer.convolution = new TunedValue { overridden = true, value = 0.25 };
                EngineAsset saved = EnginePresets.SaveAs(copy, folder + "/My V8.asset");
                Assert.IsTrue(EnginePresets.IsTemporary(copy), "the copy became the asset");
                Assert.AreEqual("My V8", copy.name);
                Assert.IsTrue(EnginePresets.IsEditable(saved));
                AssetDatabase.ImportAsset(folder + "/My V8.asset", ImportAssetOptions.ForceUpdate);
                saved = AssetDatabase.LoadAssetAtPath<EngineAsset>(folder + "/My V8.asset");
                Assert.AreEqual(0.25f, saved.TunedMix().convolution, "tuning saved");
                Assert.IsNotNull(saved.Design, "design saved");
                Assert.IsFalse(preset.Tuning.mixer.convolution.overridden, "saving the copy tuned the preset");

                EngineAsset another = EnginePresets.TemporaryCopy(preset);
                try
                {
                    Assert.Throws<ArgumentException>(() => EnginePresets.SaveAs(another, folder + "/My V8.asset"), "overwrote a saved engine");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(another);
                }

                Assert.Throws<ArgumentException>(() => EnginePresets.SaveAs(saved, folder + "/Other.asset"), "saved a saved engine again");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// The Bench's copy of a saved engine leaves the engine as it is, however the copy changes, until it is saved over it:
        /// then the engine, read back from its file, has the changes and keeps its name and file. A preset can't be saved over.
        /// </summary>
        [Test]
        public void CopyReachesItsEngineOnlyWhenSaved()
        {
            const string folder = "Assets/NxEngineBenchTestTemp";
            const string path = folder + "/My V8.asset";
            EngineAsset preset = TestEngines.Preset(TestEngines.V8);
            EngineAsset first = EnginePresets.TemporaryCopy(preset);
            EngineAsset copy = null;
            try
            {
                EngineAsset saved = EnginePresets.SaveAs(first, path);
                string guid = AssetDatabase.AssetPathToGUID(path);
                copy = EnginePresets.TemporaryCopy(saved);
                double length = saved.Design.exhausts[0].length;
                copy.Tuning.mixer.convolution = new TunedValue { overridden = true, value = 0.25 };
                copy.Design.exhausts[0].length = length + 1;
                copy.SetDesign(copy.Design, EnginePresets.ImpulseResponseFromFile);

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                saved = AssetDatabase.LoadAssetAtPath<EngineAsset>(path);
                Assert.IsFalse(saved.Tuning.mixer.convolution.overridden, "the copy's tuning reached the engine");
                Assert.AreEqual(length, saved.Design.exhausts[0].length, "the copy's design reached the engine");

                EnginePresets.SaveOver(copy, saved);
                Assert.IsTrue(EnginePresets.IsTemporary(copy));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                saved = AssetDatabase.LoadAssetAtPath<EngineAsset>(path);
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
                Assert.AreEqual("My V8", saved.name);
                Assert.AreEqual(UnityEngine.HideFlags.None, saved.hideFlags);
                Assert.AreEqual(0.25f, saved.TunedMix().convolution, "tuning saved");
                Assert.AreEqual(length + 1, saved.Design.exhausts[0].length, "design saved");

                Assert.Throws<ArgumentException>(() => EnginePresets.SaveOver(copy, preset), "saved over a preset");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>Two generators with the same engine, seed and controls produce the same samples.</summary>
        [Test]
        public void GeneratorIsDeterministic()
        {
            EngineAsset engine = TestEngines.Preset(TestEngines.V8);
            float[] a = Render(engine, EngineSoundGenerator.Core.Dense, null, null, 200);
            float[] b = Render(engine, EngineSoundGenerator.Core.Dense, null, null, 200);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; ++i)
            {
                if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) Assert.Fail($"sample {i} differs");
            }
        }

        /// <summary>
        /// The sound tuning changes what it names and nothing else: untuned, an engine builds and mixes as its script; tuned,
        /// the description it builds differs from the script's in the tuned fields only, an exhaust with another impulse
        /// response gets an entry of its own, the generator runs at the tuned frequency, and the script stays as it was.
        /// </summary>
        [Test]
        public void TuningReplacesOnlyWhatItNames()
        {
            EngineAsset preset = TestEngines.Preset(TestEngines.V8);
            EngineAsset engine = TestEngines.UntunedCopy(preset);
            try
            {
                EngineDescription script = engine.Description;
                EngineSoundMix loaded = EngineSoundMix.AsLoaded(script.engine.highFrequencyGain, script.engine.noise, script.engine.jitter);
                Assert.AreSame(script, engine.TunedDescription(), "untuned: the script's description");
                Assert.AreEqual(loaded, engine.TunedMix(), "untuned: the mix as loaded");

                ImpulseResponseAsset response = TestEngines.ImpulseResponse(ExhaustFilters.Warm.AssetName);

                EngineSoundTuning tuning = engine.Tuning;
                tuning.mixer.convolution = Tuned(0.25);
                tuning.build.simulationFrequency = Tuned(20000);
                tuning.Exhaust(1).impulseResponse = response;
                tuning.Exhaust(1).audioVolume = Tuned(0.5);
                tuning.HeadCylinder(0, 2).headerPrimaryLength = Tuned(0.3);

                EngineDescription tuned = engine.TunedDescription();
                var differences = new List<string>();
                StructuralComparer.Collect(tuned, script, "V8", differences);
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        "V8.impulseResponses", "V8.engine.simulationFrequency", "V8.engine.cylinderHeads[0].cylinders[2].headerPrimaryLength",
                        "V8.engine.exhaustSystems[1].audioVolume", "V8.engine.exhaustSystems[1].impulseResponse",
                    },
                    differences.Select(d => d.Substring(0, d.IndexOf(':'))),
                    string.Join("\n", differences));

                ImpulseResponseDescription added = tuned.impulseResponses[tuned.engine.exhaustSystems[1].impulseResponse];
                Assert.AreEqual(response.SourceFile, added.file);
                Assert.AreEqual(script.impulseResponses[script.engine.exhaustSystems[1].impulseResponse].volume, added.volume, "volume kept");
                Assert.AreSame(response.Samples, engine.ImpulseResponseSamples(response.SourceFile));

                EngineSoundMix mix = engine.TunedMix();
                Assert.AreEqual(0.25f, mix.convolution);
                mix.convolution = loaded.convolution;
                Assert.AreEqual(loaded, mix, "the rest of the mix as loaded");

                using (var generator = new EngineSoundGenerator(engine.Build(), engine.ImpulseResponseSamples, 1, mix: engine.TunedMix()))
                {
                    Assert.AreEqual(20000, generator.SimulationFrequency);
                }

                var unchanged = new List<string>();
                StructuralComparer.Collect(script, preset.Description, "V8", unchanged);
                Assert.IsEmpty(unchanged, "the script changed: " + string.Join("\n", unchanged));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(engine);
            }
        }

        /// <summary>
        /// A mix sent to a generator before its first block (as the live Bench sends the tuning) renders the same samples as
        /// that mix given at its start, on either core; the mix as loaded renders differently.
        /// </summary>
        [TestCase(EngineSoundGenerator.Core.Managed)]
        [TestCase(EngineSoundGenerator.Core.Dense)]
        public void MixSetBetweenBlocksMatchesTheStartingMix(EngineSoundGenerator.Core core)
        {
            EngineAsset engine = TestEngines.Preset(TestEngines.V8);
            EngineDescription script = engine.Description;
            EngineSoundMix mix = EngineSoundMix.AsLoaded(script.engine.highFrequencyGain, script.engine.noise, script.engine.jitter);
            mix.convolution = 0.3f;
            mix.highFrequencyGain = 0.2f;
            mix.lowFrequencyNoise = 0.4f;
            mix.lowFrequencyNoiseCutoff = 5000f;
            mix.highFrequencyNoise = 0.1f;

            float[] started = Render(engine, core, mix, null, 120);
            float[] switched = Render(engine, core, null, mix, 120);
            float[] loaded = Render(engine, core, null, null, 120);

            Assert.AreEqual(started.Length, switched.Length);
            for (int i = 0; i < started.Length; ++i)
            {
                if (BitConverter.SingleToInt32Bits(started[i]) != BitConverter.SingleToInt32Bits(switched[i])) Assert.Fail($"sample {i} differs");
            }

            CollectionAssert.AreNotEqual(started, loaded);
        }

        static TunedValue Tuned(double value) => new TunedValue { overridden = true, value = value };

        static float[] Render(EngineAsset engine, EngineSoundGenerator.Core core, EngineSoundMix? start, EngineSoundMix? set, int blocks)
        {
            using EngineSoundGenerator generator = TestEngines.Generator(engine, 42, core, start);
            if (set.HasValue) generator.SetMix(set.Value);
            var controls = new EngineSoundGenerator.Controls { speedControl = 0.2, ignition = true, starter = true };
            var output = new List<float>();
            var block = new float[generator.MaxBlockSamples];
            for (int b = 0; b < blocks; ++b)
            {
                int count = generator.RenderBlock(controls, block, 0);
                for (int i = 0; i < count; ++i) output.Add(block[i]);
            }

            return output.ToArray();
        }
    }
}
