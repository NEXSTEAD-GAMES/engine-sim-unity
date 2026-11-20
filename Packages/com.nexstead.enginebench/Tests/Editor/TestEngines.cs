// Shared helpers of the package's tests: preset engines and impulse responses, and field-by-field comparison.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Nexstead.EngineBench.Editor;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public static class TestEngines
    {
        /// <summary>The small-block V8 preset: the engine most checks run on.</summary>
        public const string V8 = "v8-pushrod-muscle";

        public static EngineAsset Preset(string name)
        {
            EngineAsset engine = EnginePresets.LoadEngine(name);
            Assert.IsNotNull(engine, $"{EnginePresets.EnginePath(name)} missing.");
            return engine;
        }

        public static ImpulseResponseAsset ImpulseResponse(string name)
        {
            ImpulseResponseAsset response = EnginePresets.LoadImpulseResponse(name);
            Assert.IsNotNull(response, $"{EnginePresets.ImpulseResponsePath(name)} missing.");
            return response;
        }

        /// <summary>An in-memory copy of an engine asset with no sound tuning: what its script plays, however the asset is tuned.</summary>
        public static EngineAsset UntunedCopy(EngineAsset asset)
        {
            EngineAsset copy = UnityEngine.Object.Instantiate(asset);
            copy.name = asset.name;
            copy.Tuning.Clear();
            return copy;
        }

        /// <summary>A generator of the engine as its preset defines it, at upstream's volume.</summary>
        public static EngineSoundGenerator Generator(EngineAsset engine, ulong seed,
            EngineSoundGenerator.Core core = EngineSoundGenerator.Core.Dense, EngineSoundMix? mix = null) =>
            new EngineSoundGenerator(engine.Build(), engine.ImpulseResponseSamples, seed, core: core, mix: mix);
    }

    /// <summary>
    /// Field-by-field comparison of serializable data: doubles and floats bit for bit. Unity serialization stores a null
    /// string, array or nested object as an empty one, so those count as equal.
    /// </summary>
    public static class StructuralComparer
    {
        public static void Collect(object actual, object expected, string path, List<string> differences)
        {
            Type type = (actual ?? expected)?.GetType();
            if (type == null) return;

            if (type == typeof(double))
            {
                if (BitConverter.DoubleToInt64Bits((double)actual) != BitConverter.DoubleToInt64Bits((double)expected))
                {
                    differences.Add($"{path}: {(double)actual:R} vs {(double)expected:R}");
                }
                return;
            }
            if (type == typeof(float))
            {
                if (BitConverter.SingleToInt32Bits((float)actual) != BitConverter.SingleToInt32Bits((float)expected))
                {
                    differences.Add($"{path}: {(float)actual:R} vs {(float)expected:R}");
                }
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
                for (int i = 0; i < a.Count; ++i) Collect(a[i], e[i], $"{path}[{i}]", differences);
                return;
            }

            object actualObject = actual ?? Activator.CreateInstance(type);
            object expectedObject = expected ?? Activator.CreateInstance(type);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                Collect(field.GetValue(actualObject), field.GetValue(expectedObject), $"{path}.{field.Name}", differences);
            }
        }
    }
}
