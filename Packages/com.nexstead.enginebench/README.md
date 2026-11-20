# Nx Engine Bench

A physical engine and exhaust sound simulator for the Unity Editor, based on
[engine-sim](https://github.com/ange-yaghi/engine-sim) by Ange Yaghi.

Nx Engine Bench models an engine part by part (crankshaft, rods, pistons,
valves, gas flow, combustion, exhaust) and synthesizes its sound from the
exhaust pressure. Make your own engines from the presets, tune their sound,
listen to them live in the Unity Editor, and export them as WAV files (a free
rev, a start and a stop).

- **Bench window** (Window > Nx Engine Bench): start, rev and load an engine
  live, with a dynamometer to hold any revs.
- **Sound**: the synthesizer's mix, live, saved on your engine asset.
- **27 preset engines**: engine-sim's library, singles to V12s, flat-fours,
  variable valve lift, a turbocharged flat-4, a Roots-supercharged V8, and
  three designed on the Bench: a 1000 cc superbike inline-4, a 125 cc kart
  twin and a 2.0 L turbocharged inline-4.
- **Forced induction**: turbochargers (single, twin, sequential) and Roots,
  twin-screw and centrifugal superchargers, with wastegate, blow-off and
  bypass valves and an intercooler; their whistle, whine and blow-off are an
  output of their own, to render apart from the engine.
- **Nine exhaust filters**: a ladder from straight pipe to muffled, equally
  loud, so a pick changes the tone, not the level.
- **Accurate and fast**: a class-by-class port, bit-identical to engine-sim on
  its golden runs, with a Burst-compiled core that runs in real time.
- `EngineAudioSource` to play a simulated engine in Play Mode, and an
  **Engine Demo** sample scene to try it in.
- **Built to be built on**: a render API that renders any engine under any
  controls, and extensions that add their own sections to the Bench window.

An Editor tool: it never goes into a player build. Requires Unity 6000.6.7f1 or
later; Burst is installed with it.

## Install

Package Manager (Window > Package Manager) > **+**, then either:

- **Install package from git URL…**:
  ```
  https://github.com/NEXSTEAD-GAMES/engine-sim-unity.git?path=/Packages/com.nexstead.enginebench
  ```
- **Install package from disk…**, and pick this folder's `package.json` (from
  a clone or a download of the repository).

Or get it from the Unity Asset Store.

## Try the demo

1. Package Manager > **Nx Engine Bench** > **Samples** > **Engine Demo** >
   **Import**.
2. Open `Assets/Samples/Nx Engine Bench/<version>/Engine Demo/Engine Demo.unity`
   and press **Play**.
3. The small-block V8 starts on its own; hold **Space** for full throttle.
   The line on screen points to **Window > Nx Engine Bench**, where engines
   are picked, designed and tuned.

The simulation keeps up with real time only Burst-compiled, so leave **Jobs >
Burst > Enable Compilation** on (the default).

## The Bench window

1. Open **Window > Nx Engine Bench**.
2. Pick an engine with the top bar's engine button, for example **Presets >
   V8, small-block**. It's a copy you can change at once.
3. Press the green **Start** under the engine: it turns over and fires. Move
   the **Throttle**, or tick **Dyno** to hold it at a speed.
4. Watch it run: the front view replays real engine cycles slowed 1:40, with
   the rev counter, the dyno's torque and power, the cylinders lighting in
   firing order, the exhaust signal and the spectrum of the sound. **Sweep**
   runs a full-throttle dyno pull to the redline and draws its torque and
   power curves.
5. **Design** it: every preset carries its design. Change
   the layout, cylinders, firing, bottom end, camshafts and exhausts on
   sliders; the crank, cam timing and ignition follow. **Restart** to hear
   it.
6. **Sound** sets the synthesizer's mix, live. The Bench works on a copy:
   the yellow **Save preset…** keeps the engine in your project, and **Save**
   writes later changes over it.

The manual is in [Documentation~/index.md](Documentation~/index.md).

## Licence

MIT; see [LICENSE.md](LICENSE.md). Nx Engine Bench is based on engine-sim and
simple-2d-constraint-solver by Ange Yaghi (MIT); their notices are in
[Third Party Notices.md](Third%20Party%20Notices.md). Nx Engine Bench is not
affiliated with or endorsed by Ange Yaghi or the engine-sim project.
