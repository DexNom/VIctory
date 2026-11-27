# Upstreams vendorizados

Dois forks do VIctory vivem dentro deste repositório como **snapshots de código-fonte**, para
consulta e porte. Nenhum dos dois é compilado pelo `cargo build` da raiz: eles ficam em pastas
próprias, com o `Cargo.toml` deles, e não entram no workspace.

A regra é simples: **o runtime que roda é o da raiz** (`crates/victory-*`). O que vem de fora é
lido, comparado e portado arquivo por arquivo, com medição antes e depois. Nunca sobrescrito em
bloco.

## `scarlettport/`

| | |
|---|---|
| origem | https://github.com/ZigZagBoi1/ScarlettPort |
| commit | `5c34a2d` (v0.5.4, snapshot público) |
| licença | `engine/` GPL-3.0-or-later; `runtime/xcl/` GPL-2.0-only (programa separado, não linkado) |
| layout | `engine/` é o runtime, com os crates renomeados `scarlett-*` |

O que interessa de lá:

- `engine/crates/scarlett-gpu/` — passes de tela com workers próprios (`SCARLETT_SCREEN_WORKERS`),
  fila de texturas justa (`SCARLETT_FAIR_DUE`, `SCARLETT_TEX_SPAN_PCT`), `SCARLETT_REBAR`,
  `SCARLETT_PRECOMPILED_PSO`, `SCARLETT_WAVE32_LOWER`, `SCARLETT_OOM_BACKOFF`,
  `SCARLETT_COMMIT_GATE`, `SCARLETT_PREFETCH_CAP`, `SCARLETT_THIN`.
- `engine/crates/scarlett-shader/` — correções de tradução (`SCARLETT_SHADER_FIXES`), incluindo
  `image_bvh_intersect_ray` em DXIL inline.
- `patches/` — os patches crus, que são mais fáceis de portar do que o crate renomeado.
- `docs/RUNTIMES.md` — a lista de switches e o que cada um faz.

O `runtime/xcl/` (GPL-2.0-only) está aqui só como parte do snapshot. **Não portar código dele**
para o runtime da raiz: GPL-2.0-only não sobe para GPL-3.0-or-later.

## `cold-project-vi/`

| | |
|---|---|
| origem | https://github.com/felicienradio/cold-project-vi |
| commit | `b4ab36e` (0.3.11) |
| licença | GPL-3.0-or-later |
| layout | idêntico ao nosso: `crates/victory-*`, `.victory-root`, `Play-VIctory.cmd` |

Este é o parente próximo. Mesmos nomes de crate, mesma raiz de workspace, então o porte é
arquivo contra arquivo, sem tradução de nome.

O que interessa de lá:

- O motor "edupode"/"luz" — VRAM de 6-8 GB com ~40 000 texturas na placa, contra ~10 000 do cgbj.
- `0.3.11` — buffers de upload recusados e listas de prelude **repetidos depois que os blocos
  vazios dos pools saem**, explicitamente para PCs de **16 GB**. É a nossa máquina.
- `docs/LUZ-24FPS.md` — as notas de desempenho deles, em português.
- `launcher/`, `installer/`, `server/` — launcher próprio, instalador e um servidor de assets.

## Diff atual entre `cold-project-vi/crates` e `crates/`

| | |
|---|---|
| arquivos iguais | 134 |
| arquivos diferentes | 77 |
| só deles | 7 |
| só nossos | 8 |

Por crate, os diferentes: `victory-gpu` 49, `victory-shader` 18, `victory-hle` 6,
`victory-loader` 3, `victory-formats` 1.

É um merge viável — não é um fork reescrito.

## `onlycheat/`

| | |
|---|---|
| origem | https://github.com/Amdavea/onlycheat |
| commit | `081cf56` (onlycheat v1.0.2) |
| licença | GPL-3.0-or-later |
| layout | igual ao nosso, mais `runtime-rs-nvidia/` e `start-nvidia.bat` |

É o VIctory-NVIDIA (`trustno1-coder`, `d12eb1b`) com **DLSS e geração de quadros**, mais o
medidor de desempenho do autor (`scripts/medidor.ps1`, `janela.ps1`).

O que interessa de lá:

- **`victory-hle/src/modules/fidelityfx.rs`** e **`victory-gpu/src/streamline/fsr.rs`** — geração de
  quadros por **FSR 3**, que é o caminho que funciona em RTX 20/30 (DLSS FG exige RTX 40+).
- **`start-nvidia.bat`, variável `AUTOR`** — 30 switches medidos pelo autor. Dezoito deles não
  existem no nosso código:

  `WC_NO_BACK`, `SOFT_VIEWS`, `PRELUDE_UPLOADS`, `SHOW_NEW_LATER`, `POOL_SMALL`, `TABLED_REREAD`,
  `QUEUE_HOST_TEXTURES`, `HEAP_SYNC_FIRST`, `SYNC_IN_PLACE`, `SPANS_QUICK`, `SYNC_FINE`,
  `HEAP_SYNC_AT_SUBMIT`, `EARLY_DEPTH`, `LEAVE_OUT`, `FRAME_INTERVAL_US`, `DLSS`, `FRAMEGEN`,
  `MENU_FRAMEGEN`, `NATIVE_1440P`, `AMBIENT_SPECULAR`.

  Cinco deles (`HEAP_SYNC_FIRST`, `SYNC_IN_PLACE`, `SYNC_FINE`, `HEAP_SYNC_AT_SUBMIT`,
  `SPANS_QUICK`) são exatamente o `heap buffers`/`sync_heaps` — a parte mais cara do nosso quadro.
- **`VICTORY_LEAVE_OUT=<hash>,<hash>`** — a versão genérica do nosso water light: em vez de dois
  hashes fixos no código, uma lista por variável de ambiente.
- **`docs/bug-journal.md`** e **`docs/findings.md`** — o caderno de medição deles.

## Diff atual entre `onlycheat/crates` e `crates/`

| | |
|---|---|
| arquivos iguais | 149 |
| arquivos diferentes | 64 |
| só deles | 16 |
| só nossos | 6 |

## `ScarlettPort` v0.5.4 (o binário que Lewis elegeu o melhor até agora)

| | |
|---|---|
| pasta | `C:\Users\Administrator\Downloads\ScarlettPort-v0.5.4-win64` |
| fonte | https://github.com/ZigZagBoi1/ScarlettPort (tag `v0.5.4`) |
| licença | GPL-3.0-or-later |
| linhagem | VIctory → fxceless `main@4e17478` → ScarlettPort |
| snapshot aqui | `scarlettport/` (repo público, `engine/` = runtime empacotado) |

O que o zip entrega que o repo público **não** entrega: o `runtime/ScarlettPort-Runtime.exe`
é um build local `fx-cand4`, mais novo que `engine/` no repo. Os 9 switches do `C1` e os 4 do
`STABLE_050` existem no binário e não no fonte público — é por isso que portar deles exige
decompilar, não ler.

### O perfil que o Lewis usou

`%LOCALAPPDATA%\ScarlettPort\launcher.json` diz `"preset": "stable"`, `"engine": "world"`,
`"console_s": true`, `"raytracing": false`, `"upscaler": ""`, `"quality": "quality"`,
`"framegen": false`, `"mouse_look": true`, `"friendly": true`, e `"display": {"mode":
"fullscreen", "resolution": "800x600"}`.

`launcher/src/presets.rs` (`stable_env`) define o Stable como 9 fixos + `C1` (9) + `CAND4`
(18) + `STABLE_050` (4) = **40 variáveis**:

| grupo | variáveis |
|---|---|
| fixos | `OWN_UPSCALER=1`, `NO_WATCHDOG=1`, `RELAY=2`, `PIPELINE_WAIT_MS=0`, `LOG=warn`, `SYNC_PRECISE=1`, `READ_SETTLE_PRECISE=1` |
| `C1` | `PRECOMPILED_GRAPHICS`, `READFIX`, `COPY_BACK_ONLY_READBACK`, `NARROW_ROOT`, `NARROW_VIEWS`, `NARROW_BVH`, `PREP_BARRIERS`, `PREP_TABLES`, `PREP_REUSE` |
| `CAND4` | os 18 que já temos, menos `LEVELS_ON_DEMAND` (reprovado aqui) e `CARD_BUFFERS_MIN_KB=64` |
| `STABLE_050` | `SAVE_NO_WAIT=1`, `CARD_BIG_ROOM=1`, `GUARD_SINK=1`, `LOADS_WIDE=1` |

### O número que importa

`docs/RUNTIMES.md:59` e `docs/STATUS.md:23`: com o bloco verificado (`READFIX` +
`COPY_BACK_ONLY_READBACK` + `NARROW_*` + `PREP_*`), **as esperas de leitura por quadro caem de
~156 ms para ~26 ms**. É o mesmo lugar onde o nosso quadro gasta: `copying back` 13-15 ms e
`preparing: copies` 11-173 ms.

E o aviso, no `docs/TROUBLESHOOTING.md:52`: **`COPY_BACK_ONLY_READBACK` sozinho deixava a
imagem preta** ("left stale data in memory"). É o último da ordem de recuo deles.

### O que explica o "melhor de todos"

Não é fps. `docs/STATUS.md:7` publica **~4 fps no prólogo** num RTX 3060 — nós estamos em
11,3. O que o ScarlettPort tem e nós não é **cache construído**: 4,11 GB / 105.164 arquivos em
`%LOCALAPPDATA%\ScarlettPort\cache`, com `host-pipelines.bin` de 358 MB, `known-shaders.bin` de
198 MB, `known-pipelines.bin` de 50 MB e 3.543 hashes em `early-pipelines.txt`. 25.200 de 25.471
pipelines pré-construídas (`STATUS.md:36`).

O nosso cache: 19.564 arquivos / 0,78 GB, e o do launcher (`C:\GTA6-PC\cache`) com **1.726
shaders** e **sem `known-pipelines.bin`**. É essa a tela preta com áudio: em `soak22`, 1.644 de
2.192 draws por quadro sem pipeline de host, GPU 0,0 ms, 534 ms de quadro.

O passo que faltava está no `GTA6.cs`: o `--prepare` agora roda `victory shaders <shaders.rpf>`
antes do `warm --used`, como o "Build shaders" deles (`scarlett warm`, 25.889 shaders em 171 s).

### O cache deles serve no nosso runtime (medido em 27/11)

Os dois runtimes saem da mesma linhagem e o formato dos arquivos de cache é **o mesmo**,
byte por byte. Testado com o binário release desta árvore:

| arquivo | teste | resultado |
|---|---|---|
| `known-pipelines.bin` (48 MB, 83.561 pipelines) | `victory warm --used --limit 20` com o livro deles | 20 feitas, 0 sem host pipeline, 0 ruins |
| `known-shaders.bin` (189 MB) | o mesmo, lendo os containers deles | 20 feitas |
| `host-pipelines.bin` (341 MB) | os primeiros 48 bytes são idênticos aos do nosso | carrega no `CreatePipelineLibrary` (a validar em partida) |
| `early-pipelines.txt` (3.543) | união com o nosso (1.948) = 3.549 | 6 hashes só nossos |

O `known-shaders.bin` deles referencia 105.151 containers; o nosso tradutor gera ~26.000 a
partir do `shaders.rpf`. Os 20 pipelines do teste acharam os shaders no nosso cache — o
tradutor produz o mesmo container para o mesmo hash.

O que isso muda: em vez de aquecer 26.000 shaders e esperar o jogo pedir as pipelines, o
cache entra com **83.561 pipelines conhecidas e 341 MB de pipelines de host já compiladas**.
É a diferença de "melhor até agora" que o Lewis viu.

## Política de porte

1. Nada entra sem medição. O chão atual é ~88-90 ms/frame (11,3 fps) com
   `VICTORY_VIEW_THREADS=5`.
2. Um patch por vez. Dois switches ao mesmo tempo já custaram um soak recusado
   (`soak14`, 140,7 ms).
3. O que quebra textura, áudio ou estabilidade fica de fora, mesmo que dê fps.
4. Crédito no comentário do commit e no `CREDITS` do arquivo alterado.
5. O que vem do ScarlettPort entra pelo **mecanismo**, não pelo nome: `SCARLETT_*` vira
   `VICTORY_*` e o comentário diz de onde veio e o que foi medido.
