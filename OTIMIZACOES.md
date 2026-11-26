# O perfil de 16 GB — o que está ligado, e por quê

Este fork é a base `Belobug/VIctory-Belobug-AMD`, branch `a-publier`, com o
Agility SDK, o mouse e o cache de pipelines opcional portados por cima. O
`GTA6.exe` já abre com o perfil abaixo; não precisa configurar nada.

A máquina de referência: Ryzen 5 5600G, 16 GB, GeForce RTX 3060 Ti 8 GB,
Windows 10 19045, janela 1280x720, console Series S.

---

## O que o jogo precisa, em números

Medido no `victory.err` com `VICTORY_LOG=info`:

| item | valor |
| --- | --- |
| memória privada do processo no pico | 18 a 23 GB |
| alocação de driver por pipeline | ~4 MB (1.200 pipelines = 4,5 GB) |
| texturas do mundo | ~29.000 na fila, ~1 GB enviado no primeiro minuto |
| VRAM no primeiro quadro do mundo | 6,5 de 7,1 GB |
| primeiro quadro do mundo | 3 a 5 minutos depois do launcher |

O título pede 20 a 30 GB de memória privada e o fork recomenda 64 GB. Em 16 GB
o Windows pagina, e é aí que o carregamento trava: com 1 a 2 quadros por
segundo, o streaming de textura nunca alcança a fila.

---

## As três coisas que destravam

### 1. O cache de pipelines

`VICTORY_PIPELINE_CACHE=1`

Sem ele, cada execução recompila ~1.200 pipelines de shader, a ~1 por segundo
com 10 threads. É meia hora só nisso, e cada pipeline custa ~4 MB de memória
de driver que não volta. O cache fica em
`%LOCALAPPDATA%\victory\cache\pipelines\host-pipelines.bin` (~170 MB) e cresce
a cada execução até cobrir o jogo.

**A primeira execução ainda compila.** A segunda é a rápida. Se você apagar o
`.bin`, volta ao começo.

### 2. A resolução que o título acredita

`VICTORY_OUTPUT=1280x720` · `VICTORY_UPSCALER=6` · `VICTORY_RENDER_SCALE=0.5`

O título reporta a TV como **3840x2160** por padrão e desenha 2560x1440 dentro
de uma janela 1080p: quatro vezes os alvos de render, quatro vezes as cópias,
quatro vezes a memória, para pixels que são jogados fora no present.

`VICTORY_OUTPUT` é o único que muda isso — o título casa contra a própria lista
de modos (`1280x720`, `1664x936`, `1920x1080`, `2560x1440`, `3840x2160`).
`VICTORY_DISPLAY_MODE` não serve: ele muda o que o host reporta, e o título não
olha para isso.

Com `1280x720` e escala `0.5` ele desenha **640x360** e sobe para 720p com o
upscaler dele mesmo (`6`). Não use `VICTORY_FFX_SCALE` (o FidelityFX, `7`): a
0.5 o contador de jitter volta 0 e o título divide por ele 49 s depois.

### 3. O teto de textura que não existe

**Não defina `VICTORY_TEXTURE_BUDGET_MB`.**

Esse knob parece um limite de memória, mas no código ele substitui a checagem
de VRAM:

```rust
if let Some(limit) = limit {
    return filled < limit;   // ignora a VRAM por completo
}
```

Com um teto de 2 GB o host para de subir textura em 2 GB e o mundo — que pede
~4 GB — fica **sem textura**, não mais leve. Foi o que manteve a tela preta nas
primeiras execuções. Sem o knob, quem manda é a VRAM
(`VICTORY_TEXTURE_ROOM_PERCENT=95`).

---

## O resto do perfil

| variável | valor | por quê |
| --- | --- | --- |
| `VICTORY_OUTPUT` | `1280x720` | o menor modo do título |
| `VICTORY_RENDER_SCALE` | `0.5` | 640x360 interno |
| `VICTORY_UPSCALER` | `6` | o upscaler do título, não o FidelityFX |
| `VICTORY_PIPELINE_CACHE` | `1` | o `.bin` de 170 MB |
| `VICTORY_PIPELINE_WORKERS` | `10` | um por thread do 5600G |
| `VICTORY_TEXTURE_ROOM_PERCENT` | `95` | textura até 95% da VRAM |
| `VICTORY_CARD_ROOM_FOR_TARGETS_MB` | `512` | menos VRAM reservada, mais textura |
| `VICTORY_MEMORY_CARD_MB` | `2048` | espelho da memória do título na placa |
| `VICTORY_CARD_WINDOWS_MB` | `512` | as janelas desse espelho |
| `VICTORY_CARD_BUFFERS_MB` | `256` | buffers na placa |
| `VICTORY_KEEP_SNAPSHOTS_MB` | `32` | snapshots para o read-back |
| `VICTORY_UPLOAD_AHEAD` | `0` | não duplica textura antes da hora |
| `VICTORY_D3D12_ADAPTER` | `NVIDIA` | a escolha nunca cai na integrada |
| `VICTORY_MOUSE_LOOK` | `1` | olhar com o botão direito |
| `VICTORY_THIN`, `VICTORY_RELAY=2`, `VICTORY_INDIRECT_WINDOW` … | `1`/`2` | os caminhos rápidos do próprio fork |

Qualquer uma pode ser sobrescrita pelo ambiente do Windows: o launcher deixa o
que estiver definido vencer.

---

## Rodar sem o launcher

```powershell
$env:VICTORY_OUTPUT = '1280x720'
$env:VICTORY_PIPELINE_CACHE = '1'
.\victory.exe run "C:\...\game_scarlett_master.exe" --execute --console series-s
```

Ou use o perfil pronto do fork:

```powershell
.\scripts\play-16gb.ps1 -Exe "C:\...\game_scarlett_master.exe" -Build release
```

---

## Limites conhecidos

- **16 GB é o piso, não o recomendado.** O processo chega a 18–23 GB privados;
  o Windows segura com o arquivo de paginação, mas a 3–5 minutos por mundo
  carregado.
- **O heap de descritores é menor na NVIDIA.** O título pede 1.245.184 views;
  em AMD o driver aceita 2.097.152, em NVIDIA o teto é 1.000.000 (737.856
  úteis depois dos slots dinâmicos). As views acima disso leem o slot nulo. O
  título usa até ~720.000, então cabe — apertado.
- **O fork foi depurado numa máquina AMD.** O código tem caminhos escolhidos
  por sondagem do driver (`raw_twins`, `UNCAPPED_VIEWS`), e em NVIDIA eles
  ficam desligados. Não é o que apaga a tela, mas é a diferença de vendor real.
- **`amd_fidelityfx_upscaler_xbox.dll` é recusado no primeiro pedido** e
  carregado depois como DLL do título. É normal no log.
