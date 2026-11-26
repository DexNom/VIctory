# Controles

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

O console do runtime não abre janela nenhuma. Se precisar do log, ele está em
`%TEMP%\victory\` ou no `stderr` de quem iniciou o processo.
