// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// The package's presets (engine-sim's library engines and its exhaust impulse responses, as assets: read-only, an engine
    /// with any sound tuning of its own), and the engines the Bench works on: an in-memory copy of whatever engine is picked (<see cref="TemporaryCopy"/>),
    /// which reaches the project only when saved, over the engine it was copied from (<see cref="SaveOver"/>) or as a new
    /// one (<see cref="SaveAs"/>).
    /// </summary>
    public static class EnginePresets
    {
        public const string PackageName = "com.nexstead.enginebench";
        public const string Folder = "Packages/" + PackageName + "/Presets";
        public const string EnginesFolder = Folder + "/Engines";
        public const string ImpulseResponsesFolder = Folder + "/ImpulseResponses";

        /// <summary>Where the Bench suggests putting a new engine.</summary>
        public const string DefaultEngineFolder = "Assets/EngineBench/Engines";

        public static string EnginePath(string name) => $"{EnginesFolder}/{name}.asset";
        public static string ImpulseResponsePath(string name) => $"{ImpulseResponsesFolder}/{name}.asset";

        public static EngineAsset LoadEngine(string name) => AssetDatabase.LoadAssetAtPath<EngineAsset>(EnginePath(name));
        public static ImpulseResponseAsset LoadImpulseResponse(string name) =>
            AssetDatabase.LoadAssetAtPath<ImpulseResponseAsset>(ImpulseResponsePath(name));

        /// <summary>The package's impulse response imported from <paramref name="sourceFile"/> (as engine descriptions name it); null if none.</summary>
        public static ImpulseResponseAsset ImpulseResponseFromFile(string sourceFile)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(ImpulseResponseAsset)}", new[] { ImpulseResponsesFolder }))
            {
                var response = AssetDatabase.LoadAssetAtPath<ImpulseResponseAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (response != null && response.SourceFile == sourceFile) return response;
            }

            return null;
        }

        /// <summary>The preset engines' names, in ordinal order.</summary>
        public static string[] EngineNames() => AssetDatabase.FindAssets($"t:{nameof(EngineAsset)}", new[] { EnginesFolder })
            .Select(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        /// <summary>The engines saved in the project's Assets, by asset path, in ordinal order.</summary>
        public static string[] ProjectEnginePaths() => AssetDatabase.FindAssets($"t:{nameof(EngineAsset)}", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        /// <summary>Whether the asset is one of the package's presets.</summary>
        public static bool IsPreset(UnityEngine.Object asset) =>
            asset != null && AssetDatabase.GetAssetPath(asset).StartsWith(Folder + "/", StringComparison.Ordinal);

        /// <summary>
        /// Whether the engine may be designed, tuned and saved where it is: an in-memory engine, or one in the project's Assets
        /// or in a package that is part of the project (embedded or local); not a preset, nor an engine in a read-only package.
        /// </summary>
        public static bool IsEditable(EngineAsset engine)
        {
            if (engine == null) return false;
            if (!EditorUtility.IsPersistent(engine)) return true;
            if (IsPreset(engine)) return false;
            string path = AssetDatabase.GetAssetPath(engine);
            if (path.StartsWith("Assets/", StringComparison.Ordinal)) return true;

            PackageInfo package = PackageInfo.FindForAssetPath(path);
            return package != null && (package.source == PackageSource.Embedded || package.source == PackageSource.Local);
        }

        /// <summary>Whether the engine lives only in memory: a copy the Bench made, not saved yet.</summary>
        public static bool IsTemporary(EngineAsset engine) => engine != null && !EditorUtility.IsPersistent(engine);

        /// <summary>
        /// The volume that plays <paramref name="to"/> as loud as <paramref name="from"/> at <paramref name="volume"/>: one that
        /// passes as much energy from a broadband signal (<see cref="ImpulseResponseAsset.Energy"/>).
        /// </summary>
        public static double MatchedVolume(double volume, ImpulseResponseAsset from, ImpulseResponseAsset to) =>
            volume * Math.Sqrt(from.Energy() / to.Energy());

        /// <summary>
        /// An in-memory copy of <paramref name="engine"/> (a preset, or any engine), which the Bench works on: the same
        /// description, impulse responses, tuning and design, named after the engine (a preset by its engine's name). An
        /// imported engine's copy gets the design read from it (<see cref="EngineAsset.ReadDesign"/>), so a preset copy is
        /// designable at once. It outlives script reloads, not the Editor; nothing done to it reaches the
        /// project until <see cref="SaveOver"/> or <see cref="SaveAs"/>.
        /// </summary>
        public static EngineAsset TemporaryCopy(EngineAsset engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            EngineAsset copy = UnityEngine.Object.Instantiate(engine);
            copy.name = IsPreset(engine) ? engine.Description.engine.name : engine.name;
            copy.hideFlags = HideFlags.DontSave;
            if (copy.Design == null) copy.ReadDesign(out _);
            return copy;
        }

        /// <summary>
        /// Saves an in-memory engine as a new asset at <paramref name="path"/> (an <c>.asset</c> path in the project's Assets)
        /// and returns it. The in-memory engine stays as it is, a copy of the new asset, named as it is.
        /// </summary>
        public static EngineAsset SaveAs(EngineAsset copy, string path)
        {
            if (!IsTemporary(copy)) throw new ArgumentException("Not an in-memory engine.", nameof(copy));
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.Ordinal))
            {
                throw new ArgumentException($"'{path}' is not an .asset path in the project's Assets.", nameof(path));
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new ArgumentException($"{path} already exists.", nameof(path));

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            EngineAsset saved = UnityEngine.Object.Instantiate(copy);
            saved.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(saved, path);
            // Only this engine: SaveAssets would also write every other unsaved change in the project.
            AssetDatabase.SaveAssetIfDirty(saved);
            copy.name = saved.name;
            return saved;
        }

        /// <summary>
        /// Writes an in-memory engine over <paramref name="target"/>, the saved engine it was copied from (one that may be
        /// changed where it is, <see cref="IsEditable"/>), which keeps its name and file.
        /// </summary>
        public static void SaveOver(EngineAsset copy, EngineAsset target)
        {
            if (!IsTemporary(copy)) throw new ArgumentException("Not an in-memory engine.", nameof(copy));
            if (IsTemporary(target) || !IsEditable(target)) throw new ArgumentException($"{target} can't be saved over.", nameof(target));

            string name = target.name;
            EditorUtility.CopySerialized(copy, target);
            target.name = name;
            target.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }

        /// <summary>Creates a project folder and its missing parents.</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
