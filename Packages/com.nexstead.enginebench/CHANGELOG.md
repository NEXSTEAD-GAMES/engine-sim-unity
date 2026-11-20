# Changelog

All notable changes to this package are documented in this file. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
package uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-11-20

First release.

- A C# port of engine-sim's engine simulation, constraint solver and
  synthesizer, bit-identical to the original on its golden runs up to the
  synthesizer's output, with a dense core that Burst compiles for real-time
  playback. Played at engine-sim's full volume, with a ceiling in its
  leveler that holds what it would overshoot just under full scale instead
  of clipping.
- Fixes from engine-sim's community fork (engine-sim-community-ultimate):
  sparks kept when the crank angle wraps, the crank-side rod joint's
  damping, variable valve lift switching each valve
  only while it is closed, and the dyno torque sample index; and three new
  values in descriptions and scripts: a camshaft's reduction ratio and the
  sparks per cycle (two-stroke engines), and the intake's air pressure and
  temperature (altitude or boost, also in Design). Credits: Third Party
  Notices.
- Forced induction from the community fork (kyan0s-git's model):
  turbochargers (single, twin, sequential; electronic or spring wastegate,
  internal or external; twin scroll), Roots, twin-screw and centrifugal
  superchargers, blow-off and bypass valves, intercooler and compressor
  surge, in descriptions, scripts (`TurbochargerNode`, `SuperchargerNode`,
  `AddForcedInduction`) and designs (the Design section's Forced induction).
  Their sounds (whistle, rotor whine, blow-off, surge, wastegate) are an
  output of their own in the render API, at the engine's gain, with the
  forced induction's state after each block; the Bench and the one-buffer
  calls play engine and forced induction leveled together, as the fork does.
  Three presets carry them.
- The Bench window: live playback, the engine's design and the synthesizer's
  mix (each value in one place),
  with live views: a front view replaying captured engine cycles slowed down,
  a rev counter, dyno torque and power, cylinder lights in firing order, the
  exhaust signal, the output spectrum, and dyno pulls drawn as torque and
  power curves.
- The Bench works on an in-memory copy of the engine picked, a preset or one
  of yours: changes play at once and reach the project only on Save (Save
  preset keeps a preset's copy as a new engine); switching engine or closing
  the window asks before dropping unsaved changes. Start/Stop under the engine
  runs it; the dyno holds a speed at full throttle.
- Engine designs: layout (mirrored banks, split crank pins), cylinders, even
  or odd firing and the parts' values (a head a bank, variable valve lift),
  with the crank, cam timing and ignition derived. Every preset carries its
  design, read from its script (building the same engine) or designed on the
  Bench.
  The Bench's Design section edits it on sliders, with checks, and every
  other value under Advanced parts. engine-sim's script library and part
  library in C#.
- The Bench's Export section, as cards: Nx Vehicle Sound System first (what
  it does, whether it is installed, its own bank export section when it is,
  its Asset Store page when not); WAV clips, one export writing a free rev to
  the limiter, a start and a stop, seeded, with a boosted engine's engine and
  forced induction layers apart; and RES2, FMOD Studio and Wwise exports
  marked coming soon.
- `EngineAudioSource`: plays a simulated engine in Play Mode.
- The render API (`EngineRenderSource`, `EngineRenderer`, `EngineControls`)
  and Bench extensions (`BenchExtension`, with `EngineBenchWindow.OpenOn` to
  open the window on an extension's tab), for tools built on the Bench. A
  render source can take a boosted engine's turbo or supercharger off
  (`WithoutForcedInduction`).
- Editor only: the package's assemblies compile only in the Editor.
- 22 of engine-sim's 25 library engines (not its two radials: the port has
  no master and slave rods), named by engine type, the community fork's
  turbocharged flat-4 and Roots-supercharged V8, and three engines designed
  on the Bench, with their designs: a 1000 cc superbike inline-4
  (`inline-4-superbike`), a 125 cc 360° kart twin (`inline-2-kart`) and a
  late-'90s 2.0 L turbocharged inline-4 (`inline-4-turbo`), as presets.
- Nine exhaust filters: a ladder of seven from least to most muffled
  (Straight pipe, Race, Sport, Street, Stock, Comfort, Muffled) and two
  variants (Warm; Muffled, roomy), level-matched; seven are engine-sim's
  recordings, Street and Comfort derived from them.
- The Engine Demo sample: a scene that plays the small-block V8 in Play Mode
  (Space for full throttle), with a line pointing to the Bench window.
