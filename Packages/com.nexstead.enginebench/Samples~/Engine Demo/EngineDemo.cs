// Engine Demo, a sample of Nx Engine Bench: plays a preset engine in Play Mode.
// MIT licence: the package's LICENSE.md.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Nexstead.EngineBench.Samples
{
    /// <summary>
    /// Drives the <see cref="EngineAudioSource"/> beside it. On Play it starts its engine (the starter turns it for
    /// <see cref="crankSeconds"/> at a little throttle, as the Bench does); then it idles, at full throttle while Space is
    /// held. A line on screen says where the Bench is, to pick, design and tune engines.
    /// </summary>
    [RequireComponent(typeof(EngineAudioSource))]
    public sealed class EngineDemo : MonoBehaviour
    {
        const string Hint = "Hold Space for full throttle.\nWindow > Nx Engine Bench opens the Bench: pick, design and tune engines there.";

        [Tooltip("How long the starter turns the engine.")]
        [SerializeField, Min(0.1f)] float crankSeconds = 1.5f;
        [Tooltip("The throttle while cranking: some engines need a little to fire.")]
        [SerializeField, Range(0f, 1f)] float crankThrottle = 0.15f;

        EngineAudioSource source;
        float crankLeft;
        GUIStyle hintStyle;

        void Awake() => source = GetComponent<EngineAudioSource>();

        void Start()
        {
            source.Ignition = true;
            source.Starter = true;
            crankLeft = crankSeconds;
        }

        void Update()
        {
            float throttle = FullThrottleKeyHeld() ? 1f : 0f;
            if (crankLeft > 0f)
            {
                crankLeft -= Time.unscaledDeltaTime;
                if (crankLeft <= 0f) source.Starter = false;
                throttle = Mathf.Max(throttle, crankThrottle);
            }

            source.Throttle = throttle;
        }

        void OnGUI()
        {
            hintStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, normal = { textColor = Color.white } };
            GUI.Label(new Rect(20, 20, Screen.width - 40, 60), Hint, hintStyle);
        }

        static bool FullThrottleKeyHeld()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.spaceKey.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.Space);
#else
            return false;
#endif
        }
    }
}
