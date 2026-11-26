# Findings: Experimental NVIDIA mode on an RTX 4060 8 GB, Windows 10

Not for merge. One machine, two 10-minute runs, unattended.

## Machine and build

| | |
|---|---|
| GPU | NVIDIA GeForce RTX 4060 8 GB (desktop, PCIe 3.0 ×8 on this board), driver 610.88, hardware-accelerated GPU scheduling on |
| CPU | Intel Core i7-5820K, 6 cores / 12 threads (Haswell-E) |
| Memory | 32 GB DDR4 |
| OS | Windows 10 Home 22H2, build 19045 |
| Build | `main` at `64bd58f`, `cargo build --locked --release -p victory --features diagnostics --bins`, plus a local Agility SDK change (below) |
| Mode | the README's Experimental NVIDIA mode: `VICTORY_RAY_TRACING=0 VICTORY_FSR_4K=0 VICTORY_AMBIENT_FALLBACK=1`, `victory.exe run <game> --execute --console series-x` (what `victory-play.exe --no-rt` runs), Series X profile |
| Scene | the prologue's cell window, the player idle, from the title screen with four Enter presses; samples from 180 s to 600 s after launch |

## Windows 10 needs an Agility SDK runtime

This tree loads no Agility SDK runtime. On this machine both upstream VIctory and fxceless/victory-fork need one on Windows 10 19045 (the in-box `d3d12.dll` refuses the translated root signatures, "Unsupported bit-flag set (root signature flags c00)"), so I added it before the first run; I did not try this tree without it. I used the change from fxceless/victory-fork#856 (the executable exports `D3D12SDKVersion` and `D3D12SDKPath`, and the Agility runtime is unpacked to `target/<profile>/D3D12/`). Here its `/EXPORT` linker arguments must go to `victory.exe` only (`cargo:rustc-link-arg-bin=victory=...`): with `rustc-link-arg-bins`, `victory-play.exe` and `victory-warm.exe` fail to link (LNK2001: they do not define the two symbols). Windows 11 is not affected.

## Preload

`victory.exe warm <game folder>` (what `victory-warm.exe` runs): **306 s** on this CPU. 24,625 shaders translated, 10 refused; 1,582 compute pipelines, 29 refused with `E_INVALIDARG` (wave64 on a 32-lane card); 1,123 omitted by the ray-tracing mode.

## Results

From the runtime's 5-second lines (world frames only) and the window title's load figures:

| | 1st start | 2nd start |
|---|---:|---:|
| fps, median (max) | 2.5 (2.8) | **2.6 (3.0)** |
| ms per frame, average | 609 | 542 |
| of which preparing / on the GPU / copying back | 259 / 130 / 284 | **259 / 113 / 266** |
| draws per frame | 4,842 | 4,739 |
| dropped without a host pipeline, per frame | 364 | 570 |
| card busy (title figure, average) | — | **29 %** |
| process CPU (title figure, average) | — | 37 % |
| card memory: peak in use / budget | — | **8,366 / 7,029 MB** |
| memory shared with the card: peak in use | — | 6,034 MB |
| refused compute pipelines in play | 4 | 6 |
| untranslated shaders in play | 0 | 0 |
| device removed | no | no |

The 5-second lines give frame time averages, not per-frame percentiles, so I cannot give a 95th percentile from them.

**On screen:** the ambient fill does what it says: the cell's walls, the papers on the wall and the character are lit, where the other fork below shows them black. The hair reads as a flat orange. Subtitles and the "Move / Move the camera" prompt show. Screenshots kept locally (game content, so not attached).

## The same PC with another fork, for scale

fxceless/victory-fork at `a20809d` (+ #856), same scene, Series X, its test switch set: **4.6 fps** median, preparing ~111 ms, copying back ~75 ms, the card 87–90 % busy (the frame is GPU-bound there), 8.1 GB of the 6.9 GB budget. With `--console series-s` and that fork's #1081 and #798: 6.4–6.6 fps and 6.8 GB, inside the budget.

So on this machine the difference between the two is on the CPU side, mostly copy-back (~266 vs ~75 ms a frame). Your "Reduce buffer copy-back" item may find prior art in that fork's copy-back work (its `VICTORY_CARD_REHASH_RUNS`, `VICTORY_MERGE_GAP` / `VICTORY_MERGE_RUNS`, `VICTORY_JOIN_PARTS` and `VICTORY_TEXTURE_BACK` switches, measured in its `diagnostics/` logs). I have not tried porting any of it.

## Your ambient fill on the other fork

Your `VICTORY_AMBIENT_FALLBACK` (`ambient_albedo` and `tiled_ambient`) ported almost line for line to fxceless/victory-fork `a20809d`, whose translator shares the same helpers (`host_word`, `heap_index`'s `>> 5`, `heap_handle`). There, on this PC, Series S with #1081 and #798: **6.5 fps median in both runs, the same as without it**, and video memory still inside the budget (6.8 of 6.95 GB). The jumpsuit and the papers on the wall are lit; the cell's walls stay darker than in your build, so something else of yours may light them. So the fill costs nothing measurable on an 8 GB card. I kept it local, credited to this repository.

## Small note for testers

With `VICTORY_DEBUG_HUD=1` (which `victory-play.exe` sets) the HUD is a window of its own, titled "DexNom RayTime diagnostics". Tools that look for the game's window by process (screenshots, key presses) can pick the HUD instead; my first attempt did. Without it, the game window's title carries the same load figures.

_AI-assisted (Claude Code). Numbers from local logs; no game data in this report._
