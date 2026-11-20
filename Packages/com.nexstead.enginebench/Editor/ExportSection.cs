// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>
    /// The Bench's Export section, as cards. First Nx Vehicle Sound System: what it is, whether it is installed, and the way to
    /// its bank export (a section its own package adds to the Bench) or to its Asset Store page. The Bench references nothing of
    /// that package: it finds it by its package name. Then the Bench's own WAV clips: the engine on the Bench (as tuned, with its
    /// unsaved changes) as a free rev, a start and a stop (<see cref="EngineExports"/>), in one export, into a folder, from a
    /// seed. Last, the audio systems an export is planned for, marked coming soon.
    /// </summary>
    sealed class ExportSection : VisualElement
    {
        /// <summary>Nx Vehicle Sound System's package name.</summary>
        public const string VehicleSoundPackage = "com.nexstead.vehiclesound";

        /// <summary>Where to get Nx Vehicle Sound System: an Asset Store search until its product page exists.</summary>
        public const string VehicleSoundStoreUrl = "https://assetstore.unity.com/search#q=Nx%20Vehicle%20Sound%20System";

        /// <summary>The audio systems an export is planned for, and what it will write.</summary>
        static readonly (string name, string text)[] ComingSoon =
        {
            ("RES2 (Realistic Engine Sounds 2)", "The engine's loops, cut and named for its engine sound slots."),
            ("FMOD Studio", "The engine's loops, laid out on revs and load parameters in an FMOD Studio event."),
            ("Wwise", "The engine's loops in a blend container on revs and load, for Audiokinetic Wwise."),
        };

        const string ProgressTitle = "Nx Engine Bench";
        const string DefaultFolder = "Assets/Engine Sounds";
        const string FolderKey = "Nexstead.EngineBench.Export.Folder";
        const string SeedKey = "Nexstead.EngineBench.Export.Seed";

        readonly Func<string, bool> openPackageSection;
        EngineAsset engine;

        readonly VisualElement vehicleSoundDot = new VisualElement();
        readonly Label vehicleSoundState = new Label();
        readonly Button vehicleSound;
        readonly Label vehicleSoundNote;

        readonly TextField folder;
        readonly IntegerField seed;
        readonly VisualElement result = new VisualElement();
        readonly Label resultText = new Label();
        string revealPath;

        /// <param name="openPackageSection">Shows the Bench section a package adds, by package name; false if it adds none.</param>
        public ExportSection(Func<string, bool> openPackageSection)
        {
            this.openPackageSection = openPackageSection;
            AddToClassList("export");

            // Nx Vehicle Sound System
            VisualElement nx = Card("Nx Vehicle Sound System", "RECOMMENDED", "export-badge--accent");
            Text(nx, "Game-ready engine sound for racing and driving games, made from this engine.", "export-card__text--tagline");
            Text(nx, "Nexstead's vehicle sound package for Unity. Installed, it adds an Export bank tab to this window: it renders " +
                     "the engine on the Bench across its revs, on and off the throttle, and builds a sound bank from the takes in " +
                     "one step. In your game, a component on each vehicle plays the bank from the vehicle's revs and throttle.");
            Item(nx, "Seamless loops crossfaded by revs and load, with the limiter, startup and shutdown");
            Item(nx, "Turbo and supercharger layers, plus tyres, wind, impacts and boost");
            Item(nx, "Any number of vehicles within one voice budget, with any vehicle physics");

            var footer = new VisualElement();
            footer.AddToClassList("export-card__footer");
            vehicleSoundDot.AddToClassList("export-status__dot");
            footer.Add(vehicleSoundDot);
            vehicleSoundState.AddToClassList("export-status");
            footer.Add(vehicleSoundState);
            vehicleSound = new Button(OpenVehicleSound);
            vehicleSound.AddToClassList("export-button");
            vehicleSound.AddToClassList("export-button--primary");
            footer.Add(vehicleSound);
            nx.Add(footer);
            vehicleSoundNote = Text(nx, "", "export-card__text--note");
            vehicleSoundNote.AddToClassList("bench__section--hidden");
            Add(nx);

            // WAV clips
            VisualElement wav = Card("WAV clips", "INCLUDED", null);
            Text(wav, "Plain audio files of this engine, for any game, engine or audio tool.", "export-card__text--tagline");
            Text(wav, "One export renders three takes of the engine as it plays on the Bench, unsaved changes included:");
            Item(wav, "<b>Free rev</b>: idle, full throttle to the rev limiter, the lift and back to idle");
            Item(wav, "<b>Start</b>: the starter cranks until the engine catches, then it settles to idle");
            Item(wav, "<b>Stop</b>: the ignition off, until the engine is at rest");
            Text(wav, "44.1 kHz mono, 16-bit, at the Bench's level. A turbo or supercharged engine also gets each take as two " +
                      "layers, the engine and the turbo or supercharger alone (32-bit float, at the engine's level, so they add " +
                      "up), to mix in your own tool. The same engine and seed write the same samples.", "export-card__text--note");

            folder = new TextField("Folder") { value = EditorPrefs.GetString(FolderKey, DefaultFolder), isDelayed = true, tooltip = "In the project's Assets (the files are imported) or anywhere." };
            folder.RegisterValueChangedCallback(e => EditorPrefs.SetString(FolderKey, e.newValue));
            folder.AddToClassList("export__folder");
            var folderRow = new VisualElement();
            folderRow.AddToClassList("export__folder-row");
            folderRow.Add(folder);
            var choose = new Button(ChooseFolder) { text = "Choose…" };
            choose.AddToClassList("export__choose");
            folderRow.Add(choose);
            wav.Add(folderRow);

            seed = new IntegerField("Seed") { value = EditorPrefs.GetInt(SeedKey, 1), tooltip = "Seeds the combustion and the noise: another seed, another take." };
            seed.RegisterValueChangedCallback(e =>
            {
                int kept = Math.Max(0, e.newValue);
                if (kept != e.newValue) seed.SetValueWithoutNotify(kept);
                EditorPrefs.SetInt(SeedKey, kept);
            });
            wav.Add(seed);

            var export = new Button(ExportClips) { text = "Export WAV clips" };
            export.AddToClassList("export-button");
            export.AddToClassList("export-button--primary");
            export.AddToClassList("export-button--wide");
            wav.Add(export);

            result.AddToClassList("export-result");
            result.AddToClassList("bench__section--hidden");
            resultText.AddToClassList("export-result__text");
            result.Add(resultText);
            var reveal = new Button(() => EditorUtility.RevealInFinder(revealPath)) { text = RevealLabel };
            reveal.AddToClassList("export-button");
            result.Add(reveal);
            wav.Add(result);
            Add(wav);

            // Coming soon
            var heading = new Label("More audio systems");
            heading.AddToClassList("export-heading");
            Add(heading);
            foreach ((string name, string text) in ComingSoon)
            {
                VisualElement card = Card(name, "COMING SOON", null);
                card.AddToClassList("export-card--soon");
                Text(card, text);
                Add(card);
            }

            RegisterCallback<AttachToPanelEvent>(_ => ShowVehicleSound());
        }

        /// <summary>The engine on the Bench, or none: the last export's result goes, as it was another engine's.</summary>
        public void ShowEngine(EngineAsset shown)
        {
            engine = shown;
            result.AddToClassList("bench__section--hidden");
            resultText.text = "";
            revealPath = null;
        }

        static bool VehicleSoundInstalled() => UnityEditor.PackageManager.PackageInfo.FindForPackageName(VehicleSoundPackage) != null;

        void ShowVehicleSound()
        {
            bool installed = VehicleSoundInstalled();
            vehicleSoundDot.EnableInClassList("export-status__dot--on", installed);
            vehicleSoundState.text = installed ? "Installed" : "Not installed";
            vehicleSound.text = installed ? "Open Export bank" : "View on the Asset Store";
            vehicleSound.tooltip = installed ? "Its Export bank tab, in this window." : VehicleSoundStoreUrl;
        }

        void OpenVehicleSound()
        {
            ShowVehicleSound();
            if (!VehicleSoundInstalled())
            {
                Application.OpenURL(VehicleSoundStoreUrl);
                return;
            }

            bool opened = openPackageSection(VehicleSoundPackage);
            vehicleSoundNote.text = opened ? "" : "It's installed, but its Export bank tab isn't loaded: it needs a Bench version it " +
                                                  "supports, and its scripts compiled.";
            vehicleSoundNote.EnableInClassList("bench__section--hidden", opened);
        }

        void ChooseFolder()
        {
            string start = Path.GetFullPath(Path.Combine(ProjectRoot, folder.value));
            string picked = EditorUtility.OpenFolderPanel("Export to", Directory.Exists(start) ? start : Application.dataPath, "");
            if (string.IsNullOrEmpty(picked)) return;

            string full = Path.GetFullPath(picked);
            string assets = Path.GetFullPath(Application.dataPath);
            folder.value = full.StartsWith(assets, StringComparison.Ordinal)
                ? "Assets" + full.Substring(assets.Length).Replace('\\', '/')
                : full;
        }

        static string ProjectRoot => Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));

        /// <summary>The platform's name for showing a file in the file manager, as the Editor's own menus have it.</summary>
        static string RevealLabel => Application.platform switch
        {
            RuntimePlatform.OSXEditor => "Reveal in Finder",
            RuntimePlatform.WindowsEditor => "Show in Explorer",
            _ => "Open Containing Folder",
        };

        /// <summary>Renders the free rev, the start and the stop, and writes them (and a boosted engine's layers) as WAV files.</summary>
        void ExportClips()
        {
            EngineAsset shown = engine;
            if (shown == null) return;

            string target = Path.GetFullPath(Path.Combine(ProjectRoot, string.IsNullOrWhiteSpace(folder.value) ? DefaultFolder : folder.value));
            var take = (ulong)seed.value;
            EngineClip[] clips;
            try
            {
                EngineRenderSource source = EngineRenderSource.Capture(shown);
                EditorUtility.DisplayProgressBar(ProgressTitle, "Rendering the free rev…", 0.0f);
                EngineClip freeRev = EngineExports.FreeRev(source, take);
                EditorUtility.DisplayProgressBar(ProgressTitle, "Rendering the start and the stop…", 0.5f);
                (EngineClip start, EngineClip stop) = EngineExports.StartAndStop(source, take);
                clips = new[] { freeRev, start, stop };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            List<string> paths = clips.SelectMany(c => EngineExports.FilesFor(c, target, shown.name)).ToList();
            List<string> existing = paths.Where(File.Exists).ToList();
            if (existing.Count > 0 && !EditorUtility.DisplayDialog("Replace files?",
                    "These files are there already:\n" + string.Join("\n", existing.Select(Path.GetFileName)), "Replace", "Cancel"))
            {
                return;
            }

            foreach (EngineClip clip in clips) EngineExports.Write(clip, target, shown.name);
            Import(paths);
            ShowResult(clips, paths, target, shown.name);
        }

        void ShowResult(EngineClip[] clips, List<string> paths, string target, string engineName)
        {
            EngineClip freeRev = clips[0], start = clips[1];
            bool fired = freeRev.fired && start.fired;
            string root = ProjectRoot + Path.DirectorySeparatorChar;
            string shownFolder = target.StartsWith(root, StringComparison.Ordinal) ? target.Substring(root.Length).Replace('\\', '/') : target;

            var lines = new List<string> { $"Wrote {paths.Count} files to {shownFolder}:" };
            foreach (EngineClip clip in clips)
            {
                string file = Path.GetFileName(EngineExports.FilesFor(clip, target, engineName)[0]);
                lines.Add($"•  {file}, {clip.Seconds:0.0} s{(clip.forcedInduction != null ? ", and its two layers" : "")}");
            }

            lines.Add(fired
                ? $"Idle {start.idleRpm:0} rpm, up to {freeRev.maxRpm:0} rpm{(freeRev.limiterCut ? " (the limiter)" : "")}."
                : "The engine didn't start: check its design and sound settings on the Bench.");

            resultText.text = string.Join("\n", lines);
            result.EnableInClassList("export-result--warning", !fired);
            result.RemoveFromClassList("bench__section--hidden");
            revealPath = paths[0];
        }

        /// <summary>Imports the files written inside the project's Assets, and their folders: only those, not the whole project.</summary>
        static void Import(IEnumerable<string> paths)
        {
            string assets = Path.GetFullPath(Application.dataPath);
            string AssetPath(string full) => "Assets" + full.Substring(assets.Length).Replace('\\', '/');
            foreach (string path in paths)
            {
                string full = Path.GetFullPath(path);
                if (!full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;

                // A new folder (and its new parents) first, outermost first
                var folders = new Stack<string>();
                for (string dir = Path.GetDirectoryName(full); dir.Length > assets.Length && !AssetDatabase.IsValidFolder(AssetPath(dir)); dir = Path.GetDirectoryName(dir))
                {
                    folders.Push(AssetPath(dir));
                }
                while (folders.Count > 0) AssetDatabase.ImportAsset(folders.Pop());
                AssetDatabase.ImportAsset(AssetPath(full));
            }
        }

        /// <summary>A card: its title and a badge beside it.</summary>
        static VisualElement Card(string title, string badge, string badgeModifier)
        {
            var card = new VisualElement();
            card.AddToClassList("export-card");
            var header = new VisualElement();
            header.AddToClassList("export-card__header");
            var name = new Label(title);
            name.AddToClassList("export-card__title");
            header.Add(name);
            var tag = new Label(badge);
            tag.AddToClassList("export-badge");
            if (badgeModifier != null) tag.AddToClassList(badgeModifier);
            header.Add(tag);
            card.Add(header);
            return card;
        }

        static Label Text(VisualElement card, string text, string modifier = null)
        {
            var label = new Label(text);
            label.AddToClassList("export-card__text");
            if (modifier != null) label.AddToClassList(modifier);
            card.Add(label);
            return label;
        }

        /// <summary>A bulleted line; its text wraps beside the bullet.</summary>
        static void Item(VisualElement card, string text)
        {
            var row = new VisualElement();
            row.AddToClassList("export-card__item");
            var bullet = new Label("•");
            bullet.AddToClassList("export-card__bullet");
            row.Add(bullet);
            var label = new Label(text);
            label.AddToClassList("export-card__item-text");
            row.Add(label);
            card.Add(row);
        }
    }
}
