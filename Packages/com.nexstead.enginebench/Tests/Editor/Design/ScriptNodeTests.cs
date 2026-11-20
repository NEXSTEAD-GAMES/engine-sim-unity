// Checks that the script nodes build what engine-sim builds.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using Nexstead.EngineBench.Simulation;
using NUnit.Framework;

namespace Nexstead.EngineBench.Tests
{
    public sealed class ScriptNodeTests
    {
        /// <summary>
        /// Engine scripts transcribed with the script nodes and the part library build their presets' descriptions exactly:
        /// every value bit for bit, parts and shared functions in engine-sim's order.
        /// </summary>
        [TestCase("boxer-4-rally-wagon")]
        [TestCase("v8-big-block-truck")]
        public void TranscribedScriptsBuildTheirPresets(string preset)
        {
            EngineDescription expected = TestEngines.Preset(preset).Description;
            Func<string, EngineDescription> transcription = preset switch
            {
                "boxer-4-rally-wagon" => ScriptTranscriptions.Flat4,
                "v8-big-block-truck" => ScriptTranscriptions.BigBlockTruckV8,
                _ => throw new ArgumentException(preset),
            };

            EngineDescription built = transcription(expected.engine.name);
            var differences = new List<string>();
            StructuralComparer.Collect(built, expected, "description", differences);
            Assert.IsEmpty(differences, string.Join("\n", differences.GetRange(0, Math.Min(20, differences.Count))));
        }
    }
}
