# Low-VRAM testing: RTX 5070 Laptop (8 GB)

Results from running the runtime on an 8 GB NVIDIA card — the lowest-VRAM
configuration tested so far. Game build 1.0.2.1, Series X profile, Windows 11
Pro 10.0.26200, i9-14900HX, 32 GB RAM, NVIDIA driver 32.0.16.2140.

## Summary

Boot, menus and the cinematic work (after a texture warm-up). The **open
world loads and the prologue mission runs**; the session ends in a
reproducible title breakpoint on the first attempt to move the player.

## Timeline (single session, ~12 min)

1. **Boot + menus** — OK.
2. **Cinematic warm-up (~8-9 min)** — audio and letterbox only, frame content
   a constant blue (cleared target). The texture backlog drains during this
   phase: `1133 title textures have no host copy yet (3630 MB)` growing to
   `16586 textures (9088 MB)` as streaming pulls the world set. Once the live
   set lands in VRAM, real image content appears.
3. **Open world loads** — game state `Game Running`, mission
   `fbk_stry000_FBK_STRY000_INTRO` (Prestream), player spawned at
   `(-6774.5, 5322.4, 8.8)`, health 130/130. Pools alive: BUILDING
   22342/22500, OBJECT 19142/27500, PED 21/600, VEHICLE 19/700,
   INSTANCE_LIST 45000/45000, GRASS 5430/9000.
4. **First player movement** — crash: `first-chance exception 0x80000003`
   (intentional breakpoint) at **title image offset `+0x5149aec`**, game
   frame 2511, immediately after new mesh+pixel pipelines first-bound for
   the movement.

## Crash dump highlights (game's own DEBUG INFO)

```
Game Running Time: 0h:11m:32s   Frame: 1770   Streaming Requests: 1450
TileHeap: used (5024Mb) - total (6236Mb)          <- 80%
Game Physical: 2185310128/2305818624              <- 95% of physical budget
Current SRL: fbk_stry000_FBK_STRY000_INTRO [Prestream]
Player: <-6774.509277, 5322.457520, 8.799997>  Health 130/130  Wanted 0
Version: 930.048.020-vb_sub_story   Conole Type: Anaconda
```

The breakpoint is a deliberate title assert, not a memory fault. Memory was
under heavy pressure (physical 95%, tile heap 80%, 1450 pending streaming
requests), but the signature is a fixed `int 3` at a stable offset —
reproducible on demand, and the offset should pinpoint the asserting path.

## Dropped draws (expected, RT off)

`dropped 141.0/frame without a host pipeline: a stage of the ray-tracing
structure builder, which is off`, plus a handful of refused compute shaders
(wave64 / SGPR-address classes).

## Notes for low-VRAM work

- The warm-up phase is dominated by texture upload; the resident-texture
  admission and fill-worker paths are doing their job, the 8 GB card just
  needs the live set to land. A second run with the caches warm should reach
  the world faster — worth measuring.
- The movement breakpoint is the current wall on 8 GB, and it is a specific
  title assert rather than VRAM exhaustion — the next low-VRAM frontier.

This box (RTX 5070 Laptop 8 GB) is available as a low-VRAM test rig.
