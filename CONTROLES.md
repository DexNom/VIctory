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
