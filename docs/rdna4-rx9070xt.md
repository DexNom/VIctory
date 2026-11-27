# RDNA4 (RX 9070 XT) test results

First RDNA4 run: menus, first-boot setup and world loading work; the load ends in a crash
inside AMD's DXIL compiler. No GPU device loss.

**Machine:** Windows 11 (26300), Ryzen 7 5700X3D, 32 GB, **Radeon RX 9070 XT (gfx1201)**,
driver 32.0.32029.2010 (Adrenalin 26.11.2), Resizable BAR on, 2560x1440.

**Build:** this snapshot, `cargo build --locked --release -p victory --features diagnostics --bins`
with Rust 1.98.1 and MSVC 19.44 (no Visual Studio install), run through `victory-play.exe`.

## Timeline

| Stage | Draws/frame | Frame time |
| --- | ---: | ---: |
| Account picker | 21 | 33.3 ms |
| First-boot wizard pages (brightness, navigation, subtitles, legal) | 22-52 | 33.3 ms |
| Control-scheme settings screen | 112-114 | 33.3 ms |
| Loading artwork | 6 | 33.3 ms |
| World load begins (streaming) | 46 | 151 ms (6.6 fps) |
| −20 s | — | 1,093 ms (0.9 fps) |
| −12 s | — | 12,249 ms (0.08 fps) |
| −7 s, then crash | — | 12,318 ms |

Menus hold a locked 30 fps for the whole setup, including the settings screen that draws 114
per frame. Nothing stutters and no device is lost.

## The crash

Loading reaches the world and the frame time collapses as streaming starts, then:

```
first-chance 0xc0000005 at amdxc64.dll+0x861b9f (read 0x8: nothing mapped there) on thread "victory-pipelines-1"
unhandled    0xc0000005 at amdxc64.dll+0x861b9f (read 0x8: nothing mapped there) on thread "victory-pipelines-1"
```

`amdxc64.dll` is AMD's DXIL compiler, and the fault is on the pipeline worker, not on the
render thread and not in the runtime's own code. State when it happened: **895 shaders
translated, 652 host pipelines created, 140 refused**, no device removal, no `DXGI_ERROR_DEVICE_HUNG`.
The last pipelines before the fault were a compute pipeline, a vertex+pixel pair and two mesh
pipelines, all logged as created immediately before the crash.

The title itself also faulted afterwards (`0xc0000005` in the title's exit code) and its
Breakpad handler tried to upload a 34 MB dump to `submit.backtrace.io`; the network layer blocked
the upload.

## Copy and refresh churn at load

The change-tracking path is what the load spends its time in:

| Sample | Title copies/frame | Refreshes/frame | Bytes refreshed/frame |
| --- | ---: | ---: | ---: |
| Loading, early | 6 | 7 | 169 MB |
| Loading, mid | 908 | 1,361 | 1,419 MB |
| Worst sampled frame | 14,041 | 24,680 | **24,932 MB** |

Frame time tracks that column: 151 ms at 170 MB/frame, 12.2 s at 25 GB/frame.

## Not the same failure as upstream VIctory here

On this same machine, upstream VIctory fails differently: its host device is removed with a page
fault at `0x1a00c4000` during the same stage. This snapshot instead keeps the device alive and
dies in the AMD compiler, which suggests the device-loss half of the upstream problem is fixed
here and what remains is the compiler path — the area PR #2 isolates.

## Getting past the setup screens

The wizard is drivable from the keyboard with `VICTORY_KEYS`, because no XInput pad was attached
during this run (`XInputGetState(0)` returns 1167, no pad), so the keyboard's stand-in pad exists
and carries the scripted keys. Note that this is not always the case: a wireless receiver
answering for a switched-off pad occupies slot 0 with an all-zero report, which suppresses the
stand-in. Treating a slot as a pad only after its first non-rest report (cleared on disconnect)
covers that case; it was not needed for the run below and is not part of this document.

## Wave width is not what kills the compiler

The same load was run with `VICTORY_HOST_WAVE=32` (wired into the `run` path locally; in this
snapshot it is only read by `translate`). That changes lowering for wave64 programs and changes
the shader cache key, so every pipeline is rebuilt from scratch.

| Run | Translation | Shaders / pipelines / refused at the fault | Stage reached | Fault |
| --- | --- | ---: | --- | --- |
| default | native wave64 | 895 / 652 / 140 | world load, ~5 min in | `amdxc64.dll+0x861b9f`, `victory-pipelines-1` |
| `VICTORY_HOST_WAVE=32` | wave64 emulated on 32 lanes | 358 / 275 / 11 | **menus**, ~30 s in | `amdxc64.dll+0x861b9f`, `victory-pipelines-6` |

Identical fault offset on a different wave width, at a different stage, with a fresh pipeline
cache. So the crash is not a wave64-specific path: whatever construct kills AMD's compiler is
present in both sets of pipelines, and recompiling from cold only makes it happen earlier. That
argues against wave64 emulation as a workaround for this fault, and for isolating the compiler
call itself (the area PR #2 targets) or finding the shader that trips it.

Both runs end with the same two lines and no device removal:

```
first-chance 0xc0000005 at amdxc64.dll+0x861b9f (read 0x8: nothing mapped there)
unhandled    0xc0000005 at amdxc64.dll+0x861b9f (read 0x8: nothing mapped there)
```

## Not tested

- Ray tracing (`--rt`) on RDNA4; only the default renderer was run.
- Moving the player in the world: the run never got a first world frame.
- Frame rates after the load: the crash happens before any world frame is presented.
- NVIDIA and RDNA3 on this snapshot.
