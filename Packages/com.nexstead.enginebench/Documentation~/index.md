# Nx Engine Bench

Nx Engine Bench simulates a combustion engine and its exhaust in the Unity
Editor and makes its sound: crankshaft, connecting rods, pistons, valves, gas
flow, combustion and exhaust pressure, fed into a synthesizer that filters the
exhaust pulses through a muffler's impulse response. It is based on
[engine-sim](https://github.com/ange-yaghi/engine-sim) by Ange Yaghi, ported to
C# and checked bit for bit against the original (see Accuracy).

Use it to:

- make your own engines from presets and tune their sound (the **Bench**
  window);
- listen to them live in the Editor: in the Bench, in Play Mode with
  `EngineAudioSource`, and in the Engine Demo sample;
- export their sound as WAV files: a free rev, a start and a stop, in one
  export (the Bench's Export section);
- build tools on it: the render API renders any engine under any controls,
  and extensions add their own sections to the Bench window.

It ships 22 of engine-sim's library engines, two engines with forced induction
(a turbocharged flat-4 and a Roots-supercharged V8), a 1000 cc superbike
inline-4, a 2.0 L turbocharged inline-4 and a 125 cc kart twin designed on the
Bench, and nine exhaust filters as presets.

## Requirements

- Unity 6000.6.7f1 or later.
- Burst, installed with the package. Live playback keeps up with
  real time only Burst-compiled: Jobs > Burst > Enable Compilation must be on.
  With it off, everything still works, several times slower than real time.

## An Editor tool

Nx Engine Bench works in the Unity Editor only. Its assemblies compile only
when `UNITY_EDITOR` is defined, so no player build contains the simulator,
the Bench or the demo's scripts. Your own code that uses the package must be
Editor-only too: in an assembly definition with the `UNITY_EDITOR` define
constraint, in an Editor folder, or inside `#if UNITY_EDITOR`. A scene that
uses `EngineAudioSource`, such as the Engine Demo, plays in the Editor's Play
Mode; in a player build its components are missing.

## Installing

Window > Package Manager > **+** > **Install package from git URL…** with
`https://github.com/NEXSTEAD-GAMES/engine-sim-unity.git?path=/Packages/com.nexstead.enginebench`,
or **Install package from disk…** and pick the package's `package.json` from a
clone of the repository; or get it from the Unity Asset Store.

## Getting started

**The demo scene.** Package Manager > **Nx Engine Bench** > **Samples** >
**Engine Demo** > **Import**, then open
`Assets/Samples/Nx Engine Bench/<version>/Engine Demo/Engine Demo.unity` and
press **Play**. The small-block V8 starts on its own; hold **Space** for full
throttle. A line on screen points to **Window > Nx Engine Bench**, where
engines are picked, designed and tuned.

The sample is one script: `EngineDemo` drives the `EngineAudioSource` beside
it (the start, then the throttle from Space, with either input system) and
draws that line.

**The Bench.**

1. Open **Window > Nx Engine Bench**.
2. Pick an engine with the top bar's engine button, for example
   **Presets > V8, small-block**.
3. Press the green **Start** under the engine: it turns over and fires. Move
   the **Throttle**, or tick **Dyno** to hold it at a speed.
4. Watch **Render cost** in the instrument panel: below 1 the simulation keeps
   up with real time.

## Presets and your own engines

The presets are read-only: an installed package can't be written, and a
preset stays as the package ships it. The Bench works on an in-memory
copy of whatever engine you pick, so you can change a preset at once, and an
engine of yours changes only when you save; **Save preset…** saves a
preset's copy in your project, where you pick. The saved engine is an ordinary
asset that you can rename and keep under version control. It still points
at the package's impulse response presets.

| Preset | Engine | Cylinders | Redline (rpm) |
|---|---|---|---|
| `boxer-4-equal-length` | Boxer-4 2.5 L · Equal-Length (Smooth) | 4 | 6500 |
| `boxer-4-rally-wagon` | Boxer-4 2.5 L · Rally Wagon | 4 | 6500 |
| `boxer-4-turbo` | Boxer-4 2.5 L Turbo · Rally Twin-Scroll | 4 | 6500 |
| `boxer-4-unequal-length` | Boxer-4 2.5 L · Unequal-Length (Rumble) | 4 | 6500 |
| `inline-2-kart` | Inline-2 125 · 360° Kart | 2 | 14500 |
| `inline-4-superbike` | Inline-4 1000 · Early-2000s Superbike | 4 | 12000 |
| `inline-4-turbo` | Inline-4 2.0 L Turbo · Late-'90s JDM Coupe | 4 | 7500 |
| `inline-4-variable-lift` | Inline-4 1.8 L · '90s High-Rev Variable Lift | 4 | 8400 |
| `inline-5-group-b` | Inline-5 2.3 L · '80s Group B Rally | 5 | 6000 |
| `inline-5-rally-road` | Inline-5 2.2 L · '80s Rally Five, Road | 5 | 7500 |
| `inline-6-german` | Inline-6 2.8 L · '90s German Straight-Six | 6 | 7000 |
| `inline-6-jdm` | Inline-6 3.0 L · '90s JDM Straight-Six | 6 | 6000 |
| `single-atv` | Single 520 · Utility ATV | 1 | 5000 |
| `v-twin-cruiser` | V-twin 1340 · '70s Cruiser | 2 | 5000 |
| `v-twin-garden-tractor` | V-twin 750 · Garden Tractor | 2 | 3600 |
| `v-twin-mower` | V-twin 750 · Governed Mower | 2 | 3600 |
| `v10-supercar` | V10 4.8 L · Japanese Supercar | 10 | 9000 |
| `v12-fighter` | V12 27 L · WWII Fighter (its starter can't turn it over: it runs on the dyno only) | 12 | 3000 |
| `v12-screamer` | V12 3.0 L · '90s F1 Screamer | 12 | 18000 |
| `v6-even-fire` | V6 90° Even-Fire · Split-Pin | 6 | 5500 |
| `v6-odd-fire` | V6 90° Odd-Fire · '70s Lumpy Six | 6 | 5500 |
| `v6-smooth` | V6 60° · Smooth Six | 6 | 5500 |
| `v8-big-block-muscle` | V8 454 ci Big-Block · '70s Muscle | 8 | 5500 |
| `v8-big-block-truck` | V8 454 ci Big-Block · Work Truck | 8 | 5500 |
| `v8-flat-plane-supercar` | V8 4.5 L Flat-Plane · Italian Supercar | 8 | 9000 |
| `v8-pushrod-muscle` | V8 5.3 L Pushrod · 2000s Muscle | 8 | 6500 |
| `v8-roots-blown` | V8 5.3 L Roots-Blown · Street Muscle | 8 | 6500 |

The presets are engine-sim's library engines, named by their type, and the
community fork's two forced induction examples. Three more were designed on
the Bench. `inline-2-kart` is a 125 cc inline twin (44 × 41 mm, 12:1) with both
pistons on one crank pin, firing 360° apart into one exhaust: one firing a
revolution, as a two-stroke kart single fires. It revs to a 14,800 rpm limiter
through the Straight pipe filter; on the Bench's dyno it makes 13.3 N·m at
8,300 rpm and 15.1 kW at 13,300. `inline-4-superbike` is a 1000 cc inline-4 of the kind superbikes use
(73 × 59 mm, 12.0:1, a flat-plane crank firing 1-2-4-3), with a 12,000 rpm
redline and a 12,500 rpm limiter, through the Race exhaust filter; on the
Bench's dyno it makes 108 N·m at 8,500 rpm and 114.5 kW at 10,250.
`inline-4-turbo` is a 2.0 L DOHC
inline-4 of the kind late-'90s Japanese sports coupes used (86 × 86 mm,
8.5:1, firing 1-3-4-2) with a single ball-bearing turbocharger at 62 kPa
(9 psi), an intercooler and a recirculating blow-off valve, a 7,500 rpm
redline and a 7,700 rpm limiter, through the Warm exhaust filter. Its
turbine is sized for the boost to come in between 2,000 and 2,500 rpm; on the
Bench's dyno it makes 278 N·m at 3,950 rpm (276 at 4,800) and 185 kW at
7,400, and it idles at about 860 rpm. All four carry their designs, so their copies open in the Design
section like the others. Their sound is engine-sim's simulation of each layout, not a
recording of any manufacturer's engine.

The exhaust filters (Presets > ImpulseResponses) are the impulse responses
the exhaust's pulses go through: the muffler, and the space between the pipe
and the listener. Seven run from least to most muffled, each letting less of
the top end through:

| Filter | Sound | Energy at 2 kHz and up |
|---|---|---|
| 1 · Straight pipe | raw, all rasp | 82 % |
| 2 · Race | loud rasp, light body | 70 % |
| 3 · Sport | bright, with body | 48 % |
| 4 · Street | balanced, mild rasp | 25 % |
| 5 · Stock | smooth, factory muffler | 8.5 % |
| 6 · Comfort | quiet, soft edges | 5 % |
| 7 · Muffled | deep boom, no rasp | 2.7 % |

Two variants have another character: **Warm** (Stock's top end, about 6 dB
more around 250 Hz) and **Muffled, roomy** (Muffled with a short room tail).
Picked in the Bench, a filter plays as loud as the one it replaces, so
moving along the ladder changes the tone, not the loudness; at their own
volumes (`ExhaustFilters`) all nine play equally loud. Seven are engine-sim's
sound library recordings; Street and Comfort are Sport and Stock through a
gentle low-pass (about 2.2 kHz). They are raw 16-bit samples, not
AudioClips, so no import setting can change them.

## The Bench

### The top bar

The engine button picks what is on the bench:

- **Presets**: a preset, to design and tune.
- **In the project**: an engine you saved.

Either is worked on as a copy in memory: what you change plays at once but
reaches the engine's asset only when you save. For a preset's copy, the
yellow **Save preset…** saves it in the project, where you pick; for an
engine of yours, **Save** (shown once something changed, the name marked
"changed") writes the changes over it. Switching to another engine, or
closing the window, asks first (Save, Cancel or Discard) and drops changes not
saved; stopping the engine keeps them. The tabs switch the
section on the left: **Design**, **Sound**, and any that other packages add.

### Running the engine

The controls sit under the engine's front view. The engine runs in real time
and plays through a hidden `EngineAudioSource`, in Edit or Play Mode.

- **Start** (green) builds the engine fresh and turns it over with the
  starter, at a little throttle: 1.5 s of the simulation's time, and on
  until it runs on its own (up to 5 s, saying so if it didn't fire). It
  becomes **Stop** (red), which silences it.
- **Throttle**: 0 closed to 1 open.
- **Dyno**: a dynamometer holds the crank at **Dyno rpm**, at full throttle,
  so the engine runs there under its full load; the throttle slider gives way
  to the dyno's speed. Started with the dyno, the dyno takes the crank once it
  turns (or after 1.5 s of cranking), so an engine its starter can't fire
  (the aero V12) runs on the dyno.
- **Volume** is the listening level.
- After a change that takes a fresh engine (the design, or engine tuning in
  Sound), **Restart** appears over the controls.

The Bench plays at the synthesizer's full volume, as engine-sim does. Its
leveler is engine-sim's: it aims peaks at 30000, but its gain rises between
firing pulses faster than it comes back down on the next one, so engine-sim's
pulses overshoot and clip. The package adds a ceiling: where a pulse would
pass 32000, the leveler cuts its gain at once to fit, so the output never
clips, and every other sample is engine-sim's. The **Volume** slider is the
listening level on top.

### Design

Every preset carries its design: the engine's layout, firing and parts, read
from its script, so a copy of any preset is designed here. Every value has a slider, with a range wider than the presets span
(bore 20 to 200 mm, say), and a field to type in; a value typed past the
range is kept at its end. Until you change the design, the copy plays its
preset exactly.

- **Layout**: inline, V (with the bank angle) or flat; the cylinder count;
  which side the first bank sits on (**Banks mirrored**); on a V, **Split
  crank pins** (a pin for each cylinder, not one shared by each pair across
  the banks). The firing order, from the common orders that fit, or your own
  (cylinders numbered bank by bank: on two banks the first bank is 1 to
  half); or **Odd firing**, with each cylinder's firing angle in the cycle
  (V-twins, odd-fire V6s).
- **Bottom end**: bore, stroke, rod length, compression height, deck
  clearance, piston and rod mass.
- **Camshafts**: each lobe's duration at 50 thou and lift, the lobe centres
  and the cam advance.
- **Head and intake**: chamber volume, throttle flow, runner length, plenum
  volume.
- **Exhausts**: each system's length, header length, outlet flow, loudness
  and the exhaust filter it sounds through, from the ladder above.
- **Forced induction**: none, a turbocharger, or a Roots, twin-screw or
  centrifugal supercharger, feeding every intake. A turbo: single, twin or
  sequential, its boost (the wastegate's set point, above the intake's air)
  and how it is held (electronic, or a spring that lets it creep), turbine
  A/R and rotor inertia (spool and lag), twin scroll, ball bearings, an
  external wastegate. A supercharger: its drive ratio, and a Roots or
  twin-screw blower's displacement and bypass valve. A turbo or centrifugal
  compressor's wheel and flow; the blow-off valve (to the air, recirculating,
  or none: the charge stays in the pipes; the model's surge flutter needs
  reverse flow through the compressor, which a lift hardly makes); the
  intercooler, the charge
  volume, and the whistle's pitch. Changing the type starts it from that
  type's defaults.
- **Limits**: redline, rev limit and the limiter's cut.
- **Advanced parts…**: every other value the engine is built from, as
  engine scripts set them: the starter, dyno range and simulation frequency;
  the throttle (direct, or a governor); crank and
  flywheel masses and inertia, rod inertia; per cylinder, its blow-by,
  loudness, header pipe, intake and exhaust; how deep the cylinders are
  drawn; the heads' runners and their intake and exhaust flow against valve
  lift, and a head of its own for the second bank; the lobes' shape;
  variable valve lift (a high-lift pair of lobes and when the valvetrain
  switches to it); the intakes' and exhausts' remaining values (an intake's
  air pressure and temperature: altitude); the forced induction's compressor
  and turbine efficiencies and shaft speed limit; the timing
  curve; the fuel; the vehicle and gearbox (which the dyno doesn't use).

The crank, the cam timing and the ignition follow from the layout and the
firing: the crank pins and which cylinders share them, the lobe
centrelines, the spark plug angles and the deck height. The section shows
what the design makes (displacement, compression, bore/stroke, rod/stroke,
mean piston speed at the redline, the crank pins and the firing interval),
and what keeps it from building, such as a V's firing order that would need a
shared crank pin at two angles. A design with a problem keeps playing as it
was before that change. Changes go through Undo (a slider drag is one step),
reach the engine's asset when you save, and play on Restart. Each value has this
one place; the synthesizer's mix is in Sound.

A preset's design builds the engine its script builds, as it runs: the same
parts and values, and the same timing (when each cylinder reaches top dead
centre, its spark and its cam lobes from there). Scripts write angles in
forms that round differently, so once changed, a design's engine can sit a
rounding step off its preset's in values engine-sim reads the same way.

### The views

What the live engine does, beside the sections:

- **Front view**: the engine seen from the front, drawn from its description:
  banks, heads, valves (their lift drawn twice its size), pistons, rods and
  crank. It replays the live engine's captured cycles slowed 1:40: the last
  whole engine cycle, step by step, then the latest one again. So at any revs
  you watch a real cycle of the simulation, not an animation. A chamber glows
  as its charge burns. Cylinders behind one another in a bank are drawn
  stepped back.
- **Rev counter**: thousands of rpm, red from the redline.
- **Dyno**: torque and power while the dyno is on: the dyno's torque on the
  crank averaged over an engine cycle, which is the engine's torque while the
  dyno holds it.
- **Firing order**: a lamp per cylinder in the order its spark plugs fire,
  lit with the replay, so a misfire or a limiter cut shows. Cylinders are
  numbered as the engine's description lists them, bank by bank, which can
  differ from a manufacturer's numbering.
- **Rendering**: **Render cost** (seconds of computing per second of sound),
  **Buffered**, **Underrun** (sound the simulation didn't deliver in time),
  and whether the engine runs Burst-compiled.
- **Exhaust signal**: each exhaust's input to the synthesizer, the pressure
  pulses the sound is made from, over the last 60 ms in real time.
- **Output spectrum**: the sound playing, from 20 Hz to 20 kHz in dB of full
  scale; the orange line marks the firing frequency (rpm × cylinders / 120).
- **Dyno pull**: torque and power against rpm. **Sweep** runs a pull on the
  live engine (ignition on) the way engine-sim runs one: full throttle, the
  dyno braking the engine to a speed that starts at the engine's lowest dyno
  speed and rises 500 rpm per second while the engine makes torque against it,
  to the redline (or to the rev limiter, if that is lower). The Run controls wait while it runs, and **Stop** ends it
  early. The torque read is the dyno's, as in engine-sim: what the engine makes
  less what speeding up its rotating parts takes, a few percent. The pull
  before stays on the graph, faint, to compare a change of tuning; the peaks
  are written in, and a marker shows the live engine (with a dot at its torque
  while the dyno is on).

Recording what the views show changes no sample of the sound and adds about
2 % to the render cost.

### Sound

The synthesizer's mix, which plays at once. Everything else about the engine
is in Design. Each value has a tick: ticked, it replaces the engine's own
value and is kept on the engine (with Undo); unticked, the engine's own
applies (shown greyed). **Reset all to the engine's own** clears the tuning.
Everything that plays or renders the engine uses it: Live,
`EngineAudioSource` and the render API.

**Mixer**, applied while the engine runs:

| Value | What it does |
|---|---|
| Filter amount | how much of the sound goes through the exhaust filter, the rest being the raw exhaust pulses (engine-sim's convolution level) |
| High freq. gain | mixes in the pulses' rate of change: brighter, raspier |
| Low freq. noise | low-passed noise modulating the pulses: rougher, breathier |
| Low freq. noise cutoff | that noise's low-pass cutoff, in Hz |
| High freq. noise | random jitter in reading the simulation's samples |

**Engine, on Restart**, for an engine without a design, whose
values can't be changed in Design: applied when the engine is built, on top
of its description. An engine that has a design but still holds such tuning
(tuned here before it had one) shows it too, since it replaces the design's
values; untick it to edit them in Design.

- **Simulation frequency**: steps per second. It is also the rate the exhaust
  is sampled at, so before the filter the sound has nothing above half of it.
  The render cost grows in proportion.
- Per exhaust system: its **Filter** (one of the exhaust filters; a newly
  picked one starts at the volume that passes as much energy as the current
  one, and the engine's own goes back to its script's volume),
  **Filter volume**, **Pipe volume**, **Pipe length**, **Collector area**,
  **Outlet flow** and **Primary flow** (in cfm, as engine-sim's scripts write
  `k_carb`), **Primary tube length** and **Velocity decay**.
- Per cylinder: its **header** length and **loudness**.

The Bench tells you when a change waits for Restart.

### Export

The engine as tuned on the Bench, unsaved changes included, out of the Bench.
The section is a column of cards, one per way out:

- **Nx Vehicle Sound System** (recommended): Nexstead's vehicle sound package
  for Unity. It renders the engine across its revs, on and off the throttle,
  and builds a sound bank from the takes in one step; in a game, a component
  on each vehicle plays the bank from the vehicle's revs and throttle. The
  card says whether it is installed. **Open Export bank** shows the section
  it adds to the Bench; when it is not installed, **View on the Asset Store**
  opens its store page.
- **WAV clips** (included): one **Export WAV clips** renders three takes as
  44.1 kHz WAV files at the Bench's level, named after the engine:
  - **Free rev**: 1 s of idle, full throttle to the rev limiter, held there
    half a second (a governed engine stops at its governor, after 4 s at
    most), then the throttle shut and 3 s of idle.
  - **Start**: from rest, the cranking, the catch and 2.5 s settling to idle.
  - **Stop**: from that idle, the ignition off, until the engine is at rest.

  Pick the **Folder** (in the project's Assets the files are imported) and
  the **Seed**: the same engine and seed write the same samples, another seed
  another take. Each take starts the engine for real: the starter cranks it
  with the throttle a little open until it catches (up to 6 s), then lets go.
  When the export is done, the card lists the files, the idle and top revs,
  and a button that shows them in the file manager.
- **More audio systems**: RES2 (Realistic Engine Sounds 2), FMOD Studio and
  Wwise exports, coming soon.

Each clip is `<engine> - <clip>.wav` (16-bit). An engine with a turbocharger
or supercharger also gets `<engine> - <clip> (engine).wav` and
`<engine> - <clip> (forced induction).wav`: the engine and the turbo or
supercharger apart, at the engine's level, so they add up (32-bit float, as
the forced induction's can pass full scale on an overrun). The main file is
the two as the Bench plays them, leveled together.

### Sections from other packages

Packages built on the Bench add their own sections, as tabs after Export, for
example to export the engine's sound in their own format. See Extending the
Bench.

## Playing an engine in Play Mode

Add **Engine Audio Source** next to an `AudioSource` and give it an engine.
It plays in the Editor's Play Mode (in Edit Mode too with `playInEditMode`;
see An Editor tool). It starts
with the ignition off: drive it from code. The Engine Demo sample's
`EngineDemo.cs` is a complete example, with keyboard input for either input
system. In short:

```csharp
using Nexstead.EngineBench;
using UnityEngine;

public sealed class EngineStarter : MonoBehaviour
{
    [SerializeField] EngineAudioSource source;
    [SerializeField, Range(0f, 1f)] float throttle;
    float crankLeft = 1.5f;

    void Start()
    {
        source.Ignition = true;
        source.Starter = true;
    }

    void Update()
    {
        // Crank for 1.5 s at a little throttle, as the demo does; some engines need it to fire.
        if (crankLeft > 0f && (crankLeft -= Time.deltaTime) <= 0f) source.Starter = false;
        source.Throttle = crankLeft > 0f ? Mathf.Max(throttle, 0.15f) : throttle;
    }
}
```

Controls: `Throttle`, `Ignition`, `Starter`, `DynoEnabled`, `DynoHold`,
`DynoRpm`. Readouts: `Rpm`, `RenderCost`, `BufferedSeconds`,
`UnderrunSeconds`, `Failure`. `ApplyTunedMix` sends the
engine's mixer tuning to the running engine; `Restart` builds it again.

The engine runs on its own thread (`EngineSoundStream`) and the audio thread
only copies samples out, so it never blocks. The synthesizer runs at 44.1 kHz;
the output is resampled once to the device's rate.

A simulated engine costs about half a CPU core Burst-compiled: fine for
listening in the Editor, too much to run in a game, which is one reason the
package stays out of player builds.

## The render API

Renders an engine's sound under your own controls, for tools that build on
the Bench, such as exporters:

```csharp
using Nexstead.EngineBench;

// On the main thread: the engine as tuned, as plain data.
EngineRenderSource source = EngineRenderSource.Capture(engineAsset);

// On any thread: a renderer of its own, from rest.
using var renderer = new EngineRenderer(source, seed: 1);
var crank = new EngineControls { ignition = true, starter = true, throttle = 0.15 };
renderer.Run(crank, renderer.BlocksFor(1.5), null, 0, out _);      // start; nothing kept

var held = new EngineControls { ignition = true, throttle = 1, dyno = true, dynoHold = true, dynoRpm = 3000 };
int blocks = renderer.BlocksFor(3.0);
var samples = new float[blocks * renderer.MaxBlockSamples];
int count = renderer.Run(held, blocks, samples, 0, out RpmSummary rpm);
```

- `EngineRenderSource.Capture(engine)`: the tuned description, impulse
  responses and mix, read on the main thread; immutable, so renderers on any
  thread can share it. Its figures: `RedlineRpm`, `RevLimitRpm`,
  `StarterRpm`, `Cylinders`, `Name`, and `ForcedInductionKind`.
  `WithoutForcedInduction()` is the same engine with its turbocharger or
  supercharger taken off (the intake breathes the atmosphere), for an
  exporter's take of the engine without it; an engine without one returns its
  own source.
- `EngineRenderer(source, seed, volume)`: renders 44.1 kHz mono samples on the
  16-bit scale (a 16-bit value / 32768), Burst-compiled whenever Burst
  compilation is on (`EngineRenderer.BurstCompiled`). The default volume is
  1, engine-sim's and the Bench's: the leveled peaks sit at 30000 and a pulse
  the leveler overshoots is held at its ceiling (32000). The volume comes after
  the leveler: lower renders the same sound, quieter. The same source, seed and controls render the same samples.
- `RenderBlock(controls, output)` renders one block (`BlockSeconds`, 2.5 ms);
  `Run(controls, blocks, output, offset, out rpm)` renders several under fixed
  controls, or discards them when `output` is null, and reports the revs over
  them (`mean`, `min`, `max`, `limiterCut`). `Rpm` and `RevLimiterCutting`
  read the engine after the last block.
- **Forced induction**: an engine with a turbocharger or supercharger makes
  its sounds (the whistle or rotor whine, blow-off, surge and an external
  wastegate's rasp) as an output of their own.
  `RenderBlock(controls, engine, forcedInduction)` and
  `Run(controls, blocks, engine, forcedInduction, offset, out rpm)` render
  the engine's sound and the forced induction's apart. Both go through the
  gain the leveler (and its ceiling) takes from the engine's sound alone, so
  the engine output doesn't change with the forced induction's sounds; the
  forced induction output is floats, neither rounded nor clamped, and can
  pass ±1 (a blow-off on a quiet overrun, where that gain is high): scale it
  before writing it to 16 bits. The one-buffer calls play the two leveled
  together, as engine-sim's community fork does, under the same ceiling.
  Without forced induction the second output is silence and the one-buffer
  output is the engine's. `ForcedInductionKind`
  (None, Turbocharger, Roots, TwinScrew, Centrifugal) and `BlowoffValveKind`
  (Atmospheric, Recirculating, None) say what the engine has;
  `ForcedInduction` reads its state after the last block: `shaftHz`,
  `toneHz`, `boostKpa` (above the intake's air), and the levels its sounds
  follow, 0 to 1 (`whineLevel`, `blowoffLevel`, `flowLevel`, `surgeLevel`,
  `wastegateNoise`). `ToneHz(shaftHz)` is the tone at a shaft speed: a
  whistle's is 400 Hz plus 2.4 (turbo) or 2.0 (centrifugal) times the shaft
  speed, so not proportional to it; a Roots or twin-screw whine is its lobe
  frequency (6 or 5 times the rotor speed). The forced induction's noise is
  seeded from the renderer's seed.
- `EngineControls`: `throttle` (0 to 1), `ignition`, `starter`, `dyno`,
  `dynoHold` and `dynoRpm`. Engage the dyno on a turning engine and ramp
  `dynoRpm` from the engine's revs to the target, block by block: from rest
  it drives the crank backwards. Held, the crank runs at `dynoRpm` exactly.
- Dispose a renderer to free its memory. Use each from one thread at a time.

## Extending the Bench

Another package can add a section to the Bench window: derive a public class
from `BenchExtension`, with a public parameterless constructor, in an Editor
assembly that references `Nexstead.EngineBench.Editor`. The Bench finds it,
makes one instance per window, builds its content once (UI Toolkit), and
shows it as a tab in its top bar.

```csharp
using Nexstead.EngineBench;
using Nexstead.EngineBench.Editor;
using UnityEngine.UIElements;

public sealed class MyExporter : BenchExtension
{
    public override string Title => "My exporter";

    public override VisualElement CreateGUI(BenchContext context)
    {
        var export = new Button { text = "Export" };
        export.clicked += () =>
        {
            EngineRenderSource source = EngineRenderSource.Capture(context.Engine);
            // Render on a worker thread; follow it with export.schedule.Execute(...).Every(100).
        };
        export.SetEnabled(context.Engine != null);
        context.EngineChanged += engine => export.SetEnabled(engine != null);
        return export;
    }
}
```

- `Title` names the tab; `Order` sorts the tabs (then the type name).
- `CreateGUI(context)` builds the section once. `context.Engine` is the
  engine on the Bench (null when none is) and `context.EngineChanged` tells
  you when that changes. It is always the Bench's in-memory copy of the
  engine picked (`EnginePresets.IsTemporary`), with the user's unsaved
  changes, and has no asset path.
- For work in progress, use your element's scheduler
  (`schedule.Execute(...).Every(...)`).
- `Dispose()` runs when the window closes or the scripts reload: stop any
  work there.
- `EngineBenchWindow.OpenOn(packageName)` opens the Bench window on the tab
  your package adds (the one whose extension's assembly belongs to the
  package `packageName`), for a button of your own that leads to it.

To compile your extension only when the Bench is installed, give its
assembly definition a version define and constrain it:

```json
"versionDefines": [
    { "name": "com.nexstead.enginebench", "expression": "[1.0.0,2.0.0)", "define": "HAS_NX_ENGINE_BENCH" }
],
"defineConstraints": [ "HAS_NX_ENGINE_BENCH" ]
```

## Compatibility

The render API (`EngineRenderSource`, `EngineRenderer`, `EngineControls`,
`RpmSummary`), the extension API (`BenchExtension`, `BenchContext`,
`BenchExtensions`, `EngineBenchWindow.OpenOn`), and `EngineAsset`, `ImpulseResponseAsset`,
`EngineSoundTuning`, `EngineAudioSource` and `EnginePresets` follow semantic
versioning: they change incompatibly only in a new major version. The
simulation's own types (the `Simulation`, `Scs` and `Dense` namespaces,
`EngineSoundGenerator`, `EngineSoundStream`) are public for the port's tools
and may change in any version.

## Accuracy

The simulation is a class-by-class C# port of engine-sim, and checked against
the original C++ code built headless: on its golden runs (start and idle, free
rev, dyno sweep on seven engines), the C# simulation reproduces every step's
state bit for bit, and with it what it feeds the synthesizer. Burst-compiled,
Burst's own maths functions change the last bits.

The sound is engine-sim's, sample for sample, except where engine-sim's
leveler overshoots past 32000: there the package's ceiling holds the pulse at
32000 where engine-sim's clips (see Running the engine). The synthesizer's
default convolution is partitioned (FFT) rather than engine-sim's direct
form: much cheaper, and at least as accurate.

## Limitations

- No gear changes or clutch control while running Burst-compiled; the
  transmission stays as built. The Bench and the render API hold revs with
  the dyno, in neutral, so they don't need them.
- Engines are built from engine descriptions: the arguments of the calls that
  construct an engine-sim script's parts, exported by the C++ reference tool in
  the package's repository. engine-sim's `.mr` script language is not part of
  the package.
- The golden runs were recorded on macOS (arm64). On another platform the
  system maths library differs and the last bits may too.

## Credits and licence

Nx Engine Bench is MIT-licensed. It is based on engine-sim by Ange Yaghi
(MIT), including its simulation, its library engines and its impulse
responses, and on his simple-2d-constraint-solver (MIT); see
`Third Party Notices.md`. Nx Engine Bench is not affiliated with or endorsed
by Ange Yaghi or the engine-sim project.
