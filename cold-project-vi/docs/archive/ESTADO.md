# Estado do VIctory neste portatil

Atualizado pelo Claude a cada volta do ciclo. Para ver o jogo ao vivo, olha para a janela do
"VIctory teste em ciclo": de 15 em 15 s mostra FPS, ms por frame, desenhos e RAM livre (sem custo).

## Como jogar e comparar as texturas na RTX 5070 Ti Laptop

Abre **Jogar VIctory** no ambiente de trabalho ou executa `.\Jogar.cmd` na raiz deste projeto. Carrega em **Enter** no seletor. A versão normal usa `target\release\victory.exe`. Os atalhos **VIctory teste automático** e **VIctory teste em ciclo** usam `target\diag\victory.exe`. O ciclo recompila a cada volta e termina com **H** na sua janela. Os três usam a instalação em `%USERPROFILE%\Downloads\dump private build` e a pasta `victory-xtx-run` indicada em `scripts\jogar.ps1`.

A referência de 26 de novembro é `0875feec976f5244bdc8aad3a1b1764da208437a` no ramo `codex/cgbj-nvidia`. Na sessão das 11h34, o utilizador chegou ao corredor e ao gabinete. Os fotogramas do corredor marcaram 15 e 17 FPS. Há materiais pretos, manchas roxas e relatos de oscilações nas superfícies e na luminosidade de todo o ecrã com a câmara parada. O executável dessa sessão tem SHA-256 `4AD437B8647A1613976E2AD4E84897FD02C66116B44FB7CD72021AE9AD126573`. A cópia encontra-se em `checkpoints\victory-cgbj-0875fee-20261126\release-4ad437b8-victory.exe`, junto à pasta de corrida.

A fila de texturas agora alterna entre primeiras cargas e novas tentativas. Quando páginas de uma textura saem da memória, a fila volta a aceitar essas páginas mesmo que regressem com o mesmo número de bytes. O log distingue cópias parciais de cópias completas à espera de verificação. O total de entradas inclui ambas e pode incluir vários descritores da mesma textura.

O perfil padrão de jogo passou a usar os quatro blocos de reserva, o limite de 1 GiB para buffers via BAR e os limiares de memória de 1 e 2 GiB já usados no ciclo. `.\Jogar.cmd -Seguro` usa os valores de origem para estas quatro opções. Depois de fechar, o próprio executável que correu prepara os pipelines registados com `warm --used`. As páginas das texturas continuam a chegar do jogo durante a cena.

Para registar o piscar, corre `.\scripts\gravar-piscar.ps1` com o jogo aberto e volta ao jogo. A gravação começa cinco segundos depois e guarda oito segundos da janela em **Vídeos\VIctory**. Usa o FFmpeg com o filtro `gfxcapture`. Para comparar duas builds, entra no corredor, para junto à porta marcada B36 e mantém a câmara no mesmo ângulo. Regista se mudam as paredes, o chão, a personagem ou o ecrã inteiro. A validação visual destas alterações no corredor continua em aberto.

## Registo das 09h50 de 26 de novembro

- O ciclo usava cgbj, 1080p, 1 GB de buffers, 4 blocos a frente, aperto de RAM abaixo de
  1 GB e luz ambiente 1,6. Naquele momento o atalho manual ainda usava os limites de reserva e de RAM do fork.
- Escuridao e olhos a brilhar: sem ray tracing (1-12 FPS aqui) a luz indireta e uma estimativa.
- Proximo: confirmar a luz 1,6 nas capturas; memoria das listas de comandos (4-7 GB).

## Antes (08:55)

- A jogar: atalho "VIctory cgbj (original)" = launcher do cgbj sem alteracoes + 1080p + buffers
  na placa (agora 1 GB, para sobrar VRAM para texturas). Limite atual: VRAM e RAM cheias.

## Antes (08:30)

- Versao: ramo `codex/cgbj-nvidia` = reupload cgbj (pareto-residency, afinado numa RTX 5090 com
  32 GB) + as nossas ferramentas. Shaders pre-traduzidos: 25 756 (229 nao traduzem; 26 deles sao
  shaders de 64 threads sem conversao para a NVIDIA, que antes eram recusados pelo driver).
- Definicoes: 1080p, buffers do jogo na placa (1-1536 MB), 64 listas no ar, texturas ate 3 GB na
  placa, niveis de texturas a pedido. Series X.
- A seguir: primeira volta do ciclo com esta versao; comparar FPS, crashes e capturas.

## Resultados por volta

| Hora | Versao / mudanca | FPS mediana (mundo) | Placa ms | Resultado |
|---|---|---|---|---|
| 04:46 | xtx, Series X, 4K | ~2,3 | ~200 | 1 h sem crash; ~20 % das tabelas recusadas |
| 06:24 | + 1080p, buffers na placa, 64 listas | 9,1 | 53 | sem crash; nevoa e objetos em falta |
| 08:21 | + VICTORY_WAVE_WIDE=1, 32 listas | - | - | placa perdida aos 1,7 min: descartado |
| 08:42 | cgbj, launcher dele tal e qual (4K) | ~1 | 400-1600 | 0 tabelas recusadas; demasiado pesado para 12 GB |
| 08:47 | cgbj + 1080p + 1,5 GB de buffers na placa | 18-22 parado, 8-10 a andar | - | sem crash; VRAM cheia (9,46 de 9,6 GB): 19 453 texturas por carregar |
| 09:37 | + BAR (janelas na VRAM), 4 blocos a frente, aperto so abaixo de 1 GB, 1 GB de buffers | 18,1 (mediana, 60 intervalos) | - | sem crash; texturas mais rapidas (~24 MB/frame), VRAM ainda o limite |

## O que ja se sabe

- A RTX aceita no maximo 1 000 000 descritores: as tabelas copiadas esgotam o espaco dinamico.
- 8 shaders de computacao (visibilidade, nevoa/volumetria) sao de 64 threads e a NVIDIA so corre 32.
- O metodo 0x1c8 nao e preciso em Series X (o jogo tem alternativa); em Series S a intro depende dele.
- Detalhes: `docs/nvidia-notes.md`, `docs/device-0x1c8.md`.
