# Porting notes

How the C# port of engine-sim is written and checked. Read this before
changing anything under `Packages/com.nexstead.enginebench/Runtime`: the
simulation must stay bit-identical to the C++ reference, and the parity
tests prove it. Both are in the private reference suite, which checks out at
`Assets/Reference`.

Namespaces: `Nexstead.EngineBench.Simulation` (the engine model, the
synthesizer, engine descriptions), `Nexstead.EngineBench.Scs` (the constraint
solver), `Nexstead.EngineBench.Dense` (the dense core), `Nexstead.EngineBench`
(playback, the render API and the Unity layer), `Nexstead.EngineBench.Design`
(the script layer: engine-sim's script nodes and part library),
`Nexstead.EngineBench.Editor` (the Bench and its extension hook).

## Port conventions

The simulation core mirrors upstream class by class, so a file reads side
by side with its `.h`/`.cpp`. The managed port is the oracle the dense core
is checked against, so it follows upstream's arithmetic exactly, quirks
included.

- **One file per upstream class**, same type name (`Engine`, `GasSystem`,
  `CombustionChamber`, `RigidBody`…), nested types too
  (`GasSystem.Mix`, `Engine.Parameters`).
- **Methods** take upstream's name in PascalCase: `getVolume()` →
  `GetVolume()`, `pressure()` → `Pressure()`, `fast_f()` → `FastF()`.
- **Fields** drop `m_` and stay camelCase whatever their access:
  `m_system` → `system`, `m_body` → `body`. Upstream names without `m_`
  (`p_x`, `v_theta`, `F_t`, `n_mol`, `E_k`) are kept exactly. Access is as
  upstream (public stays public). A C# keyword gets `@` (`@throw`).
- **`Parameters` structs** become nested classes with upstream's field
  names, casing and default values.
- **Value or reference:** a type whose address upstream takes or shares
  (`RigidBody`, `GasSystem`, parts, constraints) is a class. A type copied
  by value (`GasSystem.Mix`) is a struct; upstream's default
  constructor values come from a named static (`GasSystem.Mix.Default`),
  never from `default`.
- **Pointers** become references; out-pointers become `out` parameters.
- **Arithmetic is upstream's, operation for operation.** Same order, same
  constants (`Constants.pi` is upstream's `3.14159265359`), double where
  upstream uses double, float where it uses float. No fused multiply-add,
  no algebraic shortcuts. Use the helpers in `Utilities` where C++ and C#
  differ:
  - `std::min`/`std::max` → `Utilities.StdMin`/`StdMax`
    (`(b < a) ? b : a`), not `Math.Min`/`Max`;
  - `std::fmin`/`fmax` → `Utilities.Fmin`/`Fmax`;
  - `std::round` → `Utilities.Round` (half away from zero, exactly;
    `Math.Round` rounds half to even). It is built on `Math.Floor`, so Burst
    compiles it;
  - `std::fmod` → `%` on doubles (same truncated remainder);
  - `std::sqrt`, `pow`, `exp`, `sin`, `cos`, `acos`, `tanh`, `abs`,
    `floor`, `ceil` → the `Math` method of the same name.
- **Randomness** comes only from `DeterministicRandom` (the reference's patch),
  one stream per consumer, as in the reference.
- **No memory management code.** Destructors and `destroy()` methods that
  only free memory are not ported; ones that reset state are. Allocation
  happens where upstream allocates (initialize), never per step.
- **No asserts** (upstream builds with `NDEBUG`).
- **Not ported:** rendering and UI, the script compiler, frame pacing
  (`startFrame`/`endFrame`, latency, the audio thread: the playback layer's
  job, `EngineSoundGenerator` and `EngineSoundStream`), the generic rigid body
  system and its solvers.
- **Each file starts with** `// Port of engine-sim <upstream files>.` (or
  `simple-2d-constraint-solver`) and the licence pointer to
  `Third Party Notices.md`. Files written for the port say so instead.

**Where the port is not a literal transliteration** (none changes a computed
value; the parity tests prove it):
- `GasSystem.mix()` is `GetMix()` (C# can't name a member like its nested
  type); upstream's `= {}` default `Mix` arguments are overloads passing
  `Mix.Default`. `GasSystem.State`, `CombustionChamber.FlameEvent` and
  `FrictionModelParams` are classes (held by value upstream, never copied).
- `Simulator` holds the engine, vehicle and transmission once (upstream's
  `PistonEngineSimulator` redeclares them); `Engine` is sealed with
  non-virtual methods (upstream has no subclass); `LoadSimulation` is
  virtual instead of hidden.
- The synthesizer keeps upstream's state, buffering and arithmetic behind
  a single-threaded API (`WriteInput` per step, then `EndInputBlock`,
  `RenderAudio`, `ReadAudioOutput`); upstream's mutex, condition variable
  and thread are left to the runtime. `Simulator.synthesizer()` is
  `GetSynthesizer()`.
- `ButterworthLowPassFilter<float>` and `<double>` are two classes
  (`…Float`, `…Double`): C# 9 has no generic arithmetic. The float one
  uses `MathF.Tan`, as C++ calls `tanf`.
- `InitializeImpulseResponse` throws on an impulse response with no sample
  louder than 100 (upstream would build a zero-length filter and crash).
- The solver includes the small NSV integrator (`OdeSolver`,
  `NsvOdeSolver`) the optimised system uses, and reuses one
  `Constraint.Output` (upstream: an uninitialised stack copy per call that
  every constraint fully writes).
- Dropped as dead: wall-clock profiling counters, never-read locals and
  fields (`Transmission.m_newGear`, `Fuel.m_name`, `PistonEngineSimulator::m_derivativeFilter`,
  unused locals in `GasSystem::flow` and `Intake::process`), the protected
  `IgnitionModule::getPlug`, the solver's `fastRowSwap` (Gaussian
  elimination only).
- `Intake.flowRate` accumulates per step; nothing resets or scales it, as
  nothing paces frames. It is display data, read by nothing.
- Managed classes carry internal read accessors (`GetX()`, or properties)
  for the dense core's snapshot. They change no behaviour.

Upstream quirks kept for parity are listed in the reference suite's README
(Notes for the port), including the synthesizer's: every channel's air
noise goes through channel 0's low-pass.

## The dense core and Burst

`Runtime/Dense/` is the per-step code a second time, in a form Burst can
compile: an engine's whole state is one `DenseEngine` (parts, solver,
simulator, synthesizer) in unmanaged memory, and the steps are static
methods over it (`PartsOps`, `SolverOps`, `SynthOps`, `SimulatorOps`).
There are no managed objects or exceptions in per-step code, virtual calls
become an int kind and a switch, and nothing allocates per step. The rules
are at the top of `Dense/DenseEngine.cs`.

- **A snapshot, not a second builder.** `DenseCore` copies a managed
  `PistonEngineSimulator` once it is loaded, has its impulse responses and is
  seeded, so all of upstream's set-up stays in the managed port. It refuses a
  throttle or valvetrain type it has no mirror for, and a synthesizer channel
  with no impulse response.
- **Same arithmetic.** Operation for operation as the managed port, so run
  as managed code it is bit-identical to it: every golden run's trace under
  .NET (the reference suite's `ParityRun`, both cores) and the V8's start
  and idle in the Editor (`GoldenRunTests`),
  and the package's tests render the same samples and stems on both cores.
- **The generator.** `EngineSoundGenerator.Core` picks `Dense` (the default)
  or `Managed`. A dense block is rendered by a `DenseBlockRenderer`:
  `SimulatorOps.RenderBlock`, or `BurstBlockRenderer.Renderer`, its Burst direct
  call (`FloatMode.Strict`, `FloatPrecision.Standard`, compiled on first
  use). With the dense core, the managed simulator stays at its loaded
  state; read `Rpm` from the generator.
- **Burst-compiled, it is close, not identical.** Burst brings its own
  maths functions, so the run drifts from the managed arithmetic in the last
  bits. No test covers this: the tests check the managed arithmetic, which is
  the same whatever the machine's Burst setting.
- **Not in the dense core yet:** gear changes and clutch control (the
  snapshot copies the transmission as it is; `ScenarioRunner` refuses those
  scenario steps in dense mode). Bookkeeping that no step reads is dropped:
  `Intake.flowRate` and its total fuel injected, a chamber's peak
  temperature, burnt fuel and lit flag, and a flame's lit counters.

- **The Bench's capture.** With capture on (the live Bench only), each step
  ends by recording the engine's state for the visuals: the crank's angle in
  the engine cycle, piston travel, rod poses, valve lifts, ignitions and the exhaust signal
  (`CaptureOps.Record`, the same from the managed port's objects). It only
  reads, so the simulation and the sound are unchanged
  (`CaptureLeavesTheSoundUnchanged`), and both cores record the same values
  (`BothCoresCaptureAlike`). Ignitions come from a counter each chamber keeps
  (`CombustionChamber.ignitions`), display bookkeeping that no step reads.

The parity tests step the dense core as managed code (`SimulatorOps`
directly), so they check the same arithmetic whatever the machine's Burst
setting. `EngineRenderer`, and through it `EngineAudioSource` and the Bench,
renders through `BurstBlockRenderer.Renderer`, Burst-compiled whenever Burst
compilation is on.

## The synthesizer's convolution

`Synthesizer.convolutionMode` picks the filter the next
`InitializeImpulseResponse` builds:
- **`Partitioned`** (the default): `PartitionedConvolutionFilter`. The first
  128 taps are convolved directly; the rest in 128-tap partitions, in the
  frequency domain (uniformly partitioned overlap-save, 256-point float
  FFT), computed once per block from blocks already complete. Same
  per-sample interface, no added latency. Against an exact double-precision
  convolution it is never less accurate than the direct form, and up to
  18 times more accurate on long responses (worst 9.1e-7 of the output
  peak, against 7.6e-6).
- **`Direct`**: upstream's `ConvolutionFilter`, at up to 10,000
  multiply-adds per sample per channel.

The synthesizer runs at upstream's 44.1 kHz, so its per-sample time
constants (leveler, filters) stay as tuned. A device at another rate gets
the output resampled once, at the end of the chain (`Resampler`).

## The script layer

engine-sim builds engines from scripts (`.mr`, in its own language). The
package builds them from descriptions instead, but a designer needs what the
scripts do: `Runtime/Design` ports the script library so engines can be
written in C# and described exactly as engine-sim describes them.

- **What is ported**: the units and constants (`ScriptUnits`, from
  `es/constants`), the script nodes with their defaults (`ScriptNodes.cs`,
  `EngineNode`, from `es/objects/objects.mr`, `es/actions/actions.mr` and the
  C++ nodes in `scripting/include`), the helpers (`ScriptFunctions`: the
  harmonic cam lobe generator from `scripting/include/actions.h`, `k_carb`,
  `k_28inH2O`, `circle_area`, the moments of inertia), the impulse response
  library (the recordings the package ships: `real_engine_0`, `_1` and
  `sharp_0` are left out, near-duplicates or a copy of others) and the part
  library (`PartLibrary`, from `es/part-library/parts`) under generic names.
  The package's own exhaust filters (`ExhaustFilters`) name and level-match
  those recordings, with two derived ones.
- **Values**: every unit and default is computed in the script library's own
  order, so a transcribed script produces the same doubles. The script
  library's `psi` is built from mass pounds (`lb`), unlike the C++ units;
  it is kept as the scripts have it. The harmonic generator and the
  application defaults use the C++ units, as upstream does.
- **`EngineNode.Describe`** follows `EngineNode::generate` and the reference
  exporter: parts in engine-sim's order (intakes and exhausts in the order
  the cylinders first use them), each function written once per script
  node, numbered by first use (heads bank by bank with their camshafts'
  lobes, then the timing curve, the fuel's turbulence curve, the chambers'),
  spark plugs at their wires' angles (the last connection wins), and the
  chambers' turbulence curve and crankcase pressure that engine-sim makes in
  C++. A script that sets no vehicle or transmission gets the application's
  defaults (`VehicleNode.ApplicationDefault`, `TransmissionNode.ApplicationDefault`).
- **Impulse responses**: every engine-sim export has one entry per exhaust
  system, in exhaust order, even where two exhausts name the same library
  entry, so `Describe` writes them that way.
- **Left out**: radial engines. The port has no master and slave rods
  (upstream's journals on a rod, `add_slave_journal`), and the reference
  exporter refuses an engine with them.
- **Checks**: `ScriptNodeTests` builds two presets (`boxer-4-rally-wagon`,
  `v8-big-block-truck`) from line-by-line transcriptions of their scripts and
  compares every value with the preset, bit for bit. The dev project's
  `ReferenceTranscriptionTests` does the same for a third script, the
  reference engine `inline-4-superbike` was first imported from, against its
  reference description (that preset is designed on the Bench now).

### Designs and the generator

`EngineDesign` holds what a designer chooses (layout, cylinder count,
firing, the parts' values); `EngineGenerator` derives what engine scripts
write by hand and builds the engine with the script nodes:

- banks upright (inline), at half the bank angle either side (V, first bank
  to the right) or level (flat, first bank to the left), the other way round
  when mirrored; top dead centre along the first bank;
- each cylinder fires at its firing angle: by default its place in the
  firing order, `k/N` of the cycle, or its own angle (odd firing); its spark
  plug at that angle, its lobes at the lobe centres from it, and a second,
  high-lift pair of lobes when the valvetrain has variable lift;
- journals one a cylinder (inline, flat, a V with split crank pins) or one a
  pair across the banks (V), in cylinder order, at `c + b − b0` (firing
  angle, bank, first bank);
- one head for all banks or one each;
- deck height half the stroke plus the rod plus the compression height plus
  the deck clearance.

`DesignExtraction` reads a design back out of a description: the layout from
the banks and journals, each cylinder's firing from where its piston reaches
top dead centre (the crank's geometry; of the two, the one nearest its
spark), the lobe centres from there, the cam lobes refitted to the harmonic
generator's shape, and every value into the design's units. Some scripts
time the spark a fixed lead from top dead centre (the 2.8 L inline-6, 30°);
that lead goes into the timing curve and the lobe centres, and the cycle is
counted from cylinder 1's top dead centre. Every preset reads as a design; the Bench copies a preset with its design, so every one
of those is designable. An unedited copy keeps the preset's description;
the first change builds it from the design.

Scripts write the same angles in different forms, and the forms round
differently: `(k/N)·cycle` equals `k·(cycle/N)` exactly when N is a power of
two, but `(k·90)·deg` does not (k = 3, 6, 7 are a rounding step off), and
some scripts give journals in radians (`-3π/2`) or number them their own
way. One generator uses one form, so a design rebuilds its preset as it
runs, not bit for bit. `DesignGeneratorTests` checks every preset that way:
the same parts and values (within 1e-9 relative; functions by what they
hold, so a script sharing one or writing two alike is the same), and the
same timing as it acts (each cylinder's top dead centre after cylinder 1's,
its spark and its lobes from its own top dead centre, within 1e-6°), however
the script labels the cycle or numbers the journals. All pass; the worst
value is 1.6e-13 relative off.

### The leveler's ceiling

The leveler is upstream's: its peak falls back by 0.999 a sample and its
gain moves a tenth of the way to target / peak each sample. Its gain rises
between firing pulses faster than it falls on the next one, so at full volume
pulses overshoot past the 16-bit limit and upstream's output clips. The
package adds a ceiling inside it: where a sample would pass
`Synthesizer.OutputCeiling` (32000 at the output, after the volume), the gain
is cut at once to fit and moves on from there as upstream's does. Wherever no
pulse overshoots, the output is upstream's sample for sample; where one does,
it is held under full scale instead of clipped. The golden runs' audio clips
where upstream's overshoots, so the parity tests compare the trace (the
synthesizer's input with it), not the audio.

### Community fixes

The C++ reference is upstream plus fixes from engine-sim's community fork,
each credited in the package's Third Party Notices. The port carries them in both cores,
bit for bit with the reference: sparks kept when the crank angle wraps
(`IgnitionModule`), the crank-side rod joint's damping (`PistonEngineSimulator`), a variable-lift
valve switching cams only while closed (`VariableLiftValvetrain`, a flag a
cylinder; the dense core's `ValvetrainData` copies them), the dyno torque
sample index (`Simulator`), the Gaussian filter's out-of-range read
(`GaussianFilter`), and three values: `Camshaft` reduction ratio,
`IgnitionModule` sparks per cycle, and `Intake` atmosphere pressure and
temperature. The description format is version 2 for them; an engine asset
written at version 1 no longer builds, so rebuild it (`SetDesign` from its
design, or import the preset again). The fork's null-convolution guard has no
counterpart: the port requires an impulse response for every channel and
refuses one without. Designs carry the intake's air; a camshaft ratio other
than 0.5 or more than one spark a cycle (two-stroke timing) isn't a design
yet.

### Forced induction

The reference adds the fork's forced induction after the community fixes: the fork's forced induction as merged in its `cf96419`
(kadaxo's port of kyan0s-git's model), with the script library's psi in
pounds-force (Oblongy, `5162b27`). The port is `ForcedInduction` (a force
generator, for a supercharger's drive torque), its hooks in `ExhaustSystem`
(the outlet flows through the turbines), `Intake` (the throttle draws from
the charge air) and `GasSystem.Inject`, the simulator's step (after the
exhausts, before the intakes) and its synthesizer input, and the
synthesizer's sounds, driven at the audio rate by seven control channels
written after the exhaust channels. The dense core mirrors each:
`ForcedInductionData` and `ForcedInductionOps`, the charge air as the last
gas system, a force generator index of -1 for the drive, the control
channels as synthesizer channels with input only.

Two departures, neither changing what the simulation computes. The control
channels exist only for an engine with forced induction (the fork adds them
to every engine, where they carry zeros and make no sound). And the sounds
are an output of their own: the fork adds them to the engine's signal
before its antialiasing and the leveler; here each has its own
antialiasing, and both take the gain the leveler and its ceiling set from
the engine's signal alone, so the engine output is the same whatever the
forced induction sounds like (the forced induction output is floats,
unclamped: at that gain it can pass full scale). A third output plays the two
as the fork does: their sum through a leveler of its own, under the same
ceiling; the Bench, `EngineAudioSource` and the one-buffer render calls play
it. The sound itself, before its antialiasing, is
the fork's sample for sample: the reference writes it per output sample
(`.fi.f32`), and the reference suite's `ParityRun` compares it bit for bit (under .NET:
Unity's Mono keeps float intermediates in double precision, so the
synthesizer's float arithmetic can't match there). It follows the
engine signal's loudness, so for those runs the scenario runner convolves
with upstream's direct form (the partitioned one equals it only up to
rounding). Its noise (xorshift32) is seeded from the run's seed through its
own stream, in the reference too; the fork's is a fixed seed.

Designs hold a forced induction as the script's `turbocharger()` or
`supercharger()` makes it; a description that sets what those fix (a turbo
with a crank drive, a supercharger with a boost target) isn't a design.

The fork's model has two behaviours worth knowing. Its compressor surge (the
flutter) is reverse flow through the compressor, which a lift without a
blow-off valve hardly makes: the wheel keeps spinning faster than the
trapped charge bleeds away. And a Roots blower's boost spikes on a lift (471
kPa about 70 ms after a full-throttle lift at 6000 rpm, in the reference too)
until its bypass valve opens, which waits for the plenum to fall below 0.9
atm and then opens in 0.05 s.

Boost changes the engine's own sound as well: more cylinder pressure, the
supercharger's drive load, and the turbine taking up to 35 % off the
exhaust pulses. Render a boosted engine's engine output from the boosted
engine, not from a twin without forced induction.

## Real-time cost

Wall-clock seconds per second of sound over the dyno sweep (6 s to
redline), partitioned convolution, measured on one Apple-silicon Mac on
2026-11-07 before the package was split out. Reported, not gated. Below 1
keeps up with real time; above it the stream underruns.

`v8-pushrod-muscle` (10 kHz):

| Where | Cost |
|---|---|
| Editor, dense core Burst-compiled (batchmode) | 0.56 |
| .NET 8, dense core | 0.42 (simulation 0.40, synthesizer 0.02) |
| .NET 8, managed port | 0.5–0.6 |
| Unity's Mono 6.13, Release code, outside the Editor: managed port, dense core | 1.48, 1.34 |
| Editor (Mono), batchmode: managed port, dense core | 4.88, 3.78 |
| Editor, managed port, live in Edit Mode | 3.7–3.8 |

Burst-compiled, six library engines cost 0.43 (`v-twin-mower`, 30 kHz)
to 0.59 (`v12-fighter`, 7 kHz): `v8-pushrod-muscle` 0.56, `v8-big-block-truck`
0.56, `boxer-4-equal-length` 0.55, `inline-4-variable-lift` 0.54.

- The simulation is almost all of it: under .NET 8 the synthesizer is
  0.01–0.02 with the partitioned convolution, against 0.29–0.44 for the
  simulation. With the direct convolution the synthesizer alone costs
  0.2–0.4.
- Burst-compiled code is slower here than .NET 8 running the same dense
  core. That is not profiled yet. A likely cause is that Burst uses its own
  maths functions (`pow`, `exp`, `sin`, `cos`, called by the gas flow every
  step) instead of the system library's.
