# Running GTA VI (Xbox Series dump) with VIctory on an NVIDIA laptop, and getting close to 30 FPS

This guide is for the `edupode` branch of this repository: the **cgbj** build of
VIctory with the **VIctory-NVIDIA** lighting fixes, tuned for a laptop with an
RTX 5070 Ti (12 GB) and 32 GB of RAM. It explains how to build it, how to start
it, and which settings get the frame rate close to 30.

**No game files are included.** You need your own dump of the game.

## What to expect

Measured on an Acer Predator PHN16-73 (RTX 5070 Ti Laptop 12 GB, Core Ultra 9
275HX, 32 GB RAM), Windows 11, Series S profile:

| Mode | Cutscenes | Open world, standing | Open world, moving / driving |
|---|---|---|---|
| 1080p (`Jogar-Luz.cmd`) | ~26 | 20-26 | 12-15 |
| 720p (`Jogar-Luz.cmd 720p`) | ~30 | 23-26 | lower when streaming new areas |
| 540p (`Jogar-Luz.cmd 540p`) | 31-32 | 25-29 | ~15 / ~10 |

With NVIDIA Smooth Motion on (see below), the frames shown on screen are about
doubled: 15 real FPS look like ~30.

## 1. Requirements

- Windows 11, an NVIDIA RTX card (shader model 6.6, mesh shaders), 32 GB of RAM
  recommended (VIctory uses about 24 GB of private memory).
- Rust: the version pinned in `rust-toolchain.toml` (rustup installs it).
- Visual Studio Build Tools with the C++ workload and a Windows SDK.
- Your own game dump: the folder that contains `game_scarlett_master.exe`.

## 1b. Easiest: the VI Launcher

`launcher/build-launcher.cmd` builds `VI Launcher.exe` (with the C# compiler that ships with Windows). It
builds VIctory, finds your game, checks the GPU, video memory and Resizable BAR, prepares the shaders, offers
the modes below with check boxes, and shows the controls. The steps after this are the same by hand.

## 2. Build

```powershell
git clone -b edupode https://github.com/edupode/cold-project-vi.git
cd cold-project-vi
cargo build --locked --profile diag -p victory --features diagnostics
```

Use the `diag` build: the list of ray-tracing shaders the launcher leaves out
(`VICTORY_SKIP_PIPELINES`) only works in builds with diagnostics.

**Windows Defender:** its ransomware protection rule can block the freshly
built `victory.exe` ("Access denied"). In an administrator PowerShell:

```powershell
Add-MpPreference -AttackSurfaceReductionOnlyExclusions "C:\path\to\cold-project-vi\target"
```

## 3. Prepare the shader cache (once, and after every rebuild of the translator)

```powershell
$env:VICTORY_GAME_DIR = 'D:\Games\MyDump'   # the folder with game_scarlett_master.exe
.\Jogar-Luz.cmd preparar
```

This translates the game's ~25,700 shaders (about 2 minutes) and prepares the
pipelines of earlier sessions. Without it the first session runs at about 1 FPS
while it translates.

## 4. Play

```powershell
.\Jogar-Luz.cmd          # 1080p
.\Jogar-Luz.cmd 720p     # faster, still sharp (recommended)
.\Jogar-Luz.cmd 540p     # fastest, softer picture
.\Jogar-Luz.cmd 720p qualidade   # less stretched geometry, about 7 FPS slower
```

`720p` and `540p` draw the 3D world at that height and let the game's own
temporal upscaler stretch it to 1080p; menus, HUD and subtitles stay sharp.
Render scales 0.75 and 1.0 with the forced upscaler stop the game at start; use
only these two.

Cache, saves and logs go to `%LOCALAPPDATA%\VIctory\luz` (set `RUN` to use
another folder). When you close the game, the launcher prepares the pipelines
used in that session for the next one.

## 5. Getting close to 30 FPS: what matters most

1. **Use `720p` or `540p`.** At 1080p the card spends 30-50 ms a frame and runs
   out of video memory in the open world.
2. **Close everything else that uses the GPU or RAM** before playing: Android
   emulators, other games, Wallpaper Engine, browsers, Discord, overlays. Windows
   gives the game less video memory when other apps hold some (we measured the
   budget dropping from 11.2 GB to 9.0 GB). Over that budget, textures go to
   system RAM and the frame rate halves.
3. **NVIDIA Smooth Motion** (driver frame generation, RTX 40/50):
   - NVIDIA App → Graphics → Program settings → add
     `...\cold-project-vi\target\diag\victory.exe` (or the copy your launcher
     starts) → **Smooth Motion: On**.
   - It needs a borderless full-screen window (`VICTORY_BORDERLESS=1`). That
     window is **off by default**: on the 2560x1600 laptop screen with 150 %
     scaling it dropped the game to 1-2 FPS (under investigation), so Smooth
     Motion does not engage yet.
   - Also in the NVIDIA App for that program: **Power management mode: Prefer
     maximum performance**, **Low latency mode: On**.
4. **Turn off "levels on demand"** (`VICTORY_LEVELS_ON_DEMAND=0`, what
   `Jogar-Luz.cmd ... texturas` does). The host copies of textures then hold
   only the levels the game shows: on the test laptop video memory went from
   ~1 GB over the system's budget to 0.8 GB under it, 40,000 textures fit in
   3.6 GB instead of 10,000 in 4.5 GB, textures waiting fell from 6,000-15,000
   to ~130, and the open world went from 14-15 to 20-26 FPS at 720p. The same
   mode compares 1.5 GB of game buffers on the card (`VICTORY_CARD_BUFFERS_MB`)
   and caps textures at 2048 texels (`VICTORY_TEXTURE_MAX`, few are larger on
   Series S).
5. **More helper threads on many-core CPUs** (`VICTORY_WIDE_THREADS`, default
   a quarter of the cores up to 6; the `cpu` switch uses 10) and the write
   watch asked early by a thread of its own (`VICTORY_ASK_EARLY=2`): the
   submitting thread spends ~5 ms a frame asking which pages the game wrote and
   ~9 ms refreshing copies. Under test.
6. **Past 30 FPS:** the game asks the console for a frame every 33.3 ms.
   `VICTORY_FPS_MAX=60` (`Jogar-Luz.cmd ... 60`) answers with a 60 Hz clock, so
   scenes the CPU can prepare faster than 33 ms go above 30. Under test.
7. **Plug the laptop in** and use the vendor's performance mode.
8. **Windows virtual memory: "System managed"** (System → About → Advanced
   system settings → Performance → Virtual memory). VIctory's memory grows over
   a long session (we saw 23 GB → 33 GB in 50 minutes); with a small page file
   the game can run out of memory.
9. **Restart the game about every 45 minutes** for the same reason.

## 6. Controls (keyboard and mouse)

The game reads an Xbox controller; a connected controller replaces all of this.

| Key | Button | On foot | In a vehicle |
|---|---|---|---|
| W/A/S/D | left stick | move | W accelerates (RT), S brakes/reverses (LT), A/D steer or roll |
| I / K | left stick up/down | (camera) | aircraft nose down (I) / up (K): to take off hold W and K |
| mouse | right stick | look | look |
| left mouse button | RT / RB | shoot | shoot (RB) |
| right mouse button | LT | aim / interact | |
| Left Shift | L3 | run | horn / nitro |
| F | Y + vehicle mode | enter vehicle | leave vehicle |
| E or Y | Y | interact, talk | |
| Space / Enter | A | jump / climb | handbrake |
| Left Ctrl / C / Backspace | B | crouch / rest | headlights |
| R or X | X | melee | |
| Q | RB | take cover | |
| Tab | LB | inventory | draw weapon |
| M | View | map | map |
| V | R3 | look back | look back |
| arrows or 1-4 | D-pad | phone, radio, quick nav, partner | |
| Esc | Menu | pause | pause |

- **Mouse:** click inside the game window to capture the mouse as the camera;
  **Alt** or **F1** releases it, and switching windows releases it too.
- **Vehicles:** the game drives with the triggers, so **F** (enter/leave) also
  switches the keyboard to vehicle mode, where holding **W** accelerates. A high
  beep means vehicle mode, a low beep means on foot. If it gets out of sync
  (F did not put you in the car), press **Caps Lock** once to fix it.

## 7. What this build changes (short)

- cgbj's memory, descriptor-table and texture handling, plus the NVIDIA fork's
  lighting: ray walks end at the root (`VICTORY_BVH_MISS`), NaN values zeroed
  (`VICTORY_NAN_SCRUB`), ambient capped (`VICTORY_AMBIENT_MOST`), 1,034
  ray-tracing shaders left out (`scripts/rt_off_hashes.txt`).
- One of the game's wave64 compute shaders (water/fog volumetrics, local lights)
  translated for 32-lane NVIDIA waves (`VICTORY_WAVE_WIDE_DIGESTS`); without it
  the open world ran at 1 FPS.
- Settings for a 12 GB card: 1080p output, 1 GB of game buffers on the card, a
  smaller render-target reserve, fewer texture blocks made ahead.
- Keyboard/mouse layout; an optional borderless window for Smooth Motion
  (`VICTORY_BORDERLESS=1`, off by default, see above).
- Textures that come back after being evicted are evicted last
  (`VICTORY_TEXTURE_FAVOR_REUSED`, on by default): in a long session a third
  of the evicted textures had to be loaded again.

Details, measurements and the full list of changes: [LUZ-24FPS.md](LUZ-24FPS.md)
(Portuguese).

## 8. Known problems

- Small blue rectangles on some moving thin objects (seagull wings, a picked-up
  bag) and missing volumetric fog in places: the `TransmissionVolumetrics_Tiled`
  shader is still off because its 32-lane translation draws black squares on the
  water. Work in progress.
- Stretched geometry when passing some objects, birds drawn as distorted
  blocks (the `qualidade` mode reduces it).
- Video memory above the system's budget in the open world; textures reload
  ("blinking" in the distance).
- Memory grows during long sessions (see 5.8 and 5.9).
- No ray tracing: its structure builder uses wave64 programs NVIDIA refuses.
- Two wave64 shaders still untranslated (`DefragHeap`, `CollectTriangles_Meshlets`).

---

Maintained by **@edupode** on GitHub: <https://github.com/edupode>

## Latest changes (27 Nov 2026)

- **Overlay (F3)**: FPS now, the last minute's average (`AVG 1M`), the whole run's (`ALL`) and the worst
  half second (`LOW`).
- **Video memory**: VIctory asks Windows to reserve video memory for itself (`VICTORY_VRAM_RESERVE`: `max`
  by default, a number of MB, or `0`), so other apps are moved out first. Copies of textures whose memory the
  game has already unmapped are let go after 3 s (`VICTORY_TEXTURE_DEAD=0` turns this off); about 1 GB sat in
  them in a 720p session. The memory line in the log counts them.
- **VI Launcher**: new design; a Performance page that lists the apps holding video memory and RAM and closes
  the ones you tick; NVIDIA Smooth Motion written into the driver's profile for `victory.exe` (no NVIDIA App
  needed); a loading bar that makes the pipelines of earlier sessions before the game opens; an explanation
  for each option, ★ marking what was tested on the author's PC; English by default, Portuguese in the side bar.
- **Frame recorder**: F9 in the game creates the trigger file when `VICTORY_RECORD` is set (a high beep).
  `VICTORY_REPLAY_WARP=1` replays a capture on WARP, which runs waves of 64, to compare a wave64 program's
  native form with the two-waves-of-32 form NVIDIA cards get (the `_Tiled` volumetrics work).
- Crowds and fights: 14-17 FPS in the last session (were 10-15).
