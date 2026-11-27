# Findings: RTX 4070 (Ada), DexNom main 64bd58f, NVIDIA experimental mode

Run report for the "Experimental NVIDIA" section of the README. Nothing here is a fix: it is
measured data, a root cause for one class of dropped dispatches, and a few negative results
so nobody repeats them. No game data, shader names or hashes are included.

## Setup

| | |
| --- | --- |
| GPU | NVIDIA GeForce RTX 4070 (12 GB), Windows driver version string 32.0.16.1088 |
| CPU / RAM | Intel Core i7-13700K, 64 GB |
| OS | Windows 11 Home, build 26200 |
| Source | DexNom/VIctory `main` at 64bd58f, release build with `--features diagnostics` |
| Launch | `victory-play.exe --no-rt --console series-x`, `VICTORY_FSR_4K=0`, `VICTORY_AMBIENT_FALLBACK=1` (the README's NVIDIA recipe) |
| Scene | Prologue, first area, character idle. First-run setup done once, then startup driven by `VICTORY_KEYS` (Enter = A) |
| Logging | `VICTORY_LOG=warn,victory_gpu=info`; debug layer runs used `VICTORY_D3D12_DEBUG=1` |

The game reached the prologue and rendered. Four runs of 2 to 5 minutes each; figures below
are ranges over the steady-state log lines, not a controlled benchmark.

## 1. Frame time (steady state, prologue)

| Measure | Observed |
| --- | --- |
| Frame rate | 1.3 to 7 fps, typically 2 to 3 (average over 30 lines: 833 ms/frame) |
| Draws per frame | about 1,900 to 4,700 |
| Preparing (CPU) | about 75 to 300 ms; copies 32 to 61 ms (90 to 210 MB), textures 7 to 167 ms |
| On the GPU | about 70 to 410 ms |
| Copying back | about 100 to 370 ms, 12 to 73 MB per frame |
| Process RAM | about 21 GB |

Copy-back and CPU preparation are the largest parts of a frame, matching the README's priorities.
The card itself is mostly idle in short frames ("about 3% busy" in one sample).

## 2. Write-watch and refresh volume (copystat lines)

Per frame, from the `victory_gpu::copystat` line:

- refreshes of title memory: 6 to 20 GB
- write-watch checks: 1,400 to 3,100 askings, 6 to 14 GB checked, about 98 to 99% unchanged
- page faults: 20,000 to 90,000; guard faults: 250 to 540
- reads that fell back around an unmapped page: 9 to 42, with 1.5 to 6 MB of unmapped bytes
- texture streaming: "VRAM texture budget reached; new allocations wait", and in one sample
  2,889 texture slots deferred by the VRAM budget with 6.9 GB uploaded so far

The mostly-unchanged checking is the same cost upstream tracks as `GPU-SNAPSHOT-COST`; this is
one more measurement of it on NVIDIA.

## 3. Compute pipelines refused on a 32-lane GPU

Four to five distinct compute shaders per run are refused at `CreateComputePipelineState` with
`E_INVALIDARG` (0x80070057). Each is dropped about once per frame from the frame it is first
bound, for the rest of the run.

Cause (debug layer): "Shader wants wave lane count of 64 but maximum wave lane count for
device is 32."

These are console wave64 programs the existing 32-lane lowering cannot take. With
`VICTORY_WAVE_WHY=1` and a cold shader cache (582 diagnostic lines), the stuck programs report
combinations of:

- LDS in a group over 32 threads, with an LDS read and write in one lane-branch region (every
  stuck case)
- masks used as data, `v_mbcnt`
- `v_readlane`
- lane-constant masks (a few)

Group sizes among them: 64 (most), 128, 256, 1024. Some retries do succeed ("without skips" and
"without early-outs" reach wave 32 in dozens of cases), which is why only a handful remain.
Closing the rest needs real wave64 emulation, not a lowering tweak.

## 4. Other D3D12 debug-layer warnings seen

- `CreateDescriptorHeap: NumDescriptors 2097152, but the limit for shader visible view heaps is
  1000000` (once at start-up).
- `Load*Pipeline: This library does not have a pipeline stored with name ...` (expected on a
  cold library).

## 5. Tests on this machine

`cargo test --workspace --locked`: everything passes except two tests in `victory-shader`,
`emit::rect::tests::rectangles_are_drawn_whole` and
`emit::rect::tests::the_rectangle_stage_is_a_geometry_shader`, both with "Container part
'Pipeline State Validation' does not match expected for module." The validator picked is the
Windows SDK 10.0.26100 `dxil.dll` (101.7.2308.24). This is Marvisak/VIctory#1945 (old validator
rejects our PSV string table); the runtime does not use that validator, so this is test noise
for Windows contributors on SDK 26100. `v_med3_f32_matches_rdna2_with_nan` passes here, so
`TEST-MED3-NAN` does not reproduce on an RTX 4070.

## 6. Negative results

- **Copy-back write fallback is not hot.** `write_mapped` retrying in 64 KiB pieces after a
  failed whole-range write can be made 6.8 times faster in a synthetic 8 MiB test (2.3 ms to
  0.35 ms), but in about five minutes of the real prologue the fallback ran fewer than 200
  times (counter logged every 200). The saving would be well under 0.5 s of 300 s. Not worth a
  change on its own.
- **Device slots 0x1c8 and 0x218 "not implemented"** warnings (16,000+ and 2,000+ calls per run)
  are expected: the title falls back to its ordinary pipeline description (see
  Marvisak/VIctory#3163).

## What this does not show

- Ray tracing and FSR were off. Interior or night scenes and moving gameplay were not measured.
- The diagnostics build adds counting overhead; absolute numbers will be slightly better in a
  plain build.
- One machine, one scene, no repeat of each configuration, so no confidence intervals.

## Reproduce

1. Build with `cargo build --locked --release -p victory --features diagnostics --bins`.
2. Launch with the arguments above plus `VICTORY_KEYS=15:Enter,30:Enter,45:Enter,60:Enter,80:Enter,100:Enter`.
3. Read `logs\latest.txt`: the `fps,` line, the `copystat: copies per frame` line, and any
   `host pipeline refused` warnings.
