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

## Política de porte

1. Nada entra sem medição. O chão atual é ~88-90 ms/frame (11,3 fps) com
   `VICTORY_VIEW_THREADS=5`.
2. Um patch por vez. Dois switches ao mesmo tempo já custaram um soak recusado
   (`soak14`, 140,7 ms).
3. O que quebra textura, áudio ou estabilidade fica de fora, mesmo que dê fps.
4. Crédito no comentário do commit e no `CREDITS` do arquivo alterado.
