# Controles

## Antes de jogar: compilar

O botão **Preparar (compilar)** no launcher (ou `GTA6.exe --prepare`) fabrica as
pipelines de shader que o jogo vai pedir, antes de a partida começar. Sem isso o
compilador do driver trabalha no meio do quadro e cada pipeline nova é um
congelamento.

Duas coisas, uma vez cada:

1. `GTA6.exe --book` — uma sessão normal que grava o livro de pipelines em
   `%LOCALAPPDATA%\VIctory\cache\pipelines\known-pipelines.bin`.
2. `GTA6.exe --prepare` — compila as que os quadros desenham e deixa no cache do
   driver.

Depois disso o botão só é preciso quando o driver ou o tradutor mudam (o
`victory warm` sabe pelo fingerprint e refaz só o que falta).

O runtime não espera controle de Xbox: se nenhum estiver conectado, o teclado e o
mouse viram um pad de Xbox sozinho. Se você plugar um controle de verdade, ele
assume na hora e o teclado continua funcionando junto.

## Teclado

| tecla | botão do Xbox |
| --- | --- |
| `W` `A` `S` `D` | analógico esquerdo (andar) |
| setas | direcional |
| `Enter` | A |
| `Backspace` | B |
| `X` | X |
| `Y` | Y |
| `Q` | LB |
| `E` | RB |
| `F` | clique do analógico esquerdo |
| `H` | clique do analógico direito |
| `Esc` | Menu |
| `Tab` | View |
| `Z` | gatilho esquerdo |
| `C` | gatilho direito |

## Mouse

| entrada | no jogo |
| --- | --- |
| movimento **com o botão direito apertado** | olhar (analógico direito) |
| botão esquerdo | gatilho direito (atirar) |
| botão direito | gatilho esquerdo (mirar) **e** segura o olhar |

**O cursor é seu.** O olhar só é preso enquanto o botão direito está apertado.
Soltou o botão, o cursor volta a ser o do Windows — mesmo com o jogo rodando a
dois quadros por segundo. `Esc` também solta.

Sensibilidade: `VICTORY_MOUSE_SENSITIVITY` (padrão `0.05`; maior = mais rápido).

## Painel

`F3` liga e desliga o painel do runtime: FPS grande, grafo de tempo de quadro,
GPU, VRAM, RAM e draws. O botão **Jogar com painel** já abre com ele ligado.

## Janela

A janela do jogo nasce fora da tela e sem foco. O launcher a traz para frente
nos primeiros dois minutos e para depois — não fica roubando o foco.

`Alt+Enter` ou `F11` alternam entre janela e **tela cheia**. É uma janela sem
borda do tamanho do monitor: a imagem que o jogo desenha mantém o tamanho dela e
é esticada por cima de todos os pixels, como o console faz com um jogo de
1280x720 numa TV 4K. Nenhum modo de vídeo muda, então não há piscada preta nem
espera do monitor — e `Alt+Tab` continua sendo do Windows.

O launcher tem a caixa **Tela cheia**, que abre a partida já assim
(`settings.ini`, `fullscreen=1`).

O console do runtime não abre janela nenhuma. Se precisar do log, ele está em
`%TEMP%\victory\` ou no `stderr` de quem iniciou o processo.

## Nível de textura

**Desligado nesta build.** O seletor saiu do launcher e o runtime não reduz mais
nada: toda textura sobe inteira, como antes da opção existir.

O motivo está em `OTIMIZACOES.md`: com **Médio** e com **Baixo** o jogo removia o
device do host na tela de carregamento (`0x887A0001`, chamada inválida) e morria
antes do mundo. Com **Alto** o mesmo binário roda estável. Três consertos
entraram no caminho de cópia (tiled sem piso, twin de profundidade na descrição
reduzida, `TEXTURE2DARRAY` deslocado, envio limitado ao que a cópia tem), mas
sobrou pelo menos uma chamada inválida que a camada de depuração do D3D12 não
nomeia — ela não está instalada nesta máquina.

Se quiser a textura mais macia e menos VRAM de volta, o próximo passo é instalar
os *Graphics Tools* do Windows e rodar uma build `diagnostics` com
`VICTORY_D3D12_DEBUG=1`; a mensagem diz o comando exato.

## Ajustes gráficos do próprio jogo

O jogo guarda as configurações gráficas dele em
`%LOCALAPPDATA%\victory\data\<id>\storage\settings\settings.xml`. Esse arquivo
tem coisas que nenhuma variável `VICTORY_*` alcança, e o launcher aplica o
perfil `balanced` antes de cada partida (o título regrava o arquivo ao sair,
por isso é antes e não uma vez só).

Para aplicar à mão:

```powershell
cd C:\GTA6-PC
# ou, no repo:
.\scripts\tweak-game-settings.ps1 -Profile balanced -DryRun   # ver o que muda
.\scripts\tweak-game-settings.ps1 -Profile balanced           # aplicar
.\scripts\tweak-game-settings.ps1 -Profile maxfps             # mais agressivo
.\scripts\tweak-game-settings.ps1 -Profile sharp              # desfaz o LOD
.\scripts\tweak-game-settings.ps1 -Profile restore            # volta o backup
```

Perfis:

| perfil | UI | LOD de textura | para quem |
|---|---|---|---|
| `balanced` | 1280x720 | 1.0, mip bias -1.5 | padrão: toda textura presente, menos nítida |
| `fps` | 1280x720 | 2.0, mip bias -2.5 | textura a um quarto, mesma geometria. Nada some |
| `maxfps` | 640x480 | 2.0 | quem quer quadros e aceita imagem macia |
| `potato` | 640x480 | 4.0, mip bias -2.0 | o piso: geometria em 0.4, culling em 3.0, veículo no último LOD |
| `sharp` | 1920x1080 | 1.0, mip bias 0 | quem tem folga e quer nitidez |

Nenhum perfil tira textura: todos só descem o nível de mip, então a textura
fica mais macia e continua no lugar. Textura que *sumia* era o
`VICTORY_MIP_BIAS` do runtime, que deu `0x887A0001` e está desligado desde o
`85003c4` — não é o mesmo ajuste.

**Sobre o `fps`:** medido, ele **não ganha fps**. O custo do runtime é por draw,
não por texel: `potato` com textura a um dezesseis avos roda no mesmo 115,8 ms
da textura cheia. O `fps` existe para quem prefere a imagem mais macia; não é
alavanca de desempenho.

**O que ganhou fps de verdade foi thread, não textura:** `VICTORY_VIEW_THREADS`
de 3 para 5 levou o quadro de 115,5 ms para **90,0 ms** (soak15/soak16), sem
mexer em imagem nenhuma. É o padrão do perfil desde 27/11.

**Água sem blocos escuros:** dois programas de luz da água do título
(`5dc3b25986622949`, `6f225b641c5b0708`) rodam em wave de 64 e numa GeForce
(32 lanes) saem em forma wave-wide: deixam tiles do mar pretos e custam ~15 ms
de GPU. Ficam de fora por padrão — água mais escura e azulada, blocos fora,
GPU ~10 ms mais leve. `VICTORY_WATER_LIGHT=1` religa os dois (e os blocos).
Portado de `trustno1-coder/VIctory-NVIDIA`.

O que `balanced` corta: sombra por ray tracing, reflexo da água por SSR,
motion blur, sombra de partícula, duas amostras de SSRO para uma, UI de 4K para
720p, VSync, o limite de 30 quadros e o LOD de geometria de 1.0 para 0.8.

A sombra por ray tracing é o corte mais importante: o builder da estrutura de
ray tracing **não roda** aqui (`VICTORY_BVH_BUILDER` desligado), e o
`VICTORY_AMBIENT` põe a luz indireta no lugar. Com `rtDynamicShadows` ligado
o título monta e despacha trabalho de RT que a gente descarta — trabalho puro.

No `settings.ini` do launcher, `gamecuts=0` desliga tudo isso, e
`profile=fps` troca só a textura (mais macia, nada some); `profile=balanced`
volta ao padrão.
