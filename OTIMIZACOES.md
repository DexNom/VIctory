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
`potato` (o piso: `lodScale` 0.4, `smallCullThreshold` 3.0, veículo no último
LOD, modelos de dano fora), `sharp` (desfaz LOD e mip bias, mantém só os cortes
de pixel), `restore`.
O launcher aplica o `balanced` antes de cada partida (`gamecuts=0` no
`settings.ini` desliga), porque o título regrava o arquivo ao sair.

### Medido: +11% de fps, e o melhor custo por draw até agora

Três medições de 180 s de assentamento e 120 s de janela, mesma máquina, mesmo
perfil, cenas diferentes (é o mundo aberto, a câmera muda):

| run | fps médio | ms/frame | draws/frame | µs por draw | o que mudou |
|---|---|---|---|---|---|
| soak1 | 8,69 | 116,7 | 3.748 | 31,1 | base: `CARD_BUFFERS_MB=768`, `CARD_WINDOWS_MB=96` |
| soak2 | 7,80 | 130,7 | 4.096 | 31,9 | `1024`/`256`, sem os cortes do jogo |
| soak3 | **8,68** | **115,5** | 4.118 | **28,0** | os mesmos `1024`/`256` **com** o perfil `balanced` |
| soak4 | 8,17 | 126,9 | ~4.100 | 31,0 | perfil `maxfps` |
| soak5 | 6,86 (mediana 7,00) | — | ~4.900 | — | `balanced` + `VICTORY_RAW_VIEWS=0` |
| soak6 | 8,78 (mediana 9,00) | — | ~4.400 | — | `balanced` + `VICTORY_HEAP_SYNC=0` |
| soak9 | 7,48 (mediana 8,00) | 142,9 | ~4.000 | — | `balanced` + `VICTORY_SUBMIT_FRAMES=0` |
| soak10 | 7,88 (mediana 8,00) | 136,9 | ~4.000 | — | `potato` (640x480, LOD 0,4, culling 3,0) |
| soak11 | 8,35 (mediana 8,00) | 124,9 | ~4.700 | — | `balanced` com **300 s** de assentamento (controle) |
| soak12 | 7,84 (mediana 8,00) | 158,8 | ~4.900 | — | `balanced` + `VICTORY_UPLOAD_AHEAD=1` |
| soak13 | 8,41 (mediana 9,00) | 121,5 | ~4.000 | ~30,4 | perfil `fps` (textura a um quarto) |
| soak14 | 7,22 (mediana 7,00) | 140,7 | ~3.900 | — | `balanced` + `VIEW_THREADS=5` **e** `RECORD_THREADS=3` |
| soak15 | 10,76 (mediana 11,00) | **97,3** | ~3.800 | 26,2 | `balanced` + `VIEW_THREADS=5` |
| soak16 | **11,36 (mediana 12,00)** | **90,0** | ~5.400 | **16,7** | `balanced` + `VIEW_THREADS=5` (réplica) |

O soak2 e o soak3 rodaram a mesma cena (4.096 e 4.118 draws): **o corte do
`settings.xml` valeu 15,2 ms por quadro, 11%**, e o custo por draw caiu de
31,9 para 28,0 µs — o melhor que essa máquina já mediu. O soak1 tinha 31,1 µs
com 10% menos geometria.

O ganho é menor do que o tamanho do corte porque **o quadro aqui é de CPU**:
`preparing` 74–81 ms mais `copying back` 23–31 ms dão ~105 ms de trabalho de
CPU, e a GPU fica em 20–24 ms. Tirar trabalho da GPU encurta a fila dela, não a
nossa. O que sobrou para atacar, em ordem:

- `copies` 29–33 ms para 50–54 MB (≈1,7 GB/s): é `ReadProcessMemory` peça por
  peça, limitado por banda de memória da CPU.
- `heap buffers` 15–44 ms: o `sync_heaps`, com ~10 ms por quadro que os três
  contadores internos não explicam.
- `copying back` 23–31 ms para 6,4 MB (≈250 MB/s): a thread do relay passa 43 ms
  esperando o cartão sem ter o que aterrissar. É onde o `VICTORY_QUEUE2` do
  `uiliannn/VItoriaBr` ataca (fila de cópia própria para o fetch).

### Ataques medidos e recusados (27/11)

**`VICTORY_RAW_VIEWS=0`** (os shaders leem os slots do título em vez dos twins):
6,86 fps, 186,4 ms, e a fila de textura passou de 6 mil para 13 mil slots
"passed over unmapped". O caminho dos twins é mais rápido **e** mais correto.
Fica ligado.

**`VICTORY_HEAP_SYNC=0`** (o `sync_heaps` inteiro desligado): 8,78 fps, mediana
9,00, contra 8,68/115,5 ms do soak3 — dentro do ruído da cena. O motivo está no
próprio código: o `sync_heaps` roda **ao lado** do resto do `preparing`
(`VICTORY_HEAP_SYNC_ASIDE`, ligado por padrão) e só 13,6 ms por quadro vazam
para a thread que submete. Desligar o passe não devolve o que ele custa, porque
ele quase não está no caminho crítico. **Não é a alavanca** — e o item 4 da
lista de suspeitos (os 14 mil `mirror()` sem filtro) não pagaria o que prometia.

**`VICTORY_LATE_WAIT=3`** (presente livre, sem esperar o quadro anterior) não
chega ao mundo: o processo morre antes, em 02:22:20. O título depende da espera
e o perfil fica em `2`.

O que a tabela mostra de verdade: as cenas variam de 3.000 a 6.000 draws entre
runs, e é a cena que decide o número, não o knob. Comparar fps entre runs só
vale quando o µs/draw é parecido. A média de `ms/frame` também sai inflada
pelos engasgos (o script divide o total pelo número de amostras), por isso o
número honesto é a mediana e o `ms/frame` das últimas amostras.

### O split do `preparing`, medido (build com `diagnostics`)

O `soak` só diz o total. Um build com `--features diagnostics` abre o
`preparing` em pedaços, e o retrato de 27/11 02:14–02:21 é este:

| onde | ms por quadro |
|---|---|
| passos das listas, na thread que submete | **39,0** |
| — refreshes (42 flushes de 7.661, dos quais 6.538 "copiam nada") | 23,7 |
| — copies e fills | 7,9 |
| — serial, em 384 listas | 0,8 |
| esperando as *flights* da GPU | **26,4** |
| snapshots | 5,1 |
| copies no cartão | 8,1 |
| envio das listas | 3,0 |
| esperando os records dos heaps | 4,6 |
| passe do heap de views (thread `victory-aside`) | 30,9 |
| — olhando slots | 30,9 |
| — spans | 4,1 |
| — texturas | 20,8 |
| **espera no fecho das listas por esse passe** | **12,5** |
| `asking about title memory` (245 askings, 1.321 MB) | 5,7 |

Duas leituras, e as duas importam:

**26,4 ms são a thread que submete parada, esperando a GPU.** Somado aos
12,5 ms do fecho das listas e aos 4,6 ms dos records, são **43 ms de espera
pura num quadro de 115** — 37%. Não há knob que conserte espera; só sobreposição
conserta, e as três tentativas de sobrepor (`SUBMIT_FRAMES=0`, `LATE_WAIT=3`,
`RELAY=2`) ou morreram ou não mudaram nada.

**O passe do heap de views era uma thread só** (`victory-aside`, 30,9 ms), e a
primeira lista que precisa dele espera. Era o único alvo de código com tamanho:
paralelizar o passe devolvia até 12,5 ms. **Feito:** `VIEW_THREADS` 3 → 5 no
`profile-16gb.ps1` (soak15/soak16, 97,3 e 90,0 ms). O passe roda em pool desde
antes; o que faltava era tamanho de pool.

**`VICTORY_SUBMIT_FRAMES=0`** (dois presentes na fila em vez de um: o quadro
seguinte pode ser preparado enquanto a GPU roda o atual) mediu 7,48 fps de
média, mediana 8,00, 142,9 ms/frame — pior que o soak3. A fila mais funda só
acrescenta latência: o `preparing` continua esperando as flights
(26,4 ms/frame) porque as leituras de memória do título não podem passar na
frente da escrita que a GPU ainda vai fazer. **Recusado.**

**`potato`** (640x480, `lodScale` 0,4, culling 3,0, veículos no último LOD,
modelos de dano fora): 7,88 fps de média, mediana 8,00, mas com uma curva
clara — as primeiras 25 amostras dão 6,68 fps e as últimas 25 dão 9,12, com
picos de 11. Só que o `ms/frame` das últimas 25 é **115,8 ms**, contra os
**115,5 ms** do soak3 em `balanced`. É o mesmo chão. O perfil corta geometria
e resolução, e o número de draws **não cai** (3.831–4.286, contra 4.118 do
`balanced`): o título desenha o que quer e o custo é por draw, não por pixel.
**Não é a alavanca** — e custa imagem.

O controle fecha a dúvida: `balanced` com **300 s** de assentamento (soak11)
deu 8,35 fps e 124,9 ms, com a mesma cena mais pesada (4.700 draws). A curva
do `potato` não era aquecimento que o `balanced` também teria — era a cena
mudando. **Assentar mais não melhora nada.**

**`VICTORY_UPLOAD_AHEAD=1`** (fila de cópia própria para os primeiros uploads
de textura, lidos/detilados/copiados por threads nossas): 7,84 fps, mediana
8,00, 158,8 ms/frame, e a pior cauda de todas — um engasgo de **1,26 s** numa
amostra de 4.078 draws. A fila extra disputa banda e cores com o resto, e o
trabalho que ela tira da frente volta como espera no `copying back`. **Recusado.**

**Textura a um quarto (perfil `fps`)**: `manualMipBias` -1,5 → -2,5,
`texLodScale_*` 1,0 → 2,0, `maxTexUpgradesPerFrame` 5 → 2, com a geometria e a
resolução do `balanced` intactas. Deu 8,41 fps de média e **mediana 9,00** —
a melhor mediana junto com o soak6 — mas 121,5 ms/frame e ~30,4 µs/draw contra
os 115,5 ms e 28,0 µs/draw do soak3. **Empata dentro do ruído, não ganha.** O
motivo é o mesmo de sempre: o custo é por draw, e mip não muda draw. O perfil
fica no script como opção de gosto, não como alavanca; o padrão continua
`balanced`.

### O chão é 90 ms desde 27/11 — e ele caiu por thread, não por knob

Até o soak13, cinco perfis e sete knobs paravam em **~115 ms por quadro
(8,7 fps)**: `balanced` 115,5; `potato` 115,8; `fps` (textura a um quarto)
121,5; `balanced` com 300 s de assentamento 124,9 (cena mais pesada); `maxfps`
126,9; `SUBMIT_FRAMES=0` 142,9; `UPLOAD_AHEAD=1` 158,8; `RAW_VIEWS=0` 186,4.

O **soak15** e o **soak16** mudaram o chão com uma linha: `VICTORY_VIEW_THREADS`
de 3 para 5. **97,3 ms** e **90,0 ms** (mediana 12,00 fps), com o mesmo perfil,
o mesmo jogo, `887A: 0` e o processo vivo no fim. O soak16 rodou uma cena
**mais pesada** (5.400 draws contra 4.100 do soak3) e ainda assim ficou 22% mais
rápido — **16,7 µs/draw**, contra 28,0 do melhor run anterior.

O que caiu, do log do soak15:

| pedaço | antes | depois |
|---|---|---|
| `preparing` | ~70 ms | 41–48 ms |
| refreshes de textura (todas as threads) | 23,7 ms | 9,6 ms |
| `copying back` | 23–37 ms | 13–15 ms |
| espera da thread que submete pela placa | 26,4 ms | 45–52 ms (maior em proporção, mesmo absoluto) |

O passe do heap de views era o único item de 30,9 ms numa thread só e 12,5 ms de
espera no fecho das listas. Com 5 threads ele engole o passe, a espera do fecho
some, e o resto do `preparing` encolhe junto.

**A lição do soak14, que vale mais que o número:** ele subiu as views para 5
**e** baixou os records para 3 ao mesmo tempo, e deu 140,7 ms — pior que tudo.
As duas variáveis brigaram pelos mesmos núcleos. Isolar uma por vez não é
capricho de método: é o que separa um ganho de 22% de um retrocesso de 22% com o
mesmo código.

**O que sobrou:** a thread que submete ainda passa 45–52 ms por quadro parada
esperando a placa sem ter o que aterrissar, e a GPU fica em ~40 ms. Sobrepor os
dois vale outros ~30 ms (para ~60 ms, 16 fps), e continua sendo o desenho do
caminho de submissão, não um knob. As três tentativas (`SUBMIT_FRAMES=0`,
`LATE_WAIT=3`, `RELAY=2`) morreram.

**20 fps (50 ms) continua fora de alcance.** A 16,7 µs/draw seriam precisos
3.000 draws; o título desenha 4.000–5.700. Chegar lá exigiria ~8,8 µs/draw, que
nem um 9800X3D faz neste runtime. Para referência, a máquina dos docs
(i7-14700KF + RTX 4070 12 GB + 32 GB) roda este mesmo título a **2–5 fps**.
Aqui está em **10,8–11,4**.

### O water light, portado do `trustno1-coder/VIctory-NVIDIA` (27/11)

Dois programas de compute do título — `water_shading:CS_TransmissionVolumetrics_Tiled`
e `_Tiled_LocalLights`, hashes `5dc3b25986622949` e `6f225b641c5b0708` — são de
**uma wave de 64 lanes por grupo**. Numa placa de 32 lanes (toda GeForce) eles
só têm a forma wave-wide, e nessa forma deixam **tiles inteiros do mar escuros**
(em volta de barcos, na beira, sob pontes) e custam **~15 ms de GPU por quadro**.

O fork NVIDIA os deixa de fora. Portado para `pipeline/make.rs` (`left_out`),
com a mesma licença (GPL-3.0-or-later) e o crédito no comentário. O log confirma
no nosso dump:

```
a pipeline of the water's tiled light was asked for asked=false wave=32 hashes=["5dc3b25986622949"]
the water's tiled light on a host of 32-lane waves (dark blocks; VICTORY_WATER_LIGHT=1 runs it), left out wave=32
a pipeline of the water's tiled light was asked for asked=false wave=32 hashes=["6f225b641c5b0708"]
```

**O que muda na imagem:** a água perde a luz da própria profundidade — fica mais
escura e mais azul — e os blocos escuros somem. `VICTORY_WATER_LIGHT=1` religa
os dois programas (e os blocos).

**O que muda no número, medido (soak17, `balanced`):**

| | soak16 (sem) | soak17 (com) |
|---|---|---|
| fps médio | 11,36 | 11,34 |
| ms/frame | 90,0 | 88,6 |
| GPU por quadro | 39,6–50,1 ms | **28,3–36,5 ms** |

**A GPU caiu ~10 ms e o fps não mudou** — porque o quadro é limitado pela CPU
(`preparing` 42–48 ms). É ganho de folga na placa, não de relógio. Guardado
porque a placa agora tem margem para quando o caminho de submissão for atacado.

### O cache de pipelines do ScarlettPort entra no nosso runtime (27/11)

O que faz o ScarlettPort parecer melhor não é fps — `docs/STATUS.md:7` deles
publica **~4 fps no prólogo** num RTX 3060, contra os nossos 11,3. É **cache
construído**: 4,11 GB em `%LOCALAPPDATA%\ScarlettPort\cache`, com
`host-pipelines.bin` de 358 MB, `known-shaders.bin` de 198 MB e 83.561
pipelines no livro.

A tela preta com áudio era isso: em `soak22`, **1.644 de 2.192 draws por quadro
sem pipeline de host**, GPU 0,0 ms, 534 ms de quadro. O nosso cache tinha 1.726
shaders de ~26.000 — o `victory shaders` nunca tinha rodado até o fim.

Medido em 27/11, com o binário release desta árvore:

| arquivo | teste | resultado |
|---|---|---|
| `known-pipelines.bin` (48 MB) | `victory warm --used --limit 20` | 20 feitas, 0 sem host pipeline, 0 ruins |
| `known-shaders.bin` (189 MB) | o mesmo | 20 feitas (o tradutor gera o mesmo container) |
| `host-pipelines.bin` (341 MB) | carrega (`mapped=true`), mas so 16 das 3.549 pipelines iniciais sao achadas pelo nome | **nao entra** - ver abaixo |
| `early-pipelines.txt` (3.543) | união com o nosso (1.948) | 3.549 hashes |

O `GTA6.exe` agora faz duas coisas com isso:

1. `ImportScarlettCache()` no Setup: se o ScarlettPort estiver instalado, copia
   o livro (known-pipelines.bin), os shaders (known-shaders.bin) e une
   a lista de hashes. Sem ele, nada muda.
2. `--prepare` roda `victory shaders <shaders.rpf>` **antes** do `warm --used`,
   como o "Build shaders" deles (`scarlett warm`, 25.889 shaders em 171 s).
   Era o passo que faltava para o cache ser construído em vez de improvisado.

**A biblioteca de host de 341 MB não entra.** Medido em 27/11 (`soak24`): ela
carrega - `host pipeline library loaded bytes=358274130 mapped=true` - mas só
**16 das 3.549** pipelines iniciais são achadas pelo nome. As descrições que o
nosso runtime monta não batem com as que a deles gravou, então o `LoadPipeline`
devolve erro para quase tudo. E o custo é real: 341 MB de páginas mapeadas
disputando memória com o título levaram o quadro de **88-90 ms para 178 ms**
(6,2 fps, `dropped 1311`), com `host memory pressure` já no quadro 49.

A biblioteca é construída pelo uso, no nosso formato. O que sobra do ScarlettPort
é o livro e os shaders: eles poupam a tradução, não a criação.

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

---

## Os modos do GTA6.exe e a paridade com o launcher da Julia

O `GTA6.exe` deixou de ter um perfil unico. Ele abre com seis modos na primeira
tela e um codigo de perfil que se cola num chat.

| modo | escala | buffers | alvos | cartao | janelas | ripe | pressao |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Recomendado | 0.6667 | 768 | 768 | 128 | 96 | 4 | padrao |
| Nitido | 1.0 | 768 | 768 | 128 | 96 | 4 | padrao |
| Rapido | 0.5 | 768 | 768 | 128 | 96 | 4 | padrao |
| PC modesto | 0.5 | 512 | 512 | 96 | 64 | 2 | 1024 / 2048 |
| Ultra baixo | 0.5 | 384 | 384 | 64 | 48 | 2 | 1536 / 3072 |
| Classico | 1.0 | 768 | 768 | 128 | 96 | 4 | padrao |

So o **Recomendado** e o **Nitido** foram medidos nesta maquina. O Recomendado
e o `soak28`: escala 0.6667, GPU de 34,0 para 13,7 ms e quadro de 99,2 para
90,1 ms a 6.191 draws, sem artefato visivel. Repetido hoje pelo launcher: 91,1
a 95,8 ms a ~6.000 draws. O Nitido e o perfil anterior a escala.

### O codigo do perfil

```
s=escala;b=buffers;t=alvos;m=cartao;w=janelas;p=percentual;r=ripe;g=pressao;c=limpeza
```

O botao **Codigo do perfil** copia o modo atual e aceita um colado. Um codigo
incompleto ou fora da faixa e recusado inteiro: o jogo nunca abre com metade
dos valores em zero. Pela linha de comando:

```powershell
GTA6.exe --code                                     # o codigo do modo atual
GTA6.exe "--code=s=0.5;b=512;t=512;m=96;w=64;p=90;r=2;g=1024;c=2048"
```

### As duas linhas de pressao de RAM

`VICTORY_PRESSURE_FREE_MB` e `VICTORY_PRESSURE_CLEAR_MB` sao novas no runtime.
Cruzou a linha de entrada, o host corta o prefetch de textura para 64 MiB por
quadro e solta staging parado; voltou acima da linha de limpeza, ele retoma. O
padrao do runtime e 3 GiB para entrar e 5 GiB para sair, que e o valor de toda
run medida ate aqui, e e o que o modo Recomendado usa. Os modos apertados descem
para 1 GiB / 2 GiB.

O `residency.rs` ganhou as linhas como parametro, com a histerese presa: uma
linha de limpeza abaixo da linha de entrada e empurrada para entrada + 256 MiB,
porque sem isso o estado alterna a cada amostra de um segundo e cada alternancia
reescreve o orcamento de prefetch.

### O que o launcher da Julia tem e este runtime nao honra

O `Julias-Launcher.exe` aponta para `felicienradio/cold-project-vi`. Das 64
chaves que ele escreve, 44 existem aqui e 20 sao escritas no vazio:

```
AMBIENT_MOST BAKE_NO_WAIT BORDERLESS BORDERLESS_DPI BVH_MISS FPS_MAX
FRAMEGEN INF_AS KBM_GTA NAN_SCRUB OWN_UPSCALER PANEL_FRAMEGEN
POOL_FULLEST PORT_DESCRIPTORS PRESSURE_CLEAR_MB PRESSURE_FREE_MB
TEXTURE_MAX TEXTURE_RAM_GUARD VRAM_RESERVE WAVE_WIDE_DIGESTS
```

Duas dessas passaram a existir com este trabalho (`PRESSURE_*`). As outras
dezoito descrevem recursos que este runtime nao tem: geracao de quadros,
upscaler proprio, borderless, sanitizacao numerica, BVH. O launcher nao as
oferece, para nao vender um botao que nao faz nada.

O relatorio completo esta em `reference/REPORT-JULIAS.md`, com o binario e os 45
arquivos descompilados em `reference/`. Nada disso entra no build.

### O "Setup" do Windows

O botao **Ajustes do Windows** faz o que o `AdminTasks.cs` da Julia faz, menos o
pagefile: plano de energia Ultimate Performance (`powercfg /overlaysetactive`) e
`GpuPreference=2` para o `victory.exe` em `HKCU\Software\Microsoft\DirectX\
UserGpuPreferences`. O proprio processo se relanca com `runas`; sem elevacao ele
diz na linha de status. O pagefile nao e alterado — o desta maquina ja esta fixo
em 40 GB, e mexer nele pede reinicio e pode deixar o sistema sem swap se a conta
sair errada. Ele so e relatado.

### Um teste que ja estava vermelho

`pipeline::host::tests::depth_bounds_pipeline_loads_and_filters_draws` falha
nesta maquina com `E_INVALIDARG` na serializacao da root signature. Nao e
regressao: `pipeline/host.rs` esta byte-identico ao commit anterior, e o binario
de teste roda sem o `D3D12Core.dll` do Agility SDK ao lado, entao cai no
`d3d12.dll` do Windows 10, que recusa os flags de acesso direto a heap. O teste
que depende de dispositivo real continua vermelho aqui; os outros 372 passam.

### A copia visual do launcher da Julia

O `GTA6.cs` foi redesenhado com o desenho dela: a paleta do `Theme.cs`
(`Back 36,12,32`, `Side 28,9,25`, `Card 46,17,41`, acento `255,244,163`,
`HotPink 255,79,184`), o `Hero` de 362 px com as duas orbitas, os aneis, o
arco, o blob e as quatro pastilhas de estado, a barra de titulo de 52 px, a
lateral de 216 px com os `NavButton`, o rodape de 64 px com o `FootMeta` e o
`Play`, os `ModeCard`, os `OptionCard` com interruptor, o `Dropdown` pintado
por nos, o `StatusStrip` e a barra de progresso com brilho.

Nada dos fontes dela entra no build: o desenho foi reimplementado em cima do
nosso perfil, e o `reference/` guarda o binario e os 45 arquivos
descompilados so para conferencia. As nove chaves do perfil continuam as
mesmas; nenhuma chave nova foi inventada.

Medido nesta build:

- `--code` devolve `s=0.6667;b=768;t=768;m=128;w=96;p=90;r=4;g=0;c=0`.
- `--code` com valor fora da faixa sai com 2 e imprime `invalido`.
- `--install` sai com 0 e nao toca nos arquivos do jogo.
- As quatro paginas abrem sem excecao; o dialogo do codigo abre e fecha.
- Partida real de 220 s: mundo carregado em ~120 s, 4.618 a 6.128 draws,
  89 a 98 ms por quadro (~11 fps), sem fechamento espontaneo nesta run.
## O que mudou na build de 27/11 (noite)

- **Cabelo ligado por padrao.** Os 18 programas de cabelo eram pulados de
  proposito (hair_strands), uma gambiarra de quando o tradutor nao dava
  conta. O upstream nunca teve essa exclusao. Agora eles rodam;
  VICTORY_HAIR=0 desliga. Toda linha de shader traduzido ou recusado
  carrega program= com o nome do programa, para o diagnostico.
- **Blocos de staging zerados** antes de um bloco reciclado entregar bytes
  de outra textura (VICTORY_STAGING_ZERO=0 desliga). Era a causa das caras
  e roupas arco-iris.
- **Suspend/resume da command queue** implementados (o slot 0xb8 devolvia
  E_NOTIMPL). Provado no log: 	he title suspended its command queue.
- **VICTORY_AMBIENT_MOST** (default 4) e **VICTORY_AMBIENT_MARCH**: o teto
  da nevoa de ar. O perfil usa 2 (o valor do launcher da Julia); 4 lava a
  imagem em interior e da a cara de massinha. Um froxel NaN ou infinito e
  zerado no air clamp.
- **Descricoes de view colocada sem as palavras de enchimento.** O titulo
  deixa as palavras 1 e 13 da descricao de 56 bytes sem escrever, e a palavra
  3/9 da descricao da view idem: a mesma view virava uma forma nova a cada
  vez. As 2^22 formas enchiam em meia hora e cada view colocada virava uma
  entrada de tabela guardada ate o fim da run (34,9 milhoes, 16,7 GB depois
  de 70 minutos). Era o "confetti nas caras" e o frame de 1,6 s.
- **VICTORY_OWNERLESS_VIEWS** (default 1.048.576; 0 guarda tudo): teto para
  as entradas de views sem recurso, as mais antigas liberadas.
- **Merge do origin/main**: lane-split (#80), mascara de lane (#66), resgate
  em pilha de 4 GiB (#39), o lowering wave32 de defrag e de seis computes de
  uma onda, e o resto. cargo test -p victory-gpu --lib: 408 passam.
