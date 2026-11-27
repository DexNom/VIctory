# VIctory "cgbj + luz NVIDIA" numa RTX 5070 Ti portátil (~24 FPS)

Este ramo (`edupode`) junta três trabalhos e afina-os para um portátil com uma
RTX 5070 Ti Laptop (12 GB) e 32 GB de RAM:

- **cgbj** (`cputroll/cgbj-reupload`, perfil pareto-residency): a base, com a
  gestão de memória, as tabelas de descritores e as texturas que funcionam
  melhor nesta máquina;
- **VIctory-NVIDIA** (`trustno1-coder/VIctory-NVIDIA`): a luz (salas sem
  branco a mais, sem manchas de NaN, ray tracing desligado onde só estraga);
- **afinações próprias**: VRAM, 3 shaders de 64 threads, teclado e rato.

Não há aqui ficheiros do jogo. Precisas do teu próprio dump.

## Resultado (2026-11-26)

Medido no portátil acima, com Windows 11, 1080p e o perfil Series S.

| Cena | FPS |
|---|---|
| Prisão, interiores (cutscenes) | 18-22 |
| Mundo aberto, barco | 24-26 |
| Cutscenes | ~26 (31-32 no modo rápido) |
| 720p + upscaler do jogo, mundo aberto | 23-26 |
| 540p + upscaler do jogo, mundo aberto parado / a andar / de carro | 25-29 / ~15 / ~10 |

A luz e as texturas estão próximas do fork NVIDIA, e os FPS são os do cgbj ou
melhores. As sessões de 30-40 minutos correram sem crashes.

## Montar

1. **Ferramentas:** o Rust da versão fixada em `rust-toolchain.toml` (o rustup
   instala-a sozinho) e as Build Tools do Visual Studio (C++ e Windows SDK).
2. **Compilar a build `diag`:**
   ```powershell
   git clone -b edupode https://github.com/edupode/cold-project-vi.git
   cd cold-project-vi
   cargo build --locked --profile diag -p victory --features diagnostics
   ```
   Tem de ser a `diag`: a lista de shaders de ray tracing que ficam de fora
   (`VICTORY_SKIP_PIPELINES`) só funciona nas builds com diagnósticos.
3. **Windows Defender:** a regra ASR de proteção contra ransomware pode bloquear
   o `victory.exe` acabado de compilar, com o erro "Acesso negado". Numa
   PowerShell de administrador:
   ```powershell
   Add-MpPreference -AttackSurfaceReductionOnlyExclusions "C:\caminho\cold-project-vi\target"
   ```
4. **Preparar a cache.** Faz isto uma vez e outra vez sempre que recompilares
   o tradutor de shaders. Demora uns 2 minutos de shaders e mais uns minutos
   de pipelines. Sem isto, a primeira sessão corre a ~1 FPS enquanto traduz.
   ```powershell
   $env:VICTORY_GAME_DIR = 'D:\Jogos\MeuDump'   # a pasta com game_scarlett_master.exe
   .\Jogar-Luz.cmd preparar
   ```
5. **Jogar:**
   ```powershell
   .\Jogar-Luz.cmd
   ```
   Ou, com mais FPS: `.\Jogar-Luz.cmd 720p` (o mundo a 1280x720, esticado
   para 1080p pelo upscaler temporal do próprio jogo: 23-26 FPS no mundo
   aberto) ou `540p` (mais rápido e mais suave). `qualidade` como segundo
   argumento reduz a geometria esticada, com ~7 FPS a menos. As escalas 0,75 e
   1,0 com o upscaler forçado param ao arrancar.

   A cache, os saves e os logs ficam em `%LOCALAPPDATA%\VIctory\luz`. Para
   escolher outra pasta, define `RUN`. Quando fechas o jogo, o launcher prepara
   os pipelines usados nessa sessão para a seguinte.

**Antes de jogar,** fecha as aplicações que gastam placa e RAM, como emuladores
Android, outros jogos, Wallpaper Engine e overlays. O VIctory usa cerca de
24 GB de memória privada e enche os 12 GB da placa. Com outras aplicações
abertas, o Windows passa memória para o disco e os FPS caem.

## Teclado e rato

| Tecla | Comando | A pé | Em veículos |
|---|---|---|---|
| W/A/S/D | analógico esquerdo | mover | W acelera (RT), S trava ou faz marcha-atrás (LT), A/D vira ou inclina |
| I / K | analógico esquerdo cima/baixo | (câmara) | avião: nariz para baixo (I) / para cima (K); para levantar voo segura W e K |
| rato | analógico direito | olhar | olhar |
| botão esquerdo | RT / RB | atirar | atirar (RB) |
| botão direito | LT | mirar ou interagir | |
| Shift esq. | L3 | correr | buzina ou nitro |
| F | Y + modo veículo | entrar no veículo | sair do veículo |
| E ou Y | Y | interagir, falar | |
| Espaço / Enter | A | saltar ou escalar | travão de mão |
| Ctrl esq. / C / Backspace | B | agachar ou descansar | faróis |
| R ou X | X | corpo a corpo | |
| Q | RB | proteger-se | |
| Tab | LB | inventário | sacar arma |
| M | View | mapa | mapa |
| V | R3 | olhar para trás | olhar para trás |
| setas ou 1-4 | D-pad | telemóvel, rádio, navegação, parceiro | |
| Esc | Menu | pausa | pausa |

- **Rato:** um clique na janela do jogo prende o rato para servir de câmara.
  **Alt** ou **F1** soltam-no, e mudar para outra janela também o solta. O F1
  volta a prendê-lo.
- **Veículos:** o jogo conduz com os gatilhos. O **F** (entrar ou sair) liga e
  desliga sozinho o modo veículo, em que o W e o S passam a acelerar e travar.
  Ouve-se um bip agudo ao entrar no modo veículo e um bip grave ao sair.
  Basta segurar o W. Se o modo ficar trocado (por exemplo, se o F não te pôs
  no carro), carrega no **Caps Lock** para o corrigir. Para falar com pessoas
  sem mudar de modo, usa o **E**.
- Um comando Xbox ligado substitui o teclado e o rato.

## O que mudou em relação ao cgbj

Commits deste ramo, por cima do cgbj e das ferramentas de teste:

- `Shader: VICTORY_BVH_MISS`, `fix(gpu): ambient depth`, `Loader: render scale`
  e `Faces move, ray tracing mostly off…`: vêm do fork NVIDIA, juntos ao cgbj.
  Nos conflitos ficaram as tabelas (TablePlan), a ordem das texturas e a gestão
  de memória do cgbj. As vistas de 16 bits do fork NVIDIA marcam a tabela para
  ser refeita dentro do TablePlan.
- `VICTORY_SKIP_PIPELINES: @file form and a hash set`: a lista de 1 034
  shaders de ray tracing passa a vir de um ficheiro (num `.cmd` não cabia) e
  deixa de ser procurada um a um em cada desenho.
- `VICTORY_WAVE_WIDE_DIGESTS`: liga a tradução "wave-wide" só para os shaders
  de 64 threads escolhidos. Ligada para todos, a placa perdia-se com as
  partículas.
- `Input: keyboard and mouse…`: o teclado e o rato com o esquema da tabela acima.
- No launcher: 1080p, 1 GB de buffers na placa, 4 blocos de texturas à
  frente, menos aperto de RAM e a reserva para imagens do ecrã baixada de
  3 GB para 1,8 GB.

## O que ainda falta resolver

1. **Shaders de volumetria da água (resolvido por agora).** Três programas
   de 64 threads (`TransmissionVolumetrics_Tiled` `24126c14…`, `…_LocalLights`
   `7099a57f…` e `…Submerged` `f1f161e4…`) traduzem-se para ondas de 32 em modo
   "wave-wide" (`VICTORY_WAVE_WIDE_DIGESTS`). Testados um a um:

   | Ligados | Água | FPS no mundo aberto |
   |---|---|---|
   | nenhum | certa | ~1 (placa a 250-600 ms por frame) |
   | só `_Tiled` | quadrados pretos | ~1 |
   | `_Tiled` + `_LocalLights` | quadrados pretos | ~24 |
   | os 3 | quadrados pretos | ~14 |
   | **só `_LocalLights`** | **certa** | **~26** |

   O launcher usa só o `_LocalLights`. O `_Tiled` continua mal traduzido: as
   duas metades de 32 de cada grupo dão resultados diferentes, e isso aparece
   como retângulos do tamanho dos azulejos. A correção de fundo é traduzi-lo
   corretamente (`crates/victory-shader/src/emit/mod.rs`, `widen`).
2. **Dois shaders de 64 threads ainda sem tradução:** `DefragHeap`
   (`02384a60…`, grupos de 128) e `CollectTriangles_Meshlets` (`6b82bc7d…`,
   grupos de 1024). Têm várias ondas de 64 por grupo, e o tradutor ainda não
   sabe emular isso com ondas de 32. A NVIDIA recusa programas com WaveSize 64.
3. **VRAM.** A 1080p, os 12 GB da placa ficam acima do limite que o Windows
   dá ao jogo (9-10 GB), e milhares de texturas esperam por espaço.
   - Há sempre 1-2,5 GB vazios dentro dos blocos de texturas.
   - Ideias: devolver blocos vazios quando a placa passa o limite, ou
     carregar só os níveis menores das texturas grandes.
4. **RAM.** O VIctory usa ~24 GB de memória privada. Com 32 GB e outras
   aplicações abertas, o Windows tira ~11 GB para o disco e a placa fica à
   espera.
5. **Processador.** Cada frame gasta ~30 ms a preparar, sobretudo a copiar
   ~80 MB de buffers do jogo, e mais ~10 ms a copiar dados de volta.
6. **Escalas de resolução.** 0,5 (540p) e 2/3 (720p) funcionam. 0,75 fecha
   o jogo ~5 s depois de arrancar, e 1,0 com o upscaler forçado para num
   breakpoint (nota do fork NVIDIA). Também há geometria esticada ao passar
   por objetos e gaivotas como blocos deformados (o modo `qualidade` atenua).
7. **Ray tracing.** O construtor de estruturas (BVH) usa programas de 64
   threads que a NVIDIA recusa. Os resolves de ray tracing ficam de fora, por
   isso faltam os reflexos de ray tracing (na água, por exemplo) e algumas
   salas ficam mais escuras.
8. **Objetos e fumo escuros.** Às vezes, numa cutscene, aparece um fumo ou
   "areia" escura em alguns objetos. Ainda está por investigar: pode ser a
   volumetria sem o `_Tiled` ou o `VICTORY_NAN_SCRUB`, que põe a 0 (preto) os
   valores inválidos.
9. **Geração de frames.** O DLSS Frame Generation (Streamline) precisa de
   vetores de movimento, profundidade e imagem sem HUD, que o VIctory não sabe
   identificar entre os buffers do jogo. O NVIDIA Smooth Motion (no driver,
   pela NVIDIA App, perfil para `victory.exe`) é a alternativa sem código.

## Logs

- Cada sessão: `%LOCALAPPDATA%\VIctory\luz\logs\luz-*.log`.
- A linha `host: N fps, N ms/frame (…)` mostra o tempo do processador
  (`preparing`), da placa (`on the GPU`) e das cópias de volta.
- A linha `video memory as the system counts it` mostra a VRAM usada e o limite.

## Novidades (27/11/2026)

- **Overlay (F3)**: FPS agora, média do último minuto (`AVG 1M`), média desde o início (`ALL`) e o pior meio
  segundo (`LOW`).
- **VRAM**: o VIctory pede ao Windows uma reserva de VRAM (`VICTORY_VRAM_RESERVE`: `max` por defeito, um número
  de MB, ou `0`), para as outras apps saírem primeiro. As cópias de texturas cuja memória o jogo já desmapeou saem
  da placa ao fim de 3 s (`VICTORY_TEXTURE_DEAD=0` desliga); numa sessão a 720p estava ~1 GB preso nelas.
- **VI Launcher**: design novo; página Desempenho com as apps que ocupam VRAM e RAM (fecha as que marcares);
  NVIDIA Smooth Motion escrito no perfil do driver para o `victory.exe` (sem NVIDIA App); barra de carregamento
  que prepara as pipelines das sessões anteriores antes de o jogo abrir; explicação de cada opção, com ★ no que
  foi testado no PC do autor; inglês por defeito, português na barra lateral.
- **Gravador de frames**: F9 no jogo cria o ficheiro `go` quando o `VICTORY_RECORD` está definido (apito agudo).
  `VICTORY_REPLAY_WARP=1` corre uma gravação no WARP, que corre ondas de 64, para comparar a forma nativa de um
  programa wave64 com a forma de duas ondas de 32 que as placas NVIDIA recebem (trabalho do `_Tiled`).
- Multidões e lutas: 14-17 FPS na última sessão (eram 10-15).
