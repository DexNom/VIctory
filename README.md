# cool_fox (CGBJ Runtime Continuation)

[![CI](https://github.com/DoubleGhosty/cool_fox/actions/workflows/ci.yml/badge.svg)](https://github.com/DoubleGhosty/cool_fox/actions/workflows/ci.yml)
[![PortWatch VI](https://img.shields.io/badge/PortWatch%20VI-91.5%25%20Playable-brightgreen)](https://portwatch-vi.xyz/#effort-cgbj)

Active community continuation and development branch of `cool_fox` (the CGBJ runtime, ranked **#1 / 91.5% Playable** on PortWatch VI).

This build unites the cutting-edge optimizations from leading PortWatch projects (`avatarmanz`, `codec-d`, `DexNom`, `Belobug`, and community contributors) into a unified, verified runtime for both AMD Radeon and NVIDIA GeForce GPUs.

---

## Key Features & Improvements

- **Pareto Memory Residency & 32 GB RAM Support:** Adaptive GPU memory residency limits, preventing WDDM paging exhaustion (`0x8007000E`) on mid-range and 32 GB systems.
- **Shader LOD Slot Fix (PR #2):** Leaves unused LOD slots undefined in array texture coordinate queries, resolving DXIL validator errors on retail shaders.
- **NVIDIA & Video Stream Synchronization (PR #6):** Synchronous flush for decoded video frame copies, eliminating stutter and repeated frames in opening videos during multithreaded recording (`VICTORY_THIN=1`), and constant ambient lighting (`VICTORY_AMBIENT=constant:0.8`).
- **Wine / Proton & DXIL Robustness (PR #10):** Code-generated root signatures replacing fragile comment pragmas for vkd3d-shader, single-latch DXIL loop CFG normalization, and 256 MiB pipeline worker stack.
- **Voxel Visibility Recovery:** Restores wave64 mask provenance when restored from scalar copies, enabling foliage and volumetric voxel visibility shaders to pass compilation.
- **Custom Resolution & Scaling:** `VICTORY_OUTPUT=1920x1080` (or `2560x1440`) and `VICTORY_RENDER_SCALE=0.667..1.0` with the 4K backbuffer pool check bypass.
- **Frame Rate Unlock:** `VICTORY_FRAME_INTERVAL_MS=16.667` (60 FPS) or `8.333` (120 FPS) while keeping game simulation clock strictly at 1.00x.
- **Renderer Port 5 (Zero-Copy CBV):** `VICTORY_PORT_CBV=1` reads constant buffers as raw bytes in-place, eliminating ~22,000 buffer copies per frame.
- **Launcher modes and a shareable profile code:** `GTA6.exe` opens with six measured presets (Recommended, Sharp, Fast, Modest PC, Ultra Low, Classic) and a `s=...;b=...` profile string you can copy, paste, or pass on the command line. The Recommended preset draws at 0.6667 and lets the title's own upscaler rebuild 720p — measured here at 90.1 ms/frame against 99.2 ms at 1.0, with no visible artifact.
- **Host RAM pressure lines as knobs:** `VICTORY_PRESSURE_FREE_MB` and `VICTORY_PRESSURE_CLEAR_MB` set the free-RAM lines that start and clear host pressure, with the hysteresis clamped so the state cannot flap between one-second samples. Defaults are the 3 GiB / 5 GiB every measured run used.

---

## How to Run

You need Windows 10/11 x64, a CPU with AVX2, a Direct3D 12 GPU with shader model 6.6 and mesh shaders, Rust (1.98+), MSVC build tools, and your own legal Xbox Series dump of the game.

### Build from Source

```powershell
git clone https://github.com/DoubleGhosty/cool_fox.git
cd cool_fox
cargo build --release --locked -p victory
```

### Launch

```powershell
$env:VICTORY_GAME_DIR = 'D:\Games\MyDump'
.\Play-VIctory.cmd
```

For 4GB / 8GB VRAM laptops or low-memory systems, you can also launch with:

```powershell
.\Play-VIctory-4GB.cmd
```

### Launcher modes

`GTA6.exe` carries the whole thing: the runtime, the Agility SDK, the profile and
the launcher. It opens with a mode picker.

| mode | scale | buffers MB | targets MB | card MB | windows MB | ripe | RAM pressure |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Recommended | 0.6667 | 768 | 768 | 128 | 96 | 4 | runtime default |
| Sharp | 1.0 | 768 | 768 | 128 | 96 | 4 | runtime default |
| Fast | 0.5 | 768 | 768 | 128 | 96 | 4 | runtime default |
| Modest PC | 0.5 | 512 | 512 | 96 | 64 | 2 | 1024 / 2048 |
| Ultra Low | 0.5 | 384 | 384 | 64 | 48 | 2 | 1536 / 3072 |
| Classic | 1.0 | 768 | 768 | 128 | 96 | 4 | runtime default |

Only Recommended and Sharp were measured on the reference machine (Ryzen 5
5600G, RTX 3060 Ti 8 GB, Windows 10). Recommended is the 0.6667 run: GPU 34.0 to
13.7 ms and frame 99.2 to 90.1 ms at 6,191 draws, no visible artifact. Sharp is
the profile before that change, for a side-by-side.

The **Codigo do perfil** button copies the current mode as one line and accepts a
pasted one:

```
s=scale;b=buffers;t=targets;m=card;w=windows;p=percent;r=ripe;g=pressure;c=clear
```

A code with a missing key or an out-of-range value is rejected whole — the game
never starts with half the values at zero. From a shell:

```powershell
GTA6.exe --code                                     # the current mode's code
GTA6.exe "--code=s=0.5;b=512;t=512;m=96;w=64;p=90;r=2;g=1024;c=2048"
```

---

## Upstream & Acknowledgements

- **codec-d / CGBJ:** Original `cool_fox` runtime and pareto residency architecture.
- **AvatarManz (VIctory-7900XTX):** Multi-threaded command list recording, framerate unlocking, and renderer ports.
- **DexNom:** Wave64 lowering, scalar recovery, and shader structure passes.
- **Belobug:** Ambient lighting and integer target blend protections.
- **Marvisak & fxceless:** Foundational VIctory compatibility layer.

**Legal notice:** This repository contains no copyrighted game code, assets, or executables. All code is licensed under GPL-3.0-or-later.
copy victory.example.toml victory.toml      # then set your paths, outside this folder
cargo build --locked --profile diag -p victory --features diagnostics
scripts\play.ps1 -Exe <path of your copy's executable>
```

`scripts\play.ps1 -Safe` leaves out the newest, faster ways; try it first when
something is wrong. `-RayTracing` turns the title's ray-traced indirect light on.

Before the first run, ahead of time (both can be stopped and go on later):

```powershell
target\diag\victory.exe shaders <the shader archives>   # every shader translated, minutes
target\diag\victory.exe warm --used                     # pipelines earlier runs drew with, for a new driver or build
```

Commands, debugging switches and upstream's own description:
[docs/README-fxceless.md](docs/README-fxceless.md). Every `VICTORY_...` switch is
described in a comment where it is read.

## What is different here

In short; the reasons and the measurements are in [docs/findings.md](docs/findings.md).

- **List calls are written down, not carried out, on the title's threads**
  (`VICTORY_THIN`): eight threads of ours translate them.
- **Submissions are prepared by a thread of ours and joined**
  (`VICTORY_ASYNC_SUBMIT`, `VICTORY_JOIN_PARTS`), with what can be spread over
  threads spread: refreshes, compares (AVX2), descriptor records, landings.
- **What shaders wrote comes back beside the write guards**, by the dwords that
  changed, on a thread of its own (`VICTORY_RELAY`, `VICTORY_LAND`).
- **Buffer fills that need bytes still on the card are done on the card**
  (`VICTORY_CARD_STEPS`) instead of waiting for it.
- **The title's writes are caught more cheaply**: one view opened at a fault,
  blocks that rest, places in the title's code that look before they write.
- **Textures go up on a copy queue of their own** and a slot shows one only after
  its upload ran, which also ended a device loss that came every 15 minutes or so.
- **Boats float**: textures that shaders write and the title's CPU reads go back
  to title memory (from fxceless and Geckomacabre, on by default here).
- **Frame record and replay** with per-pass times and shader variants compared
  turn by turn ([docs/replay.md](docs/replay.md)).

## Credits

This is a tree of other people's work with more on top. The history is kept, and
commits taken from elsewhere name the commit and its author.

- **[VIctory](https://github.com/Marvisak/VIctory)** by Marvisak and its
  contributors: the loader, the runtime services, the Direct3D 12 back end and
  the shader recompiler.
- **[fxceless/victory-fork](https://github.com/fxceless/victory-fork)**: the fork
  this one started from, and the people whose pull requests it carries.
- **[DexNom/VIctory](https://github.com/DexNom/VIctory)**: ray tracing fixes,
  cube gradients, sparse textures.
- **[Belobug/VIctory-Belobug-AMD](https://github.com/Belobug/VIctory-Belobug-AMD)**:
  the ambient light idea, pipeline workers, and work by romainhedouin.
- **[FQY55kai/ZJOU-VICTORY](https://github.com/FQY55kai/ZJOU-VICTORY)**.
- **[myokex/victory-amd](https://github.com/myokex/victory-amd)**: the guard over
  every view of a page (with Spexivus, joroyt and tightfall), and Joshua Odom's
  reads into title memory.
- **Geckomacabre** and **cxy0072019-sketch**: the texture read-back.
- **[XCL](https://github.com/ivall/xcl)** (GPL-2.0-only) was read for how the same
  title behaves under another layer; no code of it is here.
- This fork's changes were made with [Claude Code](https://claude.com/claude-code).

## License

GPL-3.0-or-later, as VIctory. See [LICENSE](LICENSE); the one file that is not
covered is named in [docs/asset-policy.md](docs/asset-policy.md).

---

## Perfil de 16 GB (português)

Esta base foi medida numa máquina de 16 GB (Ryzen 5 5600G, 16 GB DDR4, RTX 3060
Ti 8 GB, Windows 10, 720p). O que está ligado, por quê, e os números:
[OTIMIZACOES.md](OTIMIZACOES.md). Os controles: [CONTROLES.md](CONTROLES.md).

**O caminho curto** — sem compilar nada:

1. `tools\pack\build.ps1 -Out <pasta>` monta o `GTA6.exe` (runtime + Agility SDK
   + docs embutidos). Abra o `GTA6.exe`, aponte a pasta do dump uma vez, e ele
   guarda o caminho no `game-path.txt` ao lado.
2. Na janela: **Jogar 720p**. Na primeira vez, antes, **Preparar (compilar)**
   depois de uma sessão com `--book` (ou use `GTA6.exe --book` uma vez).
3. Se for rodar o `victory.exe` direto, **a pasta `D3D12\` (com o
   `D3D12Core.dll` 1.620.2) precisa ficar ao lado dele** — sem ela o Windows 10
   recusa o root signature e nenhuma pipeline é criada.
4. Pelo PowerShell: `scripts\play-16gb.ps1 -Exe <game_scarlett_master.exe>
   -Build release`, e depois `scripts\prepare-16gb.ps1 -Exe <victory.exe>` para
   as pipelines ficarem no cache do driver antes de o jogo pedir.

O perfil inteiro mora em `scripts\profile-16gb.ps1`, um arquivo só: o
`play-16gb.ps1` e o `prepare-16gb.ps1` leem dele, e é isso que garante que o
cache foi aquecido com os mesmos ajustes que o jogo usa (a chave de uma host
pipeline inclui os ajustes do host).

**Launcher `GTA6.exe`.** `tools\pack\build.ps1` monta um executável único com o
runtime, o Agility SDK e os docs embutidos. Ele acha o dump, escreve o
`game-path.txt`, e abre uma janela com quatro páginas: **Jogar** (o herói com o
estado dos quatro passos, o caminho do jogo, a saída 720p/1080p, os sete modos e
as opções de arranque), **Desempenho** (o código do perfil e as tabelas
medidas), **Preparar** (os quatro passos, a barra e o log) e **Ajuda**. O visual
é uma cópia do launcher da Julia — mesma paleta, mesmo herói com órbitas,
mesmos cartões, mesma barra de estado —, com o nosso perfil dentro; o
`reference/` guarda o binário e os fontes dela só para conferência. Sem
interface: `GTA6.exe --play`, `GTA6.exe --prepare`, `GTA6.exe --book`,
`GTA6.exe --install`, `GTA6.exe --code`.

**O que muda nesta máquina**

| variável | valor | por quê |
| --- | --- | --- |
| `VICTORY_OUTPUT` | `1280x720` | o menor modo do título |
| `VICTORY_RENDER_SCALE` | `1.0` | 720p inteiro, sem o halo do upscaler |
| `VICTORY_LIBRARY` | `1` | a biblioteca de pipelines em disco |
| `VICTORY_CARD_ROOM_FOR_TARGETS_MB` | `512` | menos VRAM reservada, mais textura |
| `VICTORY_TEXTURE_ROOM_PERCENT` | `95` | textura até 95% da VRAM |
| `VICTORY_THIN` | `1` | tradução de lista em 8 threads |
| `VICTORY_PORT_CBV` | `1` | o buffer de constantes lido no lugar |

**Não defina `VICTORY_TEXTURE_BUDGET_MB`.** Ele substitui a checagem de VRAM por
um teto fixo, e o mundo (que pede ~4 GB de textura) fica sem textura.

**Não use `scripts\play.ps1 -Safe` nesta máquina.** Medido: 3.000 a 8.800
ms/quadro contra 460 a 730 ms do perfil padrão.

