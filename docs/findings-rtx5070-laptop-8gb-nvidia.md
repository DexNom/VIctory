Findings: RTX 5070 Laptop 8 GB — current main crashes in `--no-rt` with the `+0x5149aec` pool-exhaustion breakpoint (regression vs the 25/11 build)

Hardware: RTX 5070 Laptop (8 GB VRAM), i9-14900HX, 32 GB RAM, Windows 11, NVIDIA driver 621.40. Build: **current main `64bd58f`** (built from source), launched via the play launcher. Mode: ray tracing **off** (`--no-rt`).

## The regression

The `+0x5149aec` pool-exhaustion breakpoint now fires in **`--no-rt`** on current main. On the earlier 25/11 build, the same `--no-rt` mode reached the world and **moved with no crash** (reported earlier on this PR). So current main regresses RT-off on an 8 GB card.

Crash signature (unhandled, 7 threads, then exit):
```
VIctory: unhandled exception 0x80000003 at victory-image-....dll+0x5149aec
  rax 0x1c100001fffe ...
VIctory exited with exit code: 0x80000003
```
`0x80000003` = `STATUS_BREAKPOINT` (a deliberate `int3`). The `0x1fffe` in `rax` is the empty-pool free-list head — the same `INSTANCE_LIST` exhaustion signature as the RT-on crash, here reached **without** ray tracing.

## Timeline (this run)

1. Boot → cinematic at **0.1 fps, 12 444 ms/frame** — dominated by cold pipeline compilation (2041 draws dropped while 614 pipelines were being made; 1352 indirect draws). The preparing+GPU+copy-back sum was only ~570 ms, so ~11.9 s/frame is pipeline-compile wait.
2. → world at **2.7 fps, 375.7 ms/frame, 2040 draws/frame**, 34 475 textures uploaded (2344 MB), 174 460 texture objects.
3. → crash at `+0x5149aec` (`rax=0x1fffe`).

So it is not a black screen and not a cinematic stall — it reaches a rendering world, then the instance pool exhausts.

## Memory footprint (why 8 GB is the trigger)

- System RAM peaked at **~14.8 GB** during the run (the 25/11 build held ~11 GB in the world).
- The card is 8 GB and VRAM-oversubscribed (dedicated budget ~6.9 GB, spilling to system shared memory); the texture queue shows slots "deferred by the VRAM budget."
- The new GPU/texture code carries a heavier footprint than the 25/11 build, which on an 8 GB card pushes the `INSTANCE_LIST` (45000) pool to exhaustion.

This maps to the TODO items **"Reduce VRAM use and stabilize texture streaming"** and **"Scale the GPU buffer budget with VRAM."**

## The "black screen" signatures are present but not causal

Both signatures the TODO cites for the NVIDIA black/grey screen are in this log, with **no black screen**:
```
the host does not render to this format: 8-bit RGBA stands in format=86
the host does not render to this format: 8-bit RGBA stands in format=115
host pipeline refused error=Paramètre incorrect. (0x80070057) shaders=[compute 02384a60fda6f2d0]
```
The 4 refused wave64 compute shaders (`02384a60fda6f2d0`, `369cbe906ea72764`, `2cc4c197ee29e291`, `2da4beb386509b77`) are dropped, but the run renders and only fails at the pool breakpoint. So those signatures are not what causes a black screen on this box.

Relevant log lines:
```
0.1 fps, 12444.6 ms/frame (356.1 preparing ...; 164.7 on the GPU; 49.7 copying back 1.8 MB), 2394 draws/frame, dropped 2041 without a host pipeline (614 being made) and 1352 indirect; ... ram 14797 MB
2.7 fps, 375.7 ms/frame (265.1 preparing ...; 66.9 on the GPU; 77.9 copying back 15.8 MB), 2040 draws/frame, dropped 238 without a host pipeline (2 being made) and 2 indirect; ... ram 7904 MB
VIctory: unhandled exception 0x80000003 at victory-image-....dll+0x5149aec   (rax 0x1c100001fffe)
VIctory exited with exit code: 0x80000003
```
