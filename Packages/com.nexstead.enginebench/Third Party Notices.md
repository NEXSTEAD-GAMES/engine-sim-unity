# Third Party Notices

This package contains third-party software components governed by the
licences indicated below. Nx Engine Bench is based on engine-sim by Ange
Yaghi; it is not affiliated with or endorsed by Ange Yaghi or the engine-sim
project.

---

Component Name: engine-sim

License Type: MIT

Source: https://github.com/ange-yaghi/engine-sim (commit `85f7c3b959a908ed5232ede4f1a4ac7eafe6b630`)

Used in this package:

- `Runtime/Simulation`, `Runtime/Synthesis`, `Runtime/Description` and
  `Runtime/Dense`: the engine simulation and the synthesizer, ported from
  engine-sim's C++ sources to C# (each file names the upstream files it ports
  or derives from), with determinism changes: seeded random streams and
  floating-point operations kept in upstream's order.
- `Presets/Engines`: engine-sim's library engines (`assets/engines`), as
  engine descriptions exported from its scripts, under generic names.
- `Presets/ImpulseResponses`: the exhaust filters. Seven are engine-sim's
  exhaust impulse responses (`es/sound-library`), as 16-bit samples:
  `1-straight-pipe` is `new/minimal_muffling_03.wav`, `2-race`
  `new/minimal_muffling_01.wav`, `3-sport`
  `archive/test_engine_16_eq_adjusted_16.wav`, `5-stock`
  `smooth/smooth_39.wav`, `7-muffled` `new/mild_exhaust.wav`, `warm`
  `new/minimal_muffling_02.wav` and `muffled-roomy`
  `new/mild_exhaust_reverb.wav`. `4-street` and `6-comfort` are derived from
  `3-sport` and `5-stock` through a one-pole low-pass.

```
Copyright 2022 AngeTheGreat (Ange Yaghi)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

---

Component Name: engine-sim community fixes

License Type: MIT (as engine-sim, the notice above)

Source: engine-sim-community-ultimate (https://github.com/kadaxo/engine-sim-community-ultimate), which gathers fixes
from engine-sim's forks; its `COMMUNITY.md` credits each one.

Used in this package: in the files of `Runtime/Simulation` that port them (each names its fix and author), their mirrors
in `Runtime/Dense`, and the new values in `Runtime/Description` and `Runtime/Design`:

- Sparks kept when the crank angle wraps, and the crank-side rod joint's damping: Samuel
  (https://github.com/Samzax/engine-sim).
- The variable valve lift switching each valve's cam only while it is closed: sillysigmabollocks
  (https://github.com/sillysigmabollocks/engine-sim-test).
- The camshaft reduction ratio and the intake's atmosphere pressure and temperature: Alexey Egorov
  (https://github.com/Evil-Spirit/engine-sim, engine-sim pull request #309).
- Sparks per cycle: karpuzikov (https://github.com/karpuzikov/engine-sim).
- The dyno torque sample index: bobsayshilol (https://github.com/bobsayshilol/engine-sim).
- The Gaussian filter's out-of-range read: danieljsinclair (https://github.com/danieljsinclair/engine-sim).

---

Component Name: engine-sim community forced induction

License Type: MIT (as engine-sim, the notice above)

Source: engine-sim-community-ultimate (https://github.com/kadaxo/engine-sim-community-ultimate), commit `cf96419`:
kadaxo's port of kyan0s-git's forced induction model (https://github.com/kyan0s-git/engine-sim, commits `27ed482d`
and `c1440107`), replacing Oblongy's (https://github.com/Oblongy/engine-sim, merged as `5162b27`), from which the
script library's psi in pounds-force and the two example engines come.

Used in this package: `Runtime/Simulation/ForcedInduction.cs` and the hooks it names, the forced induction sounds in
`Runtime/Synthesis/Synthesizer.cs`, their mirrors in `Runtime/Dense`, the script nodes in `Runtime/Design`, and the
presets "Boxer-4 2.5 L Turbo · Rally Twin-Scroll" and "V8 5.3 L Roots-Blown · Street Muscle" (the example engines).

---

Component Name: simple-2d-constraint-solver

License Type: MIT

Source: https://github.com/ange-yaghi/simple-2d-constraint-solver (commit `e009f4ff1c9c4c5874e865e893cdb62e208fb2b3`)

Used in this package: `Runtime/Scs`, the 2D rigid-body constraint solver
engine-sim runs on, ported from C++ to C#.

```
MIT License

Copyright (c) 2022 Ange Yaghi

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
