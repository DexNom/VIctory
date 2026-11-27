# Notes from an RTX 5070 Ti Laptop (12 GB) with 32 GB of RAM, 2026-11-26

- Shader-visible view heaps are capped at 1,000,000 (`MaxViewDescriptorHeapSize`,
  binding tier 3; `cargo run -p victory-gpu --example heap_probe`). The title's view heap
  has 1,245,184 slots: the host keeps 737,855 of them and 2^18 dynamic slots. Every table
  of the title's past slot 737,855 is made afresh in the dynamic slots for each list, so
  they run out: a one-hour Series X session averaged 10,963 tables asked and 2,159 refused
  a frame; each refusal drops a draw or dispatch (`indirect kind N: prepare failed:
  binding`: up to 1,400 mesh dispatches, 800 dispatches and 340 draws a frame), likely the
  missing backgrounds and odd surfaces. The memory line now splits fresh tables by why they
  are fresh and gives the peak of dynamic slots in use; `VICTORY_DYNAMIC_SLOTS` moves the
  line on a capped host too. A real fix shares fresh tables across lists.
- 0x1c8: the title has a fallback when it fails (docs/device-0x1c8.md).
- `VICTORY_TEXTURE_KEEP=0` with 32 GB coincided with a device loss at 16 minutes; without it
  a session ran an hour. `VICTORY_TEXTURE_BUDGET_MB=3072` kept the card from spilling into
  the PC's memory (the card at 5 % before, 99 % after).
- The Series S profile draws its intro with 0x1c8 pipelines (black) and looks worse.
- `tests/replay.rs` crashes (access violation) here on the untouched fork too.
