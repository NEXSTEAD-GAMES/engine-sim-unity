// Part of Nx Engine Bench, based on engine-sim (not an upstream file).
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using System;
using Nexstead.EngineBench.Dense;
using Nexstead.EngineBench.Simulation;
using UnityEngine;

namespace Nexstead.EngineBench
{
    /// <summary>
    /// Plays a simulated engine through this GameObject's AudioSource: an <see cref="EngineSoundStream"/> runs the engine on its
    /// own thread, and the audio thread reads it in <c>OnAudioFilterRead</c>. Drive it with the control properties. Runs in Play
    /// Mode; in Edit Mode only when <see cref="playInEditMode"/> is set (the bench sets it). Editor only, as the whole package:
    /// its assembly compiles only with <c>UNITY_EDITOR</c>, so no player build contains the simulator.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class EngineAudioSource : MonoBehaviour
    {
        [SerializeField] EngineAsset engine;
        [SerializeField, Range(0f, 1f)] float volume = 0.5f;
        [Tooltip("Seconds of sound kept ready ahead of the audio thread.")]
        [SerializeField, Range(0.01f, 0.25f)] float targetLatency = 0.04f;
        [Tooltip("Seeds the engine's random streams (combustion, noise).")]
        [SerializeField] int seed = 1;
        [Tooltip("Play outside Play Mode too.")]
        public bool playInEditMode;

        EngineRenderer renderer;
        EngineSoundStream stream;
        EngineSoundGenerator.Controls controls;
        volatile float gain;

        public EngineAsset Engine
        {
            get => engine;
            set
            {
                engine = value;
                if (stream != null) Restart();
            }
        }

        public float Volume
        {
            get => volume;
            set => volume = Mathf.Clamp01(value);
        }

        /// <summary>The engine's speed control: throttle, 0 closed to 1 open.</summary>
        public float Throttle
        {
            get => (float)controls.speedControl;
            set { controls.speedControl = Mathf.Clamp01(value); Push(); }
        }

        public bool Ignition
        {
            get => controls.ignition;
            set { controls.ignition = value; Push(); }
        }

        public bool Starter
        {
            get => controls.starter;
            set { controls.starter = value; Push(); }
        }

        public bool DynoEnabled
        {
            get => controls.dynoEnabled;
            set { controls.dynoEnabled = value; Push(); }
        }

        /// <summary>The dyno drives the crank up to its speed, not only brakes it. Engage on a turning engine only.</summary>
        public bool DynoHold
        {
            get => controls.dynoHold;
            set { controls.dynoHold = value; Push(); }
        }

        /// <summary>The crank speed the dyno holds (rpm).</summary>
        public float DynoRpm
        {
            get => (float)Units.ToRpm(controls.dynoSpeed);
            set { controls.dynoSpeed = Units.Rpm(Mathf.Max(0f, value)); Push(); }
        }

        public bool Playing => stream != null;
        public double Rpm => stream?.Rpm ?? 0;
        public double RenderCost => stream?.RenderCost ?? 0;
        public double BufferedSeconds => stream?.BufferedSeconds ?? 0;
        public double ProducedSeconds => stream?.ProducedSeconds ?? 0;
        public double UnderrunSeconds => stream?.UnderrunSeconds ?? 0;
        public Exception Failure => stream?.Failure;

        /// <summary>Set by the Bench before a (re)start: record the engine's state for its visuals.</summary>
        internal bool captureForBench;

        /// <summary>The running engine's recorded state; null when not playing or not recording.</summary>
        internal EngineCapture Capture => renderer?.Generator.Capture;

        void OnEnable()
        {
            if (Application.isPlaying || playInEditMode) Begin();
        }

        void OnDisable() => End();

        void Update() => gain = volume;

        /// <summary>
        /// Sends the engine's tuned mixer (<see cref="EngineAsset.TunedMix"/>) to the running engine, which plays it from
        /// its next block. The rest of the tuning takes a <see cref="Restart"/>.
        /// </summary>
        public void ApplyTunedMix()
        {
            if (stream != null && engine != null) stream.SetMix(engine.TunedMix());
        }

        /// <summary>Starts again from a fresh engine (after a change of engine or of its tuning, or to restart the run).</summary>
        public void Restart()
        {
            End();
            if (isActiveAndEnabled && (Application.isPlaying || playInEditMode)) Begin();
        }

        void Begin()
        {
            if (engine == null || stream != null) return;

            renderer = new EngineRenderer(EngineRenderSource.Capture(engine), unchecked((ulong)seed), 1f, captureForBench);
            stream = new EngineSoundStream(renderer.Generator, AudioSettings.outputSampleRate, targetLatency);
            stream.SetControls(controls);
            gain = volume;
            stream.Start();

            var source = GetComponent<AudioSource>();
            source.clip = null;
            source.Play();
        }

        void End()
        {
            if (stream == null) return;

            GetComponent<AudioSource>().Stop();
            EngineSoundStream ending = stream;
            stream = null;
            ending.Dispose();
            renderer.Dispose();
            renderer = null;
        }

        void Push() => stream?.SetControls(controls);

        void OnAudioFilterRead(float[] data, int channels)
        {
            EngineSoundStream current = stream;
            if (current == null) return;
            current.Read(data, channels, gain);
        }
    }
}
