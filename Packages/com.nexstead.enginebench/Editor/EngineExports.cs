// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nexstead.EngineBench.Editor
{
    /// <summary>A rendered clip: its samples (44.1 kHz, the 16-bit output's scale) and what the engine did in it.</summary>
    sealed class EngineClip
    {
        /// <summary>Its part of the file names: "free rev", "start", "stop".</summary>
        public readonly string label;
        /// <summary>What the Bench plays: the engine and its forced induction leveled together.</summary>
        public readonly float[] mix;
        /// <summary>A boosted engine's layers at the engine's gain (they add up to that gain's mix); null without forced induction.</summary>
        public readonly float[] engine;
        public readonly float[] forcedInduction;

        /// <summary>Mean revs at idle (before the rev, or after the start).</summary>
        public double idleRpm;
        /// <summary>The highest revs in the clip.</summary>
        public double maxRpm;
        /// <summary>The revs at the clip's end.</summary>
        public double endRpm;
        public bool limiterCut;
        /// <summary>The engine ran: its revs passed twice the starter's speed, which the starter alone can't reach.</summary>
        public bool fired;

        public EngineClip(string label, float[] mix, float[] engine, float[] forcedInduction)
        {
            this.label = label;
            this.mix = mix;
            this.engine = engine;
            this.forcedInduction = forcedInduction;
        }

        public double Seconds => mix.Length / (double)EngineRenderer.SampleRate;
    }

    /// <summary>
    /// The Bench's own exports: a free rev, a start and a stop, as WAV files. Each renders the engine as tuned from a seed, in
    /// fixed blocks, so the same engine and seed write the same samples. The engine is really started: its starter cranks it
    /// with the throttle a little open (<see cref="CrankThrottle"/>), as the Bench does, since with the throttle shut an engine
    /// needing idle air never fires.
    /// </summary>
    static class EngineExports
    {
        public const double CrankThrottle = 0.15;
        /// <summary>The engine has caught once its revs pass this many times the starter's speed.</summary>
        public const double CatchFactor = 2.0;

        /// <summary>The longest the starter cranks; most engines catch within a second.</summary>
        const double MaxCrankSeconds = 6.0;
        /// <summary>After the starter lets go, the throttle stays where it cranked this long, so the engine doesn't stall as it catches.</summary>
        const double CatchSeconds = 0.2;

        // Free rev
        const double SettleSeconds = 2.0;
        const double IdleBeforeSeconds = 1.0;
        const double MaxRevSeconds = 4.0;
        const double LimiterHoldSeconds = 0.5;
        const double IdleAfterSeconds = 3.0;

        // Start and stop
        const double StartIdleSeconds = 2.5;
        const double MaxStopSeconds = 8.0;
        const double RestRpm = 30.0;
        const double StopTailSeconds = 0.3;

        /// <summary>
        /// Idle, a rev, a lift and back to idle: after an unrecorded start (<see cref="Start"/>) and 2 s settling, 1 s of idle, then
        /// full throttle to the rev limiter (held there 0.5 s; at most 4 s if it never gets there, as a governed engine), then the
        /// throttle shut and 3 s more.
        /// </summary>
        public static EngineClip FreeRev(EngineRenderSource source, ulong seed)
        {
            using var recorder = new Recorder(source, seed, "free rev");
            Start(recorder, source, record: false);
            recorder.Run(Idle, SettleSeconds, record: false);

            recorder.Run(Idle, IdleBeforeSeconds, record: true);
            double idle = recorder.MeanRpm;
            int blocks = recorder.BlocksFor(MaxRevSeconds);
            for (int b = 0; b < blocks && !recorder.LimiterCutting; ++b) recorder.Block(FullThrottle, record: true);
            if (recorder.LimiterCutting) recorder.Run(FullThrottle, LimiterHoldSeconds, record: true);

            recorder.Run(Idle, IdleAfterSeconds, record: true);
            EngineClip clip = recorder.Clip();
            clip.idleRpm = idle;
            clip.fired = clip.maxRpm > CatchFactor * source.StarterRpm;
            return clip;
        }

        /// <summary>
        /// From rest, the start (<see cref="Start"/>), then 2.5 s settling to idle; and the
        /// stop that follows: the ignition off, until the engine is at rest (at most 8 s) and 0.3 s more.
        /// </summary>
        public static (EngineClip start, EngineClip stop) StartAndStop(EngineRenderSource source, ulong seed)
        {
            using var recorder = new Recorder(source, seed, "start");
            bool caught = Start(recorder, source, record: true);
            recorder.Run(Idle, StartIdleSeconds - 1.0, record: true);
            recorder.ResetMean();
            recorder.Run(Idle, 1.0, record: true);
            double idle = recorder.MeanRpm;
            EngineClip start = recorder.Clip();
            start.idleRpm = idle;
            start.fired = caught && idle > source.StarterRpm;

            recorder.Begin("stop");
            var off = new EngineControls();
            int stopping = recorder.BlocksFor(MaxStopSeconds);
            for (int b = 0; b < stopping && recorder.Rpm > RestRpm; ++b) recorder.Block(off, record: true);
            recorder.Run(off, StopTailSeconds, record: true);
            EngineClip stop = recorder.Clip();
            stop.idleRpm = idle;
            stop.fired = start.fired;
            return (start, stop);
        }

        /// <summary>
        /// The starter cranks, the throttle a little open, until the engine catches (its revs pass <see cref="CatchFactor"/> times
        /// the starter's speed) or for <see cref="MaxCrankSeconds"/>; then lets go, the throttle held <see cref="CatchSeconds"/>
        /// more. Returns whether it caught.
        /// </summary>
        static bool Start(Recorder recorder, EngineRenderSource source, bool record)
        {
            int crank = recorder.BlocksFor(MaxCrankSeconds);
            bool caught = false;
            for (int b = 0; b < crank && !caught; ++b)
            {
                recorder.Block(Crank, record);
                caught = recorder.Rpm > CatchFactor * source.StarterRpm;
            }

            recorder.Run(new EngineControls { ignition = true, throttle = CrankThrottle }, CatchSeconds, record);
            return caught;
        }

        static EngineControls Crank => new EngineControls { ignition = true, starter = true, throttle = CrankThrottle };
        static EngineControls Idle => new EngineControls { ignition = true };
        static EngineControls FullThrottle => new EngineControls { ignition = true, throttle = 1 };

        /// <summary>
        /// The clip's files in <paramref name="folder"/>, named after the engine: "<i>engine</i> - <i>label</i>.wav", the mix as
        /// 16-bit PCM; for a boosted engine also "… (engine).wav" and "… (forced induction).wav", its layers as 32-bit float, as
        /// the forced induction's can pass full scale at the engine's gain. Returns the paths.
        /// </summary>
        public static IReadOnlyList<string> FilesFor(EngineClip clip, string folder, string engineName)
        {
            string stem = Path.Combine(folder, $"{FileSafe(engineName)} - {clip.label}");
            var paths = new List<string> { stem + ".wav" };
            if (clip.forcedInduction != null)
            {
                paths.Add(stem + " (engine).wav");
                paths.Add(stem + " (forced induction).wav");
            }

            return paths;
        }

        /// <summary>Writes the clip's files (<see cref="FilesFor"/>), replacing any there; returns their paths.</summary>
        public static IReadOnlyList<string> Write(EngineClip clip, string folder, string engineName)
        {
            Directory.CreateDirectory(folder);
            IReadOnlyList<string> paths = FilesFor(clip, folder, engineName);
            WritePcm16(paths[0], clip.mix);
            if (clip.forcedInduction != null)
            {
                WriteFloat32(paths[1], clip.engine);
                WriteFloat32(paths[2], clip.forcedInduction);
            }

            return paths;
        }

        /// <summary>The name without the characters file names can't hold.</summary>
        static string FileSafe(string name)
        {
            var safe = new StringBuilder(name.Length);
            foreach (char c in name) safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == '/' || c == '\\' ? '_' : c);
            return safe.Length > 0 ? safe.ToString() : "Engine";
        }

        /// <summary>16-bit mono PCM; the samples are on the 16-bit scale, so this only rounds (and clamps a sample past full scale).</summary>
        public static void WritePcm16(string path, float[] samples)
        {
            using var writer = new BinaryWriter(File.Create(path));
            WriteHeader(writer, samples.Length, 1, 16);
            foreach (float v in samples)
            {
                double scaled = Math.Round(v * 32768.0, MidpointRounding.AwayFromZero);
                writer.Write((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, scaled)));
            }
        }

        /// <summary>32-bit float mono, unclamped.</summary>
        public static void WriteFloat32(string path, float[] samples)
        {
            using var writer = new BinaryWriter(File.Create(path));
            WriteHeader(writer, samples.Length, 3, 32);
            foreach (float v in samples) writer.Write(v);
        }

        static void WriteHeader(BinaryWriter writer, int frames, short format, short bits)
        {
            int bytesPerSample = bits / 8;
            int data = frames * bytesPerSample;
            bool floating = format == 3;
            // A float file carries a fact chunk (the frame count), as the format asks of anything not PCM.
            int fmtSize = floating ? 18 : 16;
            int riff = 4 + (8 + fmtSize) + (floating ? 12 : 0) + (8 + data);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(riff);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(fmtSize);
            writer.Write(format);
            writer.Write((short)1);
            writer.Write(EngineRenderer.SampleRate);
            writer.Write(EngineRenderer.SampleRate * bytesPerSample);
            writer.Write((short)bytesPerSample);
            writer.Write(bits);
            if (floating)
            {
                writer.Write((short)0);
                writer.Write(Encoding.ASCII.GetBytes("fact"));
                writer.Write(4);
                writer.Write(frames);
            }

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(data);
        }

        /// <summary>Renders block by block, keeping what it records and the revs.</summary>
        sealed class Recorder : IDisposable
        {
            readonly EngineRenderer renderer;
            readonly bool forcedInduction;
            readonly float[] mix, engine, induction;
            readonly List<float> mixClip = new List<float>(), engineClip = new List<float>(), inductionClip = new List<float>();
            string label;
            double maxRpm, rpmSum;
            int rpmCount;
            bool limiterCut;

            public Recorder(EngineRenderSource source, ulong seed, string label)
            {
                renderer = new EngineRenderer(source, seed);
                forcedInduction = renderer.ForcedInductionKind != ForcedInductionKind.None;
                mix = new float[renderer.MaxBlockSamples];
                engine = new float[renderer.MaxBlockSamples];
                induction = new float[renderer.MaxBlockSamples];
                this.label = label;
            }

            public double Rpm => renderer.Rpm;
            public bool LimiterCutting => renderer.RevLimiterCutting;
            public double MeanRpm => rpmCount > 0 ? rpmSum / rpmCount : renderer.Rpm;
            public int BlocksFor(double seconds) => renderer.BlocksFor(seconds);

            public void Run(in EngineControls controls, double seconds, bool record)
            {
                int blocks = renderer.BlocksFor(seconds);
                for (int b = 0; b < blocks; ++b) Block(controls, record);
            }

            public void Block(in EngineControls controls, bool record)
            {
                int n = renderer.RenderBlockOutputs(controls, mix, engine, induction);
                if (!record) return;

                for (int i = 0; i < n; ++i)
                {
                    mixClip.Add(mix[i]);
                    if (!forcedInduction) continue;
                    engineClip.Add(engine[i]);
                    inductionClip.Add(induction[i]);
                }

                maxRpm = Math.Max(maxRpm, renderer.Rpm);
                limiterCut |= renderer.RevLimiterCutting;
                rpmSum += renderer.Rpm;
                ++rpmCount;
            }

            public void ResetMean()
            {
                rpmSum = 0;
                rpmCount = 0;
            }

            /// <summary>What it recorded since the start or <see cref="Begin"/>.</summary>
            public EngineClip Clip() => new EngineClip(label, mixClip.ToArray(),
                forcedInduction ? engineClip.ToArray() : null, forcedInduction ? inductionClip.ToArray() : null)
            {
                maxRpm = maxRpm,
                endRpm = renderer.Rpm,
                limiterCut = limiterCut,
            };

            /// <summary>Starts another clip from where the engine is.</summary>
            public void Begin(string nextLabel)
            {
                label = nextLabel;
                mixClip.Clear();
                engineClip.Clear();
                inductionClip.Clear();
                maxRpm = 0;
                limiterCut = false;
                ResetMean();
            }

            public void Dispose() => renderer.Dispose();
        }
    }
}
