using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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

    static Form window;
    static Label status;
    static Button play720;
    static Button play1080;
    static Button playPanel;
    static Button prepare;
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
            if (File.Exists(target) && new FileInfo(target).Length == source.Length)
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
        ProfileInput(start, panel);
        ProfileKept(start);
    }

    // O fork em modo rapido: as estradas novas dele, medidas nesta maquina.
    static void ProfileFork(ProcessStartInfo start)
    {
        start.EnvironmentVariables["VICTORY_TEXTURE_NO_WAIT"] = "1";
        start.EnvironmentVariables["VICTORY_RELAY"] = "2";
        start.EnvironmentVariables["VICTORY_WIDE_BACK"] = "1";
        start.EnvironmentVariables["VICTORY_CARD_REHASH_RUNS"] = "1";
        start.EnvironmentVariables["VICTORY_JOIN_COPIES"] = "1";
        start.EnvironmentVariables["VICTORY_INDIRECT_WINDOW"] = "1";
        start.EnvironmentVariables["VICTORY_RELAY_COPY"] = "1";
        start.EnvironmentVariables["VICTORY_CARD_STEPS"] = "2";
        start.EnvironmentVariables["VICTORY_INDIRECT_BATCH"] = "2";
        start.EnvironmentVariables["VICTORY_MAP_THREADS"] = "1";
        start.EnvironmentVariables["VICTORY_TITLE_PRIORITIES"] = "0";
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
        // O titulo acredita que a TV e 4K e desenha 2560x1440 dentro de uma
        // janela 1080p: 4x os alvos, 4x as copias. VICTORY_OUTPUT e o modo que
        // ele reporta; a escala 1.0 desenha na resolucao do modo, sem upscaler.
        // A 0.5 (640x360) o upscaler dele mesmo (6) aplica nitidez e a imagem
        // fica dura, com halo; a placa fica 78% ociosa de qualquer jeito, entao
        // desenhar 720p inteiro custa quase nada e tira o serrilhado.
        start.EnvironmentVariables["VICTORY_OUTPUT"] = high ? "1920x1080" : "1280x720";
        start.EnvironmentVariables["VICTORY_UPSCALER"] = "6";
        start.EnvironmentVariables["VICTORY_RENDER_SCALE"] = high ? "0.85" : "1.0";
        // A biblioteca de pipelines: 200 e poucos MB de disco que valem a fila
        // inteira. STORE_LATER entrega a pipeline na hora e a grava com o save,
        // em vez de segurar o quadro esperando o disco.
        start.EnvironmentVariables["VICTORY_LIBRARY"] = "1";
        start.EnvironmentVariables["VICTORY_LIBRARY_STORE_LATER"] = "1";
        start.EnvironmentVariables["VICTORY_PIPELINE_WORKERS"] = "10";
    }

    // A placa de 8 GB e o que ela guarda.
    static void ProfileCard(ProcessStartInfo start)
    {
        // 512 MB, nao os 3.072 do padrao: num cartao de 8 GB com 6,8 em uso, o
        // espaco reservado para alvos que ainda nao existem e o que falta para
        // a fila de textura. 95% e o teto pratico (97 e o maximo do knob).
        start.EnvironmentVariables["VICTORY_CARD_ROOM_FOR_TARGETS_MB"] = "512";
        start.EnvironmentVariables["VICTORY_TEXTURE_ROOM_PERCENT"] = "95";
        start.EnvironmentVariables["VICTORY_MEMORY_CARD_MB"] = "2048";
        start.EnvironmentVariables["VICTORY_CARD_WINDOWS_MB"] = "512";
        start.EnvironmentVariables["VICTORY_CARD_BUFFERS_MB"] = "256";
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
        start.EnvironmentVariables["VICTORY_KEEP_SNAPSHOTS_MB"] = "32";
        // Duas texturas com as mesmas palavras de descricao, mudando so o
        // "filler", viravam dois recursos e duas copias (8.438 delas na run 15).
        start.EnvironmentVariables["VICTORY_PLACED_FILLER"] = "1";
        // Um pipeline que falta faz o draw sumir; 1 s de espera por quadro
        // custa menos que um personagem sem corpo na cutscene.
        start.EnvironmentVariables["VICTORY_PIPELINE_WAIT_MS"] = "1000";
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
            "VICTORY_FIDELITYFX_DIR",
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

    static void Prepare()
    {
        if (preparing)
        {
            Say("Ja esta preparando.");
            return;
        }
        if (!HasBook())
        {
            Say("Ainda nao ha livro de pipelines. Rode uma sessao com --book uma vez.");
            return;
        }
        preparing = true;
        Say("Compilando as pipelines do jogo antes da partida...");
        Thread worker = new Thread(new ThreadStart(delegate
        {
            try
            {
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
        window.ClientSize = new Size(560, 360);
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
        prepare = MakeButton("Preparar (compilar)", 30, 208, 240);
        prepare.Click += delegate { Prepare(); };
        window.Controls.Add(prepare);

        status = new Label();
        status.Text = "Jogo: " + (ReadGamePath().Length > 0 ? ReadGamePath() : "nao encontrado");
        status.ForeColor = Color.FromArgb(150, 150, 160);
        status.SetBounds(30, 264, 500, 80);
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
