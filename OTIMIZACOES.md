# O perfil de 16 GB — o que está ligado, por quê, e o que foi medido

Esta base é o **`DoubleGhosty/cool_fox`** (`28ebb28`), que já unifica quatro
trabalhos: `avatarmanz` (os ports do renderer), `codec-d`, `DexNom` e
`Belobug`. Por cima dele: o mouse look, o perfil em funções, o launcher
`GTA6.exe` e esta documentação.

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
| pipelines no livro | 84.000 criadas, ~2.400 desenhadas |

O título pede 20 a 30 GB de memória privada e o fork recomenda 64 GB. Em 16 GB
o Windows pagina: o processo fica com 22 GB de commit e 6,4 GB residentes, e é
por isso que cada chamada de lista custa ~67 µs aqui contra ~6 µs num 12900K.

---

## O que o cool_fox conserta

Três consertos desta base atacam exatamente o que se via na base anterior
(`docs/bug-journal.md`):

1. **`resource.rs`: os guards de lane são restaurados em joins mortos.** O
   compute `CS_AlphTransmissionPrefilterMip` (`2da4beb386509b77`) era recusado
   em todo quadro; agora é feito. É a água e o vidro do prólogo.
2. **`Detached::release_tables()` + `settle_for_fence()`** (`host.rs`/`draw.rs`):
   slots dinâmicos presos pelo `bury_lag()` (16 submissions) esgotavam o pool de
   262k da NVIDIA e derrubavam draws. Era o frame drop e o flick.
3. **`residency.rs`: pressão leve separada de `critical_commit()`** (<1536 MiB).
   Antes dezenas de milhares de texturas travavam em `neutral()` mesmo com RAM
   sobrando. Era a fila de textura: **13.494 slots, 186 devidas por quadro,
   9.107 esperando memória, 15 subindo**.

O terceiro é o que mais importa aqui: a fila era a causa do flick, e agora a
admissão é pareto — com 32 GiB ou menos ela já limita o prefetch a 64 MiB por
quadro e solta staging parado, **sem knob**.

### O Agility SDK

O `cool_fox` não traz o export que carrega o Direct3D 12 1.620. Sem ele o
`d3d12.dll` do Windows 10 19045 (que para no Shader Model 6.5) é usado, e ele
recusa o root signature que o runtime monta:

```
root signature: Unsupported bit-flag set (root signature flags c00).
host root signature refused error=Parâmetro incorreto. (0x80070057)
```

Resultado: **nenhuma host pipeline é criada** — o `warm` reporta "2.438 have no
host pipeline" e o jogo fica sem desenho. O `apps/victory/build.rs` desta base
exporta `D3D12SDKVersion=620` e `D3D12SDKPath=.\D3D12\`, e a pasta `D3D12\`
com o `D3D12Core.dll` 1.620.2 anda ao lado do executável.

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

### 1. A biblioteca de pipelines

`VICTORY_LIBRARY=1` · `VICTORY_LIBRARY_STORE_LATER=1`

Sem ela, cada execução recompila ~1.200 pipelines de shader, a ~1 por segundo
com 10 threads, e cada pipeline custa ~4 MB de memória de driver que não volta.
A biblioteca fica em `%LOCALAPPDATA%\VIctory\cache\pipelines\` e cresce a cada
execução até cobrir o jogo.

`STORE_LATER` entrega a pipeline na hora e a grava junto com o save, em vez de
segurar o quadro esperando o disco.

**A primeira execução ainda compila.** A segunda é a rápida. Se você apagar os
`.bin`, volta ao começo.

**Compilar antes de jogar.** O livro guarda o que o jogo *já pediu*. O que o
jogo vai pedir no meio de uma cena ainda é compilado dentro do quadro — e
`docs/findings.md` conta **270 a 660 s de uma sessão com quadros acima de 200
ms, quase todos enquanto uma pipeline nova era feita**. Dois passos, uma vez
cada (ou o botão **Preparar (compilar)** / `GTA6.exe --prepare`):

```powershell
# 1. uma sessão que grava o livro de pipelines que o título criou
$env:VICTORY_PIPELINE_BOOK = '1'
scripts\play-16gb.ps1 -Exe <game_scarlett_master.exe>
# 2. traduz os shaders e fabrica as host pipelines das que os quadros desenham
scripts\prepare-16gb.ps1 -Exe <victory.exe> -Shaders <shaders.rpf>
```

O livro é `known-pipelines.bin` (as chaves) mais `known-shaders.bin` (o código
do título, 189 MB, fica no cache e não sai de lá); o `warm` roda em processos
próprios, de dois terços dos núcleos, e cada pipeline é solta em seguida — quem
guarda é o cache do driver.

O perfil é o mesmo dos dois lados (`scripts\profile-16gb.ps1`): a chave de uma
host pipeline inclui os ajustes do host, então aquecer com outro perfil fabrica
pipelines que o jogo não pede.

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

Nesta base a residência também não precisa de knob: `residency.rs` detecta
32 GiB ou menos e aplica o perfil de máquina pequena sozinho.

---

## Os ports do avatarmanz: o que valeu nesta máquina

| port | o que faz | veredito nesta máquina |
| --- | --- | --- |
| `VICTORY_PORT_DESCRIPTORS=1` | troca a varredura de ~600.000 slots por um patch nas stores do título | **inerte**: a varredura continuou em 119 ms por quadro. O patch de byte-check não casou com a versão 1.0.2.1 |
| `VICTORY_PORT_GUARD=1` | lock dos write guards por bloco de 64 KiB | inerte junto (mesma run) |
| `VICTORY_PORT_JOURNAL=1` | journal dos heaps em 64 partes | inerte junto (mesma run) |
| `VICTORY_PORT_CBV=1` | shaders leem constant buffers no lugar, como bytes crus: ~22.000 fills por quadro viram leitura de GPU | **a aposta**: ligado só depois da baseline limpa, porque muda a chave da pipeline e exige re-traduzir os 25.694 shaders |
| `VICTORY_PORT_CONSTANTS=1` | os 174 MB de constantes lidos pela placa onde o título escreve: sem upload, sem fill | adiado. Risco de VRAM com a placa em 6,8/7,0 GB |

`VICTORY_INDIRECT_REST=0` foi medido e descartado: **0,00 registros indiretos
vazios por quadro**, não há o que restaurar.

### O `-Safe` do fork não serve nesta máquina

O `scripts/play.ps1 -Safe` desliga `VICTORY_UNDER_WIDE` e `VICTORY_LAND_ECHO`,
que vêm ligados por padrão. Medido aqui, com o resto do perfil igual:

| perfil | quadro no mundo |
| --- | --- |
| padrão (`LAND_ECHO=1`, `UNDER_WIDE=1`) | 460 a 730 ms |
| `-Safe` (`LAND_ECHO=0`, `UNDER_WIDE=0`) | **3.000 a 8.800 ms** (0,1 a 0,4 fps) |

Com `LAND_ECHO=0` as cópias voltam para o caminho do quadro: 1.400 a 2.900 ms e
350 a 640 MB por quadro. Não é uma opção de conserto aqui — é 10x mais lento.

---

## O resto do perfil

| variável | valor | por quê |
| --- | --- | --- |
| `VICTORY_OUTPUT` | `1280x720` | o menor modo do título |
| `VICTORY_RENDER_SCALE` | `1.0` | 720p inteiro, sem upscaler |
| `VICTORY_UPSCALER` | `6` | o upscaler do título, não o FidelityFX |
| `VICTORY_LIBRARY` | `1` | a biblioteca de pipelines em disco |
| `VICTORY_LIBRARY_STORE_LATER` | `1` | grava com o save, sem segurar o quadro |
| `VICTORY_PIPELINE_WORKERS` | `10` | um por thread do 5600G |
| `VICTORY_TEXTURE_ROOM_PERCENT` | `95` | textura até 95% da VRAM |
| `VICTORY_CARD_ROOM_FOR_TARGETS_MB` | `512` | menos VRAM reservada, mais textura |
| `VICTORY_MEMORY_CARD_MB` | `2048` | espelho da memória do título na placa |
| `VICTORY_CARD_WINDOWS_MB` | `512` | as janelas desse espelho |
| `VICTORY_CARD_BUFFERS_MB` | `256` | buffers na placa |
| `VICTORY_KEEP_SNAPSHOTS_MB` | `32` | snapshots para o read-back |
| `VICTORY_UPLOAD_AHEAD` | `1` | sobe textura em threads nossas, à frente do quadro |
| `VICTORY_PLACED_FILLER` | `1` | duas texturas iguais com filler diferente viram uma |
| `VICTORY_THIN` | `1` | tradução de lista em 8 threads |
| `VICTORY_JOIN_PARTS` | `8` | listas cortadas em partes, enviadas juntas |
| `VICTORY_RELAY` | `2` | o caminho rápido de submissão do fork |
| `VICTORY_MOUSE_LOOK` | `1` | olhar com o botão direito |
| `VICTORY_MOUSE_SENSITIVITY` | `0.05` | radianos por pixel contado |

A residência (`residency.rs`) é automática: com 32 GiB ou menos ela já limita o
prefetch a 64 MiB por quadro e solta staging parado. Não existe
`VICTORY_RESIDENCY` nesta base.

Qualquer uma pode ser sobrescrita pelo ambiente do Windows: o launcher deixa o
que estiver definido vencer.

---

## Rodar sem o launcher

```powershell
$env:VICTORY_OUTPUT = '1280x720'
$env:VICTORY_LIBRARY = '1'
.\victory.exe run "C:\...\game_scarlett_master.exe" --execute --console series-s
```

Ou use o perfil pronto:

```powershell
.\scripts\play-16gb.ps1 -Exe "C:\...\game_scarlett_master.exe" -Build release
```

---

## Memória: o que a medição de 26/11 mudou

O perfil antigo tratava o *commit* do Windows como se fosse RAM. Não é: com
40 GB de arquivo de paginação o commit nunca acaba, e o processo chegou a
21,5 GB privados com 325 MB de RAM livre — o driver recusou alocar, a lista
não fechou (`0x8007000E`) e o cartão saiu.

O que passou a valer:

- **`residency` lê o menor entre commit e RAM livre.** Todo portão do arquivo
  (reserva de upload, reserva de crescimento, estado crítico) usava
  `ullAvailPageFile`. Agora usa `min(ullAvailPageFile, ullAvailPhys)`, e o
  estado crítico dispara abaixo de 1,5 GB efetivos.
- **Janelas fora de buffers não nascem abaixo de 1 GB livre.** Eram 4,6 GB de
  cópias num PC de 16 GB. A leitura devolve zeros até a RAM voltar, e a janela
  é feita depois.
- **O pass de texturas não varre slot nenhum quando a placa não tem sala.**
  Era 43 a 176 ms de quadro lendo 48 KiB por slot para não subir nada.
- **`VICTORY_FILL_TITLE=2`.** O anel de constantes do título é reaproveitado,
  então os dois testes do `=1` nunca passavam e 22 a 24 ms por quadro iam em
  espera. `=2` deixa só o tamanho decidir (até 64 KiB).

### Memória do título em place: o censo

`VICTORY_INPLACE_PROBE=1` mediu o que poderia ser aberto como heap da placa,
sem cópia: 3.410 buffers de 736 MB, 3.281 de 217 MB privados commitados
inteiros, e 37.468 views de páginas físicas somando 6,5 GB. Mas o pool do
título é uma seção `SEC_RESERVE` (3.977 MB numa view só), e
`OpenExistingHeapFromFileMapping` recusa seção reservada, enquanto abrir por
endereço trava cada página na RAM e impede o título de descommitar. Não é
caminho para o pool que faz streaming — e o probe em si custa 256 MB travados.

## Perfil de 16 GB: o que foi medido (cf-19 em diante)

O mundo aberto nao crasha mais e o quadro caiu de 83-100 ms para 57,5 ms.

| | antes | depois |
|---|---|---|
| fps no mundo | 10-11,7 | **17,4** |
| ms por quadro | 83-100 | **57,5** |
| `0x8007000E` | 21.239 slots esperando memoria | **0** |
| `device removed` | frequente | **0** em 600 s |

O quadro de 57,5 ms se divide em 21,4 ms do proprio titulo (logica dele, entre
o present e a primeira lista), ~17 ms de trabalho nosso e 19,2 ms de placa. A
placa **nao** e o gargalo: ela roda escondida atras da preparacao (estagio 3 do
`VICTORY_LATE_WAIT`, ligado pelo `VICTORY_RELAY=1`).

- **Os tres portoes de RAM eram a regressao.** Com 0,3-2,0 GB livres no regime,
  qualquer piso perto de 1 GB fica fechado para sempre: 21.239 texturas paradas
  em "waiting for their memory" e janelas fora de buffers que nunca nasciam
  (slot lendo zero - era o piscar). Pisos agora em 192-384 MB, que ainda
  segura o `0x8007000E`.
- **`VICTORY_RECHECK_MB=48`.** A rechecagem de uma textura ja subida le 48 KiB
  da memoria dela para uma impressao digital. Milhares por quadro liam centenas
  de MB (35 ms) para achar as poucas que o titulo mudou depois do upload. O
  resto da fila espera o proximo quadro: sao os mais antigos, e os uploads ja
  sao limitados pelos bytes de staging.
- **Staging limitado pela RAM livre** (`pool::staging_most`, 2/4/16 blocos) e
  devolvido antes de recusar: os 1,86 GB de upload write-combined sumiram.
- **Build sem `diagnostics`.** O binario de medicao paga dois `Instant::now()`
  e varios atomicos em cada uma das 3.279 copias de buffer por quadro. O de
  release tem 11,5 MB contra 15,9 e mantem fps/ms/draws no titulo da janela.
- **Nenhuma varredura de heap morta por copia.** `in_shader_heap` pegava o lock
  de leitura e varria todos os heaps registrados so para alimentar o diario de
  portas, que esta desligado: agora esta atras do interruptor dele.

## Estabilidade: o que estava travando o quadro

O número de fps esconde o problema. A conta do quadro (run de 17,4 fps, 57,5 ms)
mostra onde ele vai:

| fase | ms/quadro |
| --- | --- |
| o título, de um *present* até a primeira chamada de lista | 21,4 |
| preparo nosso no thread que grava (passos das listas) | 19,3 |
| espera pelos *flights* (retorno da placa) | 15,7 |
| do último *submit* até o *present* (retorno de 5,5 MB) | 14,6 |
| espera pelos registros dos heaps no fim | 5,0 |

Dentro dos 19,3 ms: **12,7 ms em refreshes**, e **4.109 de 5.068 refreshes não
copiam nada** — o filtro que os descarta antes (`Shadow::nothing_new`, três
locks e uma comparação de página) só rodava abaixo de 256 spans, e o mundo
despacha 5.068. Acima disso o trabalho ia inteiro para as threads.

Três mudanças, todas medidas ou justificadas pela conta acima:

- **`FEW` de 256 para 8192** (`VICTORY_REFRESH_FEW` move): o filtro serial
  descarta 81% dos spans antes de eles virarem trabalho de thread. É a mudança
  de maior efeito esperado desta rodada.
- **O catch-up não triplica mais o *span* do passe de textura.** Ele triplicava
  o tempo (250 → 750 ms) junto com os bytes. Como o passe roda no thread que
  grava o quadro, um quadro de streaming custava até três quartos de segundo:
  era o engasgo que o mundo aberto e as cutscenes mostram quando um plano traz
  uma tela de texturas novas. Os bytes continuam triplicando — é o orçamento que
  drena a fila.
- **A cauda da rechecagem caiu de 400 para 96 quadros** (`[4, 12, 24, 48, 96]`).
  O passe olha um número limitado de slots por quadro (`most` e
  `VICTORY_RECHECK_MB`), então encurtar a cauda não custa nada e só decide quais
  slots competem pelas olhadas que existem. A 400 quadros, uma textura que o
  título repinta por outra view das mesmas páginas podia mostrar o que ela
  tinha 23 segundos antes — que é exatamente o que "textura piscando" e
  "textura faltando" parecem.

## Rodada 27/11 — a instrução não era o custo

Duas hipóteses testadas e as duas medidas, para não serem repetidas.

- **`VICTORY_RELAY=2` reprovado.** Mediana 9 fps contra 17,5 do `RELAY=1`, com
  o pior quadro em 577 ms contra 737 ms. A documentação promete 420→240 ms na
  referência; nesta máquina ele só piorou. Fica `RELAY=1`.
- **`rep movsb` trocado por stores AVX2 em `safecopy` — neutro.** O raciocínio
  era bom: 18.305 cópias por quadro de 453 bytes cada, 2 µs por cópia, e o
  Zen 3 não tem fast-short-rep (a AMD só ganhou isso no Zen 4). A troca passou
  nos testes do projeto (`copies_every_length_and_alignment`,
  `faults_are_failures`) e **não mudou a taxa**: 0,335 ms/MB antes, 0,277 a
  0,328 depois. Ou seja, a cópia não é limitada por instrução, é limitada por
  banda de memória — 57,7 MB de cópia por quadro a 3 GB/s é o que a memória
  entrega com o resto do quadro disputando o controlador. A troca fica (não é
  pior em nenhuma medição e remove uma penalidade conhecida), mas não é
  ganho, e não deve ser vendida como tal.

O que a rodada ensinou sobre onde **não** está o custo:

- não está no `rep movsb`;
- não está no número de locks do `nothing_new` (3 locks sem contenção = ~0,5 ms);
- não está na falta de paralelismo do `WIDE_BACK` (já vai para ~5 threads em
  peças de 1 MiB).

O que sobra, medido: **59,4 ms de CPU por quadro** (42,9 de preparo + 16,5 de
cópia de volta) contra **26,8 ms de placa**, num quadro de 74,9 ms. A placa
não é o muro; o preparo é, e ele é feito de 3.264 draws traduzidos por quadro
(8,2 µs de CPU cada) mais 9.749 spans de refresh.

## Rodada 27/11 (2) — nível de textura e tela cheia

Pedido do chef: baixar a qualidade gráfica para ganhar fps, e poder jogar em tela
cheia. A tela cheia ficou. O nível de textura ficou **desligado**: ele removia o
device na tela de carregamento e o jogo morria antes do mundo.

### Nível de textura: desligado (removia o device)

A ideia: a cópia do host de uma textura colocada pelo título já começa no
primeiro nível que as views dela mostram (`VICTORY_HELD_LEVELS`); o nível de
textura empurrava esse começo para baixo — nível 1 custa 25% dos bytes do nível
0, nível 2 custa 6%. Nada sumia da tela: a view era deslocada (`from_base`) e
mostrava o nível 1 no lugar do 0.

O que aconteceu: com `medium` e com `low` o jogo morria na tela de carregamento,
~56 s depois de abrir, com `0x887A0001` — chamada inválida no device do host — e
o `awake` relatando `the card is not kept awake`. Três partidas seguidas, sempre
no mesmo ponto. Com `high` (viés 0) o mesmo binário roda 17,5 fps sem uma única
ocorrência de `887A`.

Corrigido no caminho, mas não bastou para provar o resto:

- **textura com níveis sob demanda (`crate::tiles`) não aceita piso.** Ela é a
  cadeia inteira do título, cortada em tiles; `host.base` recebia o piso mesmo
  assim e o envio pedia `mips = base + d.MipLevels` — um nível a mais do que a
  textura tem. Agora `base` volta a zero quando a textura é tiled.
- **o twin de profundidade nasce da descrição reduzida** (`from_level(base)`),
  senão os dois têm tamanhos diferentes e a cópia entre eles é inválida.
- **`from_base` passou a deslocar também `TEXTURE2DARRAY`**, que é como o título
  vê quase toda textura de uma fatia. Antes ele saía fora e a view pedia níveis
  que a cópia não tinha.
- **o envio nunca pede um nível que a cópia não tem** (`have`/`count` em
  `texture.rs`).

Sobrou pelo menos uma chamada inválida em algum ponto do caminho de cópia que
não foi identificado — a camada de depuração do D3D12 não está instalada nesta
máquina (`D3D12GetDebugInterface` responde "componente SDK ausente"), então ela
não nomeia o comando. Enquanto isso, `mip_bias()` devolve 0 e `base_floor`
devolve 0: **toda textura sobe inteira, exatamente como antes da opção existir**.
O launcher não oferece mais o seletor.

Para religar, o caminho é: instalar os Graphics Tools do Windows, rodar com
`VICTORY_D3D12_DEBUG=1` numa build `diagnostics`, e fechar a chamada que sobra.

### Tela cheia: janela sem borda, swap chain esticada

`Alt+Enter` não fazia nada por dois motivos: o `wndproc` do título não trata a
tecla, e o DXGI tem o próprio Alt+Enter, que chama `SetFullscreenState` numa
swap chain que nunca foi feita para trocar de modo (sem
`DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH`) — ele falha calado.

Agora `MakeWindowAssociation(DXGI_MWA_NO_ALT_ENTER)` tira a tecla do DXGI, e o
`window_proc` do título (e o da janela própria do host, quando é ela) troca o
estilo para `WS_POPUP` e põe a janela no retângulo do monitor
(`MonitorFromWindow`). A swap chain não é recriada: ela mantém o tamanho que o
título fez (1280x720) e o flip model estica para 1920x1080. Nenhum modo de vídeo
muda, então não há piscada preta, não há espera do monitor e `Alt+Tab` continua
sendo do Windows.

Duas coisas que a primeira versão errava:

- **quem aplica `VICTORY_FULLSCREEN=1` é a thread dona da janela.** A primeira
  versão esticava a janela do título de dentro da thread do apresentador, no
  instante em que a swap chain nascia. O título ainda estava dimensionando a
  própria janela e levava um `WM_SIZE` de fora: `0xc0000005` escrevendo em
  `0x0` dentro de um módulo do jogo, 15 s depois de abrir. Agora a janela do
  título recebe o pedido por um `WM_TIMER` de 5 s na thread dela (o pedido é
  feito no `window_proc` que o `lifecycle` instala), e a janela própria do host
  aplica no `make_window`, que é nossa.
- **Enter sozinho não é nosso.** A primeira versão tratava `WM_KEYDOWN` +
  `VK_RETURN`, e o Enter do título é o botão A: confirmar qualquer menu viraria
  troca de tela cheia. Agora só `Alt+Enter` (que chega como `WM_SYSKEYDOWN`) e
  `F11` trocam; o Enter simples segue para o jogo.

## O `settings.xml` do próprio título — a alavanca que não é nossa

O jogo guarda as configurações gráficas dele em
`%LOCALAPPDATA%\victory\data\<id>\storage\settings\settings.xml`, e esse arquivo
tem coisas que nenhuma variável `VICTORY_*` alcança. O que estava lá antes de a
gente mexer:

| campo | valor | por que importa |
|---|---|---|
| `rtDynamicShadows` | `true` | sombra por ray tracing ligada num 3060 Ti de 8 GB |
| `uiResolution` | 3840x2160 | a UI (HUD, mapa, legendas) desenhada em 4K num jogo que sai em 720p |
| `vSync` | `2` | swap presa no vsync |
| `frameRateLimit` | 30 | o título se limita a 30 quadros |
| `desiredFPS` | 30 | idem, pelo lado do jogo |
| `waterReflectionSSR` | `true` | reflexo da água por SSR, tela cheia |
| `motionBlur` | `true` | passe de motion blur |
| `ssroSampleCount` | `2` | duas amostras de sombra em espaço de tela |
| `manualMipBias` | `-1.500000` | **o próprio título já reduz o mip em 1,5 nível** |

Esse último é o achado: o "menos nítido, nada sumido" que a gente tentou fazer no
caminho de cópia (e que removia o device, ver acima) **o título já faz sozinho**,
no lugar dele, com o `manualMipBias`. É por isso que a imagem já estava macia sem
a gente pedir nada, e é por isso que a nossa redução de mip era redundante *e*
perigosa: ela agia num lugar onde o título não esperava.

`scripts/tweak-game-settings.ps1` aplica o perfil `balanced`: corta o trabalho de
pixel caro (RT, SSR, motion blur, 4K de UI, vsync, limite de 30) e **mantém** o
`manualMipBias=-1.5`, o `sharpenEnabled=true` e todos os `texLodScale` em 1.0.
Toda textura continua presente; a nitidez volta pelo sharpen, que é barato.

Perfis: `balanced` (padrão), `maxfps` (LOD de textura em 2.0 e saída em 640x480),
`sharp` (desfaz LOD e mip bias, mantém só os cortes de pixel), `restore`.
O launcher aplica o `balanced` antes de cada partida (`gamecuts=0` no
`settings.ini` desliga), porque o título regrava o arquivo ao sair.

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
