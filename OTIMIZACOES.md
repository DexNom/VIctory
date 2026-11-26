# O perfil de 16 GB — o que está ligado, por quê, e o que foi medido

Este fork é o **merge de dois trabalhos** por cima da base
`Belobug/VIctory-Belobug-AMD` (branch `a-publier`, `cd8dfc0`):

- `avatarmanz/VIctory-7900XTX` (`126acb3`): os cinco ports do renderer, o
  `VICTORY_PORT_CONSTANTS_ORDER`, o `VICTORY_AMBIENT_RT` e a documentação de
  medição. É o fork que levou o quadro de 280 ms para ~45 ms no 7900 XTX.
- `Belobug`: FSR4 4.1.2, o prelúdio de texturas e o alvo RX 9070 XT.
- Nossos commits: Agility SDK 1.620, mouse, cache de pipeline opcional,
  `scripts/play-16gb.ps1`, launcher `GTA6.exe` e esta documentação.

O `GTA6.exe` já abre com o perfil abaixo; não precisa configurar nada.

A máquina de referência: Ryzen 5 5600G (6c/12t), 16 GB DDR4-3200, GeForce RTX
3060 Ti 8 GB, Windows 10 19045, janela 1280x720, console Series S.

---

## O que o jogo precisa, em números

Medido no `victory.err` com `VICTORY_LOG=info` e `VICTORY_MEMORY_CATEGORIES=1`:

| item | valor |
| --- | --- |
| memória privada do processo no pico | 22 GB |
| working set (o que está de fato na RAM) | 6,4 GB |
| VRAM no mundo | 6,8 de 7,0 GB |
| primeiro quadro do mundo | ~3 minutos depois do launcher |
| quadro no mundo | 460 a 730 ms (1,4 a 2,2 fps) |
| quadro no menu | 34 ms (29 fps) |
| GPU ocupada por quadro | ~107 ms (30%) |
| pipelines no cache | 2.323 carregados, 237 MB |

O título pede 20 a 30 GB de memória privada e o fork recomenda 64 GB. Em 16 GB
o Windows pagina: o processo fica com 22 GB de commit e 6,4 GB residentes, e é
por isso que cada chamada de lista custa ~67 µs aqui contra ~6 µs num 12900K.

---

## O que o merge mudou

**A tela preta acabou.** Antes do merge o build instalado morria em
`host GPU device removed` com `DXGI_ERROR_INVALID_CALL` durante o carregamento
do mundo. Com os ports do `avatarmanz` o mundo renderiza: a cena do prólogo
aparece limpa, com luz, personagem e sombra, e o processo só termina quando o
próprio título pede para suspender.

O que **não** mudou: o custo por quadro. O gargalo desta máquina é tradução de
lista na CPU, não desenho na placa.

---

## Onde o quadro vai (mundo, 546 ms)

| etapa | ms |
| --- | --- |
| tradução das listas (`VICTORY_THIN`, 8 threads) | 3.257 ms de CPU somada → ~550 ms de parede em 6 núcleos |
| passos das listas no thread de submissão | 105 |
| varredura do view heap (`looking at slots`) | 97 a 119 |
| snapshots | 65 |
| cópias (ida) | 40 a 130 |
| cópias de volta (read-back do write watch) | 60 a 120 |
| GPU | 107 |

35 a 48 mil chamadas de lista por quadro, 188 a 223 listas, 318 a 477 pedaços.
É o preço de emular o console: cada chamada é traduzida para D3D12 na CPU antes
de virar trabalho para a placa. Num 12900K são 230 ms somados; aqui são 3 a 5
segundos, e é isso que separa 45 ms de 550 ms.

---

## As três coisas que destravam

### 1. O cache de pipelines

`VICTORY_PIPELINE_CACHE=1`

Sem ele, cada execução recompila ~1.200 pipelines de shader, a ~1 por segundo
com 10 threads, e cada pipeline custa ~4 MB de memória de driver que não volta.
O cache fica em `%LOCALAPPDATA%\VIctory\cache\pipelines\host-pipelines.bin`
(~237 MB) e cresce a cada execução até cobrir o jogo.

**A primeira execução ainda compila.** A segunda é a rápida. Se você apagar o
`.bin`, volta ao começo.

### 2. A resolução que o título acredita

`VICTORY_OUTPUT=1280x720` · `VICTORY_UPSCALER=6` · `VICTORY_RENDER_SCALE=1.0`

O título reporta a TV como **3840x2160** por padrão e desenha 2560x1440 dentro
de uma janela 1080p: quatro vezes os alvos de render, quatro vezes as cópias,
quatro vezes a memória, para pixels que são jogados fora no present.

`VICTORY_OUTPUT` é o único que muda isso — o título casa contra a própria lista
de modos (`1280x720`, `1664x936`, `1920x1080`, `2560x1440`, `3840x2160`).
`VICTORY_DISPLAY_MODE` não serve: ele muda o que o host reporta, e o título não
olha para isso.

Com escala `1.0` ele desenha 720p inteiro e não há upscaling nenhum: a imagem
sai dura de propósito, sem o halo de nitidez que a escala 0.5 produzia. A placa
está 70% ociosa de qualquer jeito — o que manda no quadro é a CPU.

**Não suba para 1080p nesta placa.** A 720p a VRAM já está em 6,8 de 7,0 GB;
os alvos 2,25x maiores derramam textura para a RAM do sistema, que já vive com
300 MB livres. Ficaria mais bonito e mais lento.

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

## Os ports do avatarmanz: o que valeu nesta máquina

Os cinco ports vêm desligados por padrão no fork de origem. Medidos aqui, um a
um, no mesmo trecho do mundo:

| port | o que faz | veredito nesta máquina |
| --- | --- | --- |
| `VICTORY_PORT_DESCRIPTORS=1` | troca a varredura de ~600.000 slots por um patch nas stores do título | **inerte**: a varredura continuou em 119 ms por quadro. O patch de byte-check não casou com a versão 1.0.2.1 |
| `VICTORY_PORT_GUARD=1` | lock dos write guards por bloco de 64 KiB | inerte junto (mesma run) |
| `VICTORY_PORT_JOURNAL=1` | journal dos heaps em 64 partes | inerte junto (mesma run) |
| `VICTORY_PORT_CBV=1` | shaders leem constant buffers no lugar, como bytes crus: ~22.000 fills por quadro viram leitura de GPU | **em teste**. Muda a chave do cache de shaders: exige re-traduzir os 25.694 shaders antes (`victory shaders`) |
| `VICTORY_PORT_CONSTANTS=1` | os 174 MB de constantes lidos pela placa onde o título escreve: sem upload, sem fill | em teste. Risco de VRAM com a placa em 6,8/7,0 GB |

`VICTORY_PORT_CONSTANTS_ORDER` é outro: vem **ligado por padrão** no merge e
conserta árvores e palmeiras piscando e a luz que oscilava entre clara e escura.

---

## O resto do perfil

| variável | valor | por quê |
| --- | --- | --- |
| `VICTORY_OUTPUT` | `1280x720` | o menor modo do título |
| `VICTORY_RENDER_SCALE` | `1.0` | 720p inteiro, sem upscaler |
| `VICTORY_UPSCALER` | `6` | o upscaler do título, não o FidelityFX |
| `VICTORY_PIPELINE_CACHE` | `1` | o `.bin` de 237 MB |
| `VICTORY_PIPELINE_WORKERS` | `10` | um por thread do 5600G |
| `VICTORY_TEXTURE_ROOM_PERCENT` | `95` | textura até 95% da VRAM |
| `VICTORY_CARD_ROOM_FOR_TARGETS_MB` | `512` | menos VRAM reservada, mais textura |
| `VICTORY_MEMORY_CARD_MB` | `2048` | espelho da memória do título na placa |
| `VICTORY_CARD_WINDOWS_MB` | `512` | as janelas desse espelho |
| `VICTORY_CARD_BUFFERS_MB` | `256` | buffers na placa |
| `VICTORY_KEEP_SNAPSHOTS_MB` | `32` | snapshots para o read-back |
| `VICTORY_UPLOAD_AHEAD` | `0` | não duplica textura antes da hora |
| `VICTORY_PLACED_FILLER` | `1` | duas texturas iguais com filler diferente viram uma |
| `VICTORY_RESIDENCY` | `1` | abaixo de 3 GiB livre, prefetch de 64 MiB por quadro |
| `VICTORY_THIN` | `1` | tradução de lista em 8 threads |
| `VICTORY_JOIN_PARTS` | `8` | listas cortadas em partes, enviadas juntas |
| `VICTORY_RELAY` | `2` | o caminho rápido de submissão do fork |
| `VICTORY_D3D12_ADAPTER` | `NVIDIA` | a escolha nunca cai na integrada |
| `VICTORY_MOUSE_LOOK` | `1` | olhar com o botão direito |

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

- **16 GB é o piso, não o recomendado.** O processo chega a 22 GB privados; o
  Windows segura com o arquivo de paginação, mas o mundo leva ~3 minutos para
  carregar e roda a 1,4–2,2 fps nesta CPU.
- **O teto é a CPU, não a placa.** A GPU fica em 30% porque só recebe 2 quadros
  por segundo. Não existe knob de GPU que conserte isso: o trabalho é traduzir
  chamada de lista, e é trabalho de CPU.
- **O heap de descritores é menor na NVIDIA.** O título pede 1.245.184 views;
  em AMD o driver aceita 2.097.152, em NVIDIA o teto é 1.000.000 (737.856
  úteis depois dos slots dinâmicos). As views acima disso leem o slot nulo. O
  título usa até ~720.000, então cabe — apertado.
- **`raw twins` não existe na NVIDIA.** É o caminho que o fork usa em RDNA para
  ler buffer por índice sem cópia; aqui `raw_views` fica desligado.
- **195 shaders não traduzem** de 25.694 (containers sem programa de vertex,
  pixel, compute ou mesh). São geometria não usada.
- **`amd_fidelityfx_upscaler_xbox.dll` é recusado no primeiro pedido** e
  carregado depois como DLL do título. É normal no log.
