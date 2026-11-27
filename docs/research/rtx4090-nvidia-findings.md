# NVIDIA findings: RTX 4090 (not for merge)

Issues are disabled, so this goes here. NVIDIA test results for the README's
"Experimental NVIDIA" mode, on the one NVIDIA card with 24 GB that has been
reported so far. Read this beside #79 (RTX 5080) and #86 (RTX 4070), which set
the format this follows.

## Tested

- Commit: `64bd58f5fb794216894bc0e33e03a3a64d19c347` ("Document experimental
  NVIDIA test modes"), no local changes.
- GPU: NVIDIA GeForce RTX 4090, 24564 MiB, VBIOS 95.02.3c.c0.e9.
- Driver: 621.24.
- CPU: Intel Core i7-13700K (16 cores, 24 threads). Same CPU as #86.
- RAM: 63.8 GB.
- OS: Windows 11 Home, build 26300.
- Build: `cargo build --locked --release -p victory --features diagnostics
  --bins`, then `Setup-DexNomRuntime.ps1 -GamePath <exe>`. Build finished in
  49.3 s on this machine.
- Title build: v1.0.2.1, 108 files, 223,880,932,870 bytes.

## Archive, translation only (`victory translate --validate --jobs 16`)

- 26,599 containers, 614 duplicates skipped.
- Programs translated: **26,148 of 26,148** (40,642,858 IR instructions).
- Structured: 26,148 of 26,148 (32,576,698 vector instructions, 797,113
  per-lane branches).
- Resources: 25,953 of 26,148.
- I/O raised: 14,259 of 14,259 vertex and pixel programs.
- DXIL emitted: 14,249 vertex/pixel, 2,734 compute, 8,765 mesh.
- DXIL validated: 14,225 + 2,734 + 8,673 = 25,632 of 25,748 emitted.
- Refusal buckets, largest first: 195 an address in an SGPR that is no root
  parameter; 71 validator "vertex plus primitive output size > 32768"; 29 no
  DXIL part; 41 total interpolation-mode mismatches across seven counts
  (largest 26, example `PS_PropFoliageDeferred_Fire_ExportDepth_fec46121`);
  10 a hardware SGPR input.

Two observations we did not expect to be able to make:

1. **Nothing is refused for wave size at translation time on this host.** Every
   program translates on a 32-lane target, so the wave64 problem lives
   entirely at pipeline creation.
2. Our archive holds slightly more programs than the numbers quoted in the
   README (26,148 against 25,758). Same title version; we flag it rather than
   assume the same archive.

## Archive, host pipelines (`victory translate --validate --pipelines --jobs 16`)

The first full pipeline-creation census we have seen on an NVIDIA host.

| Stage | Emitted | Validated | Pipelines created | Refused by the driver |
| --- | ---: | ---: | ---: | ---: |
| Vertex + pixel | 14,249 | 14,225 | 14,249 | 0 |
| Compute | 2,734 | 2,734 | 2,333 | **401** |
| Mesh | 8,765 | 8,673 | 8,765 | 0 |
| Total | 25,748 | 25,632 | 25,347 (98.4%) | 401 |

- Every refusal is compute, and every one is the driver's "The parameter is
  incorrect." (0x80070057). Not the wave-size message from #16's
  `VICTORY_WAVE_WHY` output; these programs emit valid DXIL and pass the
  Microsoft validator, then the driver refuses the pipeline.
- Every mesh and vertex/pixel pipeline the archive asks for is created on this
  host. On a Radeon reference the same archive is the one that creates
  everything, so the compute refusals are the NVIDIA-specific cliff in this
  archive, and they are much narrower than "wave64 does not work" reads like.

## Cache warm (`victory warm --no-rt --jobs 16`)

- 24,625 translated, 10 refused; 1,582 compute pipelines, 29 refused; 1,123
  omitted by the ray-tracing mode; 269 s.
- Host pipeline library saved 55,228,918 bytes.

## Import census (`victory census`)

995 imports: 131 implemented by the runtime, 790 host pass-through, 74
unimplemented. The 74 sit in `bink2scarlett.dll` (23), `libhttpclient.gdk.dll`
(38), `xboxbvhbuilder.dll` (7), `oleaut32.dll` (2) and singles.

## Gameplay

Ran with the README's NVIDIA path: `VICTORY_FSR_4K=0`,
`VICTORY_AMBIENT_FALLBACK=1`, `victory-play` flags `--no-rt --console
series-x`, diagnostics build, debug HUD on. Scripted keys (`VICTORY_KEYS`)
pressed through the first-run screens, unattended.

Honest caveat first: **this is not the same save and camera as #79 or #86.**
Our run had no save yet, so these numbers are whatever view the title reaches
after the first-run screens, while it is still streaming. Read them as one
point on the same curve, not a controlled comparison.

Stability: the run lasted 17 minutes unattended, reached about frame 6,400
with the world rendering throughout, and exited cleanly (exit code 0, no
device removal, no fatal lines). The host pipeline library saved on shutdown
(148,056,938 bytes, 2,434 pipelines loaded from the warm cache and 1,450 made
during play). Series-X only; the Series-S device removal from #79 was not
reproduced or attempted.

Window: 36 readings over three minutes once the world was up.

| Measurement | Median | p95 | Notes |
| --- | ---: | ---: | --- |
| Frame time | 180.2 ms | 562.6 ms | worst single frame 1,916 ms during streaming |
| Frame rate | 5.55 fps | | range 0.5 to 14.6 fps across readings |
| Preparing (CPU) | 72.3 ms | 200.5 ms | |
| On the GPU | 35.6 ms | 118.0 ms | the card is not the wall |
| Copying back | 67.9 ms | 99.5 ms | 36.8 MB median, 82.6 MB p95 |
| Draws per frame | 3,286 | 5,513 | max 6,993 |
| Dropped, no host pipeline | 370/frame | 1,652 | max 1,950 during streaming |
| Private memory | peak 30,751 MB | | resident peak 21,738 MB, so about 9 GB sits in shared memory |

Reading of it, offered for what it is worth: the shape matches #79 exactly
(CPU preparing plus copy-back dominate while the card idles), but this card
holds more of the working set in local memory before it spills, and the
same-mode median lands above #79's 4.4 fps and the Radeon indoor 3.5 to 4 fps.
A 24 GB card is not the cure, it is a slower version of the same problem.

## Observed on screen (operator notes, cross-checked against the log)

The operator watched this entire run and wrote down what they saw. Their
observations line up with the log closely enough to give each symptom a
timestamp:

| Operator saw | What the log says |
| --- | --- |
| "startup ran smooth, 30fps" | locked 30.0 fps, 22 draws/frame, 18:49:00 to 18:50:06 |
| intro at ~5.5 fps | readings 1.6 to 6.1 fps from 18:50:36 through 18:58:55 |
| "fast cuts... several audio sources firing at once" | first audio coprocessor commands at 18:50:45; dropped draws peak 1,950/frame; texture prepare spikes 53.8 ms / 53.5 MB mid-cut |
| "longer scene cuts somewhat able to stabilize" | recovery readings to 12.2 fps (18:54:48) between cuts |
| "assets not appearing" (a character leaning on something invisible), low-poly solid-color assets, one low-poly character wearing full-res clothes | not directly visible in logs; consistent with assets not yet resident during cuts (the texture spikes above) plus ~370 draws/frame dropped for want of a host pipeline, and with this mode's base-colour ambient fill, which renders an unresident or unsupported material as a flat colour |
| "lighting glitches were abundant"; night car scene, "one of the characters was glowing white" | consistent with this mode replacing the lighting resolve with the diffuse fallback (`VICTORY_AMBIENT_FALLBACK=1`); that emission survives at all is worth noting |
| "audio was not synced at all", ambient only where music and dialogue were expected | the audio stack comes up clean (APU connected, 256 streams; DSP connected; 401 MB audio memory) and the first coprocessor commands fire exactly when the intro starts; there are no error lines, so the desync lives upstream of anything the runtime complains about |
| "mouse input wasn't detected but keyboard was; controller worked correctly" | there is no mouse device in the HLE at all. The keyboard arrives as a stand-in pad (18:48:55) and the gamepad connect lands at 19:00:12. Upstream #82 asks for exactly this |
| "some input delay" | at 180 ms median frames, two to three frames of input lag is arithmetic |
| "visual bugs mostly in shadows and reflective surfaces" | those are the newest, least-settled parts of this snapshot (five recovered shadow compute programs, the ray-node reflection work) |

One addition the log makes to the story: the playable sequence
(19:00:04 to 19:02:16) ran 4.8 to 5.8 fps at the heaviest draws of the whole
run (up to 7,656 per frame), and after it the title settled onto a light
locked-30 screen (87 to 92 draws) until shutdown.

## Play-time refusals

Seven distinct compute programs are refused during play, and together they
account for most of the "dropped" line:

| Hash | Dropped per frame |
| --- | ---: |
| `2cc4c197ee29e291` | 78.8 |
| `02384a60fda6f2d0` | 78.4 |
| `369cbe906ea72764` | 77.8 |
| `2da4beb386509b77` | 66.8 |
| `5dc3b25986622949` | 3.7 |
| `6f225b641c5b0708` | 3.7 |
| `926c46bab3b86bd6` | 0.7 |

Four of them at ~70 to 79 draws per frame look like one family the title calls
every frame. `926c46bab3b86bd6` is the same hash `victory warm` reported as
"host pipeline refused, The parameter is incorrect", so the warm-time refusal
and the play-time refusal are the same program.

## Not covered

- No ray-tracing run; `--no-rt` only, per the README's NVIDIA path.
- No fixed save or camera, so no controlled before/after and no comparison
  against the Radeon numbers on equal terms. That needs a save in the prologue
  and a second session.
- No capture of the refused DXIL to identify which pipeline parameter the
  driver rejects. `VICTORY_WAVE_WHY` reports nothing for them, because the
  translator is not refusing them; the driver is.
- No Series-S console run (the #79 device-removal case).
- One machine, one driver version.

## Reproduction

```powershell
$env:VICTORY_FSR_4K = '0'
$env:VICTORY_AMBIENT_FALLBACK = '1'
$env:VICTORY_DEBUG_HUD = '1'
& "$env:LOCALAPPDATA\DexNom Runtime\victory.exe" translate <game> --validate --pipelines --jobs 16
& "$env:LOCALAPPDATA\DexNom Runtime\victory.exe" warm --no-rt --jobs 16
& "$env:LOCALAPPDATA\DexNom Runtime\victory.exe" run --no-rt --console series-x --execute <exe>
```

The frame line in the run log (`victory_gpu::host: <n> fps, ...`) carries the
per-frame numbers quoted above; the memory line (`victory_gpu::copystat`) the
private-memory peak.
