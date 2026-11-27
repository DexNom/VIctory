# Julias-Launcher.exe — relatório de engenharia reversa

Artefato de referência mantido para trabalho de paridade. Não é código nosso, não é
distribuído com o `GTA6.exe`, e não é carregado por nada em tempo de execução.

| Campo | Valor |
|---|---|
| Arquivo | `reference/Julias-Launcher.exe` |
| Tamanho | 671.744 bytes |
| SHA-256 | `8874DBE7D25C0925EE0D09600B52CBC0ABED9348734AE499E6D24706666C8535` |
| Formato | PE32 (`0x014C`, x86 / 32-bit), CLR |
| Assembly | `Julia's Launcher` |
| Target | `net40` — .NET Framework 4.0 |
| Tipo | `WinExe` (sem console) |
| LangVersion | 14.0 |
| Unsafe | habilitado |
| Ícone | `app.ico`, 61,9 KB embutido |
| Empacotador | nenhum — IL puro, sem ofuscação, sem packing |

Método: `ilspycmd 11.2.0.10050 -p` sobre o assembly. 45 arquivos C# produzidos, sem
símbolos, sem nomes de variáveis locais preservados (compilado em release sem PDB).

---

## 1. Referências de assembly

```
System.Core
System.IO.Compression
Microsoft.VisualBasic          -> Interaction.Shell (elevação)
System.IO.Compression.FileSystem -> ZipFile.ExtractToDirectory
System.Management              -> ManagementObjectSearcher (WMI de hardware)
System.Web.Extensions          -> JavaScriptSerializer (manifesto JSON)
```

`Microsoft.VisualBasic` + `System.Management` juntos são a assinatura de um launcher
que eleva privilégio e sonda hardware. Nenhuma P/Invoke de terceiros, nenhum driver,
nenhum payload embutido.

---

## 2. Ponto de entrada

```csharp
// Program.cs
[STAThread]
static void Main(string[] args)
```

Ordem de operação:

1. `AdminTasks.Run(args)` intercepta `--admin-setup`, `--pagefile`, `--elevated` antes
   de qualquer janela. Retorna `int` e encerra o processo quando consumiu o argumento.
2. Mutex nomeado de instância única.
3. `Application.EnableVisualStyles()` + `SetCompatibleTextRenderingDefault(false)`.
4. `Application.Run(new LauncherForm())`.
5. Handler de exceção não tratada: loga em `%LOCALAPPDATA%\VIctory\launcher-error.log`
   e mostra um `MessageBox`.

Não há verificação de assinatura, não há validação de ambiente antes da UI, não há
telemetria obrigatória.

---

## 3. Anatomia dos 45 arquivos

### Núcleo (3)

| Arquivo | KB | Função |
|---|---|---|
| `LauncherForm.cs` | 467 | monolito: UI, ambiente, perfis, download, monitor, driver |
| `Program.cs` | 2,6 | entrada, mutex, crash handler |
| `CustomProfile.cs` | 4,0 | perfil serializado em string |

### Tema e widgets (31)

`Theme`, `Kit`, `Fx`, `Mix`, `Anim` (+`Anim.Ambient`), `SmoothMotion`, `Pill`, `KeyCap`,
`Poster`, `Hero`, `Heading`, `Eyebrow`, `Rule`, `FootMeta`, `StatusStrip`, `LoadBar`,
`RoundCard`, `TimelineLine`, `StatusMark`, `BarChart`, `StatsTable`, `InfoCard`,
`PageHead`, `NavButton`, `Dropdown`, `ThemedList`, `Toggle`, `OptionCard`, `ModeCard`,
`GpuAdapter`

Desenho próprio sobre `System.Drawing`: retângulos arredondados, gradientes, sombras
suaves, animação por interpolação (`SmoothMotion`). Sem biblioteca de UI externa. Todo
o tema é uma paleta escura com um acento; `Theme.cs` (10,5 KB) concentra cores, fontes
e métricas.

### Hardware (3)

| Arquivo | Função |
|---|---|
| `Gpus.cs` / `GpuAdapter` | enumera `Win32_VideoController`, detecta vendor e VRAM |
| `HardwareInfo` (aninhado em `LauncherForm`) | `Win32_Processor`, `Win32_PhysicalMemory`, RAM total |

### Operação (5)

| Arquivo | Função |
|---|---|
| `AdminTasks.cs` | plano de energia, pagefile, preferência de GPU no registro |
| `ReleaseKey.cs` | lê o manifesto de release, verifica assinatura |
| `MemoryUser.cs` | consome o endpoint de hardware (`vi-hardware.edupodept.workers.dev`) |
| `Mode.cs` | preset de perfil |
| `Step` (aninhado) | etapa do assistente de preparação |

---

## 4. Pipeline de execução

```
LauncherForm.OnLoad
  ├─ DetectHardware()          WMI: GPU / CPU / RAM
  ├─ LoadSettings()            settings persistidos
  ├─ BuildUi()                 6 páginas: Play, Setup, Modes, Tweaks, Help, About
  └─ MonitorLoop()             timer lendo o log do runtime

LauncherForm.OnLaunch
  ├─ EnsureGameDir()           valida a pasta do jogo
  ├─ EnsureRuntime()           baixa/extrai o runtime se ausente
  ├─ WriteProfile()            grava o perfil escolhido
  ├─ Environment2()            monta as 64 variáveis VICTORY_*
  ├─ ApplyTestEnv()            sobrepõe test-env.txt (debug)
  ├─ Process.Start(victory.exe, env)
  └─ MonitorLoop()             lê victory.log, atualiza a barra de estado
```

---

## 5. As 64 chaves de ambiente

O contrato inteiro do launcher com o runtime. Valores são os do perfil "Recomendado"
salvo indicação contrária.

### Caminhos (4)
```
VICTORY_GAME_DIR      pasta do jogo
VICTORY_CACHE_DIR     %LOCALAPPDATA%\VIctory\cache   (ou cache-amd)
VICTORY_DATA_DIR      %LOCALAPPDATA%\VIctory\data
VICTORY_OUTPUT        WxH do modo reportado
```

### Log (1)
```
VICTORY_LOG   "warn,victory_gpu::host=info,victory_gpu::copystat=info,victory_gpu::framegen=info"
```

### Vídeo (5)
```
VICTORY_DISPLAY_MODE     WxH
VICTORY_WINDOW_MAX       WxH
VICTORY_BORDERLESS       1
VICTORY_BORDERLESS_DPI   0
VICTORY_FULLSCREEN       (implícito por BORDERLESS)
```

### Escala e reconstrução (5)
```
VICTORY_RENDER_SCALE   0.6666667      <- default do perfil
VICTORY_OWN_UPSCALER   1
VICTORY_FRAMEGEN       1
VICTORY_PANEL_FRAMEGEN SMOOTH MOTION | NVSMOOTH30 | VI 2X | OFF
VICTORY_FPS_MAX        60
VICTORY_FRAME_INTERVAL_MS 16.667
```

### Memória de placa (11)
```
VICTORY_CARD_BUFFERS_MB            1024   (512 / 768 / 1024 / 1536 / 2048)
VICTORY_CARD_BUFFERS_MIN_KB        64     (64 / 128)
VICTORY_CARD_ROOM_FOR_TARGETS_MB   1792   (768 / 1024 / 1792)
VICTORY_CARD_STEPS                 2
VICTORY_CARD_AWAKE                 1
VICTORY_CARD_REHASH_RUNS           1
VICTORY_TEXTURE_MAX                2048   (0 / 512 / 1024 / 2048)
VICTORY_TEXTURE_STAGE_MB           384    (64 / 128 / 384)
VICTORY_TEXTURE_NO_WAIT            1
VICTORY_TEXTURE_RAM_GUARD          0
VICTORY_VRAM_RESERVE               max | 0
VICTORY_WINDOWS_BAR_MB             1024
```

### RAM do host (2) — **não existem no nosso runtime**
```
VICTORY_PRESSURE_FREE_MB   1024
VICTORY_PRESSURE_CLEAR_MB  2048
```

### Pipelines (6)
```
VICTORY_PIPELINE_BOOK     1
VICTORY_SKIP_PIPELINES    "@<lista>"
VICTORY_ASK_EARLY         2
VICTORY_BAKE_NO_WAIT      1
VICTORY_JOIN_COPIES       1
VICTORY_JOIN_PARTS        8
VICTORY_LEVELS_ON_DEMAND  0 | 1
```

### Listas e desenho (9)
```
VICTORY_THIN              1
VICTORY_RELAY             2
VICTORY_RELAY_COPY        1
VICTORY_MAP_THREADS       1
VICTORY_INDIRECT_BATCH    2
VICTORY_INDIRECT_WINDOW   1
VICTORY_DYNAMIC_SLOTS     524288
VICTORY_POOL_FULLEST      1
VICTORY_RIPE_BLOCKS       4   (2 / 4)
VICTORY_UPLOAD_AHEAD      1
```

### Ports (5)
```
VICTORY_PORT_CBV          1
VICTORY_PORT_CONSTANTS    1
VICTORY_PORT_DESCRIPTORS  1
VICTORY_PORT_GUARD        1
VICTORY_PORT_JOURNAL      1
```

### Correções numéricas (3)
```
VICTORY_NAN_SCRUB          1
VICTORY_INF_AS             65504
VICTORY_BVH_MISS           1
```

### Onda e onda larga (2)
```
VICTORY_WAVE_WIDE          1
VICTORY_WAVE_WIDE_DIGESTS  7099a57fded142613b44f410463d6be6
VICTORY_WIDE_THREADS       max(2, min(12, ProcessorCount/2))
VICTORY_WIDE_BACK          1
```

### Apresentação e ambiente (3)
```
VICTORY_AMBIENT       1.6
VICTORY_AMBIENT_AIR   1.5
VICTORY_AMBIENT_MOST  2
VICTORY_SHOW_AFTER    1
```

### Entrada (1)
```
VICTORY_KBM_GTA   1
```

---

## 6. Perfis embutidos

| Nome | Escala | Texturas | Buffers | Guardas | FPS | Alvo |
|---|---|---|---|---|---|---|
| Recommended | 0.6666667 | completas | 1024 | off | 60 | 720p→1080p, 6-8 GB VRAM |
| Fastest | 0.5 | completas | 1024 | off | 60 | 540p, CPU fraca |
| Modest PC | 0.5 | ≤1024 | 768 | on | 60 | 6-8 GB, 16 GB RAM |
| Sharp | — | completas | 1024 | off | 60 | 1080p nativo, 12 GB+ |
| Classic cgbj | — | completas | 1024 | off | 60 | referência |
| Ultra Low | 0.5 | ≤512 | 512 | on | 30 | 4-6 GB, 8-16 GB RAM |
| Custom | livre | livre | livre | livre | 30/60 | ajustes avançados |

`Custom` serializa como código compartilhável:

```
s=<escala>;t=<textureMax>;b=<buffersMb>;r=<ripeBlocks>;g=<stageMb>;f=<fps>;m=<guardas>;l=<levelsOnDemand>
```

Com validação por hash — o `CustomProfile.Check` rejeita strings que não batem, para
um código colado não injetar valor fora da faixa.

---

## 7. Tarefas administrativas (`AdminTasks.cs`)

Invocadas por `--admin-setup` com elevação:

| Tarefa | Comando / chave |
|---|---|
| Plano de energia | `powercfg /overlaysetactive ded574b5-45a0-4f42-8737-46345c09c238` (Ultimate Performance) |
| Pagefile | `SetManagedPagefile()` se `PagefileOk()` for falso |
| Preferência de GPU | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` → `GpuPreference=2;` para `victory.exe` |

Nada disso é obrigatório para o jogo abrir. É conveniência de primeira execução.

---

## 8. Perfil de driver NVIDIA

O launcher escreve no perfil do driver, sem NVIDIA App:

- **Frame generation** para RTX 40/50 (`Smooth Motion`) em `victory.exe`
- **Resizable BAR** (`rBAR Enable`, `Enable v2`, `Options`)
- **Smooth Motion** ligado/desligado

Isso é ganho de driver, não de runtime. Não temos equivalente e não precisamos: o
`GTA6.exe` não depende disso para funcionar.

---

## 9. Rede

| Endpoint | Uso |
|---|---|
| `api.github.com/repos/felicienradio/cold-project-vi/releases` | lista de releases |
| `releases/latest/download/manifest.json` + `manifest.sig` | manifesto assinado |
| `github.com/felicienradio/cold-project-vi/archive/refs/heads/main.zip` | fonte |
| `github.com/Belobug/VIctory-Belobug-AMD/archive/<sha>.zip` | engine AMD |
| `vi-hardware.edupodept.workers.dev/v1/hw` | envio de hardware (opt-in) |
| `discord.gg/648WEXCAsZ` | convite |

**O launcher aponta para `felicienradio/cold-project-vi`, não para o nosso fork.**
Isso explica as 20 chaves que ele seta e o nosso runtime não lê.

---

## 10. As 20 chaves sem implementação no nosso runtime

Comparação entre as 64 chaves do launcher e as 407 `VICTORY_*` que o nosso código Rust
lê de fato:

```
VICTORY_AMBIENT_MOST
VICTORY_BAKE_NO_WAIT
VICTORY_BORDERLESS
VICTORY_BORDERLESS_DPI
VICTORY_BVH_MISS
VICTORY_FPS_MAX
VICTORY_FRAMEGEN
VICTORY_INF_AS
VICTORY_KBM_GTA
VICTORY_NAN_SCRUB
VICTORY_OWN_UPSCALER
VICTORY_PANEL_FRAMEGEN
VICTORY_POOL_FULLEST
VICTORY_PORT_DESCRIPTORS
VICTORY_PRESSURE_CLEAR_MB
VICTORY_PRESSURE_FREE_MB
VICTORY_TEXTURE_MAX
VICTORY_TEXTURE_RAM_GUARD
VICTORY_VRAM_RESERVE
VICTORY_WAVE_WIDE_DIGESTS
```

**Consequência:** o launcher da Julia é 100% funcional *contra o runtime do
`cold-project-vi`*. Contra o nosso, 44 das 64 chaves têm efeito e 20 são escritas no
vazio. "Igual a ele" tem de significar **a UI e o fluxo iguais**, com o conjunto de
chaves que o nosso runtime honra — não as 20 mortas.

---

## 11. Decisão de paridade

| Chave | Decisão | Motivo |
|---|---|---|
| `PRESSURE_FREE_MB` | **implementar** | knob limpo sobre `residency.rs`; default preserva o medido |
| `PRESSURE_CLEAR_MB` | **implementar** | idem |
| `TEXTURE_MAX` | documentar, não oferecer | redução de nível de textura derrubou o device no carregamento |
| `TEXTURE_RAM_GUARD` | documentar | sem efeito com `0`; o guarda atual é automático |
| `BORDERLESS` / `_DPI` | documentar | temos `VICTORY_FULLSCREEN`; borderless não existe |
| `FPS_MAX` / `FRAME_INTERVAL_MS` | não oferecer | `FRAME_INTERVAL_MS` reprovado por medição |
| `KBM_GTA` | documentar | teclado/mouse já entram como controle Xbox no `GTA6.exe` |
| `NAN_SCRUB` / `INF_AS` | documentar | sanitização numérica não existe no tradutor |
| `POOL_FULLEST` | documentar | escolha de pool é automática aqui |
| `BVH_MISS` | documentar | sem BVH de ray tracing neste caminho |
| `OWN_UPSCALER` / `FRAMEGEN` / `PANEL_FRAMEGEN` | documentar | geração de quadros não existe neste runtime |
| `PORT_DESCRIPTORS` | não oferecer | reprovado por medição (`soak29`) |
| `VRAM_RESERVE` | documentar | sem equivalente |
| `WAVE_WIDE_DIGESTS` | documentar | hash de shader específico da build deles |
| `AMBIENT_MOST` / `BAKE_NO_WAIT` | documentar | cosmético / específico da build deles |
| Tarefas administrativas | **implementar** | plano de energia + preferência de GPU; pagefile só relatado |
| Detecção de hardware | **implementar** | registro + `GlobalMemoryStatusEx`, sem WMI |
| Código de perfil compartilhável | **implementar** | `s=;b=;t=;m=;w=;p=;r=;g=;c=`, validado inteiro |
| Presets de modo | **implementar** | seis modos; só dois medidos nesta máquina |
| Download/atualização do runtime | não | o runtime vem embutido no `GTA6.exe` |
| Perfil de driver NVIDIA | não | ganho de driver, não de runtime; não é necessário |
| Telemetria de hardware | não | o nosso launcher não envia nada |

O que a paridade entrega: a mesma janela, os mesmos presets, o mesmo código de perfil
compartilhável, a mesma detecção de hardware, o mesmo assistente de preparação — com o
conjunto de chaves que este runtime honra de fato.

---

## 12. Licença

`Julias-Launcher.exe` é um binário de terceiro. Fica em `reference/` como artefato de
estudo, fora do `GTA6.exe`, fora do build, fora de qualquer caminho de execução. Nada
dele é copiado para o nosso código-fonte: o launcher que escrevemos é original e usa o
nosso runtime.
