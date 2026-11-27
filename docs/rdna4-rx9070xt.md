# RDNA4 testing: RX 9070 XT

Results from the default renderer on an RDNA4 card, at `main` `921d8d3`.
Windows 11 Pro 10.0.26300, AMD Radeon RX 9070 XT 16 GB (driver
32.0.32029.2010), Ryzen 7 7800X3D, 64 GB RAM. Game build 1.0.2.1, Series X
profile. Release build with `--features diagnostics`, `VICTORY_GPU_TIMING=1`,
Windows' default 2 s GPU timeout. A wired DualSense was used for input. Each
run started from the same checkpoint in Chapter 1 and played about 2 to 4
minutes in the world.

## Summary

- With default settings, the runtime gets into the world but **crashes about
  70 s in, inside AMD's shader compiler** (`amdxc64.dll`) while making a
  `CS_RaytracingResolve` compute pipeline.
- **`VICTORY_RT_RESOLVE=0` avoids it.** With it set, five runs played in the
  world. Four ended normally, and one ended in a separate GPU page fault after
  about 3 minutes.
- In the world, frames take **about 350 ms (2.9 fps)**. About half of that is
  GPU work, spread over many pipelines; for much of the rest the card is idle
  while the CPU prepares the next work or waits for results.
- `VICTORY_JOIN_COPIES=1` together with `VICTORY_CARD_REHASH_RUNS=1` reduced
  copy and copy-back time by about a quarter in both runs that used them.

## Driver compiler crash

```
unhandled exception 0xc0000005 at amdxc64.dll+0x861b9f (read 0x8: nothing mapped there)
on thread "victory-pipelines-7"
```

To identify the shader, each compute program translated in the last second
before the crash was compiled on its own, one process per program:

```powershell
$env:VICTORY_TRANSLATE_ONLY = '<hash>'
victory.exe translate --pipelines --list --jobs 1 "<game>\shaders.rpf"
```

| shader | result |
|---|---|
| `501f0865fdf45dfd` (`ped_standard_lod_muv_cutout:CS_RaytracingResolve_b1139868_Wrapped`) | `0xC0000005` at the same offset |
| `b2ff2ef0d6c98dd7`, `2bf91065944bcff4`, `476446edcac7d521`, `2582aab1c7e668e5`, `e5742545a3591526`, `4c573a76d2eeba04`, `2cc6decf9a73aedd` (all `CS_RaytracingResolve`) | pipeline created |

`translate --validate` passes `501f0865fdf45dfd` with the Windows SDK's
`dxil.dll` (10.0.26100). This is the same crash, from the same shader family,
as the one PR #7 fixes for `a1266cc7391ae0d6` on an RX 9060 XT.

`VICTORY_RT_RESOLVE=0` (see `without_structures` in `pipeline/make.rs`)
leaves out every `CS_RaytracingResolve` program while the structure builder
is off, so these pipelines are never made.

## World frame time

All runs use `VICTORY_RT_RESOLVE=0`. Values are the frame-rate lines from the
last part of each run, after the world finished loading (frames with more than
4,000 draws).

| run | added switches | frame | preparing (copies) | on the GPU | copying back |
|---|---|---|---|---|---|
| baseline | none | ~350 ms | ~87 ms (~58 ms, ~235 MB) | ~170 ms | ~50 ms |
| A | `VICTORY_FILL_WORKERS=4`, `VICTORY_PARALLEL_PRELUDES=1` | 330–400 ms | ~90 ms (~53 ms, ~200 MB) | ~165–175 ms | ~42 ms |
| B | `VICTORY_JOIN_COPIES=1`, `VICTORY_CARD_REHASH_RUNS=1` | 290–395 ms, still falling when the run ended | ~80 ms (~46 ms, ~190 MB) | ~165–185 ms | ~40 ms |
| all four | both pairs | ~290 ms (best 270 ms, 3.7 fps) | ~75 ms (~45 ms, ~180 MB) | ~160 ms | ~36 ms |

**Caveat:** each configuration was run only once, and GPU time alone varied by
about 25 ms between runs depending on what was on screen. The difference in
copies and copy-back was consistent; the difference in total frame time is
within that variation, except perhaps for the run with all four switches.

What the baseline run's diagnostics show for one interval:

- **Where the frame goes.** `the card's clock per frame: nothing of ours queued 172.3 ms (the card about 48% busy by this count), 145.0 ms of that while the submitting thread was working`,
  and `the submitting thread stood waiting for the card 164.1 ms`. There were
  about 36 submissions per frame, about 29 of which hand results back to the title.
- **GPU work by command:** 93.5 ms per frame over about 1,100 kinds of command
  (`indirect` 64.9 ms in 4,568 commands, `dispatch` 18.9 ms in 3,928). The
  most expensive single kind cost 6.3 ms per frame, so no one shader dominates.
- **Shader write tracking is cheap.** Shader-written ranges compared about
  31 MB per frame (0.9 ms to find the 246 changed pieces, 2.4 ms to write them
  back). One range, `0x22360000+0xae00000`, was compared twice per frame:
  about 365 MB per frame, with about 1 MB of it written back.

## GPU page fault

One run (baseline settings) ended about 3 minutes after the world's first frame:

```
host GPU device removed after a submission of 7 lists; page fault at 0x23a3000;
no title memory copy below it
a list stopped at command 409 of 2856 (op 5)
command 409 (op 5): indirect kind 2 max 1 at 0x229ed6a0 [compute fc5e66f8f873d3bb]
```

The result was `0x887A0005`. On this card, ztrx stopped with the same pattern
(`indirect kind 2`, with compute `0452fc2d2e2fc4c3`), Zettrox/ztrx#81. The
same pattern in two forks with different programs suggests code they share
from upstream.
