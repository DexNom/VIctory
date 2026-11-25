# NVIDIA testing: RTX 4070

Results from an NVIDIA card with an Intel CPU, at `main` `8cca4d2`.
Windows 11 Pro 10.0.26200, NVIDIA GeForce RTX 4070 12 GB (driver
32.0.16.1742), Core i7-12700KF, 32 GB RAM. Series X profile. Release build
with `--features diagnostics`, run directly with
`victory.exe run <exe> --execute` (not through Setup.cmd). Each run loaded the
same checkpoint and stood still in the world with no camera movement. The
keys to reach it were scripted with `VICTORY_KEYS`.

## Summary

- The runtime boots, loads a save and plays in the world with **no crash or
  device loss** in any run below.
- In the world, frames take **about 700-1,000 ms (1-1.4 fps)**. Copying back
  is the largest share, and the card is idle most of the time.
- **Experimental RT cannot start on this GPU.** Eight compute pipelines are
  refused because they ask for 64-lane waves, and the RTX 4070 only has
  32-lane waves. At least one of them is a structure-builder stage, so the
  builder's all-or-none rule keeps it off.
- The shader warm-up translates the same set as on Radeon.
- `VICTORY_JOIN_COPIES=1` with `VICTORY_CARD_REHASH_RUNS=1` did **not**
  reduce copy-back here (315 ms against 311 ms at baseline), unlike the
  RX 9070 XT results. No switch combination beat baseline in these runs.

## Shader warm-up

`victory warm <game dir>`: 25,745 translated, 13 refused; 2,700 compute
pipelines, 31 refused; 1,208 s. The refusals I checked are pixel shaders
failing with `a hardware SGPR input is not supported`.

On a cold cache, baseline frames averaged 1,428 ms (p95 5,228 ms); warm, the
average was 718-735 ms (p95 about 1,100-1,500 ms). The warm-up mostly removed
the long stalls; the median frame barely changed.

## RT: 64-lane programs on a 32-lane GPU

With the four `--rt` switches (`VICTORY_BVH_BUILDER=1`,
`VICTORY_BVH_QUARANTINE=0`, `VICTORY_ACTIVATE_RAYS=1`,
`VICTORY_RT_RESOLVE=1`), both RT runs log:

```
the ray-tracing structure builder stays off: not all of its programs have a host pipeline, and its stages run all or none missing=5 of=53
```

From then on every builder dispatch is dropped (`a stage of the ray-tracing
structure builder, which is off`). The driver refuses these compute pipelines
with `0x80070057`:

| shader | refused |
|---|---|
| `02384a60fda6f2d0`, `2cc4c197ee29e291`, `2da4beb386509b77`, `369cbe906ea72764`, `5dc3b25986622949`, `6f225b641c5b0708` | in every configuration |
| `28918b7150a7aad5`, `caa78b8ca62bc3d6` | only with the RT switches |

A run with `VICTORY_D3D12_DEBUG=1` shows the same debug-layer error with each
of the eight refusals (8 refusals, 8 messages):

```
ID3D12Device::CreateComputeShader: Shader wants wave lane count of 64 but maximum wave lane count for device is 32. To check for support, check device caps via the CheckFeatureSupport() API id=622 severity=1
```

`docs/fork-ray-tracing.md` describes exact forms for 64-lane programs on
32-lane GPUs. These eight seem not to get them. I haven't established which
five of the eight are the missing builder programs. `BENCHMARK.md` reports
all 52 builder shaders creating pipelines on an RX 7900 XTX, which has
64-lane waves, so this would only show on 32-lane hardware. A wave32 form
for these programs, or a check of `WaveLaneCountMax` before choosing the
64-lane form, would probably unblock RT on NVIDIA.

Two other compute shaders do not translate on any card:
`39321a95223001ed` and `77360df21e12ea43`, both with `a first-lane read where
not every lane runs is not supported`.

The same debug run reported once at startup, and the run continued:

```
ID3D12Device::CreateDescriptorHeap: D3D12_DESCRIPTOR_HEAP_DESC.NumDescriptors 2097152, but the limit for shader visible view heaps is 1000000 id=645 severity=1
```

This driver accepted the heap. Others may not.

## World frame time

Warm cache. Each value is from the runtime's `victory_gpu::host` frame-rate
lines between 90 and 120 s after the world first drew 1,000 draws per frame.
Those lines average about 5 s each, so p95 is over the lines, not over single
frames.

| run | added switches | mean | median | p95 | preparing | on the GPU | copying back | GPU busy |
|---|---|---|---|---|---|---|---|---|
| baseline | none | 735 ms (1.36 fps) | 634 ms | 1,065 ms | 174 ms | 199 ms | 311 ms | 30% |
| A | `VICTORY_FILL_WORKERS=4`, `VICTORY_PARALLEL_PRELUDES=1` | 915 ms (1.09 fps) | 1,072 ms | 1,279 ms | 220 ms | 162 ms | 343 ms | 12% |
| B | `VICTORY_JOIN_COPIES=1`, `VICTORY_CARD_REHASH_RUNS=1` | 1,534 ms (0.65 fps) | 1,090 ms | 2,788 ms | 226 ms | 151 ms | 315 ms | 13% |
| A + B | all four | 925 ms (1.08 fps) | 855 ms | 1,222 ms | 292 ms | 163 ms | 400 ms | 15% |
| rt | the four `--rt` switches (builder stays off) | 1,042 ms (0.96 fps) | 993 ms | 1,600 ms | 246 ms | 156 ms | 310 ms | 14% |
| rt + A | both | 1,309 ms (0.76 fps) | 561 ms | 3,653 ms | 247 ms | 264 ms | 389 ms | 46% |

The middle columns are medians per frame. About 380-640 draws per frame were
dropped without a host pipeline in every run, and memory in use was 16-17 GB.

**Caveat:** each configuration was run once, for 30 s. A separate baseline
run (60-180 s) averaged 718 ms, close to the 735 ms above. Differences
between configurations smaller than a few hundred milliseconds are within
run-to-run variation.
