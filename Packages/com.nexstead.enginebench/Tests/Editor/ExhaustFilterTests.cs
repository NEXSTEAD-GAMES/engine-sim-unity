// Checks of the package's exhaust filters: the presets, the ladder's tone and their levels.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System.Collections.Generic;
using System.Linq;
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Nexstead.EngineBench.Tests
{
    public sealed class ExhaustFilterTests
    {
        static ImpulseResponseAsset AssetOf(ExhaustFilter filter) => TestEngines.ImpulseResponse(filter.AssetName);

        /// <summary>
        /// The package's impulse response presets are the exhaust filters, each under its name holding the file engine
        /// descriptions name it by; the ladder runs 1 to 7, the variants are off it.
        /// </summary>
        [Test]
        public void EveryFilterIsAPreset()
        {
            IEnumerable<string> presets = AssetDatabase.FindAssets($"t:{nameof(ImpulseResponseAsset)}", new[] { EnginePresets.ImpulseResponsesFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<ImpulseResponseAsset>(AssetDatabase.GUIDToAssetPath(guid)).name);
            CollectionAssert.AreEquivalent(ExhaustFilters.All.Select(f => f.AssetName), presets);
            foreach (ExhaustFilter filter in ExhaustFilters.All)
            {
                Assert.AreEqual(filter.response.file, AssetOf(filter).SourceFile, filter.Label);
                Assert.AreSame(filter, ExhaustFilters.Of(filter.response.file), filter.Label);
            }

            CollectionAssert.AreEqual(Enumerable.Range(1, 7), ExhaustFilters.Ladder.Select(f => f.step));
            CollectionAssert.AreEqual(new[] { 0, 0 }, ExhaustFilters.Variants.Select(f => f.step));
        }

        /// <summary>
        /// Along the ladder each filter lets less of the top end through than the one before (a smaller share of its energy at
        /// 2 kHz and up, <see cref="ExhaustFilterMeasure"/>), and at their volumes all of them play within 1 dB of Stock.
        /// </summary>
        [Test]
        public void LadderFallsInTopEndAtOneLevel()
        {
            double previous = 1;
            var tops = new List<string>();
            foreach (ExhaustFilter filter in ExhaustFilters.Ladder)
            {
                ImpulseResponseAsset asset = AssetOf(filter);
                double top = ExhaustFilterMeasure.Top(ExhaustFilterMeasure.BandShares(asset.Samples, asset.SampleRate));
                tops.Add($"{filter.name} {top:P1}");
                Assert.Less(top, previous, string.Join(", ", tops));
                previous = top;
            }

            double stock = ExhaustFilterMeasure.LevelDb(AssetOf(ExhaustFilters.Stock).Samples, ExhaustFilters.Stock.response.volume);
            foreach (ExhaustFilter filter in ExhaustFilters.All)
            {
                Assert.AreEqual(stock, ExhaustFilterMeasure.LevelDb(AssetOf(filter).Samples, filter.response.volume), 1.0, filter.Label);
            }
        }
    }
}
