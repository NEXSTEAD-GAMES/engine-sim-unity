// Checks that the designs read from the presets build the engines the presets are.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Editor;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Nexstead.EngineBench.Tests
{
    public sealed class DesignGeneratorTests
    {
        /// <summary>
        /// Every preset has a design, and the engine it builds is the preset's: the same parts and values,
        /// and the same timing as it acts (when each cylinder reaches top dead centre, its spark and its cam lobes from
        /// there), however the preset's script labels the cycle or numbers its parts. The designs between them use every
        /// layout option: mirrored banks, split crank pins, odd firing, a head a bank and variable valve lift.
        /// </summary>
        [Test]
        public void EveryPresetBuildsFromItsDesign()
        {
            var failures = new List<string>();
            var used = new HashSet<string>();
            foreach (string name in EnginePresets.EngineNames())
            {
                EngineDescription preset = TestEngines.Preset(name).Description;
                EngineDesign design = DesignExtraction.FromDescription(preset, out string reason);
                if (design == null)
                {
                    failures.Add($"{name}: no design ({reason})");
                    continue;
                }

                if (design.banksMirrored) used.Add("mirrored banks");
                if (design.splitPins) used.Add("split crank pins");
                if (design.firingAngles.Length > 0) used.Add("odd firing");
                if (design.heads.Length > 1) used.Add("a head a bank");
                if (design.camshaft.variableLift.enabled) used.Add("variable valve lift");
                List<string> differences = DescriptionEquivalence.Differences(EngineGenerator.Describe(design), preset);
                if (differences.Count > 0) failures.Add($"{name}:\n  " + string.Join("\n  ", differences.GetRange(0, Math.Min(8, differences.Count))));
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
            CollectionAssert.IsSubsetOf(new[] { "mirrored banks", "split crank pins", "odd firing", "a head a bank", "variable valve lift" }, used);
        }

        /// <summary>
        /// An engine asset given a design holds the description built from it, with the package's impulse responses; given
        /// an imported description, the design read from it, or none for one no design can hold (two crankshafts).
        /// </summary>
        [Test]
        public void AssetBuildsItsDescriptionFromItsDesign()
        {
            var asset = ScriptableObject.CreateInstance<EngineAsset>();
            try
            {
                EngineDesign design = DesignExtraction.FromDescription(TestEngines.Preset("boxer-4-rally-wagon").Description, out _);
                asset.SetDesign(design, EnginePresets.ImpulseResponseFromFile);
                Assert.AreSame(design, asset.Design);
                var differences = new List<string>();
                StructuralComparer.Collect(asset.Description, EngineGenerator.Describe(design), "description", differences);
                Assert.IsEmpty(differences, string.Join("\n", differences));
                for (int i = 0; i < asset.ImpulseResponses.Length; ++i)
                {
                    Assert.AreEqual(asset.Description.impulseResponses[i].file, asset.ImpulseResponses[i].SourceFile);
                }

                EngineAsset v8 = TestEngines.Preset(TestEngines.V8);
                EngineDescription twoCrankshafts = v8.Description.ShallowCopy();
                twoCrankshafts.engine = v8.Description.engine.ShallowCopy();
                twoCrankshafts.engine.crankshafts = new[] { v8.Description.engine.crankshafts[0], v8.Description.engine.crankshafts[0] };
                asset.Set(twoCrankshafts, v8.ImpulseResponses);
                Assert.IsNull(asset.Design);

                asset.Set(v8.Description, v8.ImpulseResponses);
                Assert.IsNotNull(asset.Design);
                Assert.AreSame(v8.Description, asset.Description, "reading the design changed the description");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }

    /// <summary>
    /// Whether two engine descriptions build engines that run alike. Scripts label the cycle as they like (where top dead
    /// centre is, which crank pin is first, which angle form they write) and share functions or write two alike; so the
    /// timing is compared as it acts (each cylinder's top dead centre after cylinder 1's, its spark and lobes from its own
    /// top dead centre), journals by first use, functions by what they hold, and every other value within 1e-9 relative.
    /// </summary>
    static class DescriptionEquivalence
    {
        const double Deg = Math.PI / 180;
        const double Relative = 1e-9;
        /// <summary>How close two angles (°) must be: scripts write them in forms a few rounding steps apart.</summary>
        const double SameAngle = 1e-6;
        static readonly string[] FunctionReferences =
        {
            ".intakePortFlow", ".exhaustPortFlow", ".lobeProfile", ".timingCurve", ".turbulenceToFlameSpeedRatio", ".meanPistonSpeedToTurbulence",
        };

        public static List<string> Differences(EngineDescription built, EngineDescription expected)
        {
            EngineDescription a = Copy(built), b = Copy(expected);
            Renumber(a);
            Renumber(b);
            var differences = new List<string>();
            Timing(a, b, differences);
            Unlabel(a);
            Unlabel(b);
            a.source = b.source;
            Collect(a, b, "", a.functions, b.functions, differences);
            return differences;
        }

        static double Mod(double value, double period)
        {
            double r = value % period;
            return r < 0 ? r + period : r;
        }

        /// <summary>Each cylinder's compression top dead centre in the description's cycle (°): of the two, the one nearest its spark.</summary>
        static double[] TopDeadCentres(EngineDescription d)
        {
            EngineParametersDescription e = d.engine;
            CrankshaftDescription crank = e.crankshafts[0];
            var tdc = new double[e.pistons.Length];
            for (int b = 0, c = 0; b < e.cylinderBanks.Length; ++b)
            {
                for (int k = 0; k < e.cylinderBanks[b].cylinderCount; ++k, ++c)
                {
                    double top = Mod((crank.tdc + crank.rodJournalAngles[e.connectingRods[c].journal] - e.cylinderBanks[b].angle) / Deg - 90, 360);
                    double spark = Mod(e.ignitionModule.plugs[c].angle / Deg, 720);
                    tdc[c] = Distance(top, spark, 720) <= Distance(top + 360, spark, 720) ? top : top + 360;
                }
            }

            return tdc;
        }

        static double Distance(double a, double b, double period) => Math.Abs(Math.IEEERemainder(a - b, period));

        static void Same(string what, double a, double b, double period, List<string> differences)
        {
            if (Distance(a, b, period) > SameAngle) differences.Add($"{what}: {a:R} vs {b:R}");
        }

        static void Timing(EngineDescription a, EngineDescription b, List<string> differences)
        {
            double[] ta = TopDeadCentres(a), tb = TopDeadCentres(b);
            int n = ta.Length;
            for (int c = 0; c < n; ++c) Same($"cylinder {c + 1} top dead centre after cylinder 1", ta[c] - ta[0], tb[c] - tb[0], 720, differences);

            FunctionDescription fa = a.functions[a.engine.ignitionModule.timingCurve], fb = b.functions[b.engine.ignitionModule.timingCurve];
            if (fa.x.Length != fb.x.Length)
            {
                differences.Add($"timing curve: {fa.x.Length} points vs {fb.x.Length}");
                return;
            }

            for (int c = 0; c < n; ++c)
            {
                double leadA = ta[c] - a.engine.ignitionModule.plugs[c].angle / Deg, leadB = tb[c] - b.engine.ignitionModule.plugs[c].angle / Deg;
                for (int i = 0; i < fa.x.Length; ++i)
                {
                    Same($"cylinder {c + 1} spark before top dead centre at timing point {i + 1}", leadA + fa.y[i] / Deg, leadB + fb.y[i] / Deg, 720,
                        differences);
                }
            }

            for (int h = 0, c = 0; h < a.engine.cylinderHeads.Length; ++h)
            {
                ValvetrainDescription va = a.engine.cylinderHeads[h].valvetrain, vb = b.engine.cylinderHeads[h].valvetrain;
                (CamshaftDescription, CamshaftDescription, string)[] cams =
                {
                    (va.intakeCamshaft, vb.intakeCamshaft, "intake"), (va.exhaustCamshaft, vb.exhaustCamshaft, "exhaust"),
                    (va.highLiftIntakeCamshaft, vb.highLiftIntakeCamshaft, "high-lift intake"),
                    (va.highLiftExhaustCamshaft, vb.highLiftExhaustCamshaft, "high-lift exhaust"),
                };
                int count = a.engine.cylinderHeads[h].cylinders.Length;
                foreach ((CamshaftDescription ca, CamshaftDescription cb, string label) in cams)
                {
                    if (ca?.lobeCenterlines == null || ca.lobeCenterlines.Length == 0 || cb?.lobeCenterlines == null) continue;
                    for (int k = 0; k < count; ++k)
                    {
                        Same($"head {h + 1} {label} lobe {k + 1} from top dead centre", ca.lobeCenterlines[k] / Deg - ta[c + k],
                            cb.lobeCenterlines[k] / Deg - tb[c + k], 720, differences);
                    }
                }

                c += count;
            }
        }

        /// <summary>Clears the values that only label the cycle: compared as timing already.</summary>
        static void Unlabel(EngineDescription d)
        {
            EngineParametersDescription e = d.engine;
            foreach (CrankshaftDescription crank in e.crankshafts)
            {
                crank.tdc = 0;
                crank.rodJournalAngles = new double[crank.rodJournalAngles.Length];
            }

            foreach (SparkPlugDescription plug in e.ignitionModule.plugs) plug.angle = 0;
            foreach (CylinderHeadDescription head in e.cylinderHeads)
            {
                ValvetrainDescription v = head.valvetrain;
                foreach (CamshaftDescription cam in new[] { v.intakeCamshaft, v.exhaustCamshaft, v.highLiftIntakeCamshaft, v.highLiftExhaustCamshaft })
                {
                    if (cam?.lobeCenterlines != null) cam.lobeCenterlines = new double[cam.lobeCenterlines.Length];
                }
            }

            FunctionDescription timing = d.functions[e.ignitionModule.timingCurve];
            timing.y = new double[timing.y.Length];
        }

        /// <summary>Numbers each crankshaft's journals in the order the rods first use them.</summary>
        static void Renumber(EngineDescription d)
        {
            EngineParametersDescription e = d.engine;
            for (int c = 0; c < e.crankshafts.Length; ++c)
            {
                var renumbered = new Dictionary<int, int>();
                var angles = new List<double>();
                foreach (ConnectingRodDescription rod in e.connectingRods)
                {
                    if (rod.crankshaft != c || renumbered.ContainsKey(rod.journal)) continue;
                    renumbered[rod.journal] = angles.Count;
                    angles.Add(e.crankshafts[c].rodJournalAngles[rod.journal]);
                }

                foreach (ConnectingRodDescription rod in e.connectingRods)
                {
                    if (rod.crankshaft == c) rod.journal = renumbered[rod.journal];
                }

                e.crankshafts[c].rodJournalAngles = angles.ToArray();
            }
        }

        static void Collect(object actual, object expected, string path, FunctionDescription[] actualFunctions, FunctionDescription[] expectedFunctions,
            List<string> differences)
        {
            Type type = (actual ?? expected)?.GetType();
            if (type == null || path == ".functions") return;

            if (type == typeof(int) && Array.Exists(FunctionReferences, r => path.EndsWith(r, StringComparison.Ordinal)))
            {
                Collect(actualFunctions[(int)actual], expectedFunctions[(int)expected], path + "→", actualFunctions, expectedFunctions, differences);
                return;
            }

            if (type == typeof(double))
            {
                double a = (double)actual, b = (double)expected;
                if (Math.Abs(a - b) > Relative * Math.Max(1, Math.Abs(b))) differences.Add($"{path}: {a:R} vs {b:R}");
                return;
            }

            if (type == typeof(string))
            {
                if (((string)actual ?? "") != ((string)expected ?? "")) differences.Add($"{path}: '{actual}' vs '{expected}'");
                return;
            }

            if (type.IsPrimitive || type.IsEnum)
            {
                if (!Equals(actual, expected)) differences.Add($"{path}: {actual} vs {expected}");
                return;
            }

            if (type.IsArray)
            {
                var a = (IList)actual ?? Array.Empty<object>();
                var e = (IList)expected ?? Array.Empty<object>();
                if (a.Count != e.Count)
                {
                    differences.Add($"{path}: {a.Count} entries vs {e.Count}");
                    return;
                }

                for (int i = 0; i < a.Count; ++i) Collect(a[i], e[i], $"{path}[{i}]", actualFunctions, expectedFunctions, differences);
                return;
            }

            object actualObject = actual ?? Activator.CreateInstance(type), expectedObject = expected ?? Activator.CreateInstance(type);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                Collect(field.GetValue(actualObject), field.GetValue(expectedObject), $"{path}.{field.Name}", actualFunctions, expectedFunctions,
                    differences);
            }
        }

        /// <summary>A deep copy of serializable data (public fields, arrays, nested objects): the checks relabel their copies.</summary>
        static T Copy<T>(T value) => (T)CopyObject(value);

        static object CopyObject(object value)
        {
            if (value == null) return null;
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return value;
            if (type.IsArray)
            {
                var source = (Array)value;
                Array copy = Array.CreateInstance(type.GetElementType(), source.Length);
                for (int i = 0; i < source.Length; ++i) copy.SetValue(CopyObject(source.GetValue(i)), i);
                return copy;
            }

            object result = Activator.CreateInstance(type);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public)) field.SetValue(result, CopyObject(field.GetValue(value)));
            return result;
        }
    }
}
