# NVIDIA (32-lane) report, 25 November 2026

The README has Radeon results only. These are from one NVIDIA card. Numbers and log lines only; no game data.

## Machine and build

- RTX 3060 Ti 8 GB, driver 621.24. The runtime logs `host wave lanes min=32 max=32`.
- i5-12600KF, 16 GB RAM, Windows 11 build 26200, game files on a SATA SSD.
- DexNom `8cca4d2` plus upstream Marvisak/VIctory#454 (`VICTORY_NO_WATCHDOG`). Without it, the title's own watchdog stopped slow loads with `0x80000003` in the title image.
- Release build with `diagnostics`, launched through `victory-play.exe`.

## Normal launch

- The prologue runs at about 2 fps: 517 ms a frame, the GPU 30–60 % busy.
- Stable, no device loss.
- Interiors are dark, as expected without rays. The window glass renders.

## `--rt`

```
the ray-tracing structure builder stays off: not all of its programs have a host pipeline, and its stages run all or none missing=5 of=53
```

So on a 32-lane NVIDIA card `--rt` gives the same picture as the normal launch. In the same run:

- **Refused by the driver, `E_INVALIDARG`** (the wave64 case on a 32-lane host), 8 compute programs: `02384a60fda6f2d0`, `28918b7150a7aad5`, `2cc4c197ee29e291`, `2da4beb386509b77`, `369cbe906ea72764`, `5dc3b25986622949`, `6f225b641c5b0708`, `caa78b8ca62bc3d6`.
- **Not translated**, `a first-lane read where not every lane runs`, 3 compute programs: `39321a95223001ed` (@0x23c), `77360df21e12ea43` (@0x23c), `9fc111d3be79364f` (@0x258).

The 5 missing builder programs are presumably among these 11; the log does not say which.

## `--rt` with `VICTORY_WAVE_WIDE=1`

- The builder still stays off (`missing=5 of=53`).
- The host device is lost at world entry (`0x887A0005`, no breadcrumbs), as the comment in `crates/victory-shader/src/emit/mod.rs` warns.

Not a way forward on this card.

## What it would take

On NVIDIA, rays need those builder programs translated for 32-lane waves without the barrier form of `VICTORY_WAVE_WIDE`. I can test a branch on this card.
