using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

// GTA6.exe - tudo em um so: extrai o runtime (VIctory, base Belobug + o perfil
// de 16 GB), o Agility SDK, acha o dump, cria o game-path e joga.
//
// Teclado e mouse ja entram como controle Xbox. O mouse olha enquanto o botao
// direito esta apertado; solto, o cursor e o do PC. O console do runtime nao
// abre janela: so a janela do jogo aparece.
static class GTA6
{
    const string GameExe = "game_scarlett_master.exe";
    // Tudo mora na pasta do GTA6.exe: runtime, D3D12, cache, state, logs.
    // Mover a pasta inteira nao quebra nada.
    static readonly string Runtime = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    static readonly string GamePathFile = Path.Combine(Runtime, "game-path.txt");
    // As opcoes do jogo, como as de um jogo instalado: ficam num arquivo ao
    // lado do GTA6.exe e valem para toda partida, inclusive as de linha de
    // comando. `fullscreen=0|1`.
    static readonly string SettingsFile = Path.Combine(Runtime, "settings.ini");

    static Form window;
    static Label status;
    static Button play720;
    static Button play1080;
    static Button playPanel;
    static Button prepare;
    static CheckBox fullScreenBox;
    static Process running;
    static Timer focus;
    static int focusTicks;
    static bool preparing;

    [STAThread]
    static void Main(string[] args)
    {
        bool prepareOnly = args.Length > 0 && args[0] == "--prepare";
        bool book = args.Length > 0 && args[0] == "--book";
        bool headless = args.Length > 0 && (args[0].StartsWith("--play", StringComparison.Ordinal) || book);
        bool installOnly = args.Length > 0 && args[0] == "--install";

        try
        {
            Install();
        }
        catch (Exception error)
        {
            if (headless || installOnly || prepareOnly)
            {
                Environment.Exit(1);
                return;
            }
            MessageBox.Show("Falha ao preparar o runtime.\n\n" + error.Message, "GTA6",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (installOnly)
        {
            Environment.Exit(0);
            return;
        }

        // Uma sessao que grava o livro de pipelines (VICTORY_PIPELINE_BOOK=1):
        // e dela que o `warm` tira a lista. Roda uma vez, depois --prepare.
        if (book)
        {
            Environment.SetEnvironmentVariable("VICTORY_PIPELINE_BOOK", "1");
        }

        if (prepareOnly)
        {
            // Os shaders primeiro: sem eles o `warm` fabrica pipelines de um
            // tradutor que ainda nao leu o pacote, e a partida traduz o resto
            // no meio do quadro.
            if (Shaders() <= 0)
            {
                Console.WriteLine("A traducao dos shaders nao terminou.");
            }
            if (!HasBook())
            {
                Console.WriteLine("Sem livro de pipelines: rode --book uma vez.");
                Environment.Exit(2);
                return;
            }
            Environment.Exit(Warm() > 0 ? 0 : 1);
            return;
        }

        if (headless)
        {
            try
            {
                bool high = args[0] == "--play-1080p" || args[0] == "--play-1440p";
                bool panel = args[0] == "--play-panel";
                Process game = StartGame(high, panel, true);
                game.WaitForExit();
                Environment.Exit(game.ExitCode);
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "GTA6", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        BuildWindow();
        Application.Run(window);
    }

    // ---------------------------------------------------------------- install

    static void Install()
    {
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Path.Combine(Runtime, "D3D12"));

        Extract("victory.exe", Path.Combine(Runtime, "victory.exe"));
        Extract("D3D12Core.dll", Path.Combine(Runtime, "D3D12", "D3D12Core.dll"));
        Extract("CONTROLES.md", Path.Combine(Runtime, "CONTROLES.md"));
        Extract("OTIMIZACOES.md", Path.Combine(Runtime, "OTIMIZACOES.md"));
        ImportScarlettCache();

        if (!File.Exists(GamePathFile) || !File.Exists(ReadGamePath()))
        {
            string found = FindGame();
            if (found != null)
            {
                WriteGamePath(found);
            }
        }
    }

    static void Extract(string name, string target)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using (Stream source = assembly.GetManifestResourceStream(name))
        {
            if (source == null)
            {
                throw new InvalidOperationException("Pacote incompleto: " + name);
            }
            // O tamanho nao basta: duas builds podem sair do mesmo tamanho e
            // uma delas estar velha (foi o que deixou o Agility SDK de fora).
            // O assembly e sempre mais novo que o que ele extraiu.
            if (File.Exists(target)
                && new FileInfo(target).Length == source.Length
                && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(assembly.Location))
            {
                return;
            }
            string temporary = target + ".new";
            using (FileStream output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(output);
            }
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            File.Move(temporary, target);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(assembly.Location));
        }
    }

    // O cache do ScarlettPort serve neste runtime: mesma linhagem de codigo e
    // mesmo formato de arquivo (medido em 27/11: 20 de 20 pipelines feitas a
    // partir do livro deles, 0 sem host pipeline). Traz de la as 83.561
    // pipelines conhecidas, os shaders ja traduzidos e 341 MB de pipelines de
    // host prontas - o que apaga a tela preta de cache frio sem esperar as
    // 26.000 traducoes do `shaders.rpf`. So roda se o ScarlettPort estiver
    // instalado; sem ele, nada muda.
    static void ImportScarlettCache()
    {
        string theirs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScarlettPort", "cache", "pipelines");
        if (!Directory.Exists(theirs))
        {
            return;
        }
        string ours = PipelineFolder();
        Directory.CreateDirectory(ours);
        foreach (string name in new string[] { "known-pipelines.bin", "known-shaders.bin" })
        {
            string from = Path.Combine(theirs, name);
            string to = Path.Combine(ours, name);
            if (!File.Exists(from))
            {
                continue;
            }
            if (File.Exists(to) && new FileInfo(to).Length >= new FileInfo(from).Length)
            {
                continue;
            }
            File.Copy(from, to, true);
        }
        // A biblioteca de pipelines de host: so entra se a nossa for menos da
        // metade da deles. Uma biblioteca nossa ja tem as pipelines do nosso
        // perfil e cresce com o uso; nao se troca por uma menor.
        string library = Path.Combine(theirs, "host-pipelines.bin");
        string target = Path.Combine(ours, "host-pipelines.bin");
        if (File.Exists(library)
            && (!File.Exists(target) || new FileInfo(target).Length * 2 < new FileInfo(library).Length))
        {
            File.Copy(library, target, true);
        }
        // Os hashes que os quadros desenham primeiro: a uniao dos dois.
        string earlyTheirs = Path.Combine(theirs, "early-pipelines.txt");
        if (File.Exists(earlyTheirs))
        {
            string early = Path.Combine(ours, "early-pipelines.txt");
            HashSet<string> all = new HashSet<string>();
            foreach (string path in new string[] { early, earlyTheirs })
            {
                if (!File.Exists(path))
                {
                    continue;
                }
                foreach (string line in File.ReadAllLines(path))
                {
                    string hash = line.Trim();
                    if (hash.Length == 32)
                    {
                        all.Add(hash);
                    }
                }
            }
            File.WriteAllLines(early, all);
        }
    }

    static string ReadGamePath()
    {
        try
        {
            return File.ReadAllText(GamePathFile).Trim().TrimStart('\uFEFF').Trim();
        }
        catch (Exception)
        {
            return "";
        }
    }

    static void WriteGamePath(string folder)
    {
        File.WriteAllText(GamePathFile, Path.Combine(folder, GameExe), new UTF8Encoding(false));
    }

    static string Setting(string key, string fallback)
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    int at = line.IndexOf('=');
                    if (at <= 0)
                    {
                        continue;
                    }
                    if (string.Equals(line.Substring(0, at).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Substring(at + 1).Trim();
                        if (value.Length > 0)
                        {
                            return value;
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        return fallback;
    }

    static void SaveSetting(string key, string value)
    {
        try
        {
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
            bool wrote = false;
            if (File.Exists(SettingsFile))
            {
                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    int at = line.IndexOf('=');
                    if (at > 0 && string.Equals(line.Substring(0, at).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(key + "=" + value);
                        wrote = true;
                    }
                    else
                    {
                        lines.Add(line);
                    }
                }
            }
            if (!wrote)
            {
                lines.Add(key + "=" + value);
            }
            File.WriteAllLines(SettingsFile, lines.ToArray(), new UTF8Encoding(false));
        }
        catch (Exception)
        {
        }
    }

    static bool FullScreen()
    {
        return Setting("fullscreen", "0").Trim() == "1";
    }

    static string FindGame()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        {
            Path.Combine(profile, "Downloads", "GTA6.Xbox.v1.0.2.1-CYBERLEEK"),
            Path.Combine(profile, "Downloads", "GTA6.Xbox.v1.0.2.1"),
            Path.Combine(profile, "Desktop", "GTA6.Xbox.v1.0.2.1-CYBERLEEK"),
            @"C:\GTA6.Xbox.v1.0.2.1-CYBERLEEK",
            @"D:\GTA6.Xbox.v1.0.2.1",
            @"D:\GTA6.Xbox.v1.0.2.1-CYBERLEEK",
        };
        foreach (string folder in candidates)
        {
            if (File.Exists(Path.Combine(folder, GameExe)))
            {
                return folder;
            }
        }
        return null;
    }

    static bool AskGame()
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "Escolha a pasta que tem o arquivo " + GameExe + ".";
            dialog.ShowNewFolderButton = false;
            string current = ReadGamePath();
            if (current.Length > 0)
            {
                string folder = Path.GetDirectoryName(current);
                if (Directory.Exists(folder))
                {
                    dialog.SelectedPath = folder;
                }
            }
            if (dialog.ShowDialog(window) != DialogResult.OK)
            {
                return false;
            }
            if (!File.Exists(Path.Combine(dialog.SelectedPath, GameExe)))
            {
                MessageBox.Show("Essa pasta nao tem o " + GameExe + ".", "GTA6",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            WriteGamePath(dialog.SelectedPath);
            return true;
        }
    }

    // ------------------------------------------------------------------ play

    // O perfil da build numa maquina de 16 GB (Ryzen 5 5600G, 16 GB, RTX 3060
    // Ti 8 GB, Windows 10). Cada linha foi medida; o porque esta em
    // OTIMIZACOES.md. Cada grupo e uma funcao: mexer num budget nao pede ler o
    // perfil inteiro.
    static void Profile(ProcessStartInfo start, bool high, bool panel)
    {
        ProfileFork(start);
        ProfilePorts(start);
        ProfilePicture(start, high);
        ProfileCard(start);
        ProfileMemory(start);
        ProfileThreads(start);
        ProfileInput(start, panel);
        ProfileKept(start);
    }

    // O fork em modo rapido: as estradas novas dele, medidas nesta maquina.
    static void ProfileFork(ProcessStartInfo start)
    {
        start.EnvironmentVariables["VICTORY_TEXTURE_NO_WAIT"] = "1";
        // RELAY=2 desenhou uma parede no lugar errado na documentacao do
        // proprio fork; 1 e o valor de todas as runs medidas aqui (cf-11 em
        // diante): GPU de 22-68% para 85-100%.
        start.EnvironmentVariables["VICTORY_RELAY"] = "1";
        start.EnvironmentVariables["VICTORY_WIDE_BACK"] = "1";
        start.EnvironmentVariables["VICTORY_CARD_REHASH_RUNS"] = "1";
        start.EnvironmentVariables["VICTORY_JOIN_COPIES"] = "1";
        start.EnvironmentVariables["VICTORY_INDIRECT_WINDOW"] = "1";
        start.EnvironmentVariables["VICTORY_RELAY_COPY"] = "1";
        start.EnvironmentVariables["VICTORY_CARD_STEPS"] = "2";
        start.EnvironmentVariables["VICTORY_INDIRECT_BATCH"] = "2";
        start.EnvironmentVariables["VICTORY_MAP_THREADS"] = "1";
        // TITLE_PRIORITIES=1 (run cf-13): com 0 as threads do titulo ficam em
        // IDLE (-15) e o `xmem::lift_thread` (que so levanta -2..0) nunca as
        // sobe; com o padrao elas ficam em BELOW_NORMAL e vao a ABOVE_NORMAL.
        // A fila "waiting for their memory" caiu de 22.096 para ~1.000 sem
        // custo no tempo de quadro.
        start.EnvironmentVariables["VICTORY_TITLE_PRIORITIES"] = "1";
        start.EnvironmentVariables["VICTORY_CARD_AWAKE"] = "1";
        start.EnvironmentVariables["VICTORY_THIN"] = "1";
        // O corte das listas em partes (1 a 3 ms por quadro) e o bercario de
        // listas do host ficam no padrao do fork: com 8 partes e 2.048 listas
        // guardadas o processo ficou MAIOR e morreu antes (runs 13 e 14).
        start.EnvironmentVariables["VICTORY_JOIN_PARTS"] = "8";
    }

    // O Port 5 do avatarmanz, que esta build carrega: o buffer de constantes e
    // lido como bytes no lugar, sem a copia que ele fazia por draw. Nesta CPU
    // (6 nucleos, ~48 mil chamadas de lista por quadro) a copia e o trabalho,
    // nao o desenho: e a aposta de desempenho mais direta do perfil.
    static void ProfilePorts(ProcessStartInfo start)
    {
        start.EnvironmentVariables["VICTORY_PORT_CBV"] = "1";
    }

    // A imagem: o modo que o titulo reporta, a escala e a biblioteca de
    // pipelines.
    static void ProfilePicture(ProcessStartInfo start, bool high)
    {
        start.EnvironmentVariables["VICTORY_SHOW_AFTER"] = "1";
        start.EnvironmentVariables["VICTORY_AMBIENT"] = "0.8";
        // A nevoa marrom de interior que a luz do ceu dos probes poe: 1.5 e o
        // valor que o proprio fork usa no perfil pareto-residency.
        start.EnvironmentVariables["VICTORY_AMBIENT_AIR"] = "1.5";
        // O titulo acredita que a TV e 4K e desenha 2560x1440 dentro de uma
        // janela 1080p: 4x os alvos, 4x as copias. VICTORY_OUTPUT e o modo que
        // ele reporta; a escala 1.0 desenha na resolucao do modo, sem upscaler.
        // A 0.5 (640x360) o upscaler dele mesmo (6) aplica nitidez e a imagem
        // fica dura, com halo; a placa fica 78% ociosa de qualquer jeito, entao
        // desenhar 720p inteiro custa quase nada e tira o serrilhado.
        start.EnvironmentVariables["VICTORY_OUTPUT"] = high ? "1920x1080" : "1280x720";
        start.EnvironmentVariables["VICTORY_UPSCALER"] = "6";
        // 1.0 desenha 1280x720 inteiro, 1:1. A 0.8 (1024x576) o upscaler do
        // proprio titulo (6) estica e aplica nitidez: a imagem fica dura, com
        // halo nos fios de cabelo. O ganho de fps da 0.8 nao se paga: no
        // mundo a CPU e a parede (41-50 ms de preparo contra 30-37 de GPU),
        // entao a placa tem folga para desenhar 720p inteiro.
        start.EnvironmentVariables["VICTORY_RENDER_SCALE"] = "1.0";
        // O nivel de textura fica em `high`: a reducao por mip bias removia o
        // device na tela de carregamento, entao ela esta desligada no runtime
        // e o launcher nao a oferece.
        // Janela sem borda do tamanho do monitor. Alt+Enter e F11 alternam
        // dentro do jogo; a swap chain mantem o tamanho que o titulo fez e
        // estica, entao nada e recriado e nenhum modo de video muda.
        start.EnvironmentVariables["VICTORY_FULLSCREEN"] = FullScreen() ? "1" : "0";
        // A biblioteca de pipelines: 200 e poucos MB de disco que valem a fila
        // inteira. STORE_LATER entrega a pipeline na hora e a grava com o save,
        // em vez de segurar o quadro esperando o disco.
        start.EnvironmentVariables["VICTORY_LIBRARY"] = "1";
        start.EnvironmentVariables["VICTORY_LIBRARY_STORE_LATER"] = "1";
    }

    // A placa de 8 GB e o que ela guarda.
    static void ProfileCard(ProcessStartInfo start)
    {
        // A 95% a fila enchia o cartao ate a linha e todo alvo ou copia que o
        // host fizesse depois era recusado: textura expulsa e subida de novo
        // (15.462 esperando, 5.655 MB enviados na run cf-1). 90%, com a dieta
        // menor abaixo (cf-12/cf-13), deixa ~1 GB e o orcamento nunca mais foi
        // o gatilho em run nenhuma depois disso.
        start.EnvironmentVariables["VICTORY_TEXTURE_ROOM_PERCENT"] = "90";
        start.EnvironmentVariables["VICTORY_CARD_ROOM_FOR_TARGETS_MB"] = "768";
        // 128, nao os 1.536 do perfil antigo: o espaco guardado para alvos que
        // ainda nao existem e o que a fila de textura nao tem num cartao de
        // 8 GB.
        start.EnvironmentVariables["VICTORY_MEMORY_CARD_MB"] = "128";
        start.EnvironmentVariables["VICTORY_CARD_WINDOWS_MB"] = "96";
        // 768 MB de buffers no cartao, e MIN_MB=1 para o pool poder encher ate
        // a linha: com o pool em 192 MB a varredura de escrita dos shaders
        // virou thrash (cf-4). Com 768 a varredura caiu de 66,6 ms/658 MB
        // para 3,2 ms/30 MB (cf-12).
        start.EnvironmentVariables["VICTORY_CARD_BUFFERS_MB"] = "768";
        start.EnvironmentVariables["VICTORY_CARD_BUFFERS_MIN_MB"] = "1";
    }

    // Os 16 GB de RAM: tudo o que o host guarda, pequeno. Sem
    // VICTORY_TEXTURE_BUDGET_MB de proposito: com ele o host para de
    // subir textura no teto e o mundo fica sem as 4 GB que pede. A residencia
    // e automatica nesta build: com 32 GiB ou menos ela ja limita o prefetch a
    // 64 MiB por quadro e solta staging parado, sem knob.
    static void ProfileMemory(ProcessStartInfo start)
    {
        start.EnvironmentVariables["VICTORY_UPLOAD_AHEAD"] = "1";
        // 16 GB: o que da para encolher sem tirar nada do jogo.
        // O snapshot dos buffers que os shaders escrevem era refeito inteiro
        // (339 MB) por quadro para achar os ~11 MB que mudaram: 55 ms de um
        // quadro de 134 (run cf-3). Com o relay (RELAY=1) as copias voltam
        // numa thread propria, e guardar o snapshot desliga o relay
        // (`shadow::relay`/`keeps`): a GPU foi de 22-68% para 85-100% com
        // KEEP_SNAPSHOTS=0 (cf-11).
        start.EnvironmentVariables["VICTORY_KEEP_SNAPSHOTS"] = "0";
        // O titulo commita 22 GB nesta maquina de 16. O que sobra vai para o
        // pagefile, e com a RAM cheia cada chamada de lista passou de 21 us:
        // 41.283 chamadas por quadro custavam 897,7 ms somados (run cf-1).
        // Soltar as listas paradas derrubou isso para 316,3 ms com as mesmas
        // ~40.000 chamadas (run cf-3).
        start.EnvironmentVariables["VICTORY_LIST_IDLE_TRIM_S"] = "10";
        start.EnvironmentVariables["VICTORY_LIST_IDLE_MOST"] = "192";
        // VICTORY_WINDOW_KB=256 foi medido e e PIOR (cf-16): 6.224 janelas de
        // 256 KiB para 4.081 MB contra 1.130 de 4 MiB para 4.724 MB no cf-15 —
        // economiza 643 MB de dados e custa 5,5x mais janelas e 2,6x mais
        // buffers compartilhados (42.594 shared/28.615 MB contra 16.259/
        // 10.818), e o heap pass foi de ~10 ms para 176 ms. Fica nos 4 MiB.
        // Blocos de textura feitos adiantados (64 MiB cada).
        start.EnvironmentVariables["VICTORY_RIPE_BLOCKS"] = "4";
        // O anel de constantes do titulo e' reaproveitado, entao os dois testes
        // do FILL_TITLE=1 (`written_since_taken`, `!landed_lately`) nunca
        // passavam e 22 a 24 ms por quadro iam em espera por um fill de 80
        // bytes. =2 deixa so o tamanho decidir (ate' 64 KiB): as esperas
        // desapareceram na run cf-18.
        start.EnvironmentVariables["VICTORY_FILL_TITLE"] = "2";
        // Um buffer de leitura cresce ate a maior textura que a thread leu e fica.
        start.EnvironmentVariables["VICTORY_READ_KEEP_MB"] = "64";
        // Duas texturas com as mesmas palavras de descricao, mudando so o
        // "filler", viravam dois recursos e duas copias (8.438 delas na run 15).
        start.EnvironmentVariables["VICTORY_PLACED_FILLER"] = "1";
        // Um pipeline que falta faz o draw sumir; 1 s de espera por quadro
        // custa menos que um personagem sem corpo na cutscene.
        start.EnvironmentVariables["VICTORY_PIPELINE_WAIT_MS"] = "1000";
    }

    // As threads: 6 nucleos, 12 threads de hardware.
    static void ProfileThreads(ProcessStartInfo start)
    {
        // O padrao do fork e dimensionado para um 12900K de 24 threads. Aqui
        // ele punha ~30 threads prontas em 12 e a thread que submete (a parede
        // do quadro) era preemptada: 550 ms de um quadro de 1.070 esperando no
        // present (run cf-1).
        start.EnvironmentVariables["VICTORY_THIN_THREADS"] = "6";
        start.EnvironmentVariables["VICTORY_PIPELINE_WORKERS"] = "4";
        start.EnvironmentVariables["VICTORY_LAND_THREADS"] = "2";
        start.EnvironmentVariables["VICTORY_UNDER_THREADS"] = "3";
        // 5, nao 3: o passe do heap de views era 30,9 ms numa thread e 12,5 ms
        // de espera no fecho das listas. soak15 = 97,3 ms e soak16 = 90,0 ms,
        // contra os 115,5 ms do soak3. Os records ficam em 4: o soak14 baixou
        // para 3 junto com as views e as duas brigaram (140,7 ms).
        start.EnvironmentVariables["VICTORY_VIEW_THREADS"] = "5";
    }

    // Entrada, painel e o quanto o runtime fala.
    static void ProfileInput(ProcessStartInfo start, bool panel)
    {
        start.EnvironmentVariables["VICTORY_D3D12_DRED"] = "light";
        // O mouse olha enquanto o botao direito esta apertado.
        start.EnvironmentVariables["VICTORY_MOUSE_LOOK"] = "1";
        start.EnvironmentVariables["VICTORY_MOUSE_SENSITIVITY"] = "0.05";
        start.EnvironmentVariables["VICTORY_LOG"] = "warn";
        if (panel)
        {
            start.EnvironmentVariables["VICTORY_PANEL"] = "1";
        }
    }

    // O que estiver no ambiente do Windows vence, para testar sem
    // recompilar. As pastas nunca sao sobrescritas.
    static void ProfileKept(ProcessStartInfo start)
    {
        string[] keep =
        {
            "VICTORY_CONFIG", "VICTORY_GAME_DIR", "VICTORY_CACHE_DIR", "VICTORY_DATA_DIR",
        };
        foreach (string name in keep)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                start.EnvironmentVariables[name] = value;
            }
        }
    }

    // O settings.xml do proprio titulo tem alavancas que o runtime nao tem:
    // sombra por ray tracing, a UI desenhada em 3840x2160 num jogo que sai em
    // 1280x720, VSync, limite de 30 quadros, SSR da agua, motion blur. O perfil
    // corta o trabalho de pixel caro e mantem TODA textura presente: o mip bias
    // de -1.5 e do proprio titulo, aplicado no lugar dele, entao nada some e a
    // imagem so fica menos nitida (a nitidez volta com sharpen ligado).
    // `gamecuts=0` no settings.ini desliga. O arquivo e do jogo: o titulo o
    // regrava ao sair, entao isto roda antes de cada partida.
    static readonly string[] GameCuts = new string[]
    {
        "frameRateLimit=0", "vSync=0", "desiredFPS=120",
        "uiResolution.width=1280", "uiResolution.height=720",
        "resolutionFullscreen.width=1280", "resolutionFullscreen.height=720",
        "anisotropicFiltering=0", "graphicsQualityPreset=0.000000",
        "motionBlur=false", "rtDynamicShadows=false", "shadowParticleShadows=false",
        "ssroSampleCount=1", "waterReflectionSSR=false",
        "waterReflectionResX=384", "waterReflectionResY=192",
        "grassLod=0.400000", "lodScale=0.800000", "smallCullThreshold=1.500000",
        "decorationResolutionShift=1", "maxTexUpgradesPerFrame=5",
        "manualMipBias=-1.500000", "sharpenEnabled=true", "fsrSharpen=0.200000",
        "texLodScale_Global=1.000000", "texLodScale_Buildings=1.000000",
        "texLodScale_Objects=1.000000", "texLodScale_HD=1.000000",
    };

    // `profile=fps` no settings.ini: as mesmas listas acima com a textura mais
    // macia, e nada mais. Cada textura desce dois niveis de mip (fica com um
    // quarto dos pixels) e continua la: nenhuma some, nenhuma e' descartada.
    // Medido: nao ganha fps (o custo e' por draw, nao por texel), entao nao e'
    // o padrao. E' opcao de gosto, e volta com `profile=balanced`.
    static readonly string[] GameCutsFps = new string[]
    {
        "manualMipBias=-2.500000", "maxTexUpgradesPerFrame=2",
        "texLodScale_Global=2.000000", "texLodScale_Buildings=2.000000",
        "texLodScale_Objects=2.000000", "texLodScale_HD=2.000000",
    };

    static string TitleSettings()
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "victory", "data");
            if (!Directory.Exists(root))
            {
                return null;
            }
            string best = null;
            DateTime newest = DateTime.MinValue;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string file = Path.Combine(dir, "storage", "settings", "settings.xml");
                if (!File.Exists(file))
                {
                    continue;
                }
                DateTime when = File.GetLastWriteTimeUtc(file);
                if (when > newest)
                {
                    newest = when;
                    best = file;
                }
            }
            return best;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string[] Combine(string[] first, string[] second)
    {
        string[] all = new string[first.Length + second.Length];
        Array.Copy(first, 0, all, 0, first.Length);
        Array.Copy(second, 0, all, first.Length, second.Length);
        return all;
    }

    static void TweakGame()
    {
        if (Setting("gamecuts", "1") == "0")
        {
            return;
        }
        try
        {
            string file = TitleSettings();
            if (file == null)
            {
                // O titulo ainda nao rodou uma vez: ele cria o arquivo sozinho.
                return;
            }
            string text = File.ReadAllText(file);
            int changed = 0;
            string[] cuts = Setting("profile", "balanced") == "fps"
                ? Combine(GameCuts, GameCutsFps)
                : GameCuts;
            foreach (string cut in cuts)
            {
                int at = cut.IndexOf('=');
                string tag = cut.Substring(0, at);
                string value = cut.Substring(at + 1);
                Match match = Regex.Match(text, "<" + tag + "\\s+value=\"([^\"]*)\"");
                if (!match.Success || match.Groups[1].Value == value)
                {
                    continue;
                }
                text = text.Substring(0, match.Groups[1].Index) + value + text.Substring(match.Groups[1].Index + match.Groups[1].Length);
                changed++;
            }
            if (changed == 0)
            {
                return;
            }
            string backup = file + ".bak-gta6";
            if (!File.Exists(backup))
            {
                File.Copy(file, backup);
            }
            File.WriteAllText(file, text, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // O jogo cuida das proprias configuracoes se isto falhar.
        }
    }

    static Process StartGame(bool high, bool panel, bool headless)
    {
        string game = ReadGamePath();
        if (game.Length == 0 || !File.Exists(game))
        {
            if (headless || !AskGame())
            {
                throw new FileNotFoundException("Escolha a pasta do jogo no launcher.");
            }
            game = ReadGamePath();
        }

        string exe = Path.Combine(Runtime, "victory.exe");
        TweakGame();
        ProcessStartInfo start = new ProcessStartInfo(exe);
        start.WorkingDirectory = Runtime;
        start.UseShellExecute = false;
        // O console do runtime nao abre janela: so a do titulo aparece.
        start.CreateNoWindow = true;
        start.Arguments = "run \"" + game + "\" --execute --console series-s";
        Profile(start, high, panel);
        return Process.Start(start);
    }

    static void Play(bool high, bool panel)
    {
        if (running != null && !running.HasExited)
        {
            status.Text = "O jogo ja esta rodando.";
            return;
        }

        try
        {
            running = StartGame(high, panel, false);
            status.Text = "Carregando. O primeiro carregamento demora: o mundo chega em 3 a 5 minutos.";
            focusTicks = 0;
            focus.Start();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "GTA6", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------------------------------------------------------- prepare

    // Compilar antes de jogar. O livro (VICTORY_PIPELINE_BOOK=1) guarda cada
    // pipeline que o titulo criou; `victory warm --used` fabrica as host
    // pipelines das que os quadros desenham e solta cada uma, para que o cache
    // do driver em disco ja as tenha quando o jogo pedir. Sem isso o compilador
    // do driver trabalha no meio do quadro: em docs/findings.md, 270 a 660 s de
    // uma sessao com quadros acima de 200 ms, quase todos enquanto uma pipeline
    // nova era feita.
    static bool HasBook()
    {
        return File.Exists(Path.Combine(PipelineFolder(), "known-pipelines.bin"));
    }

    static string PipelineFolder()
    {
        string cache = Environment.GetEnvironmentVariable("VICTORY_CACHE_DIR");
        if (string.IsNullOrEmpty(cache))
        {
            cache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VIctory", "cache");
        }
        return Path.Combine(cache, "pipelines");
    }

    // Um processo do runtime com o mesmo perfil do jogo: a chave da host
    // pipeline inclui os ajustes do host, entao aquecer com outro perfil
    // fabrica pipelines que a partida nao usa.
    static ProcessStartInfo RuntimeStart(string arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo(Path.Combine(Runtime, "victory.exe"));
        start.WorkingDirectory = Runtime;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.Arguments = arguments;
        Profile(start, false, false);
        return start;
    }

    // Dois tercos dos nucleos logicos: o padrao do proprio `victory warm`.
    static int Jobs()
    {
        return Math.Max(1, Environment.ProcessorCount * 2 / 3);
    }

    static int Warm()
    {
        using (Process warm = Process.Start(RuntimeStart("warm --used --jobs " + Jobs())))
        {
            string line;
            int made = 0;
            while ((line = warm.StandardOutput.ReadLine()) != null)
            {
                made++;
                if (made % 25 == 0)
                {
                    Say("Compilando: " + made + " pipelines...");
                }
            }
            warm.WaitForExit();
            Say(made + " pipelines compiladas. Pode jogar.");
            return made;
        }
    }

    // Traduzir todo programa do pacote de shaders antes da partida, o passo
    // que o launcher do ScarlettPort faz ("Build shaders": `scarlett warm`
    // traduz os 25.889 shaders em 171 s). Sem ele a traducao acontece no meio
    // do quadro e todo draw cujo programa ainda nao existe e deixado de fora:
    // em soak22 (cache do tradutor com 1.726 programas de ~26.000) foram
    // 1.644 draws de 2.192 por quadro, GPU 0,0 ms, 534 ms de quadro - a tela
    // preta com o audio rodando. Com o pacote traduzido inteiro nao falta
    // pipeline nenhuma.
    static int Shaders()
    {
        string game = ReadGamePath();
        if (string.IsNullOrEmpty(game))
        {
            Say("Sem o caminho do jogo: abra uma vez para escolher a pasta.");
            return 0;
        }
        string pack = Path.Combine(Path.GetDirectoryName(game), "shaders.rpf");
        if (!File.Exists(pack))
        {
            Say("Sem shaders.rpf na pasta do jogo.");
            return 0;
        }
        Say("Traduzindo os shaders do jogo (minutos na primeira vez)...");
        using (Process shaders = Process.Start(RuntimeStart("shaders \"" + pack + "\" --jobs " + Jobs())))
        {
            string line;
            while ((line = shaders.StandardOutput.ReadLine()) != null)
            {
                // Uma linha a cada 1.000 shaders: o proprio runtime as escreve.
                if (line.IndexOf("shaders in the cache", StringComparison.Ordinal) >= 0)
                {
                    Say(line.Trim());
                }
            }
            shaders.WaitForExit();
            Say("Shaders traduzidos (exit " + shaders.ExitCode + ").");
            return shaders.ExitCode == 0 ? 1 : 0;
        }
    }

    static void Prepare()
    {
        if (preparing)
        {
            Say("Ja esta preparando.");
            return;
        }
        preparing = true;
        Thread worker = new Thread(new ThreadStart(delegate
        {
            try
            {
                Shaders();
                if (!HasBook())
                {
                    Say("Shaders prontos. Falta o livro de pipelines: jogue uma vez com --book.");
                    return;
                }
                Say("Compilando as pipelines do jogo antes da partida...");
                Warm();
            }
            catch (Exception error)
            {
                Say("Falha ao preparar: " + error.Message);
            }
            finally
            {
                preparing = false;
            }
        }));
        worker.IsBackground = true;
        worker.Start();
    }

    // A janela pode nao existir (--prepare, --book): ai a linha vai para o console.
    static void Say(string text)
    {
        if (status == null)
        {
            Console.WriteLine(text);
            return;
        }
        if (status.InvokeRequired)
        {
            status.BeginInvoke((MethodInvoker)delegate { status.Text = text; });
            return;
        }
        status.Text = text;
    }

    // ------------------------------------------------------------------- ui

    static void BuildWindow()
    {
        window = new Form();
        window.Text = "GTA6";
        window.ClientSize = new Size(560, 420);
        window.FormBorderStyle = FormBorderStyle.FixedSingle;
        window.MaximizeBox = false;
        window.StartPosition = FormStartPosition.CenterScreen;
        window.BackColor = Color.FromArgb(24, 24, 28);
        window.ForeColor = Color.FromArgb(232, 232, 236);
        window.Font = new Font("Segoe UI", 9.5f);

        Label title = new Label();
        title.Text = "GTA6 no PC";
        title.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(255, 255, 255);
        title.SetBounds(28, 22, 500, 40);
        window.Controls.Add(title);

        Label sub = new Label();
        sub.Text = "Runtime VIctory, base Belobug, perfil de 16 GB.";
        sub.ForeColor = Color.FromArgb(150, 150, 160);
        sub.SetBounds(30, 64, 500, 22);
        window.Controls.Add(sub);

        play720 = MakeButton("Jogar  720p", 30, 104, 240);
        play720.Click += delegate { Play(false, false); };
        window.Controls.Add(play720);

        play1080 = MakeButton("Jogar  1080p", 290, 104, 240);
        play1080.Click += delegate { Play(true, false); };
        window.Controls.Add(play1080);

        playPanel = MakeButton("Jogar com painel (F3)", 30, 156, 240);
        playPanel.Click += delegate { Play(false, true); };
        window.Controls.Add(playPanel);

        Button folder = MakeButton("Pasta do jogo", 290, 156, 240);
        folder.Click += delegate
        {
            if (AskGame())
            {
                status.Text = "Jogo: " + ReadGamePath();
            }
        };
        window.Controls.Add(folder);

        // Compilar antes de jogar: as pipelines que os quadros desenham ja
        // feitas no cache do driver. Sem isso elas sao feitas no meio do
        // quadro, e cada uma custa um congelamento.
        fullScreenBox = new CheckBox();
        fullScreenBox.Text = "Tela cheia";
        fullScreenBox.ForeColor = Color.FromArgb(220, 220, 226);
        fullScreenBox.SetBounds(30, 204, 240, 26);
        fullScreenBox.Checked = FullScreen();
        fullScreenBox.CheckedChanged += delegate
        {
            SaveSetting("fullscreen", fullScreenBox.Checked ? "1" : "0");
        };
        window.Controls.Add(fullScreenBox);

        prepare = MakeButton("Preparar (compilar)", 30, 262, 240);
        prepare.Click += delegate { Prepare(); };
        window.Controls.Add(prepare);

        status = new Label();
        status.Text = "Jogo: " + (ReadGamePath().Length > 0 ? ReadGamePath() : "nao encontrado");
        status.ForeColor = Color.FromArgb(150, 150, 160);
        status.SetBounds(30, 312, 500, 80);
        window.Controls.Add(status);

        focus = new Timer();
        focus.Interval = 500;
        focus.Tick += delegate { Watch(); };
    }

    static Button MakeButton(string text, int x, int y, int width)
    {
        Button button = new Button();
        button.Text = text;
        button.SetBounds(x, y, width, 40);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 80);
        button.BackColor = Color.FromArgb(40, 40, 48);
        button.ForeColor = Color.FromArgb(240, 240, 245);
        button.Cursor = Cursors.Hand;
        return button;
    }

    // A janela do titulo nasce fora da tela e sem foco; ela e trazida para
    // frente algumas vezes no comeco e o acompanhamento para depois.
    static void Watch()
    {
        if (running == null || running.HasExited)
        {
            focus.Stop();
            status.Text = "O jogo fechou.";
            return;
        }

        focusTicks++;
        IntPtr handle = Biggest(running.Id);
        if (handle != IntPtr.Zero && focusTicks < 240)
        {
            ShowWindow(handle, 9);
            SetForegroundWindow(handle);
        }
        else if (focusTicks > 240)
        {
            focus.Stop();
        }
    }

    // O piso duro de memoria residente (SetProcessWorkingSetSizeEx com
    // HARDWS_MIN) foi medido e reprovado: com piso de 9.728 MB o gerenciador
    // segura as paginas do jogo e passa a tirar as do resto do sistema, mas o
    // processo ficou em 11.184 ms por quadro (0,1 fps) e morreu em 147 s,
    // contra 550 a 710 ms e 333 s sem piso. O Windows pagina melhor sozinho.
    // Nao volte a ligar isso.

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumProc callback, IntPtr param);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr handle, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr handle);

    delegate bool EnumProc(IntPtr handle, IntPtr param);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    static IntPtr Biggest(int pid)
    {
        IntPtr best = IntPtr.Zero;
        long area = 0;
        EnumWindows(delegate(IntPtr handle, IntPtr param)
        {
            uint owner;
            GetWindowThreadProcessId(handle, out owner);
            if (owner != pid || !IsWindowVisible(handle))
            {
                return true;
            }
            RECT rect;
            if (!GetClientRect(handle, out rect))
            {
                return true;
            }
            long size = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (size > area)
            {
                area = size;
                best = handle;
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    // --------------------------------------------------------------- extras

    static void OpenFile(string name)
    {
        string path = Path.Combine(Runtime, name);
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
