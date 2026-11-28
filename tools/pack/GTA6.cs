using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
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
    static Process running;
    static Timer focus;
    static int focusTicks;
    static bool preparing;
    static Mode activeMode;

    // A copia visual: o mesmo desenho do launcher da Julia, com o nosso
    // perfil dentro. As paginas sao construidas uma vez e so a visivel e
    // pintada; o relogio da janela para quando nao ha nada se mexendo.
    static readonly List<Panel> pages = new List<Panel>();
    static readonly List<NavButton> nav = new List<NavButton>();
    static Panel building;
    static int shownPage = -1;
    static Hero banner;
    static StatusStrip statusStrip;
    static ModeCard[] modeCards;
    static Label modeNote;
    static StepList steps;
    static FootMeta footMeta;
    static Pill playButton;
    static Pill prepareButton;
    static LoadBar loadBar;
    static OptionCard fullScreenCard;
    static OptionCard panelCard;
    static TextBox logText;
    static RoundCard logPanel;
    static Dropdown outputBox;

    [STAThread]
    static void Main(string[] args)
    {
        bool prepareOnly = args.Length > 0 && args[0] == "--prepare";
        bool book = args.Length > 0 && args[0] == "--book";
        bool headless = args.Length > 0 && (args[0].StartsWith("--play", StringComparison.Ordinal) || book);
        bool installOnly = args.Length > 0 && args[0] == "--install";
        bool adminOnly = args.Length > 0 && args[0] == "--admin";
        bool codeOnly = args.Length > 0 && args[0].StartsWith("--code", StringComparison.Ordinal);

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

        // Os ajustes que pedem elevacao: plano de energia e a preferencia de
        // placa do Windows para o victory.exe. O proprio processo se relanca
        // com runas; o que nao subir, a linha de status diz.
        if (adminOnly)
        {
            AdminSetup();
            Environment.Exit(0);
            return;
        }

        // O codigo do perfil pela linha de comando, para suporte e para testar
        // um codigo sem abrir a janela:
        //   GTA6.exe --code                  imprime o codigo do modo atual
        //   GTA6.exe "--code=s=0.5;b=512;..." valida e normaliza um codigo
        if (codeOnly)
        {
            string text = args[0].Length > 6 ? args[0].Substring(6).TrimStart('=').Trim() : "";
            if (text.Length == 0)
            {
                Console.WriteLine(Code(SelectedMode()));
                Environment.Exit(0);
                return;
            }
            Mode parsed = ParseCode(text);
            if (parsed == null)
            {
                Console.WriteLine("invalido");
                Environment.Exit(2);
                return;
            }
            Console.WriteLine(Code(parsed));
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
        // A biblioteca de host NAO entra. Medido em 27/11 (soak24): a deles
        // carrega (358 MB, mapped=true), mas so 16 das 3.549 pipelines
        // iniciais sao encontradas pelo nome - as descricoes que o nosso
        // runtime monta nao batem com as que a deles gravou. O custo e real:
        // 341 MB de paginas mapeadas disputando memoria com o titulo, e o
        // quadro foi de 88-90 ms para 178 ms. So os livros de shader e a
        // lista de hashes entram; a biblioteca e construida pelo uso.
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

    // Os modos: o mesmo perfil medido, com as alavancas que mudam de maquina
    // para maquina. `recomendado` e o perfil deste PC (Ryzen 5 5600G, 16 GB,
    // RTX 3060 Ti 8 GB, Windows 10) com a escala 0.6667 que o soak28 aprovou:
    // GPU de 34,0 para 13,7 ms e quadro de 99,2 para 90,1 ms a 6.191 draws,
    // sem artefato visivel. E o valor nativo do upscaler do proprio titulo
    // (Series X desenha 2/3). Os outros modos sao o mesmo desenho para outras
    // placas; so o recomendado foi medido aqui.
    sealed class Mode
    {
        public string Key;
        public string Name;
        public string Tag;
        public string Scale;
        public int BuffersMb;
        public int TargetsMb;
        public int MemoryCardMb;
        public int WindowsMb;
        public int RoomPercent;
        public int RipeBlocks;
        public int PressureFreeMb;
        public int PressureClearMb;
        public string Note;

        public Mode(string key, string name, string tag, string scale, int buffers, int targets,
            int memoryCard, int windows, int roomPercent, int ripe, int pressureFree, int pressureClear, string note)
        {
            Key = key;
            Name = name;
            Tag = tag;
            Scale = scale;
            BuffersMb = buffers;
            TargetsMb = targets;
            MemoryCardMb = memoryCard;
            WindowsMb = windows;
            RoomPercent = roomPercent;
            RipeBlocks = ripe;
            PressureFreeMb = pressureFree;
            PressureClearMb = pressureClear;
            Note = note;
        }

        public override string ToString()
        {
            return Name + "   " + Tag;
        }
    }

    static readonly Mode[] Modes = new Mode[]
    {
        new Mode("recomendado", "Recomendado", "720p nativo para RTX 3060 Ti (1:1)", "1.0",
            768, 768, 128, 96, 90, 4, 0, 0,
            "RTX 3060 Ti fica folgada em 720p nativo enquanto o gargalo esta na CPU; imagem 1:1 nitida."),
        new Mode("nitido", "Nitido", "720p nativo, sem upscaler", "1.0",
            768, 768, 128, 96, 90, 4, 0, 0,
            "Medido nesta maquina: 99,2 ms de quadro. A imagem e 1:1, sem reconstrucao."),
        new Mode("rapido", "Rapido", "540p reconstruido", "0.5",
            768, 768, 128, 96, 90, 4, 0, 0,
            "Para CPU apertada: mais quadros, imagem mais mole. Nao medido nesta maquina."),
        new Mode("modesto", "PC modesto", "placas de 6-8 GB, 16 GB de RAM", "0.5",
            512, 512, 96, 64, 90, 2, 1024, 2048,
            "Menos buffers e textura menor na placa, com as linhas de pressao de RAM ligadas."),
        new Mode("ultra", "Ultra baixo", "4-6 GB de placa, 8-16 GB de RAM", "0.5",
            384, 384, 64, 48, 90, 2, 1536, 3072,
            "O menor consumo de memoria de todos. Imagem macia. Para o PC mais fraco da lista."),
        new Mode("classico", "Classico", "o perfil de 16 GB sem a escala", "1.0",
            768, 768, 128, 96, 90, 4, 0, 0,
            "O perfil anterior a escala 0.6667, para comparar lado a lado."),
    };

    static Mode FindMode(string key)
    {
        foreach (Mode mode in Modes)
        {
            if (mode.Key == key)
            {
                return mode;
            }
        }
        return Modes[0];
    }

    // O modo escolhido. `custom` vem do codigo colado, e um codigo invalido cai
    // no recomendado em vez de abrir o jogo com valor fora de faixa.
    static Mode SelectedMode()
    {
        string key = Setting("mode", "recomendado");
        if (key == "custom")
        {
            Mode custom = ParseCode(Setting("custom", ""));
            if (custom != null)
            {
                return custom;
            }
        }
        return FindMode(key);
    }

    // O codigo do perfil, curto o bastante para colar num chat:
    //   s=escala;b=buffers;t=alvos;m=cartao;w=janelas;p=percentual;r=ripe;g=pressao;c=limpeza
    static string Code(Mode mode)
    {
        return "s=" + mode.Scale
            + ";b=" + mode.BuffersMb
            + ";t=" + mode.TargetsMb
            + ";m=" + mode.MemoryCardMb
            + ";w=" + mode.WindowsMb
            + ";p=" + mode.RoomPercent
            + ";r=" + mode.RipeBlocks
            + ";g=" + mode.PressureFreeMb
            + ";c=" + mode.PressureClearMb;
    }

    static Mode ParseCode(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }
        double scale = 0;
        int buffers = 0, targets = 0, memoryCard = 0, windows = 0, room = 0, ripe = 0, free = 0, clear = 0;
        int seen = 0;
        foreach (string part in code.Split(';'))
        {
            int at = part.IndexOf('=');
            if (at <= 0)
            {
                continue;
            }
            string key = part.Substring(0, at).Trim();
            string value = part.Substring(at + 1).Trim();
            switch (key)
            {
                case "s":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
                    {
                        return null;
                    }
                    seen |= 1;
                    break;
                case "b": if (!int.TryParse(value, out buffers)) { return null; } seen |= 2; break;
                case "t": if (!int.TryParse(value, out targets)) { return null; } seen |= 4; break;
                case "m": if (!int.TryParse(value, out memoryCard)) { return null; } seen |= 8; break;
                case "w": if (!int.TryParse(value, out windows)) { return null; } seen |= 16; break;
                case "p": if (!int.TryParse(value, out room)) { return null; } seen |= 32; break;
                case "r": if (!int.TryParse(value, out ripe)) { return null; } seen |= 64; break;
                case "g": if (!int.TryParse(value, out free)) { return null; } seen |= 128; break;
                case "c": if (!int.TryParse(value, out clear)) { return null; } seen |= 256; break;
            }
        }
        // Todas as nove chaves, e cada uma dentro da faixa que o runtime aceita.
        // Um codigo pela metade abriria o jogo com o resto em zero.
        if (seen != 511)
        {
            return null;
        }
        if (scale < 0.3 || scale > 1.0 || buffers < 256 || buffers > 2048
            || targets < 256 || targets > 4096 || memoryCard < 32 || memoryCard > 2048
            || windows < 32 || windows > 1024 || room < 50 || room > 98
            || ripe < 1 || ripe > 8 || free < 0 || free > 8192 || clear < 0 || clear > 16384)
        {
            return null;
        }
        return new Mode("custom", "Personalizado", "o codigo colado", scale.ToString("0.####", CultureInfo.InvariantCulture),
            buffers, targets, memoryCard, windows, room, ripe, free, clear,
            "Valores colados a mao. Sem medicao nesta maquina.");
    }

    // O perfil da build numa maquina de 16 GB (Ryzen 5 5600G, 16 GB, RTX 3060
    // Ti 8 GB, Windows 10). Cada linha foi medida; o porque esta em
    // OTIMIZACOES.md. Cada grupo e uma funcao: mexer num budget nao pede ler o
    // perfil inteiro.
    static void Profile(ProcessStartInfo start, bool high, bool panel)
    {
        activeMode = SelectedMode();
        ProfileFork(start);
        ProfilePorts(start);
        ProfileWaves(start);
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

    // A onda larga, so nos programas que provaram traduzir. Um compute de
    // wave64 que reduz a onda dentro de um trecho divergente e recusado pelo
    // lowering exato: as duas metades da onda nao se encontram. A forma larga
    // toma o branch pela onda inteira, com duas barreiras de grupo, e traduz.
    // Os 27 hashes abaixo foram medidos com `victory translate --warm` nesta
    // build do jogo: 27 de 38. Sem eles o titulo perde SSR, decalque
    // projetado, MinMax da exposicao, nuvem, agua, culling de arvore e a
    // familia de sombra por tile, e a imagem fica plana ("massinha").
    // O formulario global (VICTORY_WAVE_WIDE=1) derrubou a placa em
    // ptfx_CS_lightAtlas nas runs 121-124: por isso e' lista, nao interruptor.
    static void ProfileWaves(ProcessStartInfo start)
    {
        start.EnvironmentVariables["VICTORY_WAVE_WIDE_ONLY"] =
            "0a5ecfee6b79e13b,11276d0c8bbe180a,136986812880cead,1f1fbd86759b3da2," +
            "21f7ad7863620328,240cb516c1af0bc3,2cfa4a6ad9ef005a,2e92491c2ae5472b," +
            "31d703207b51549a,3e28ebd6128bd351,65de113e23fa4fdd,7168dd781ca7efb9," +
            "71883e40620a567a,73ac67482f6b6a5d,7c9a6a83826cdfac,804b342cb4b9d1e9," +
            "8b9483919d8d5e99,8bb272e763f76148,9809b8d965b04935,a2f1004f8d6d7aad," +
            "b96880482a17f632,bdf438d5731d36cc,cbf9b65f2249ba96,cc7b23cc0b6a8eba," +
            "d2b169b82f67ec5b,f1eff11f31566a6e,ff8fe948b132510f," +
            "20b78ee12ecdb57d,466ec41d3f9d24c0,56f3bf2d9388e5a7,6b82bc7d22016cb5," +
            "7b2c3c1db2298f44,9662515112a3f961,abd7a22106898c66,b4b0427232707598," +
            "ce79e9eba7ae0348,d7db57731ada919b,f133e35efed2a48a,fa0bd10a35bfef3c";
    }

    // A imagem: o modo que o titulo reporta, a escala e a biblioteca de
    // pipelines.
    static void ProfilePicture(ProcessStartInfo start, bool high)
    {
        start.EnvironmentVariables["VICTORY_SHOW_AFTER"] = "1";
        start.EnvironmentVariables["VICTORY_AMBIENT"] = "1.0";
        start.EnvironmentVariables["VICTORY_AMBIENT_MOST"] = "1.4";
        start.EnvironmentVariables["VICTORY_AMBIENT_AIR"] = "1.0";
        start.EnvironmentVariables["VICTORY_AMBIENT_MARCH"] = "1.0";
        // O titulo acredita que a TV e 4K e desenha 2560x1440 dentro de uma
        // janela 1080p: 4x os alvos, 4x as copias. VICTORY_OUTPUT e o modo que
        // ele reporta; a escala decide o tamanho real do desenho.
        start.EnvironmentVariables["VICTORY_OUTPUT"] = high ? "1920x1080" : "1280x720";
        start.EnvironmentVariables["VICTORY_UPSCALER"] = "6";
        // A escala vem do modo. 0.6667 (854x480 reconstruido a 1280x720) e o
        // valor nativo do upscaler do proprio titulo (Series X desenha 2/3) e
        // o que o soak28 mediu nesta maquina: GPU de 34,0 para 13,7 ms e
        // quadro de 99,2 para 90,1 ms a 6.191 draws, sem artefato visivel.
        // A 1.0 a imagem e 1:1 e o quadro custa ~9% mais; a 0.5 o upscaler
        // aplica nitidez demais e a imagem fica dura, com halo.
        start.EnvironmentVariables["VICTORY_RENDER_SCALE"] = activeMode.Scale;
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

    // A placa de 8 GB e o que ela guarda. Os numeros vem do modo: o
    // recomendado e o perfil medido nesta maquina.
    static void ProfileCard(ProcessStartInfo start)
    {
        // A 95% a fila enchia o cartao ate a linha e todo alvo ou copia que o
        // host fizesse depois era recusado: textura expulsa e subida de novo
        // (15.462 esperando, 5.655 MB enviados na run cf-1). 90%, com a dieta
        // menor abaixo (cf-12/cf-13), deixa ~1 GB e o orcamento nunca mais foi
        // o gatilho em run nenhuma depois disso.
        start.EnvironmentVariables["VICTORY_TEXTURE_ROOM_PERCENT"] = activeMode.RoomPercent.ToString(CultureInfo.InvariantCulture);
        start.EnvironmentVariables["VICTORY_CARD_ROOM_FOR_TARGETS_MB"] = activeMode.TargetsMb.ToString(CultureInfo.InvariantCulture);
        // 128, nao os 1.536 do perfil antigo: o espaco guardado para alvos que
        // ainda nao existem e o que a fila de textura nao tem num cartao de
        // 8 GB.
        start.EnvironmentVariables["VICTORY_MEMORY_CARD_MB"] = activeMode.MemoryCardMb.ToString(CultureInfo.InvariantCulture);
        start.EnvironmentVariables["VICTORY_CARD_WINDOWS_MB"] = activeMode.WindowsMb.ToString(CultureInfo.InvariantCulture);
        // 768 MB de buffers no cartao, e MIN_MB=1 para o pool poder encher ate
        // a linha: com o pool em 192 MB a varredura de escrita dos shaders
        // virou thrash (cf-4). Com 768 a varredura caiu de 66,6 ms/658 MB
        // para 3,2 ms/30 MB (cf-12).
        start.EnvironmentVariables["VICTORY_CARD_BUFFERS_MB"] = activeMode.BuffersMb.ToString(CultureInfo.InvariantCulture);
        start.EnvironmentVariables["VICTORY_CARD_BUFFERS_MIN_MB"] = "1";
    }

    // Os 16 GB de RAM: tudo o que o host guarda, pequeno. Sem
    // VICTORY_TEXTURE_BUDGET_MB de proposito: com ele o host para de
    // subir textura no teto e o mundo fica sem as 4 GB que pede. A residencia
    // e automatica nesta build: com 32 GiB ou menos ela ja limita o prefetch a
    // 64 MiB por quadro e solta staging parado, sem knob.
    static void ProfileMemory(ProcessStartInfo start)
    {
        start.EnvironmentVariables["VICTORY_UPLOAD_AHEAD"] = "0";
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
        start.EnvironmentVariables["VICTORY_RIPE_BLOCKS"] = activeMode.RipeBlocks.ToString(CultureInfo.InvariantCulture);
        // As duas linhas de pressao de RAM. Cruzou a linha de entrada, o host
        // corta o prefetch de textura para 64 MiB por quadro e solta staging
        // parado; voltou acima da linha de limpeza, ele retoma. Zero nas duas
        // deixa o padrao do runtime (3 GiB para entrar, 5 GiB para sair), que
        // e o que o perfil medido usa. Os modos apertados descem para
        // 1 GiB / 2 GiB, onde a maquina de 16 GB realmente opera.
        if (activeMode.PressureFreeMb > 0)
        {
            start.EnvironmentVariables["VICTORY_PRESSURE_FREE_MB"] = activeMode.PressureFreeMb.ToString(CultureInfo.InvariantCulture);
        }
        if (activeMode.PressureClearMb > 0)
        {
            start.EnvironmentVariables["VICTORY_PRESSURE_CLEAR_MB"] = activeMode.PressureClearMb.ToString(CultureInfo.InvariantCulture);
        }
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
        "anisotropicFiltering=8", "graphicsQualityPreset=1.000000",
        "directionalScreenSpaceShadowQuality=1.000000", "worldHeightShadowQuality=1.000000",
        "motionBlur=false", "rtDynamicShadows=false", "shadowParticleShadows=false",
        "ssroSampleCount=4", "waterReflectionSSR=true",
        "waterReflectionResX=640", "waterReflectionResY=360",
        "grassLod=0.700000", "lodScale=1.000000", "smallCullThreshold=1.000000",
        "decorationResolutionShift=0", "maxTexUpgradesPerFrame=32",
        "manualMipBias=0.000000", "sharpenEnabled=false", "fsrSharpen=0.100000",
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
        try
        {
            string logsDir = Path.Combine(Runtime, "logs");
            Directory.CreateDirectory(logsDir);
            string envLog = Environment.GetEnvironmentVariable("VICTORY_LOG");
            string envFile = Environment.GetEnvironmentVariable("VICTORY_LOG_FILE");
            start.EnvironmentVariables["VICTORY_LOG"] = string.IsNullOrEmpty(envLog) ? "info" : envLog;
            start.EnvironmentVariables["VICTORY_LOG_FILE"] = string.IsNullOrEmpty(envFile)
                ? Path.Combine(logsDir, "gameplay-latest.log")
                : envFile;
        }
        catch (Exception)
        {
        }
        return Process.Start(start);
    }

    static void Play(bool high, bool panel)
    {
        if (running != null && !running.HasExited)
        {
            Say("O jogo ja esta rodando.");
            return;
        }

        try
        {
            running = StartGame(high, panel, false);
            Say("Carregando. O primeiro carregamento demora: o mundo chega em 3 a 5 minutos.");
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
                    Progress(Math.Min(0.95f, made / 900f), "Compilando " + made + " pipelines");
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
                Progress(0.02f, "Preparando");
                Shaders();
                if (!HasBook())
                {
                    Say("Shaders prontos. Falta o livro de pipelines: jogue uma vez com --book.");
                    Progress(0f, "Falta o livro de pipelines");
                    return;
                }
                Say("Compilando as pipelines do jogo antes da partida...");
                Warm();
                Progress(1f, "Pronto para jogar");
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

    // A janela pode nao existir (--prepare, --book): ai a linha vai para o
    // console. Com a janela aberta, a mesma linha cai no rodape e no diario.
    static void Say(string text)
    {
        if (window != null && window.IsHandleCreated && window.InvokeRequired)
        {
            window.BeginInvoke((MethodInvoker)delegate { Say(text); });
            return;
        }
        if (status == null && logText == null)
        {
            Console.WriteLine(text);
            return;
        }
        if (status != null)
        {
            status.Text = text;
        }
        if (logText != null)
        {
            if (logText.TextLength > 40000)
            {
                logText.Clear();
            }
            logText.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        }
    }

    // O mesmo caminho para a barra de progresso: o trabalho roda numa thread e
    // o desenho acontece na thread da janela.
    static void Progress(float part, string text)
    {
        if (loadBar == null)
        {
            return;
        }
        if (window != null && window.IsHandleCreated && window.InvokeRequired)
        {
            window.BeginInvoke((MethodInvoker)delegate { Progress(part, text); });
            return;
        }
        loadBar.Visible = true;
        loadBar.Set(part, text);
    }

    // ------------------------------------------------------------------- ui

    // O visual do launcher da Julia, portado peca por peca: as cores dela, o
    // cartao redondo, a pastilha, o poster do topo com as orbitas e o fundo que
    // respira. O desenho e dela; o que ele mostra e o nosso perfil.
    interface IBackdrop
    {
        void PaintBackdrop(Graphics g, Rectangle area);
    }

    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(36, 12, 32);
        public static readonly Color Side = Color.FromArgb(28, 9, 25);
        public static readonly Color Card = Color.FromArgb(46, 17, 41);
        public static readonly Color CardHover = Color.FromArgb(58, 22, 52);
        public static readonly Color Selected = Color.FromArgb(82, 26, 68);
        public static readonly Color Line = Color.FromArgb(82, 55, 77);
        public static readonly Color Pink = Color.FromArgb(255, 244, 163);
        public static readonly Color Violet = Color.FromArgb(236, 220, 120);
        public static readonly Color Cyan = Color.FromArgb(255, 244, 163);
        public static readonly Color AccentHover = Color.FromArgb(255, 249, 200);
        public static readonly Color OnAccent = Color.FromArgb(42, 10, 36);
        public static readonly Color Text = Color.FromArgb(255, 246, 252);
        public static readonly Color Dim = Color.FromArgb(214, 186, 206);
        public static readonly Color Soft = Color.FromArgb(236, 220, 231);
        public static readonly Color Good = Color.FromArgb(255, 244, 163);
        public static readonly Color Warn = Color.FromArgb(255, 150, 136);
        public static readonly Color Bad = Color.FromArgb(255, 110, 140);
        public static readonly Color HotPink = Color.FromArgb(255, 79, 184);
        public static readonly Color Peach = Color.FromArgb(255, 178, 166);
        public static readonly Color Plum = Color.FromArgb(106, 20, 80);
        public static readonly Color Quiet = Color.FromArgb(74, 36, 66);
        public static readonly Color LineStrong = Color.FromArgb(150, 112, 142);
        public static readonly Color NavHover = Color.FromArgb(44, 16, 40);
        public static readonly Color Wash = Color.FromArgb(34, 255, 255, 255);
        public static readonly Color Knob = Color.White;
        public static readonly Color Shadow = Color.FromArgb(46, 0, 0, 0);
        public static readonly Color GlowMagenta = Color.FromArgb(224, 64, 168);
        public static readonly Color GlowPeach = Color.FromArgb(255, 170, 160);
        public static readonly Color Ring = Color.FromArgb(150, 255, 255, 255);
        public static readonly Color Rule = Color.FromArgb(66, 42, 61);
        public static readonly Color HairWhite = Color.FromArgb(58, 255, 255, 255);
        public static readonly Color Outline = Color.FromArgb(120, 255, 255, 255);
        public static readonly Color Muted = Color.FromArgb(182, 152, 176);
        public static readonly Color Faint = Color.FromArgb(142, 114, 136);
        public static readonly Color TrackOff = Color.FromArgb(96, 58, 90);
        public static readonly Color TrackHover = Color.FromArgb(116, 72, 108);
        public static readonly Color KnobDisabled = Color.FromArgb(124, 98, 118);

        static string mono;

        public static string MonoName
        {
            get
            {
                if (mono != null)
                {
                    return mono;
                }
                mono = "Consolas";
                try
                {
                    using (InstalledFontCollection fonts = new InstalledFontCollection())
                    {
                        foreach (FontFamily family in fonts.Families)
                        {
                            if (family.Name == "Cascadia Mono")
                            {
                                mono = family.Name;
                                break;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
                return mono;
            }
        }

        public static void ClearBack(Control c, Graphics g)
        {
            IBackdrop backdrop = c.Parent as IBackdrop;
            if (backdrop != null)
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(-c.Left, -c.Top);
                backdrop.PaintBackdrop(g, c.Bounds);
                g.Restore(state);
                return;
            }
            g.Clear(c.Parent != null ? c.Parent.BackColor : Back);
        }

        public static void Glow(Graphics g, float cx, float cy, float rx, float ry, Color c, int alpha)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(cx - rx, cy - ry, rx * 2f, ry * 2f);
                using (PathGradientBrush brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(alpha, c);
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                    g.FillPath(brush, path);
                }
            }
        }

        public static float TextW(Graphics g, string text, Font f)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                return g.MeasureString(text, f, PointF.Empty, format).Width;
            }
        }

        public static float TrackedWidth(Graphics g, string text, Font f, float spacing)
        {
            float width = 0f;
            string line = text == null ? "" : text;
            for (int i = 0; i < line.Length; i++)
            {
                width += TextW(g, line[i].ToString(), f) + spacing;
            }
            return width;
        }

        public static float Tracked(Graphics g, string text, Font f, Brush b, float x, float y, float spacing)
        {
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                string line = text == null ? "" : text;
                for (int i = 0; i < line.Length; i++)
                {
                    string glyph = line[i].ToString();
                    g.DrawString(glyph, f, b, x, y, format);
                    x += g.MeasureString(glyph, f, PointF.Empty, format).Width + spacing;
                }
            }
            return x;
        }

        public static float AscentPx(Graphics g, Font f)
        {
            FontFamily family = f.FontFamily;
            return f.SizeInPoints * g.DpiY / 72f * family.GetCellAscent(f.Style) / family.GetEmHeight(f.Style);
        }

        public static void Meta(Graphics g, string label, string value, Color valueColor, RectangleF box, StringAlignment align)
        {
            Font font = Fx.MetaFont;
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = align;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.HotkeyPrefix = HotkeyPrefix.None;
                using (SolidBrush brush = new SolidBrush(Text))
                {
                    g.DrawString(label == null ? "" : label, font, brush, new RectangleF(box.X, box.Y, box.Width, 16f), format);
                }
                using (SolidBrush brush = new SolidBrush(valueColor))
                {
                    g.DrawString(value == null ? "" : value, font, brush, new RectangleF(box.X, box.Y + 16f, box.Width, 16f), format);
                }
            }
        }

        public static void FocusRing(Graphics g, RectangleF r, float radius, Color c)
        {
            using (Pen pen = new Pen(Color.FromArgb(210, c), 1.5f))
            using (GraphicsPath path = Round(r, radius))
            {
                g.DrawPath(pen, path);
            }
        }

        public static Font Mono(float size)
        {
            return new Font(MonoName, size);
        }

        public static Font Ui(float size)
        {
            return new Font("Segoe UI", size);
        }

        public static Font UiBold(float size)
        {
            return new Font("Segoe UI Semibold", size);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180f, 90f);
            path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void Panel(Graphics g, RectangleF r, Color fill, Color border)
        {
            Panel(g, r, fill, border, 10f);
        }

        public static void Panel(Graphics g, RectangleF r, Color fill, Color border, float radius)
        {
            using (GraphicsPath path = Round(r, radius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(border))
                {
                    g.DrawPath(pen, path);
                }
            }
        }
    }

    static class Fx
    {
        static Font meta;
        static Font chipName;
        static Font chipDetail;

        public static Font MetaFont
        {
            get
            {
                if (meta == null)
                {
                    meta = Theme.Ui(8.5f);
                }
                return meta;
            }
        }

        public static Font ChipName
        {
            get
            {
                if (chipName == null)
                {
                    chipName = Theme.UiBold(9f);
                }
                return chipName;
            }
        }

        public static Font ChipDetail
        {
            get
            {
                if (chipDetail == null)
                {
                    chipDetail = Theme.Ui(8f);
                }
                return chipDetail;
            }
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            if (t <= 0f)
            {
                return a;
            }
            if (t >= 1f)
            {
                return b;
            }
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, float k)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * k))), c.R, c.G, c.B);
        }

        public static float Clamp(float v, float lo, float hi)
        {
            if (v < lo)
            {
                return lo;
            }
            if (v > hi)
            {
                return hi;
            }
            return v;
        }

        public static float SmoothStep(float e0, float e1, float x)
        {
            float t = Clamp((x - e0) / (e1 - e0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        public static float Out(float p)
        {
            float q = 1f - p;
            return 1f - q * q * q;
        }

        public static float InOut(float p)
        {
            return p * p * (3f - 2f * p);
        }
    }

    sealed class Mix
    {
        public float V;
        float a;
        float b;
        long t0;
        int dur;
        bool run;
        readonly Action inval;

        public bool Running { get { return run; } }

        public Mix(Control owner)
        {
            Control target = owner;
            inval = delegate
            {
                if (target != null && !target.IsDisposed)
                {
                    target.Invalidate();
                }
            };
        }

        public Mix(Action invalidate)
        {
            inval = invalidate;
        }

        public void Set(float v)
        {
            V = a = b = v;
            run = false;
        }

        public void To(float target, int ms)
        {
            if (target == b && (run || V == target))
            {
                return;
            }
            b = target;
            if (ms <= 0 || !Anim.Live)
            {
                V = target;
                run = false;
                inval();
                return;
            }
            a = V;
            t0 = Anim.Now;
            dur = ms;
            if (!run)
            {
                run = true;
                Anim.Add(this);
            }
            Anim.Wake();
        }

        internal bool Step(long now)
        {
            if (!run)
            {
                return false;
            }
            float t = Math.Min(1f, (float)(now - t0) / dur);
            V = a + (b - a) * Fx.Out(t);
            inval();
            if (t >= 1f)
            {
                V = b;
                run = false;
                return false;
            }
            return true;
        }

        internal void Finish()
        {
            if (V != b || run)
            {
                V = b;
                run = false;
                inval();
            }
        }
    }

    // Um relogio so para a janela inteira: as misturas (hover, selecao) e os
    // desenhos que se mexem sozinhos (o poster, a barra). 16 ms quando ha
    // trabalho, desligado quando nao ha.
    static class Anim
    {
        sealed class Ambient
        {
            public Control C;
            public Func<double, int> Step;
            public long Due;
        }

        public static bool UserOn = true;
        static Form host;
        static Timer clock;
        static readonly Stopwatch watch = Stopwatch.StartNew();
        static readonly List<Mix> mixes = new List<Mix>();
        static readonly List<Ambient> ambients = new List<Ambient>();

        public static long Now { get { return watch.ElapsedMilliseconds; } }

        public static bool Live
        {
            get
            {
                return UserOn && host != null && host.IsHandleCreated && host.Visible
                    && host.WindowState != FormWindowState.Minimized;
            }
        }

        public static void Attach(Form f)
        {
            host = f;
            f.Activated += delegate { Wake(); };
            f.Resize += delegate { Wake(); };
            f.VisibleChanged += delegate { Wake(); };
        }

        internal static void Add(Mix m)
        {
            mixes.Add(m);
        }

        public static void Register(Control c, bool needsFocus, Func<double, int> step)
        {
            Ambient a = new Ambient();
            a.C = c;
            a.Step = step;
            a.Due = 0;
            ambients.Add(a);
            c.Disposed += delegate { ambients.Remove(a); };
            Wake();
        }

        public static void Poke(Control c)
        {
            for (int i = 0; i < ambients.Count; i++)
            {
                if (ambients[i].C == c)
                {
                    ambients[i].Due = 0;
                }
            }
            Wake();
        }

        public static void Wake()
        {
            try
            {
                if (host == null || host.IsDisposed)
                {
                    return;
                }
                if (!Live)
                {
                    Settle();
                    return;
                }
                if (clock == null)
                {
                    clock = new Timer();
                    clock.Interval = 16;
                    clock.Tick += Tick;
                }
                if (!clock.Enabled)
                {
                    clock.Interval = 16;
                    clock.Start();
                }
            }
            catch (Exception)
            {
            }
        }

        static void Stop()
        {
            if (clock != null && clock.Enabled)
            {
                clock.Stop();
            }
        }

        static void Settle()
        {
            Mix[] all = mixes.ToArray();
            foreach (Mix m in all)
            {
                m.Finish();
            }
            mixes.Clear();
            Stop();
        }

        static void Tick(object sender, EventArgs e)
        {
            try
            {
                if (!Live)
                {
                    Settle();
                    return;
                }
                long now = Now;
                int next = int.MaxValue;
                for (int i = mixes.Count - 1; i >= 0; i--)
                {
                    if (!mixes[i].Step(now))
                    {
                        mixes.RemoveAt(i);
                    }
                }
                if (mixes.Count > 0)
                {
                    next = 16;
                }
                for (int i = 0; i < ambients.Count; i++)
                {
                    Ambient a = ambients[i];
                    if (a.C.IsDisposed || !a.C.Visible)
                    {
                        continue;
                    }
                    if (now < a.Due)
                    {
                        next = Math.Min(next, (int)(a.Due - now));
                        continue;
                    }
                    int wait = a.Step((double)now / 1000.0);
                    if (wait <= 0)
                    {
                        wait = 50;
                    }
                    a.Due = now + wait;
                    next = Math.Min(next, wait);
                }
                if (next == int.MaxValue)
                {
                    Stop();
                }
                else
                {
                    clock.Interval = Math.Max(15, Math.Min(next, 5000));
                }
            }
            catch (Exception)
            {
                Stop();
            }
        }
    }

    static class Kit
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string list);

        public static void Dark(Control c, string theme)
        {
            Action apply = delegate
            {
                try
                {
                    SetWindowTheme(c.Handle, theme, null);
                }
                catch (Exception)
                {
                }
            };
            if (c.IsHandleCreated)
            {
                apply();
            }
            c.HandleCreated += delegate { apply(); };
        }

        public static int TextW(string text, Font f)
        {
            return TextRenderer.MeasureText(text == null ? "" : text, f,
                new Size(100000, 100), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        }

        public static int PillW(string text, float pt, int min)
        {
            using (Font f = new Font("Segoe UI", pt))
            {
                return Math.Max(min, TextW(text, f) + 40);
            }
        }
    }

    // O fundo do poster: dois brilhos magenta, um pessego, e a mascara que
    // escurece o pe. Refeito so quando a chave muda, nunca por pixel.
    class Poster : Control, IBackdrop
    {
        Bitmap bg;
        int bgW;
        int bgH;
        int bgKey = int.MinValue;
        protected int gMx;
        protected int gMy;
        protected int gPx;
        protected int gPy;
        protected int gBreath;
        protected readonly Mix intro;

        protected float Intro { get { return intro.V; } }

        public Poster()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Width = 900;
            intro = new Mix(this);
            intro.Set(1f);
        }

        public void PlayIntro()
        {
            intro.Set(0f);
            intro.To(1f, 240);
        }

        protected int BackKey()
        {
            return (gMx * 73856093) ^ (gMy * 19349663) ^ (gPx * 83492791) ^ (gPy * 2654435) ^ (gBreath * 97);
        }

        void Rebuild(int key)
        {
            if (bg == null || bgW != Width || bgH != Height)
            {
                if (bg != null)
                {
                    bg.Dispose();
                }
                bg = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
                bgW = Width;
                bgH = Height;
            }
            bgKey = key;
            float k = gBreath / 8f;
            float up = 1f + 0.05f * k;
            float up2 = 1f + 0.03f * k;
            float down = 1f - 0.05f * k;
            float down2 = 1f - 0.03f * k;
            using (Graphics g = Graphics.FromImage(bg))
            {
                g.Clear(Theme.Back);
                Theme.Smooth(g);
                Theme.Glow(g, Width * 0.2f + gMx, Height * 0.62f + gMy,
                    Width * 0.55f * up2, Height * 0.98f * up2, Theme.GlowMagenta, (int)(170f * up));
                Theme.Glow(g, Width * 0.17f + gMx * 1.5f, Height * 0.68f + gMy * 1.5f,
                    Width * 0.26f * up2, Height * 0.6f * up2, Color.FromArgb(246, 74, 196), (int)(120f * up));
                Theme.Glow(g, Width * 0.98f + gPx, Height * 0.02f + gPy,
                    Width * 0.32f * down2, Height * 0.8f * down2, Theme.GlowPeach, (int)(112f * down));
                int mask = Math.Min(96, Height / 2);
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(0, Height - mask, Math.Max(1, Width), mask + 1),
                    Color.FromArgb(0, Theme.Back), Theme.Back, 90f))
                {
                    g.FillRectangle(brush, 0, Height - mask, Width, mask + 1);
                }
            }
        }

        public virtual void PaintBackdrop(Graphics g, Rectangle area)
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }
            int key = BackKey();
            if (bg == null || bgW != Width || bgH != Height || bgKey != key)
            {
                Rebuild(key);
            }
            Rectangle dest = Rectangle.Intersect(area, new Rectangle(0, 0, Width, Height));
            if (dest.Width > 0 && dest.Height > 0)
            {
                g.DrawImage(bg, dest, dest.X, dest.Y, dest.Width, dest.Height, GraphicsUnit.Pixel);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && bg != null)
            {
                bg.Dispose();
                bg = null;
            }
            base.Dispose(disposing);
        }

        public static Font Heavy(float pt)
        {
            return new Font("Segoe UI Black", pt);
        }

        public static Font Serif(float pt)
        {
            return new Font("Georgia", pt, FontStyle.Italic);
        }
    }

    // O poster do topo: a marca, o titulo, as orbitas com as quatro pastilhas
    // de estado e a linha de fatos. E a peca que faz o launcher parecer o da
    // Julia sem copiar uma linha de logica dela.
    sealed class Hero : Poster
    {
        public const int Rows = 362;

        public string Brand = "";
        public string BrandValue = "";
        public string RightLabel = "";
        public string RightValue = "";
        public string Title = "";
        public string Accent = "";
        public string Status = "";
        public string[] FactLabels = new string[0];
        public string[] FactValues = new string[0];
        public Color[] FactColors = new Color[0];
        public string[] StepNames = new string[0];
        public string[] Steps = new string[0];

        int[] states = new int[0];
        bool statesSeen;
        readonly Mix[] dot = new Mix[4];
        readonly Mix[] burst = new Mix[4];
        double orbitT;
        float leanX;
        float leanY;
        float tLeanX;
        float tLeanY;
        long rippleAt = -100000;
        Bitmap blob;
        readonly byte[] blobBuf = new byte[16384];
        readonly float[] bx = new float[3];
        readonly float[] by = new float[3];
        readonly float[] br2 = new float[3];

        static readonly Pen ringPen = new Pen(Theme.Ring, 1f);
        static readonly Pen arcSoft = NewArc(Color.FromArgb(64, 255, 255, 255), 1.6f);
        static readonly Pen arcBright = NewArc(Color.FromArgb(200, 255, 255, 255), 2.2f);

        static Pen NewArc(Color c, float width)
        {
            Pen pen = new Pen(c, width);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            return pen;
        }

        public int[] States
        {
            get { return states; }
            set { SetStates(value); }
        }

        public Hero()
        {
            Height = 362;
            for (int i = 0; i < 4; i++)
            {
                dot[i] = new Mix(delegate { if (!IsDisposed) Invalidate(OrbitRect()); });
                burst[i] = new Mix(delegate { if (!IsDisposed) Invalidate(OrbitRect()); });
            }
            Anim.Register(this, true, Frame);
        }

        Rectangle OrbitRect()
        {
            float x = Width - 270;
            return new Rectangle((int)(x - 176f - 120f), 63, 592, 251);
        }

        void SetStates(int[] value)
        {
            int[] before = states;
            states = value == null ? new int[0] : value;
            for (int i = 0; i < 4 && i < states.Length; i++)
            {
                bool on = states[i] == 1;
                bool was = i < before.Length && before[i] == 1;
                if (!statesSeen)
                {
                    dot[i].Set(on ? 1f : 0f);
                    continue;
                }
                dot[i].To(on ? 1f : 0f, 480);
                if (on && !was)
                {
                    burst[i].Set(0f);
                    burst[i].To(1f, 950);
                }
            }
            if (states.Length > 0)
            {
                statesSeen = true;
            }
        }

        public void Ripple()
        {
            if (Anim.Live)
            {
                rippleAt = Anim.Now;
                Anim.Poke(this);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            float dx = e.X - (Width - 200);
            float dy = e.Y - 188f - 38f;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1f)
            {
                tLeanX = tLeanY = 0f;
            }
            else
            {
                float k = Math.Min(10f, len * 0.03f);
                tLeanX = dx / len * k;
                tLeanY = dy / len * k;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            tLeanX = tLeanY = 0f;
            base.OnMouseLeave(e);
        }

        static int Q(double v)
        {
            return (int)Math.Round(v / 3.0) * 3;
        }

        int Frame(double t)
        {
            orbitT = t;
            int key = BackKey();
            gBreath = (int)Math.Round(8.0 * Math.Sin(t * (Math.PI * 2.0) / 11.0));
            gMx = Q(Width * 0.03 * Math.Sin(t * (Math.PI * 2.0) / 37.0) + Width * 0.012 * Math.Sin(t * (Math.PI * 2.0) / 23.0 + 1.3));
            gMy = Q(Height * 0.06 * Math.Sin(t * (Math.PI * 2.0) / 31.0 + 0.7) + Height * 0.02 * Math.Sin(t * (Math.PI * 2.0) / 19.0));
            gPx = Q(-Width * 0.024 * Math.Sin(t * (Math.PI * 2.0) / 43.0 + 2.1) + Width * 0.01 * Math.Sin(t * (Math.PI * 2.0) / 27.0));
            gPy = Q(Height * 0.08 * Math.Sin(t * (Math.PI * 2.0) / 41.0 + 0.2));
            leanX += (tLeanX - leanX) * 0.16f;
            leanY += (tLeanY - leanY) * 0.16f;
            UpdateBlob(t);
            if (BackKey() != key)
            {
                Invalidate();
            }
            else
            {
                Invalidate(OrbitRect());
            }
            return 50;
        }

        void UpdateBlob(double t)
        {
            if (blob == null)
            {
                blob = new Bitmap(64, 64, PixelFormat.Format32bppPArgb);
            }
            double since = (Anim.Now - rippleAt) / 1000.0;
            float ripple = since < 1.8 ? (float)(Math.Exp(-since * 2.4) * Math.Sin(since * 12.0)) : 0f;
            float lx = leanX / 10f;
            float ly = leanY / 10f;
            double[] speed = new double[] { 26.0, 19.0, 15.0 };
            double[] spread = new double[] { 0.18, 0.33, 0.3 };
            double[] phase = new double[] { 0.4, 2.2, 4.1 };
            float[] size = new float[] { 0.45f, 0.35f, 0.27f };
            for (int i = 0; i < 3; i++)
            {
                double angle = t * 2.0 * Math.PI / speed[i] * (i != 2 ? 1 : -1) + phase[i];
                bx[i] = (float)(Math.Cos(angle) * spread[i]) + lx * 0.05f * (i + 1);
                by[i] = (float)(Math.Sin(angle * 0.85 + i) * spread[i] * 0.9) + ly * 0.05f * (i + 1);
                float r = size[i] * (1f + 0.05f * (float)Math.Sin(t * 0.9 + i * 2.0) + 0.07f * ripple);
                br2[i] = r * r;
            }
            for (int j = 0; j < 64; j++)
            {
                float py = ((j + 0.5f) / 64f) * 2f - 1f;
                for (int k = 0; k < 64; k++)
                {
                    float px = ((k + 0.5f) / 64f) * 2f - 1f;
                    float sum = 0f;
                    float r0 = 0f, r1 = 0f, r2 = 0f;
                    for (int l = 0; l < 3; l++)
                    {
                        float dx = px - bx[l];
                        float dy = py - by[l];
                        float v = br2[l] / (dx * dx + dy * dy + 0.0006f);
                        sum += v;
                        float sq = v * v;
                        if (l == 0) r0 = sq;
                        else if (l == 1) r1 = sq;
                        else r2 = sq;
                    }
                    int at = (j * 64 + k) * 4;
                    float fade = 1f - Fx.SmoothStep(0.55f, 0.98f, (float)Math.Sqrt(px * px + py * py));
                    float core = Fx.SmoothStep(0.8f, 1.12f, sum) * fade;
                    float halo = Fx.SmoothStep(0.35f, 1f, sum) * 0.13f * fade;
                    float a = Math.Max(core, halo);
                    if (a <= 0.002f)
                    {
                        blobBuf[at] = 0;
                        blobBuf[at + 1] = 0;
                        blobBuf[at + 2] = 0;
                        blobBuf[at + 3] = 0;
                        continue;
                    }
                    float total = r0 + r1 + r2 + 1e-06f;
                    float cr = (r0 * 255f + r1 * 255f + r2 * 255f) / total;
                    float cg = (r0 * 74f + r1 * 178f + r2 * 244f) / total;
                    float cb = (r0 * 190f + r1 * 166f + r2 * 163f) / total;
                    float hot = Fx.SmoothStep(2.4f, 4.6f, sum) * 0.16f;
                    cr += (255f - cr) * hot;
                    cg += (246f - cg) * hot;
                    cb += (252f - cb) * hot;
                    cr += (255f - cr) * (1f - core) * 0.6f;
                    cg += (150f - cg) * (1f - core) * 0.6f;
                    cb += (186f - cb) * (1f - core) * 0.6f;
                    blobBuf[at] = (byte)(cb * a);
                    blobBuf[at + 1] = (byte)(cg * a);
                    blobBuf[at + 2] = (byte)(cr * a);
                    blobBuf[at + 3] = (byte)(a * 255f);
                }
            }
            BitmapData data = blob.LockBits(new Rectangle(0, 0, 64, 64), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            Marshal.Copy(blobBuf, 0, data.Scan0, blobBuf.Length);
            blob.UnlockBits(data);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            PaintBackdrop(g, clip);
            Theme.Smooth(g);
            float left = 40f;
            float right = Width - 40;
            float k = Intro;
            if (clip.Top < 62)
            {
                Theme.Meta(g, Brand, BrandValue, Theme.Muted, new RectangleF(left, 15f, 320f, 34f), StringAlignment.Near);
                if (!string.IsNullOrEmpty(RightLabel))
                {
                    Theme.Meta(g, RightLabel, RightValue, Theme.Muted, new RectangleF(right - 320f, 15f, 320f, 34f), StringAlignment.Far);
                }
            }
            using (Pen pen = new Pen(Theme.HairWhite))
            {
                g.DrawLine(pen, 0, 62, Width, 62);
                g.DrawLine(pen, 0, 314, Width, 314);
            }
            if (clip.IntersectsWith(OrbitRect()))
            {
                DrawOrbits(g);
            }
            if (clip.IntersectsWith(new Rectangle(0, 62, Math.Max(10, Width - 440), 252)))
            {
                g.TranslateTransform(0f, (1f - k) * 10f);
                DrawHeadline(g, left, k);
                using (Font font = Theme.Ui(10.5f))
                using (SolidBrush brush = new SolidBrush(Fx.Alpha(Theme.Soft, k)))
                using (StringFormat format = new StringFormat())
                {
                    format.Trimming = StringTrimming.EllipsisWord;
                    format.FormatFlags = StringFormatFlags.LineLimit;
                    format.HotkeyPrefix = HotkeyPrefix.None;
                    g.DrawString(Status == null ? "" : Status, font, brush,
                        new RectangleF(left, 204f, Math.Max(100f, Math.Min(440f, Width - 446f - left)), 44f), format);
                }
                g.ResetTransform();
            }
            int facts = FactLabels.Length;
            if (facts > 0 && clip.Bottom > 314)
            {
                float each = (right - left) / facts;
                for (int i = 0; i < facts; i++)
                {
                    StringAlignment align = i == 0 ? StringAlignment.Near
                        : (i == facts - 1 ? StringAlignment.Far : StringAlignment.Center);
                    Color color = i < FactColors.Length ? FactColors[i] : Theme.Muted;
                    Theme.Meta(g, FactLabels[i], i < FactValues.Length ? FactValues[i] : "", color,
                        new RectangleF(left + i * each, 324f, each - 10f, 34f), align);
                }
            }
        }

        void DrawHeadline(Graphics g, float lx, float k)
        {
            float room = Math.Max(160f, Width - 446f - lx - 16f);
            float size = 44f;
            Font heavy = Poster.Heavy(size);
            Font serif = Poster.Serif(size);
            while (size > 24f && (Theme.TrackedWidth(g, Title == null ? "" : Title, heavy, -1.5f) > room
                || Theme.TextW(g, Accent == null ? "" : Accent, serif) > room))
            {
                size -= 2f;
                heavy.Dispose();
                serif.Dispose();
                heavy = Poster.Heavy(size);
                serif = Poster.Serif(size);
            }
            float scale = size / 44f;
            float baseline = 130f;
            using (heavy)
            using (serif)
            {
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Text, k)))
                {
                    Theme.Tracked(g, Title == null ? "" : Title, heavy, b, lx - 1f, baseline - Theme.AscentPx(g, heavy), -1.5f);
                }
                using (SolidBrush brush = new SolidBrush(Fx.Alpha(Theme.Cyan, k)))
                using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
                {
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(Accent == null ? "" : Accent, serif, brush,
                        lx - 1f, baseline + 56f * scale - Theme.AscentPx(g, serif), format);
                }
            }
        }

        void DrawOrbits(Graphics g)
        {
            float cx = Width - 270;
            float cy = 192f;
            float outer = 176f;
            float inner = 110f;
            double t = orbitT;
            GraphicsState state = g.Save();
            g.SetClip(new RectangleF(0f, 63f, Width, 251f), CombineMode.Intersect);
            g.DrawEllipse(ringPen, cx - outer, cy - outer, outer * 2f, outer * 2f);
            g.DrawEllipse(ringPen, cx - inner, cy - inner, inner * 2f, inner * 2f);
            float a1 = (float)(t * 4.0 % 360.0);
            float a2 = (float)(150.0 - t * 6.5 % 360.0);
            g.DrawArc(arcSoft, cx - outer, cy - outer, outer * 2f, outer * 2f, a1, 84f);
            g.DrawArc(arcBright, cx - outer, cy - outer, outer * 2f, outer * 2f, a1 + 60f, 24f);
            g.DrawArc(arcSoft, cx - inner, cy - inner, inner * 2f, inner * 2f, a2, 70f);
            g.DrawArc(arcBright, cx - inner, cy - inner, inner * 2f, inner * 2f, a2 + 48f, 22f);
            if (blob == null)
            {
                UpdateBlob(t);
            }
            if (blob != null)
            {
                float bx0 = cx + 70f + leanX;
                float by0 = cy + 34f + leanY + 3f * (float)Math.Sin(t * 0.45);
                InterpolationMode mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(blob, new RectangleF(bx0 - 75f, by0 - 75f, 150f, 150f));
                g.InterpolationMode = mode;
            }
            g.Restore(state);
            double[][] chips = new double[][]
            {
                new double[] { outer, 205.0, -8.0, 11.0, 83.0, 0.0 },
                new double[] { inner, 335.0, 9.0, 14.0, 71.0, 1.7 },
                new double[] { inner, 140.0, -9.0, 14.0, 97.0, 3.1 },
                new double[] { outer, 25.0, 8.0, 8.0, 77.0, 4.4 },
            };
            for (int i = 0; i < chips.Length && i < Steps.Length; i++)
            {
                double angle = (chips[i][1] + chips[i][3] * Math.Sin(t * 2.0 * Math.PI / chips[i][4] + chips[i][5])) * Math.PI / 180.0;
                float px = cx + (float)(Math.Cos(angle) * chips[i][0]);
                float py = cy + (float)(Math.Sin(angle) * chips[i][0]);
                Chip(g, px, py, (float)chips[i][2],
                    "#" + (i + 1).ToString("00") + " " + (i < StepNames.Length ? StepNames[i] : ""),
                    Steps[i], i < states.Length ? states[i] : -1,
                    i < 4 ? dot[i].V : 0f, i < 4 ? burst[i].V : 1f, i < 4 && burst[i].Running);
            }
        }

        static void Chip(Graphics g, float cx, float cy, float rot, string name, string detail, int state, float fill, float burstK, bool bursting)
        {
            Font nameFont = Fx.ChipName;
            Font detailFont = Fx.ChipDetail;
            float w = Math.Max(Theme.TextW(g, name, nameFont), Theme.TextW(g, detail == null ? "" : detail, detailFont)) + 52f;
            float h = 42f;
            GraphicsState save = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(rot);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF r = new RectangleF(-w / 2f, -h / 2f, w, h);
            using (GraphicsPath path = Theme.Round(r, h / 2f))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(62, 255, 255, 255)))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Color.FromArgb(84, 255, 255, 255)))
                {
                    g.DrawPath(pen, path);
                }
            }
            Color color = state == 0 ? Theme.HotPink : Theme.Dim;
            float dotX = r.X + 18f;
            if (bursting)
            {
                float radius = 10f + 12f * Fx.Out(burstK);
                using (Pen pen = new Pen(Fx.Alpha(Theme.Cyan, 1f - burstK), 2f))
                {
                    g.DrawEllipse(pen, dotX - radius, -radius, radius * 2f, radius * 2f);
                }
            }
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, dotX - 10f, -10f, 20f, 20f);
            }
            if (fill > 0.01f)
            {
                float size = 20f * fill;
                using (SolidBrush brush = new SolidBrush(Theme.Cyan))
                {
                    g.FillEllipse(brush, dotX - size / 2f, -size / 2f, size, size);
                }
            }
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush brush = new SolidBrush(Theme.Text))
                {
                    g.DrawString(name, nameFont, brush, r.X + 38f, -h / 2f + 6f, format);
                }
                using (SolidBrush brush = new SolidBrush(Theme.Soft))
                {
                    g.DrawString(detail == null ? "" : detail, detailFont, brush, r.X + 38f, -h / 2f + 23f, format);
                }
            }
            g.Restore(save);
        }
    }

    sealed class NavButton : Control
    {
        public string Glyph;
        bool active;
        readonly Mix hv;
        readonly Mix dn;
        readonly Mix act;

        public bool Active
        {
            get { return active; }
            set
            {
                if (active != value)
                {
                    active = value;
                    act.To(value ? 1f : 0f, 220);
                    Invalidate();
                }
            }
        }

        public NavButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            hv = new Mix(this);
            dn = new Mix(this);
            act = new Mix(this);
            Cursor = Cursors.Hand;
            Size = new Size(196, 38);
            Font = Theme.Ui(10.5f);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hv.To(1f, 140);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hv.To(0f, 200);
            dn.To(0f, 120);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            dn.To(1f, 70);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dn.To(0f, 140);
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Side);
            Theme.Smooth(g);
            if (!act.Running)
            {
                act.Set(active ? 1f : 0f);
            }
            float v = act.V;
            float hot = Math.Max(hv.V, dn.V);
            float mid = Height / 2f;
            float dotSize = 6f * Fx.Clamp(v, 0f, 1f);
            if (dotSize > 0.2f)
            {
                using (SolidBrush brush = new SolidBrush(Theme.Cyan))
                {
                    g.FillEllipse(brush, 15f - dotSize / 2f, mid - dotSize / 2f, dotSize, dotSize);
                }
            }
            if (hot > 0.02f && v < 0.98f)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(90f * hot * (1f - v)), Theme.Cyan)))
                {
                    g.FillEllipse(brush, 12f, mid - 3f, 6f, 6f);
                }
            }
            float shift = 2f * hot * (1f - v);
            using (Font font = Theme.Mono(8f))
            using (SolidBrush brush = new SolidBrush(Fx.Lerp(Fx.Lerp(Theme.Faint, Theme.Muted, hot), Theme.Cyan, v)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(Glyph == null ? "" : Glyph, font, brush, new RectangleF(26f + shift, 0f, 26f, Height), format);
            }
            Font bold = v >= 0.5f ? Theme.UiBold(10.5f) : null;
            using (SolidBrush brush = new SolidBrush(Fx.Lerp(Fx.Lerp(Theme.Muted, Theme.Text, hot), Theme.Cyan, v)))
            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.HotkeyPrefix = HotkeyPrefix.None;
                g.DrawString(Text == null ? "" : Text, bold == null ? Font : bold, brush,
                    new RectangleF(54f + shift, 0f, Math.Max(10, Width - 60), Height), format);
            }
            if (bold != null)
            {
                bold.Dispose();
            }
        }
    }

    sealed class Pill : Button
    {
        public bool Link;
        bool main;
        bool shine;
        float sweep = -1f;
        readonly Mix hv;
        readonly Mix dn;
        readonly Mix mn;

        public bool Main
        {
            get { return main; }
            set
            {
                if (main != value)
                {
                    main = value;
                    mn.To(value ? 1f : 0f, 170);
                    Invalidate();
                }
            }
        }

        public bool Shine
        {
            get { return shine; }
            set
            {
                if (shine != value)
                {
                    shine = value;
                    if (value)
                    {
                        Anim.Register(this, true, Frame);
                    }
                }
            }
        }

        public Pill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            hv = new Mix(this);
            dn = new Mix(this);
            mn = new Mix(this);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            ForeColor = Theme.Text;
            BackColor = Theme.Back;
            Font = Theme.Ui(9.5f);
            Height = 34;
        }

        int Frame(double t)
        {
            if (!Enabled || !main || Link)
            {
                if (sweep >= 0f)
                {
                    sweep = -1f;
                    Invalidate();
                }
                return 5000;
            }
            double cycle = t % 5.5;
            if (cycle < 1.05)
            {
                sweep = (float)(cycle / 1.05);
                Invalidate();
                return 33;
            }
            if (sweep >= 0f)
            {
                sweep = -1f;
                Invalidate();
            }
            return (int)((5.5 - cycle) * 1000.0) + 5;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hv.To(1f, 140);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hv.To(0f, 200);
            dn.To(0f, 120);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            dn.To(1f, 70);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dn.To(0f, 150);
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.ClearBack(this, g);
            Theme.Smooth(g);
            float v = hv.V;
            float d = dn.V;
            float m = mn.V;
            float hot = Math.Max(v, d);
            float inset = d * 1.4f;
            RectangleF r = new RectangleF(0.5f + inset, 0.5f + inset * 0.7f,
                Width - 1.5f - 2f * inset, Height - 1.5f - 1.4f * inset);
            float radius = r.Height / 2f;
            string text = Text == null ? "" : Text;
            if (Link)
            {
                using (SolidBrush brush = new SolidBrush(!Enabled ? Theme.KnobDisabled : Fx.Lerp(Fx.Lerp(Theme.Dim, Theme.Text, hot), Theme.Cyan, m)))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    format.HotkeyPrefix = HotkeyPrefix.None;
                    bool bold = m >= 0.5f;
                    Font font = bold ? new Font(Font.FontFamily, Font.Size, FontStyle.Bold) : Font;
                    g.DrawString(text, font, brush, new RectangleF(0f, 0f, Width, Height - 2), format);
                    float under = Math.Max(m, v);
                    if (under > 0.01f && Enabled)
                    {
                        float width = Math.Min(Width - 2, Theme.TextW(g, text, font)) * Fx.Out(under);
                        using (SolidBrush line = new SolidBrush(Fx.Lerp(Theme.LineStrong, Theme.Cyan, m)))
                        {
                            g.FillRectangle(line, (Width - width) / 2f, Height - 5, width, 2f);
                        }
                    }
                    if (bold)
                    {
                        font.Dispose();
                    }
                }
                if (Focused && ShowFocusCues && Enabled)
                {
                    Theme.FocusRing(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6f, Theme.Cyan);
                }
                return;
            }
            if (!Enabled)
            {
                Theme.Panel(g, r, Theme.Wash, Color.FromArgb(60, 255, 255, 255), radius);
            }
            else
            {
                Color idle = Color.FromArgb((int)(22f * v + 20f * d), 255, 255, 255);
                Color edge = Fx.Lerp(Theme.Outline, Color.FromArgb(235, 255, 255, 255), hot);
                Color accent = Fx.Lerp(Fx.Lerp(Theme.Cyan, Theme.AccentHover, v), Theme.Violet, d);
                Theme.Panel(g, r, Fx.Lerp(idle, accent, m), Fx.Lerp(edge, accent, m), radius);
                if (sweep >= 0f && m > 0.9f)
                {
                    using (GraphicsPath path = Theme.Round(r, radius))
                    {
                        GraphicsState state = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        float band = r.Height * 1.7f;
                        float x = r.X - band + (r.Width + band) * Fx.InOut(sweep);
                        ColorBlend blend = new ColorBlend(3);
                        blend.Positions = new float[] { 0f, 0.5f, 1f };
                        blend.Colors = new Color[]
                        {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(104, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255),
                        };
                        using (LinearGradientBrush brush = new LinearGradientBrush(
                            new RectangleF(x, r.Y, band, r.Height), Color.White, Color.White, LinearGradientMode.Horizontal))
                        {
                            brush.InterpolationColors = blend;
                            g.FillRectangle(brush, x, r.Y - 1f, band, r.Height + 2f);
                        }
                        g.Restore(state);
                    }
                }
            }
            if (Focused && ShowFocusCues && Enabled)
            {
                Theme.FocusRing(g, new RectangleF(3.5f, 3.5f, Width - 7.5f, Height - 7.5f),
                    (Height - 7.5f) / 2f, main ? Theme.OnAccent : Theme.Cyan);
            }
            using (SolidBrush brush = new SolidBrush(Enabled ? Fx.Lerp(ForeColor, Theme.OnAccent, m) : Theme.Dim))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.HotkeyPrefix = HotkeyPrefix.None;
                Font font = Font;
                Font smaller = null;
                float room = Math.Max(10f, r.Width - Math.Min(24f, r.Height * 0.6f));
                float size = Font.Size - 0.5f;
                while (size >= 7.5f && g.MeasureString(text, font).Width > room)
                {
                    if (smaller != null)
                    {
                        smaller.Dispose();
                    }
                    font = smaller = new Font(Font.FontFamily, size, Font.Style);
                    size -= 0.5f;
                }
                g.DrawString(text, font, brush, new RectangleF(r.X + 6f, r.Y, Math.Max(10f, r.Width - 12f), r.Height), format);
                if (smaller != null)
                {
                    smaller.Dispose();
                }
            }
        }
    }

    sealed class Heading : Control
    {
        public string Note = "";
        public bool Hair = true;

        public Heading(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Text = text;
            Height = 26;
            Font = Theme.UiBold(11.5f);
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.ClearBack(this, g);
            Theme.Smooth(g);
            float note = 0f;
            Font small = null;
            if (!string.IsNullOrEmpty(Note))
            {
                small = Theme.Ui(8.5f);
                note = Math.Min((float)Math.Ceiling(g.MeasureString(Note, small).Width), Math.Max(0f, Width * 0.5f));
            }
            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.HotkeyPrefix = HotkeyPrefix.None;
                using (SolidBrush brush = new SolidBrush(Theme.Text))
                {
                    g.DrawString(Text == null ? "" : Text, Font, brush,
                        new RectangleF(-1f, 0f, Math.Max(10f, Width - note - (note > 0f ? 12 : 0)), Height - 2), format);
                }
                if (small != null)
                {
                    using (SolidBrush brush = new SolidBrush(Theme.Muted))
                    {
                        g.DrawString(Note, small, brush, new RectangleF(Width - note - 2f, 0f, note + 2f, Height - 2), format);
                    }
                    small.Dispose();
                }
            }
            if (Hair)
            {
                using (Pen pen = new Pen(Theme.Rule))
                {
                    g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
                }
            }
        }
    }

    sealed class Eyebrow : Control
    {
        public bool Hair = true;
        public Color Tint = Theme.Muted;

        public Eyebrow(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Text = text;
            Height = 24;
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.ClearBack(this, g);
            Theme.Smooth(g);
            using (Font font = Theme.UiBold(7.5f))
            using (SolidBrush brush = new SolidBrush(Tint))
            {
                Theme.Tracked(g, (Text == null ? "" : Text).ToUpperInvariant(), font, brush, 0f, 2f, 1.3f);
            }
            if (Hair)
            {
                using (Pen pen = new Pen(Theme.Rule))
                {
                    g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
                }
            }
        }
    }

    sealed class Rule : Control
    {
        public Rule()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Rule);
        }
    }

    sealed class RoundCard : Panel
    {
        public Color Edge = Theme.Rule;
        public float Radius = 10f;
        public bool Flat;

        public RoundCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            if (Flat)
            {
                using (SolidBrush brush = new SolidBrush(Theme.Rule))
                {
                    g.FillRectangle(brush, 0, 0, Width, 1);
                    g.FillRectangle(brush, 0, Height - 1, Width, 1);
                }
                return;
            }
            Theme.Panel(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), BackColor, Edge, Radius);
        }
    }

    sealed class FootMeta : Control
    {
        public string State = "";
        public string Preset = "";
        public string Engine = "";
        public string Hardware = "";
        public string Why = "";
        public string[] Labels = new string[] { "Estado", "Predefinicao", "Motor", "Hardware", "Proximo passo" };
        public Color StateColor = Theme.Good;

        public FootMeta()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void Set(string state, Color color, string preset, string engine, string hardware, string why)
        {
            State = state;
            StateColor = color;
            Preset = preset;
            Engine = engine;
            Hardware = hardware;
            Why = why == null ? "" : why;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Back);
            Theme.Smooth(g);
            float top = 13f;
            Theme.Meta(g, Labels[0], State, StateColor, new RectangleF(40f, top, 140f, 34f), StringAlignment.Near);
            if (Why.Length > 0)
            {
                Theme.Meta(g, Labels[4], Why, Theme.Dim, new RectangleF(200f, top, Math.Max(40, Width - 200 - 12), 34f), StringAlignment.Near);
                return;
            }
            float room = Width - 200 - 12;
            float first = Math.Min(240f, room * 0.34f);
            float second = Math.Min(190f, room * 0.26f);
            Theme.Meta(g, Labels[1], Preset, Theme.Dim, new RectangleF(200f, top, first - 14f, 34f), StringAlignment.Near);
            Theme.Meta(g, Labels[2], Engine, Theme.Dim, new RectangleF(200f + first, top, second - 14f, 34f), StringAlignment.Near);
            if (room > 560f)
            {
                Theme.Meta(g, Labels[3], Hardware, Theme.Dim, new RectangleF(200f + first + second, top, room - first - second, 34f), StringAlignment.Near);
            }
        }
    }

    sealed class StatusStrip : Control
    {
        public string[] Items = new string[0];
        public int[] States = new int[0];
        public string ReadyText = "Pronto para jogar";
        public string NotReadyText = "Falta preparar";

        public StatusStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 52;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.ClearBack(this, g);
            Theme.Smooth(g);
            Theme.Panel(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Card, Theme.Line, 14f);
            bool ready = Items.Length > 0;
            for (int i = 0; i < Items.Length; i++)
            {
                if ((i < States.Length ? States[i] : -1) != 1)
                {
                    ready = false;
                }
            }
            Color color = ready ? Theme.Good : Theme.Warn;
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(60, color)))
            {
                g.FillEllipse(brush, 14f, Height / 2f - 8f, 16f, 16f);
            }
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, 17f, Height / 2f - 5f, 10f, 10f);
            }
            float x = 38f;
            float right = Width - 16;
            using (Font font = Theme.UiBold(10.5f))
            {
                using (SolidBrush brush = new SolidBrush(Theme.Text))
                using (StringFormat format = new StringFormat())
                {
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    format.HotkeyPrefix = HotkeyPrefix.None;
                    string text = ready ? ReadyText : NotReadyText;
                    float width = Math.Max(40f, Math.Min((float)Math.Ceiling(g.MeasureString(text, font).Width), right - x));
                    g.DrawString(text, font, brush, new RectangleF(x, 0f, width, Height), format);
                    x += width + 14f;
                }
            }
            using (Font font = Theme.Ui(9f))
            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.HotkeyPrefix = HotkeyPrefix.None;
                for (int i = 0; i < Items.Length; i++)
                {
                    int state = i < States.Length ? States[i] : -1;
                    string text = (state == 0 ? "!  " : (state == 1 ? "\u00b7  " : "   ")) + Items[i];
                    float width = (float)Math.Ceiling(g.MeasureString(text, font).Width);
                    if (x + width > right)
                    {
                        break;
                    }
                    using (SolidBrush brush = new SolidBrush(state == 0 ? Theme.Warn : (state == 1 ? Theme.Soft : Theme.Dim)))
                    {
                        g.DrawString(text, font, brush, new RectangleF(x, 0f, width + 4f, Height), format);
                    }
                    x += width + 12f;
                }
            }
        }
    }

    sealed class ModeCard : Control
    {
        public string Title;
        public string Tag2;
        public string Line;
        bool selected;
        readonly Mix hv;
        readonly Mix dn;
        readonly Mix sel;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected != value)
                {
                    selected = value;
                    sel.To(value ? 1f : 0f, 240);
                    Invalidate();
                }
            }
        }

        public ModeCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            hv = new Mix(this);
            dn = new Mix(this);
            sel = new Mix(this);
            Cursor = Cursors.Hand;
            Size = new Size(120, 78);
            Font = Theme.Mono(8.5f);
            BackColor = Theme.Back;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hv.To(1f, 140);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hv.To(0f, 200);
            dn.To(0f, 120);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            dn.To(1f, 70);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dn.To(0f, 140);
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.ClearBack(this, g);
            Theme.Smooth(g);
            if (!sel.Running)
            {
                sel.Set(selected ? 1f : 0f);
            }
            float v = sel.V;
            float hot = Math.Max(hv.V, dn.V);
            using (SolidBrush brush = new SolidBrush(Fx.Lerp(Theme.Rule, Theme.LineStrong, hot)))
            {
                g.FillRectangle(brush, 0, Height - 1, Width, 1);
            }
            if (v > 0.01f)
            {
                using (SolidBrush brush = new SolidBrush(Theme.Cyan))
                {
                    g.FillRectangle(brush, 0f, Height - 3, Width * v, 3f);
                }
            }
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.HotkeyPrefix = HotkeyPrefix.None;
                float size = 14f;
                Font font = Theme.UiBold(size);
                while (size > 9f && Theme.TextW(g, Title == null ? "" : Title, font) > Width - 4)
                {
                    font.Dispose();
                    size -= 0.5f;
                    font = Theme.UiBold(size);
                }
                using (font)
                using (SolidBrush brush = new SolidBrush(!Enabled ? Theme.Dim : Fx.Lerp(Fx.Lerp(Theme.Dim, Theme.Text, hot), Theme.Cyan, v)))
                {
                    g.DrawString(Title == null ? "" : Title, font, brush,
                        new RectangleF(1f, 6f - hot, Math.Max(10, Width - 2), 26f), format);
                }
                using (Font tag = Theme.Ui(8.75f))
                using (SolidBrush brush = new SolidBrush(Fx.Lerp(Theme.Muted, Theme.Soft, Math.Max(v, hot * 0.6f))))
                {
                    g.DrawString(Tag2 == null ? "" : Tag2, tag, brush, new RectangleF(1f, 35f, Math.Max(10, Width - 2), 16f), format);
                }
                using (Font small = Theme.Ui(8f))
                using (SolidBrush brush = new SolidBrush(Theme.Faint))
                {
                    g.DrawString(Line == null ? "" : Line, small, brush, new RectangleF(1f, 53f, Math.Max(10, Width - 2), 16f), format);
                }
            }
        }
    }

    sealed class OptionCard : CheckBox
    {
        public string Line = "";
        readonly Mix hv;
        readonly Mix dn;
        readonly Mix kn;

        public OptionCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            hv = new Mix(this);
            dn = new Mix(this);
            kn = new Mix(this);
            AutoSize = false;
            Size = new Size(400, 70);
            Cursor = Cursors.Hand;
            Font = Theme.UiBold(10f);
            ForeColor = Theme.Text;
            BackColor = Theme.Back;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hv.To(1f, 140);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hv.To(0f, 200);
            dn.To(0f, 120);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            dn.To(1f, 70);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dn.To(0f, 140);
            base.OnMouseUp(e);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            kn.To(Checked ? 1f : 0f, 190);
            base.OnCheckedChanged(e);
        }

        static void Switch(Graphics g, RectangleF r, float on, float hot, bool enabled)
        {
            float radius = r.Height / 2f;
            Color track = !enabled ? Theme.TrackOff
                : Fx.Lerp(Fx.Lerp(Theme.TrackOff, Theme.TrackHover, hot), Theme.Cyan, on);
            using (GraphicsPath path = Theme.Round(r, radius))
            {
                using (SolidBrush brush = new SolidBrush(track))
                {
                    g.FillPath(brush, path);
                }
            }
            float size = r.Height - 6f;
            float x = r.X + 3f + (r.Width - 6f - size) * on;
            using (SolidBrush brush = new SolidBrush(enabled ? Theme.Knob : Theme.KnobDisabled))
            {
                g.FillEllipse(brush, x, r.Y + 3f, size, size);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.ClearBack(this, g);
            Theme.Smooth(g);
            if (!kn.Running)
            {
                kn.Set(Checked ? 1f : 0f);
            }
            float hot = Enabled ? hv.V : 0f;
            float down = Enabled ? dn.V : 0f;
            if (hot > 0.01f || down > 0.01f)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(12f * hot + 10f * down), 255, 255, 255)))
                {
                    g.FillRectangle(brush, 0, 0, Width, Height - 1);
                }
            }
            using (Pen pen = new Pen(Fx.Lerp(Theme.Rule, Theme.LineStrong, hot * 0.5f)))
            {
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
            if (Focused && ShowFocusCues && Enabled)
            {
                Theme.FocusRing(g, new RectangleF(1.5f, 1.5f, Width - 3f, Height - 4f), 6f, Theme.Cyan);
            }
            Switch(g, new RectangleF(Width - 42, (Height - 1 - 22) / 2, 38f, 22f), kn.V, hot, Enabled);
            string text = Text == null ? "" : Text;
            bool tested = text.StartsWith("?");
            if (tested)
            {
                text = text.TrimStart('?', ' ');
            }
            float room = Math.Max(20, Width - 42 - 16);
            using (SolidBrush brush = new SolidBrush(Enabled ? Theme.Text : Theme.Dim))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.LineAlignment = StringAlignment.Near;
                format.HotkeyPrefix = HotkeyPrefix.None;
                using (Font mark = Theme.UiBold(6.75f))
                {
                    string label = tested ? "MEDIDO" : "";
                    float markW = tested ? Theme.TrackedWidth(g, label, mark, 1.1f) + 14f : 0f;
                    float width = Math.Min(Theme.TextW(g, text, Font), room - markW);
                    g.DrawString(text, Font, brush, new RectangleF(2f, 10f, Math.Max(10f, width + 2f), 22f), format);
                    if (tested && Enabled)
                    {
                        float x = 2f + width + 9f;
                        using (SolidBrush dot = new SolidBrush(Theme.Cyan))
                        {
                            g.FillEllipse(dot, x, 15.5f, 5f, 5f);
                        }
                        using (SolidBrush b = new SolidBrush(Theme.Cyan))
                        {
                            Theme.Tracked(g, label, mark, b, x + 10f, 14f, 1.1f);
                        }
                    }
                }
            }
            using (Font font = Theme.Ui(8.5f))
            using (SolidBrush brush = new SolidBrush(Enabled ? Fx.Lerp(Theme.Muted, Theme.Soft, hot * 0.6f) : Theme.KnobDisabled))
            using (StringFormat format = new StringFormat())
            {
                format.Trimming = StringTrimming.EllipsisWord;
                format.FormatFlags = StringFormatFlags.LineLimit;
                g.DrawString(Line == null ? "" : Line, font, brush, new RectangleF(2f, 32f, room, Math.Max(10, Height - 35)), format);
            }
        }
    }

    sealed class LoadBar : Control
    {
        float done;
        float sheen = -1f;
        readonly Mix shown;

        public LoadBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Theme.Mono(8.75f);
            shown = new Mix(this);
            Anim.Register(this, false, Frame);
        }

        public void Set(float part, string text)
        {
            float before = done;
            done = Math.Max(0f, Math.Min(1f, part));
            Text = text;
            if (!Visible || done < before - 0.001f)
            {
                shown.Set(done);
                Invalidate();
            }
            else
            {
                shown.To(done, 280);
            }
            Anim.Poke(this);
        }

        int Frame(double t)
        {
            if (shown.V > 0.002f && shown.V < 0.998f && done < 0.999f)
            {
                sheen = (float)(t % 1.7 / 1.7);
                Invalidate(new Rectangle(0, Math.Max(0, Height - 9), Width, 9));
                return 40;
            }
            if (sheen >= 0f)
            {
                sheen = -1f;
                Invalidate();
            }
            return 5000;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.ClearBack(this, g);
            Theme.Smooth(g);
            if (!shown.Running && shown.V != done)
            {
                shown.Set(done);
            }
            float v = shown.V;
            float width = Math.Max(6, Width - 1);
            RectangleF back = new RectangleF(0f, Height - 7, width, 6f);
            using (GraphicsPath path = Theme.Round(back, 3f))
            using (SolidBrush brush = new SolidBrush(Theme.Card))
            {
                g.FillPath(brush, path);
            }
            if (v > 0.001f)
            {
                RectangleF fill = new RectangleF(0f, Height - 7, Math.Min(width, Math.Max(6f, width * v)), 6f);
                using (GraphicsPath path = Theme.Round(fill, 3f))
                {
                    using (SolidBrush brush = new SolidBrush(Theme.Cyan))
                    {
                        g.FillPath(brush, path);
                    }
                    if (sheen >= 0f)
                    {
                        GraphicsState state = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        float band = 90f;
                        float x = fill.X - band + (fill.Width + band) * sheen;
                        ColorBlend blend = new ColorBlend(3);
                        blend.Positions = new float[] { 0f, 0.5f, 1f };
                        blend.Colors = new Color[]
                        {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(140, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255),
                        };
                        using (LinearGradientBrush brush = new LinearGradientBrush(
                            new RectangleF(x, fill.Y, band, fill.Height), Color.White, Color.White, LinearGradientMode.Horizontal))
                        {
                            brush.InterpolationColors = blend;
                            g.FillRectangle(brush, x, fill.Y - 1f, band, fill.Height + 2f);
                        }
                        g.Restore(state);
                    }
                }
            }
            string pct = (int)(v * 100f + 0.5f) + "%";
            float pctW = (float)Math.Ceiling(g.MeasureString(pct, Font).Width);
            float top = Math.Max(10, Height - 9);
            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.HotkeyPrefix = HotkeyPrefix.None;
                using (SolidBrush brush = new SolidBrush(Theme.Text))
                {
                    g.DrawString(Text == null ? "" : Text, Font, brush,
                        new RectangleF(0f, 0f, Math.Max(10f, Width - pctW - 10f), top), format);
                }
                using (StringFormat far = new StringFormat(format))
                {
                    far.Alignment = StringAlignment.Far;
                    using (SolidBrush brush = new SolidBrush(Theme.Dim))
                    {
                        g.DrawString(pct, Font, brush, new RectangleF(Width - pctW - 4f, 0f, pctW + 4f, top), far);
                    }
                }
            }
        }
    }

    // A caixa de escolha da Julia: cartao redondo, seta desenhada, lista
    // pintada por nos em vez do tema do Windows.
    sealed class Dropdown : ComboBox
    {
        bool hover;

        public Dropdown()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 24;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            Kit.Dark(this, "DarkMode_CFD");
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }
            bool picked = (e.State & DrawItemState.ComboBoxEdit) == 0 && (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush brush = new SolidBrush(picked ? Theme.Selected : Theme.Card))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            Rectangle bounds = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, bounds,
                picked ? Theme.Cyan : Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 15 || Width < 30 || Height < 10)
            {
                return;
            }
            try
            {
                using (Graphics g = Graphics.FromHwnd(Handle))
                {
                    g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
                    Theme.Smooth(g);
                    RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                    Theme.Panel(g, r,
                        Enabled ? ((hover || DroppedDown) ? Theme.CardHover : Theme.Card) : Theme.Back,
                        DroppedDown ? Theme.Cyan : (hover ? Theme.LineStrong : Theme.Line), 10f);
                    string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : "";
                    TextRenderer.DrawText(g, text, Font, new Rectangle(12, 0, Width - 38, Height),
                        Enabled ? Theme.Text : Theme.Dim,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                    float x = Width - 18;
                    float y = Height / 2f;
                    using (Pen pen = new Pen(Enabled ? Theme.Cyan : Theme.Dim, 1.8f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawLines(pen, new PointF[]
                        {
                            new PointF(x - 4f, y - 2f),
                            new PointF(x, y + 2f),
                            new PointF(x + 4f, y - 2f),
                        });
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }

    // Uma tabela seca: chave, valor, o que faz. Usada no desempenho e na ajuda.
    sealed class Table : Control
    {
        public string[][] Rows = new string[0][];
        public int[] Weights = new int[] { 22, 18, 60 };
        public Color ValueColor = Theme.Cyan;

        public Table()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Back);
            Theme.Smooth(g);
            int rowHeight = 30;
            using (Pen pen = new Pen(Theme.Rule))
            {
                for (int i = 0; i < Rows.Length; i++)
                {
                    string[] row = Rows[i];
                    float x = 0f;
                    int total = 0;
                    for (int w = 0; w < Weights.Length; w++)
                    {
                        total += Weights[w];
                    }
                    for (int c = 0; c < row.Length && c < Weights.Length; c++)
                    {
                        float width = (Width - 20f) * Weights[c] / total;
                        RectangleF box = new RectangleF(x, i * rowHeight, width - 10f, rowHeight);
                        Font font = c == 0 ? Theme.Mono(8.75f) : (c == 1 ? Theme.UiBold(9f) : Theme.Ui(9f));
                        Color color = c == 0 ? Theme.Muted : (c == 1 ? ValueColor : Theme.Soft);
                        using (font)
                        using (SolidBrush brush = new SolidBrush(color))
                        using (StringFormat format = new StringFormat())
                        {
                            format.LineAlignment = StringAlignment.Center;
                            format.Trimming = StringTrimming.EllipsisCharacter;
                            format.FormatFlags = StringFormatFlags.NoWrap;
                            g.DrawString(row[c], font, brush, box, format);
                        }
                        x += width;
                    }
                    g.DrawLine(pen, 0, (i + 1) * rowHeight - 1, Width, (i + 1) * rowHeight - 1);
                }
            }
        }
    }

    // Os cinco passos da preparacao, com o no redondo e o risco entre eles.
    sealed class StepList : Control
    {
        public string[] Names = new string[0];
        public string[] Details = new string[0];
        public int[] States = new int[0];

        public StepList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Back;
        }

        static Color ColorOf(int state)
        {
            if (state == 1) return Theme.Good;
            if (state == 0) return Theme.Warn;
            return Theme.Dim;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Back);
            Theme.Smooth(g);
            int row = 58;
            float cx = 15f;
            using (Pen line = new Pen(Theme.Rule))
            {
                g.DrawLine(line, cx, 16f, cx, Math.Max(16f, Names.Length * row - 34f));
            }
            for (int i = 0; i < Names.Length; i++)
            {
                float cy = i * row + 16f;
                int state = i < States.Length ? States[i] : -1;
                Color color = ColorOf(state);
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(40, color)))
                {
                    g.FillEllipse(glow, cx - 11f, cy - 11f, 22f, 22f);
                }
                using (SolidBrush back = new SolidBrush(Theme.Back))
                {
                    g.FillEllipse(back, cx - 8f, cy - 8f, 16f, 16f);
                }
                using (Pen pen = new Pen(color, 1.5f))
                {
                    g.DrawEllipse(pen, cx - 8f, cy - 8f, 16f, 16f);
                }
                using (SolidBrush dot = new SolidBrush(color))
                {
                    g.FillEllipse(dot, cx - 3.5f, cy - 3.5f, 7f, 7f);
                }
                using (Font font = Theme.UiBold(10f))
                using (SolidBrush brush = new SolidBrush(Theme.Text))
                using (StringFormat format = new StringFormat())
                {
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(Names[i], font, brush, new RectangleF(40f, cy - 18f, Width - 50f, 20f), format);
                }
                using (Font font = Theme.Ui(8.75f))
                using (SolidBrush brush = new SolidBrush(Theme.Muted))
                using (StringFormat format = new StringFormat())
                {
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(i < Details.Length ? Details[i] : "", font, brush,
                        new RectangleF(40f, cy + 2f, Width - 50f, 20f), format);
                }
            }
        }
    }

    // ------------------------------------------------------------------ build

    const string RepoUrl = "https://github.com/VanviroDev/VIctory";
    const string DocsUrl = "https://github.com/VanviroDev/VIctory/blob/coolfox-16gb/OTIMIZACOES.md";
    const string BuildVersion = "1.0";

    // O que a maquina tem, sem WMI: o registro da o processador e a placa, e o
    // kernel da a RAM instalada. Uma linha, so para o usuario saber onde esta.
    static string HardwareLine()
    {
        string cpu = null;
        string gpu = null;
        try
        {
            cpu = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "ProcessorNameString", null) as string;
        }
        catch (Exception)
        {
        }
        try
        {
            string device = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DEVICEMAP\VIDEO", @"\Device\Video0", null) as string;
            if (!string.IsNullOrEmpty(device))
            {
                string path = device.Replace(@"\Registry\Machine\", @"HKEY_LOCAL_MACHINE\");
                gpu = Registry.GetValue(path, "DriverDesc", null) as string;
            }
        }
        catch (Exception)
        {
        }
        MEMORYSTATUSEX memory = new MEMORYSTATUSEX();
        memory.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        ulong ram = GlobalMemoryStatusEx(ref memory) ? memory.ullTotalPhys : 0;

        List<string> parts = new List<string>();
        if (!string.IsNullOrEmpty(cpu))
        {
            parts.Add(Regex.Replace(cpu.Trim(), @"\s+", " "));
        }
        if (ram > 0)
        {
            parts.Add(Math.Round(ram / 1073741824.0) + " GB");
        }
        if (!string.IsNullOrEmpty(gpu))
        {
            parts.Add(gpu.Trim());
        }
        return string.Join("  -  ", parts.ToArray());
    }

    static void BuildWindow()
    {
        window = new Form();
        window.Text = "GTA6";
        window.ClientSize = new Size(1200, 900);
        window.FormBorderStyle = FormBorderStyle.FixedSingle;
        window.MaximizeBox = false;
        window.StartPosition = FormStartPosition.CenterScreen;
        window.BackColor = Theme.Back;
        window.ForeColor = Theme.Text;
        window.Font = Theme.Ui(9.75f);
        window.HandleCreated += delegate { DarkTitle(); };

        Panel body = new Panel();
        body.Dock = DockStyle.Fill;
        body.BackColor = Theme.Back;
        Panel host = new Panel();
        host.Dock = DockStyle.Fill;
        host.BackColor = Theme.Back;
        body.Controls.Add(host);
        body.Controls.Add(Footer());
        pages.Add(PlayPage());
        pages.Add(PerformancePage());
        pages.Add(SetupPage());
        pages.Add(HelpPage());
        foreach (Panel page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            host.Controls.Add(page);
        }
        window.Controls.Add(body);
        window.Controls.Add(SideBar());
        window.Controls.Add(TopBar());

        Anim.Attach(window);
        Show(0);
        RefreshMode();
        RefreshState();

        Timer refresh = new Timer();
        refresh.Interval = 2000;
        refresh.Tick += delegate { RefreshState(); };
        refresh.Start();
        window.FormClosed += delegate { refresh.Stop(); };

        focus = new Timer();
        focus.Interval = 500;
        focus.Tick += delegate { Watch(); };
    }

    // A barra de titulo do Windows 10 nao aceita o tema escuro por padrao:
    // 20 (ou 19 no build antigo) pinta a barra, 35 pinta a borda.
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static void DarkTitle()
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(window.Handle, 20, ref on, 4) != 0)
            {
                DwmSetWindowAttribute(window.Handle, 19, ref on, 4);
            }
            int side = Theme.Side.R | (Theme.Side.G << 8) | (Theme.Side.B << 16);
            DwmSetWindowAttribute(window.Handle, 35, ref side, 4);
        }
        catch (Exception)
        {
        }
    }

    static Control TopBar()
    {
        Panel bar = new Panel();
        bar.Dock = DockStyle.Top;
        bar.Height = 52;
        bar.BackColor = Theme.Side;
        string[][] labels = new string[][]
        {
            new string[] { "Codigo", "no GitHub" },
            new string[] { "Ajustes", "OTIMIZACOES.md" },
        };
        string[] urls = new string[] { RepoUrl, DocsUrl };
        Rectangle[] boxes = new Rectangle[2];
        int hot = -1;
        Action place = delegate
        {
            int x = bar.Width - 28;
            using (Font f = Theme.Ui(8.5f))
            {
                for (int i = 1; i >= 0; i--)
                {
                    int width = Math.Max(Kit.TextW(labels[i][0], f), Kit.TextW(labels[i][1], f)) + 20;
                    x -= width;
                    boxes[i] = new Rectangle(x, 9, width, 34);
                    x -= 30;
                }
            }
        };
        bar.Paint += delegate(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            RectangleF logo = new RectangleF(16f, 13f, 26f, 26f);
            using (GraphicsPath path = Theme.Round(logo, 7f))
            using (SolidBrush brush = new SolidBrush(Theme.Cyan))
            {
                g.FillPath(brush, path);
            }
            using (Font font = new Font("Segoe UI Black", 8.5f))
            using (SolidBrush brush = new SolidBrush(Theme.OnAccent))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString("VI", font, brush, logo, format);
            }
            Theme.Meta(g, "GTA6 no PC", "v" + BuildVersion, Theme.Muted, new RectangleF(54f, 9f, 240f, 34f), StringAlignment.Near);
            for (int i = 0; i < 2; i++)
            {
                Theme.Meta(g, labels[i][0], labels[i][1], i == hot ? Theme.Soft : Theme.Muted,
                    new RectangleF(boxes[i].X, boxes[i].Y, boxes[i].Width + 14, 34f), StringAlignment.Near);
            }
            using (Pen pen = new Pen(Theme.Rule))
            {
                g.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            }
        };
        bar.MouseMove += delegate(object s, MouseEventArgs e)
        {
            int now = -1;
            for (int i = 0; i < 2; i++)
            {
                if (boxes[i].Contains(e.Location))
                {
                    now = i;
                }
            }
            if (now != hot)
            {
                hot = now;
                bar.Cursor = now >= 0 ? Cursors.Hand : Cursors.Default;
                bar.Invalidate();
            }
        };
        bar.MouseLeave += delegate
        {
            if (hot != -1)
            {
                hot = -1;
                bar.Cursor = Cursors.Default;
                bar.Invalidate();
            }
        };
        bar.MouseClick += delegate(object s, MouseEventArgs e)
        {
            for (int i = 0; i < 2; i++)
            {
                if (boxes[i].Contains(e.Location))
                {
                    OpenUrl(urls[i]);
                }
            }
        };
        bar.Resize += delegate
        {
            place();
            bar.Invalidate();
        };
        place();
        return bar;
    }

    static Control SideBar()
    {
        Panel side = new Panel();
        side.Dock = DockStyle.Left;
        side.Width = 216;
        side.BackColor = Theme.Side;
        side.Paint += delegate(object s, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Rule))
            {
                e.Graphics.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height);
            }
        };
        string[] names = new string[] { "Jogar", "Desempenho", "Preparar", "Ajuda" };
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            NavButton button = new NavButton();
            button.Glyph = (i + 1).ToString("00");
            button.Text = names[i];
            button.Size = new Size(200, 38);
            button.Location = new Point(8, 24 + i * 40);
            button.Click += delegate { Show(k); };
            nav.Add(button);
            side.Controls.Add(button);
        }
        Pill setup = new Pill();
        setup.Text = "Ajustes do Windows";
        setup.Size = new Size(184, 34);
        setup.Location = new Point(16, 0);
        setup.BackColor = Theme.Side;
        setup.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        setup.Click += delegate { AdminSetup(); };
        side.Controls.Add(setup);

        Pill code = new Pill();
        code.Text = "Codigo do perfil";
        code.Size = new Size(184, 34);
        code.BackColor = Theme.Side;
        code.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        code.Click += delegate { ShowCode(); };
        side.Controls.Add(code);

        Label tech = new Label();
        tech.Text = "D3D12 - SM 6.6";
        tech.Font = Theme.Mono(8f);
        tech.ForeColor = Theme.Dim;
        tech.BackColor = Theme.Side;
        tech.AutoSize = true;
        tech.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        side.Controls.Add(tech);

        side.Resize += delegate
        {
            setup.Top = side.Height - 124;
            code.Top = side.Height - 80;
            tech.Location = new Point((side.Width - tech.Width) / 2, side.Height - 44);
        };
        return side;
    }

    static Control Footer()
    {
        Panel foot = new Panel();
        foot.Dock = DockStyle.Bottom;
        foot.Height = 64;
        foot.BackColor = Theme.Back;
        footMeta = new FootMeta();
        footMeta.Location = new Point(0, 1);
        footMeta.Size = new Size(400, 62);
        foot.Controls.Add(footMeta);

        playButton = new Pill();
        playButton.Main = true;
        playButton.Shine = true;
        playButton.Text = "Jogar";
        playButton.Size = new Size(190, 44);
        playButton.Font = new Font("Segoe UI Semibold", 12f);
        playButton.Click += delegate { Play(OutputHigh(), panelCard != null && panelCard.Checked); };
        playButton.MouseDown += delegate
        {
            if (playButton.Enabled && banner != null)
            {
                banner.Ripple();
            }
        };
        foot.Controls.Add(playButton);

        prepareButton = new Pill();
        prepareButton.Text = "Preparar";
        prepareButton.Size = new Size(130, 44);
        prepareButton.Click += delegate { Prepare(); };
        foot.Controls.Add(prepareButton);

        foot.Paint += delegate(object s, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Rule))
            {
                e.Graphics.DrawLine(pen, 0, 0, foot.Width, 0);
            }
        };
        Action layout = delegate
        {
            int x = foot.Width - 40;
            playButton.Location = new Point(x - playButton.Width, 10);
            x = playButton.Left - 12;
            prepareButton.Location = new Point(x - prepareButton.Width, 10);
            x = prepareButton.Left - 12;
            footMeta.Width = Math.Max(100, x - 8);
        };
        foot.Resize += delegate { layout(); };
        layout();
        return foot;
    }

    static Panel Page()
    {
        Panel page = new Panel();
        page.BackColor = Theme.Back;
        page.AutoScroll = true;
        page.Dock = DockStyle.Fill;
        return page;
    }

    static Label Small(string text, int x, int y, int width)
    {
        Label label = new Label();
        label.Text = text;
        label.Location = new Point(x, y);
        label.Size = new Size(width, 20);
        label.ForeColor = Theme.Muted;
        label.BackColor = Theme.Back;
        label.AutoSize = false;
        return label;
    }

    static RoundCard Field(TextBox box, int x, int y, int width, int height)
    {
        box.BorderStyle = BorderStyle.None;
        box.BackColor = Theme.Card;
        box.ForeColor = Theme.Text;
        RoundCard card = new RoundCard();
        card.Location = new Point(x, y);
        card.Size = new Size(width, height);
        card.BackColor = Theme.Card;
        card.Edge = Theme.Line;
        card.Radius = 10f;
        box.Width = width - 28;
        if (box.Multiline)
        {
            box.Size = new Size(width - 28, height - 20);
            box.Location = new Point(14, 10);
            Kit.Dark(box, "DarkMode_Explorer");
        }
        else
        {
            box.Location = new Point(14, (height - box.Height) / 2);
        }
        box.GotFocus += delegate
        {
            card.Edge = Theme.Cyan;
            card.Invalidate();
        };
        box.LostFocus += delegate
        {
            card.Edge = Theme.Line;
            card.Invalidate();
        };
        card.Click += delegate { box.Focus(); };
        card.Controls.Add(box);
        return card;
    }

    static Panel PlayPage()
    {
        Panel page = Page();
        building = page;

        banner = new Hero();
        banner.Location = new Point(0, 0);
        banner.Size = new Size(960, 362);
        banner.Brand = "GTA6 no PC";
        banner.BrandValue = "v" + BuildVersion;
        banner.RightLabel = "PERFIL";
        banner.RightValue = "Recomendado";
        banner.Title = "Pronto";
        banner.Accent = "quando quiseres.";
        banner.Status = "Runtime VIctory, base Belobug. O perfil recomendado foi medido nesta maquina.";
        banner.StepNames = new string[] { "Jogo", "Motor", "Pipelines", "D3D12" };
        banner.Steps = new string[] { "game_scarlett", "victory.exe", "warm", "Agility" };
        banner.FactLabels = new string[] { "Escala", "Draws", "Quadro", "GPU" };
        banner.FactValues = new string[] { "0.6667", "6.191", "90,1 ms", "10%" };
        banner.FactColors = new Color[] { Theme.Cyan, Theme.Soft, Theme.Soft, Theme.Good };
        page.Controls.Add(banner);

        statusStrip = new StatusStrip();
        statusStrip.Location = new Point(40, 380);
        statusStrip.Size = new Size(900, 52);
        page.Controls.Add(statusStrip);

        Label gameLabel = new Label();
        gameLabel.Text = "Jogo";
        gameLabel.Font = Theme.UiBold(10.5f);
        gameLabel.ForeColor = Theme.Text;
        gameLabel.BackColor = Theme.Back;
        gameLabel.UseMnemonic = false;
        gameLabel.SetBounds(40, 455, 140, 22);
        page.Controls.Add(gameLabel);

        TextBox gameBox = new TextBox();
        gameBox.Text = ReadGamePath();
        gameBox.Font = Theme.Mono(9.5f);
        gameBox.TextChanged += delegate { RefreshState(); };
        page.Controls.Add(Field(gameBox, 190, 444, 496, 34));

        Pill detect = new Pill();
        detect.Text = "Detetar";
        detect.SetBounds(696, 446, 116, 32);
        detect.Click += delegate
        {
            string found = FindGame();
            if (found != null)
            {
                WriteGamePath(found);
                gameBox.Text = found;
                RefreshState();
                Say("Jogo: " + found);
            }
            else if (AskGame())
            {
                gameBox.Text = ReadGamePath();
                RefreshState();
            }
        };
        page.Controls.Add(detect);

        Pill browse = new Pill();
        browse.Text = "Procurar...";
        browse.SetBounds(822, 446, 118, 32);
        browse.Click += delegate
        {
            if (AskGame())
            {
                gameBox.Text = ReadGamePath();
                RefreshState();
            }
        };
        page.Controls.Add(browse);

        Label gameStatus = new Label();
        gameStatus.Text = "game_scarlett_master.exe";
        gameStatus.ForeColor = Theme.Muted;
        gameStatus.BackColor = Theme.Back;
        gameStatus.Font = Theme.Ui(9f);
        gameStatus.AutoEllipsis = true;
        gameStatus.SetBounds(190, 484, 750, 20);
        page.Controls.Add(gameStatus);

        Rule rule = new Rule();
        rule.SetBounds(40, 514, 900, 1);
        page.Controls.Add(rule);

        Heading preset = new Heading("Predefinicao de imagem");
        preset.Hair = false;
        preset.SetBounds(40, 528, 660, 26);
        page.Controls.Add(preset);

        outputBox = new Dropdown();
        outputBox.SetBounds(720, 525, 220, 26);
        outputBox.Items.AddRange(new object[] { "Saida 720p (mais leve)", "Saida 1080p (Full HD)" });
        outputBox.SelectedIndex = Setting("output", "720p") == "1080p" ? 1 : 0;
        outputBox.SelectedIndexChanged += delegate
        {
            SaveSetting("output", outputBox.SelectedIndex == 1 ? "1080p" : "720p");
            RefreshState();
        };
        page.Controls.Add(outputBox);

        modeCards = new ModeCard[Modes.Length + 1];
        for (int i = 0; i < modeCards.Length; i++)
        {
            int k = i;
            ModeCard card = new ModeCard();
            if (i < Modes.Length)
            {
                card.Title = Modes[i].Name;
                card.Tag2 = Modes[i].Tag;
                card.Line = "escala " + Modes[i].Scale;
            }
            else
            {
                card.Title = "Personalizado";
                card.Tag2 = "codigo colado";
                card.Line = "s=;b=;t=;...";
            }
            int left = 40 + (int)Math.Round(i * 900.0 / modeCards.Length);
            int right = 40 + (int)Math.Round((i + 1) * 900.0 / modeCards.Length) - 10;
            card.SetBounds(left, 570, right - left, 78);
            card.Click += delegate
            {
                if (k < Modes.Length)
                {
                    SelectMode(k);
                }
                else
                {
                    ShowCode();
                }
            };
            modeCards[i] = card;
            page.Controls.Add(card);
        }

        modeNote = new Label();
        modeNote.ForeColor = Theme.Soft;
        modeNote.BackColor = Theme.Back;
        modeNote.Font = Theme.Ui(9f);
        modeNote.SetBounds(40, 660, 800, 52);
        page.Controls.Add(modeNote);

        Heading launch = new Heading("Opcoes de arranque");
        launch.Hair = false;
        launch.SetBounds(40, 716, 520, 26);
        page.Controls.Add(launch);

        Pill reset = new Pill();
        reset.Text = "Repor o recomendado";
        reset.SetBounds(586, 713, 200, 32);
        reset.Click += delegate { SelectMode(0); };
        page.Controls.Add(reset);

        Pill advanced = new Pill();
        advanced.Text = "Codigo do perfil";
        advanced.SetBounds(794, 713, 146, 32);
        advanced.Click += delegate { ShowCode(); };
        page.Controls.Add(advanced);

        string[] columns = new string[] { "Imagem", "Desempenho", "Diagnostico" };
        for (int i = 0; i < columns.Length; i++)
        {
            Eyebrow eye = new Eyebrow(columns[i]);
            eye.SetBounds(40 + i * 300, 758, 272, 24);
            page.Controls.Add(eye);
        }

        fullScreenCard = Option("Tela cheia", "Janela sem borda do tamanho do monitor", 0, 0, FullScreen());
        fullScreenCard.CheckedChanged += delegate
        {
            SaveSetting("fullscreen", fullScreenCard.Checked ? "1" : "0");
            RefreshState();
        };
        panelCard = Option("Painel de diagnostico (F3)", "FPS, tempo de quadro, draws, VRAM e RAM", 0, 1, Toggle("panel", false));
        Option("Log detalhado", "O runtime fala mais; util para achar um problema", 1, 0, Toggle("log", false));
        Option("DRED completo", "Relatorio do driver quando o device cai", 1, 1, Toggle("dred", false));
        Option("Gravar livro de pipelines", "A sessao anota tudo que desenhou, para o warm", 2, 0, Toggle("book", false));
        Option("Console do runtime", "A janela preta aparece junto com a do jogo", 2, 1, Toggle("console", false));

        Rule activity = new Rule();
        activity.SetBounds(40, 926, 900, 1);
        page.Controls.Add(activity);

        Heading logHeading = new Heading("Atividade");
        logHeading.Hair = false;
        logHeading.SetBounds(40, 938, 300, 26);
        page.Controls.Add(logHeading);

        Pill folder = new Pill();
        folder.Text = "Abrir a pasta";
        folder.SetBounds(750, 935, 190, 32);
        folder.Click += delegate
        {
            try
            {
                Process.Start(new ProcessStartInfo(Runtime) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }
        };
        page.Controls.Add(folder);

        logText = new TextBox();
        logText.Multiline = true;
        logText.ReadOnly = true;
        logText.ScrollBars = ScrollBars.Vertical;
        logText.ForeColor = Theme.Dim;
        logText.Font = Theme.Mono(8.5f);
        logPanel = Field(logText, 40, 976, 900, 150);
        page.Controls.Add(logPanel);

        status = new Label();
        status.Text = "Pronto.";
        status.ForeColor = Theme.Muted;
        status.BackColor = Theme.Back;
        status.Font = Theme.Ui(9f);
        status.SetBounds(40, 1136, 900, 40);
        page.Controls.Add(status);

        Say("Runtime VIctory. O modo recomendado desenha a 0.6667 e reconstroi para a saida.");
        return page;
    }

    static OptionCard Option(string title, string line, int column, int row, bool on)
    {
        OptionCard card = new OptionCard();
        card.Text = title;
        card.Line = line;
        card.Checked = on;
        card.SetBounds(40 + column * 300, 782 + row * 70, 272, 70);
        card.CheckedChanged += delegate
        {
            string key = null;
            if (title.StartsWith("Log")) key = "log";
            else if (title.StartsWith("DRED")) key = "dred";
            else if (title.StartsWith("Gravar")) key = "book";
            else if (title.StartsWith("Console")) key = "console";
            else if (title.StartsWith("Painel")) key = "panel";
            if (key != null)
            {
                SaveSetting(key, card.Checked ? "1" : "0");
            }
        };
        building.Controls.Add(card);
        return card;    }

    static Panel PerformancePage()
    {
        Panel page = Page();
        Eyebrow eye = new Eyebrow("PERFIL");
        eye.SetBounds(40, 24, 900, 24);
        page.Controls.Add(eye);

        Heading head = new Heading("O perfil medido");
        head.SetBounds(40, 52, 900, 26);
        page.Controls.Add(head);

        Label note = Small("Cada linha foi medida nesta maquina. O codigo abaixo e o que o GTA6.exe poe no ambiente antes de abrir o jogo.", 40, 88, 900);
        page.Controls.Add(note);

        TextBox codeBox = new TextBox();
        codeBox.Text = Code(SelectedMode());
        codeBox.Font = Theme.Mono(9.5f);
        page.Controls.Add(Field(codeBox, 40, 116, 900, 40));

        Pill copy = new Pill();
        copy.Text = "Copiar";
        copy.SetBounds(40, 168, 140, 34);
        copy.Click += delegate
        {
            try
            {
                Clipboard.SetText(codeBox.Text);
                Say("Codigo copiado.");
            }
            catch (Exception)
            {
            }
        };
        page.Controls.Add(copy);

        Pill apply = new Pill();
        apply.Text = "Aplicar";
        apply.SetBounds(190, 168, 140, 34);
        apply.Click += delegate
        {
            if (!ApplyCode(codeBox.Text.Trim()))
            {
                MessageBox.Show(window,
                    "Codigo invalido: falta alguma chave, ou algum valor saiu da faixa que o runtime aceita.",
                    "GTA6", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Say("Codigo aplicado. Vale na proxima partida.");
        };
        page.Controls.Add(apply);

        Pill recommended = new Pill();
        recommended.Text = "Repor o recomendado";
        recommended.SetBounds(340, 168, 200, 34);
        recommended.Click += delegate
        {
            SelectMode(0);
            codeBox.Text = Code(SelectedMode());
        };
        page.Controls.Add(recommended);

        Table table = new Table();
        table.ValueColor = Theme.Cyan;
        table.Rows = new string[][]
        {
            new string[] { "s", "escala do desenho", "0.6667 desenha 854x480 e reconstroi a 1280x720" },
            new string[] { "b", "buffers do jogo na placa", "MB que o pool de buffers pode ocupar" },
            new string[] { "t", "espaco para alvos", "MB guardados para alvos que ainda nao existem" },
            new string[] { "m", "cartao reservado", "MB que o host nao encosta" },
            new string[] { "w", "janelas do cartao", "MB de janelas de leitura em voo" },
            new string[] { "p", "quanto da placa pode encher", "percentual; 90 deixa ~1 GB livre" },
            new string[] { "r", "blocos de textura prontos", "blocos de 64 MiB feitos adiantados" },
            new string[] { "g", "pressao de RAM: entra", "MB livres que ligam o modo apertado; 0 = padrao" },
            new string[] { "c", "pressao de RAM: sai", "MB livres que desligam; 0 = padrao" },
        };
        table.SetBounds(40, 224, 900, 9 * 30);
        page.Controls.Add(table);

        Heading measured = new Heading("O que a escala mudou");
        measured.SetBounds(40, 508, 900, 26);
        page.Controls.Add(measured);

        Table numbers = new Table();
        numbers.Weights = new int[] { 30, 22, 22, 26 };
        numbers.Rows = new string[][]
        {
            new string[] { "GPU por quadro", "34,0 ms", "13,7 ms", "escala 1.0 -> 0.6667" },
            new string[] { "quadro inteiro", "99,2 ms", "90,1 ms", "6.191 draws, soak28" },
            new string[] { "uso da placa", "85-100%", "10%", "a CPU virou a parede" },
            new string[] { "CPU por quadro", "~5,3 nucleos", "~5,3 nucleos", "thin 327 ms + submit 90 ms" },
        };
        numbers.SetBounds(40, 544, 900, 4 * 30);
        page.Controls.Add(numbers);

        Label tail = Small("A GPU espera 13,7 ms por um quadro que a CPU leva 90 ms para montar. Cortar a escala de novo ganha pouco: o proximo ganho esta em copies (27,4 ms) e na thread de submissao.", 40, 680, 900);
        tail.Height = 40;
        page.Controls.Add(tail);
        return page;
    }

    static Panel SetupPage()
    {
        Panel page = Page();
        Eyebrow eye = new Eyebrow("PREPARAR");
        eye.SetBounds(40, 24, 900, 24);
        page.Controls.Add(eye);

        Heading head = new Heading("Os quatro passos");
        head.SetBounds(40, 52, 900, 26);
        page.Controls.Add(head);

        steps = new StepList();
        steps.SetBounds(40, 96, 900, 250);
        page.Controls.Add(steps);

        Pill prepare = new Pill();
        prepare.Text = "Preparar (compilar)";
        prepare.SetBounds(40, 372, 200, 38);
        prepare.Click += delegate { Prepare(); };
        page.Controls.Add(prepare);

        Pill windows = new Pill();
        windows.Text = "Ajustes do Windows";
        windows.SetBounds(252, 372, 200, 38);
        windows.Click += delegate { AdminSetup(); };
        page.Controls.Add(windows);

        Pill folder = new Pill();
        folder.Text = "Pasta do jogo";
        folder.SetBounds(464, 372, 180, 38);
        folder.Click += delegate
        {
            if (AskGame())
            {
                RefreshState();
            }
        };
        page.Controls.Add(folder);

        Pill again = new Pill();
        again.Text = "Reinstalar o runtime";
        again.SetBounds(656, 372, 220, 38);
        again.Click += delegate
        {
            try
            {
                Install();
                Say("Runtime extraido de novo. Os arquivos do jogo nao foram tocados.");
                RefreshState();
            }
            catch (Exception error)
            {
                Say("Falha ao instalar: " + error.Message);
            }
        };
        page.Controls.Add(again);

        loadBar = new LoadBar();
        loadBar.SetBounds(40, 432, 900, 26);
        loadBar.Visible = false;
        page.Controls.Add(loadBar);

        Heading what = new Heading("O que cada passo faz");
        what.SetBounds(40, 480, 900, 26);
        page.Controls.Add(what);

        Table table = new Table();
        table.Weights = new int[] { 26, 74 };
        table.ValueColor = Theme.Soft;
        table.Rows = new string[][]
        {
            new string[] { "Jogo", "a pasta com game_scarlett_master.exe; sem ela nada abre" },
            new string[] { "Runtime", "victory.exe ao lado do GTA6.exe, extraido do proprio launcher" },
            new string[] { "Shaders", "traduz o shaders.rpf inteiro; sem isso o quadro fica sem programa e a tela preta" },
            new string[] { "Pipelines", "fabrica no cache do driver as pipelines que os quadros usam, para nao compilar no meio do quadro" },
        };
        table.SetBounds(40, 516, 900, 4 * 30);
        page.Controls.Add(table);

        Label note = Small("Preparar leva minutos na primeira vez e segundos depois. O jogo pode ser aberto sem preparar: ele so engasga na primeira vez que ve um lugar.", 40, 660, 900);
        note.Height = 40;
        page.Controls.Add(note);
        return page;
    }

    static Panel HelpPage()
    {
        Panel page = Page();
        Eyebrow eye = new Eyebrow("AJUDA");
        eye.SetBounds(40, 24, 900, 24);
        page.Controls.Add(eye);

        Heading head = new Heading("Controles");
        head.SetBounds(40, 52, 900, 26);
        page.Controls.Add(head);

        Table keys = new Table();
        keys.Weights = new int[] { 22, 78 };
        keys.ValueColor = Theme.Soft;
        keys.Rows = new string[][]
        {
            new string[] { "WASD", "andar" },
            new string[] { "Mouse", "olhar, com o botao direito apertado" },
            new string[] { "Botao direito", "mira / olhar" },
            new string[] { "F3", "painel de FPS, tempo de quadro e memoria" },
            new string[] { "F11", "tela cheia sem borda" },
            new string[] { "Alt+Enter", "alterna a janela" },
            new string[] { "Esc", "menu do jogo" },
        };
        keys.SetBounds(40, 92, 900, 7 * 30);
        page.Controls.Add(keys);

        Heading links = new Heading("Links");
        links.SetBounds(40, 320, 900, 26);
        page.Controls.Add(links);

        Pill repo = new Pill();
        repo.Text = "Codigo no GitHub";
        repo.SetBounds(40, 358, 190, 34);
        repo.Click += delegate { OpenUrl(RepoUrl); };
        page.Controls.Add(repo);

        Pill docs = new Pill();
        docs.Text = "OTIMIZACOES.md";
        docs.SetBounds(242, 358, 190, 34);
        docs.Click += delegate { OpenUrl(DocsUrl); };
        page.Controls.Add(docs);

        Heading about = new Heading("Sobre");
        about.SetBounds(40, 416, 900, 26);
        page.Controls.Add(about);

        Label text = Small("GTA6.exe carrega o runtime VIctory (base Belobug) embutido e escreve o perfil medido no ambiente antes de abrir o jogo. Nada e instalado fora desta pasta. O launcher da Julia foi desmontado para conferir o que ele faz; o visual daqui e uma copia do desenho dela, o perfil e o nosso.", 40, 454, 900);
        text.Height = 60;
        page.Controls.Add(text);

        Label hw = Small(HardwareLine(), 40, 530, 900);
        hw.Height = 40;
        page.Controls.Add(hw);
        return page;
    }

    static void Show(int page)
    {
        bool changed = page != shownPage;
        shownPage = page;
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].Visible = i == page;
        }
        if (changed)
        {
            foreach (Control control in pages[page].Controls)
            {
                Poster poster = control as Poster;
                if (poster != null)
                {
                    poster.PlayIntro();
                    break;
                }
            }
        }
        Anim.Wake();
        for (int i = 0; i < nav.Count; i++)
        {
            nav[i].Active = i == page;
        }
        RefreshState();
    }

    static void SelectMode(int index)
    {
        if (index < 0 || index >= Modes.Length)
        {
            return;
        }
        SaveSetting("mode", Modes[index].Key);
        RefreshMode();
    }

    static void RefreshMode()
    {
        if (modeCards == null || modeCards.Length == 0)
        {
            return;
        }
        Mode current = SelectedMode();
        bool known = false;
        for (int i = 0; i < Modes.Length; i++)
        {
            if (Modes[i].Key == current.Key)
            {
                known = true;
            }
        }
        for (int i = 0; i < modeCards.Length; i++)
        {
            bool on = i < Modes.Length ? Modes[i].Key == current.Key : !known;
            modeCards[i].Selected = on;
        }
        if (modeNote != null)
        {
            modeNote.Text = current.Note;
        }
        if (banner != null)
        {
            banner.RightValue = current.Name + "  -  " + current.Tag;
            banner.FactValues[0] = current.Scale;
        }
    }

    static bool OutputHigh()
    {
        return Setting("output", "720p") == "1080p";
    }

    static bool Toggle(string key, bool fallback)
    {
        return Setting(key, fallback ? "1" : "0") == "1";
    }

    static string cachedHardware;

    static void RefreshState()
    {
        if (banner == null)
        {
            return;
        }
        string path = ReadGamePath();
        bool game = path.Length > 0 && File.Exists(path);
        bool engine = File.Exists(Path.Combine(Runtime, "victory.exe"));
        bool book = HasBook();
        bool agility = File.Exists(Path.Combine(Runtime, "D3D12", "D3D12Core.dll"));
        int[] states = new int[]
        {
            game ? 1 : 0,
            engine ? 1 : 0,
            book ? 1 : 0,
            agility ? 1 : 0,
        };
        banner.States = states;
        banner.StepNames = new string[] { "Jogo", "Motor", "Pipelines", "D3D12" };
        banner.Steps = new string[]
        {
            game ? "encontrado" : "falta a pasta",
            engine ? "pronto" : "falta extrair",
            book ? "no cache" : "jogue uma vez",
            agility ? "pronto" : "falta extrair",
        };
        if (statusStrip != null)
        {
            statusStrip.Items = new string[] { "Jogo", "Runtime", "Pipelines", "D3D12" };
            statusStrip.States = states;
            statusStrip.ReadyText = "Pronto para jogar";
            statusStrip.NotReadyText = game ? "Falta preparar" : "Falta o jogo";
        }
        if (steps != null)
        {
            steps.Names = new string[] { "Jogo encontrado", "Runtime instalado", "Pipelines compiladas", "Agility SDK" };
            steps.Details = new string[]
            {
                game ? path : "escolha a pasta com game_scarlett_master.exe",
                engine ? Path.Combine(Runtime, "victory.exe") : "sera extraido ao abrir",
                book ? "known-pipelines.bin no cache" : "jogue uma vez e rode Preparar",
                agility ? "D3D12Core.dll ao lado do runtime" : "sera extraido ao abrir",
            };
            steps.States = states;
            steps.Invalidate();
        }
        if (footMeta != null)
        {
            if (cachedHardware == null)
            {
                cachedHardware = HardwareLine();
            }
            footMeta.Set(game && engine ? "Pronto" : "Falta preparar",
                game && engine ? Theme.Good : Theme.Warn,
                SelectedMode().Name, "VIctory", cachedHardware, "");
        }
    }

    // O codigo do perfil, para colar num chat ou receber o de outra pessoa.
    // Aplicar grava como `custom` e passa a usa-lo na proxima partida.
    static bool ApplyCode(string text)
    {
        Mode parsed = ParseCode(text);
        if (parsed == null)
        {
            return false;
        }
        SaveSetting("custom", Code(parsed));
        SaveSetting("mode", "custom");
        RefreshMode();
        return true;
    }

    static void ShowCode()
    {
        using (Form dialog = new Form())
        {
            dialog.Text = "Codigo do perfil";
            dialog.ClientSize = new Size(560, 244);
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.BackColor = Theme.Back;
            dialog.ForeColor = Theme.Text;
            dialog.Font = Theme.Ui(9.5f);

            Label hint = new Label();
            hint.Text = "Cole o codigo de outro perfil, ou copie este para compartilhar.";
            hint.ForeColor = Theme.Muted;
            hint.BackColor = Theme.Back;
            hint.SetBounds(24, 20, 512, 20);
            dialog.Controls.Add(hint);

            TextBox box = new TextBox();
            box.Text = Code(SelectedMode());
            box.Font = Theme.Mono(9.5f);
            RoundCard field = Field(box, 24, 48, 512, 40);
            dialog.Controls.Add(field);

            Pill copy = new Pill();
            copy.Text = "Copiar";
            copy.SetBounds(24, 108, 150, 38);
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(box.Text);
                    Say("Codigo copiado.");
                }
                catch (Exception)
                {
                }
            };
            dialog.Controls.Add(copy);

            Pill apply = new Pill();
            apply.Main = true;
            apply.Text = "Aplicar";
            apply.SetBounds(186, 108, 150, 38);
            apply.Click += delegate
            {
                if (!ApplyCode(box.Text.Trim()))
                {
                    MessageBox.Show(dialog,
                        "Codigo invalido: falta alguma chave, ou algum valor saiu da faixa que o runtime aceita.",
                        "GTA6", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Say("Codigo aplicado. Vale na proxima partida.");
                dialog.DialogResult = DialogResult.OK;
            };
            dialog.Controls.Add(apply);

            Pill close = new Pill();
            close.Text = "Fechar";
            close.SetBounds(348, 108, 150, 38);
            close.Click += delegate { dialog.DialogResult = DialogResult.Cancel; };
            dialog.Controls.Add(close);

            Label foot = new Label();
            foot.Text = "s=escala;b=buffers;t=alvos;m=cartao;w=janelas;p=percentual;r=ripe;g=pressao;c=limpeza";
            foot.ForeColor = Theme.Faint;
            foot.BackColor = Theme.Back;
            foot.Font = Theme.Mono(8.5f);
            foot.SetBounds(24, 168, 512, 20);
            dialog.Controls.Add(foot);

            dialog.AcceptButton = apply;
            dialog.CancelButton = close;
            dialog.ShowDialog(window);
            RefreshMode();
        }
    }

    static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }
    // A janela do titulo nasce fora da tela e sem foco; ela e trazida para
    // frente algumas vezes no comeco e o acompanhamento para depois.
    static void Watch()
    {
        if (running == null || running.HasExited)
        {
            focus.Stop();
            Say("O jogo fechou.");
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

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

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

    // O "Setup" do launcher da Julia, sem tocar no pagefile. O plano de energia
    // e a preferencia de placa sao reversiveis e nao pedem reinicio; o pagefile
    // desta maquina ja esta fixo em 40 GB, entao ele so e relatado.
    static void AdminSetup()
    {
        if (!IsAdmin())
        {
            try
            {
                ProcessStartInfo again = new ProcessStartInfo(Application.ExecutablePath, "--admin");
                again.UseShellExecute = true;
                again.Verb = "runas";
                Process.Start(again);
            }
            catch (Exception)
            {
                Say("Sem elevacao: os ajustes do Windows nao foram aplicados.");
            }
            return;
        }

        List<string> lines = new List<string>();

        try
        {
            ProcessStartInfo power = new ProcessStartInfo(
                "powercfg", "/overlaysetactive ded574b5-45a0-4f42-8737-46345c09c238");
            power.UseShellExecute = false;
            power.CreateNoWindow = true;
            using (Process process = Process.Start(power))
            {
                process.WaitForExit(30000);
                lines.Add("energia: " + (process.ExitCode == 0
                    ? "ultimate performance"
                    : "codigo " + process.ExitCode));
            }
        }
        catch (Exception error)
        {
            lines.Add("energia: " + error.Message);
        }

        // GpuPreference=2 e "alto desempenho". Num portatil com duas placas e o
        // que impede o Windows de dar a integrada ao runtime.
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\DirectX\UserGpuPreferences"))
            {
                key.SetValue(Path.Combine(Runtime, "victory.exe"), "GpuPreference=2;");
                lines.Add("placa: alto desempenho para victory.exe");
            }
        }
        catch (Exception error)
        {
            lines.Add("placa: " + error.Message);
        }

        lines.Add("pagefile: " + PagefileLine());
        Say(string.Join("  |  ", lines.ToArray()));
    }

    static bool IsAdmin()
    {
        try
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    static string PagefileLine()
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
            {
                string[] files = key == null ? null : key.GetValue("PagingFiles") as string[];
                if (files == null)
                {
                    return "gerenciado pelo Windows";
                }
                List<string> parts = new List<string>();
                foreach (string file in files)
                {
                    if (!string.IsNullOrEmpty(file.Trim()))
                    {
                        parts.Add(file.Trim());
                    }
                }
                return parts.Count == 0 ? "gerenciado pelo Windows" : string.Join("; ", parts.ToArray());
            }
        }
        catch (Exception)
        {
            return "nao lido";
        }
    }

    static void OpenFile(string name)
    {
        string path = Path.Combine(Runtime, name);
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
