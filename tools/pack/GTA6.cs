using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

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
    static Process running;
    static Timer focus;
    static int focusTicks;

    [STAThread]
    static void Main(string[] args)
    {
        bool headless = args.Length > 0 && args[0].StartsWith("--play", StringComparison.Ordinal);
        bool installOnly = args.Length > 0 && args[0] == "--install";

        try
        {
            Install();
        }
        catch (Exception error)
        {
            if (headless || installOnly)
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

    // O perfil do fork numa maquina de 16 GB (Ryzen 5 5600G, 16 GB, RTX 3060 Ti
    // 8 GB, Windows 10). Cada linha foi medida; o porque esta em OTIMIZACOES.md.
    static void Settings(ProcessStartInfo start, bool high, bool panel)
    {
        // O fork em modo rapido.
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
        start.EnvironmentVariables["VICTORY_SHOW_AFTER"] = "1";
        start.EnvironmentVariables["VICTORY_AMBIENT"] = "0.8";
        start.EnvironmentVariables["VICTORY_D3D12_DRED"] = "light";
        start.EnvironmentVariables["VICTORY_THIN"] = "1";
        start.EnvironmentVariables["VICTORY_JOIN_PARTS"] = "8";
        // A placa de 8 GB, nomeada para a escolha nunca cair na integrada.
        start.EnvironmentVariables["VICTORY_D3D12_ADAPTER"] = "NVIDIA";
        // O titulo acredita que a TV e 4K e desenha 2560x1440 dentro de uma
        // janela 1080p: 4x os alvos, 4x as copias. VICTORY_OUTPUT e o modo que
        // ele reporta; a escala 0.5 desenha 640x360 e sobe para 720p com o
        // upscaler dele mesmo (6), nao o FidelityFX (7).
        start.EnvironmentVariables["VICTORY_OUTPUT"] = high ? "1920x1080" : "1280x720";
        start.EnvironmentVariables["VICTORY_UPSCALER"] = "6";
        start.EnvironmentVariables["VICTORY_RENDER_SCALE"] = high ? "0.67" : "0.5";
        // O cache de pipelines: 171 MB de disco que valem a fila inteira
        // (1.200 pipelines, um por segundo, sem ele).
        start.EnvironmentVariables["VICTORY_PIPELINE_CACHE"] = "1";
        start.EnvironmentVariables["VICTORY_PIPELINE_WORKERS"] = "10";
        // 16 GB de RAM: tudo o que o host guarda, pequeno. Sem
        // VICTORY_TEXTURE_BUDGET_MB de proposito: com ele o host para de
        // subir textura no teto e o mundo fica sem as 4 GB que pede.
        start.EnvironmentVariables["VICTORY_UPLOAD_AHEAD"] = "0";
        start.EnvironmentVariables["VICTORY_CARD_ROOM_FOR_TARGETS_MB"] = "512";
        start.EnvironmentVariables["VICTORY_TEXTURE_ROOM_PERCENT"] = "95";
        start.EnvironmentVariables["VICTORY_MEMORY_CARD_MB"] = "2048";
        start.EnvironmentVariables["VICTORY_CARD_WINDOWS_MB"] = "512";
        start.EnvironmentVariables["VICTORY_CARD_BUFFERS_MB"] = "256";
        start.EnvironmentVariables["VICTORY_KEEP_SNAPSHOTS_MB"] = "32";
        // O mouse olha enquanto o botao direito esta apertado.
        start.EnvironmentVariables["VICTORY_MOUSE_LOOK"] = "1";
        start.EnvironmentVariables["VICTORY_MOUSE_SENSITIVITY"] = "0.05";
        start.EnvironmentVariables["VICTORY_LOG"] = "warn";
        if (panel)
        {
            start.EnvironmentVariables["VICTORY_PANEL"] = "1";
        }
        // O que estiver no ambiente do Windows vence, para testar sem
        // recompilar. As pastas nunca sao sobrescritas.
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
        Settings(start, high, panel);
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

    // ------------------------------------------------------------------- ui

    static void BuildWindow()
    {
        window = new Form();
        window.Text = "GTA6";
        window.ClientSize = new Size(560, 300);
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

        status = new Label();
        status.Text = "Jogo: " + (ReadGamePath().Length > 0 ? ReadGamePath() : "nao encontrado");
        status.ForeColor = Color.FromArgb(150, 150, 160);
        status.SetBounds(30, 212, 500, 60);
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
