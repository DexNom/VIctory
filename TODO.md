# TODO

A shared list of open work, so contributors can see what is known, what is
not, and who is already on it. The README's [Things to Be Done](README.md#things-to-be-done)
explains how to measure a change; this file tracks the individual items.

Before starting an item marked **in progress**, check with the person named
on it so the work isn't done twice. To claim an item, add your name in your
PR or ask in the developers channel.

## Performance

- **Reduce buffer copy-back.** Track which pages the GPU actually writes and
  read back only the ranges CPU consumers need. Start with the ray-tracing
  header builder, which declares much broader write ranges than it uses.
  [host.rs](crates/victory-gpu/src/host.rs) ·
  [shadow.rs](crates/victory-gpu/src/shadow.rs) ·
  [memory.rs](crates/victory-gpu/src/memory.rs)
- **Reduce VRAM use and stabilize texture streaming.** Some materials still
  render black. Trace the draw's base color back to the bound texture and mip.
  [texture.rs](crates/victory-gpu/src/texture.rs) ·
  [descriptor/host.rs](crates/victory-gpu/src/descriptor/host.rs)
- **Improve frame-time stability.** Measure cold pipeline compilation and warm
  gameplay separately.
  [emit](crates/victory-shader/src/emit) ·
  [pipeline.rs](crates/victory-gpu/src/pipeline.rs)
- **Scale the GPU buffer budget with VRAM.** *In progress: Krighz.*
  `card_limits()` in [shadow.rs](crates/victory-gpu/src/shadow.rs) uses a
  fixed 1,024 MB default (`VICTORY_CARD_BUFFERS_MB`). On cards with 12 GB or
  less, raising it from 512 MB to 3,072 MB took frames from 365 ms to 86 ms in
  one report. Known: the budget the system gives is already read
  (`Host::video_memory`). Unknown: a default that is safe across VRAM sizes.

## Presentation

- **HDR output.** `try_enable_hdr` in
  [display.rs](crates/victory-hle/src/modules/display.rs) reports HDR as
  disabled because the swap chain presents SDR. Once it can present HDR,
  report enabled with the host display's luminance.
- **Screenshots.** `take_screenshot` in
  [display.rs](crates/victory-hle/src/modules/display.rs) fails as if capture
  were unavailable. It needs the last presented frame read back as BGRA8 from
  the GPU side after `PresentX`.

## NVIDIA

NVIDIA gameplay is untested (see the README).

- **Black screen with `--no-rt`.** Reported on an RTX 5070: the run stays at
  14 draws per frame after the log line "8-bit RGBA stands in" (`resource.rs`) for formats 86
  and 115. Likely cause: `target_format()` in
  [resource.rs](crates/victory-gpu/src/resource.rs) substitutes RGBA8 for
  B5G5R5A1 and B4G4R4A4 render targets, and copies between such a target and
  a 16-bit texture are skipped. Implementing that conversion is the first
  thing to try. Unknown: whether it is the only cause.
- **Grey screen with `--rt-static-bvh`.** Same report: gameplay is reached but
  presents grey, and compute shader `02384a60fda6f2d0` fails to create with
  `E_INVALIDARG`.

## Windows 10

- **Agility SDK exports.** *In progress: batman.* Without `D3D12SDKVersion`
  and `D3D12SDKPath` exports, Windows 10 loads the system `D3D12Core.dll`,
  which gives a black window and a crash. Setup also needs to copy
  `D3D12Core.dll` next to the executable.

## Tooling

- **Log bundler.** *In progress: miska.* One command that zips
  `logs\latest.txt` with GPU, driver and RAM info, and strips personal details
  such as the game path and Windows user name, so testers can share logs
  safely.
