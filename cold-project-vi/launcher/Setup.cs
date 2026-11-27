// The Setup page: everything a first run needs, checked and done for the player. Each step says
// whether it is done and has a button that does it; "Do everything for me" runs the missing ones
// in order. Steps that change Windows ask for administrator rights (the UAC prompt) or a
// confirmation first; nothing restarts the PC without a yes.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        // 1: done, 0: to do, -1: not known (or nothing to do here).
        class Step
        {
            public string Title, Why, FixText;
            public Func<int> Check;
            public Func<bool> Fix;        // runs on a worker thread; true if it did its part
            public bool Auto = true;      // part of "Do everything for me"
            public Func<bool> Applies;    // null: every PC; false: not for this one (another card maker)
            public Label Mark, Text, Detail;
            public Pill Button;
            public int State = -1;
        }

        readonly List<Step> steps = new List<Step>();
        LoadBar setupBar;
        Pill everything;
        volatile bool setupBusy;

        [DllImport("kernel32.dll")] static extern bool GetFirmwareType(out uint type);

        string CargoExe
        {
            get
            {
                var mine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "cargo.exe");
                return File.Exists(mine) ? mine : "cargo";
            }
        }

        static bool Uefi()
        {
            try { uint t; return GetFirmwareType(out t) && t == 2; } catch (Exception) { return false; }
        }

        // ---------------------------------------------------------------- page
        Panel SetupPage()
        {
            var page = Page();
            const int X = 24, W = 852;
            int y = 20;
            page.Controls.Add(new Heading(L("Setup: everything done for you", "Preparar: tudo feito por ti")) { Location = new Point(X, y), Width = W });
            y += 32;
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(W - 250, 40), ForeColor = Theme.Dim, BackColor = Theme.Back,
                Text = L("Each step checks your PC. Press its button, or 'Do everything for me' to run the missing ones in order. Windows asks before anything that needs administrator rights.",
                         "Cada passo verifica o teu PC. Carrega no botao dele, ou em 'Fazer tudo por mim' para correr os que faltam por ordem. O Windows pergunta antes de tudo o que precisa de administrador.") });
            everything = new Pill { Main = true, Text = L("Do everything for me", "Fazer tudo por mim"), Location = new Point(X + W - 236, y), Size = new Size(236, 44), Font = new Font("Segoe UI Semibold", 11f) };
            everything.Click += (s, e) => DoEverything();
            page.Controls.Add(everything);
            y += 52;
            setupBar = new LoadBar { Location = new Point(X, y), Size = new Size(W, 26), Visible = false };
            page.Controls.Add(setupBar);
            y += 34;

            DefineSteps();
            foreach (var st in steps)
            {
                var card = new Panel { Location = new Point(X, y), Size = new Size(W, 58), BackColor = Theme.Card };
                st.Mark = new Label { Location = new Point(12, 14), Size = new Size(30, 30), Font = new Font("Segoe UI Semibold", 15f), ForeColor = Theme.Dim, BackColor = Theme.Card, Text = "•" };
                st.Text = new Label { Location = new Point(48, 8), Size = new Size(W - 260, 22), Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Theme.Text, BackColor = Theme.Card, Text = st.Title };
                st.Detail = new Label { Location = new Point(48, 31), Size = new Size(W - 260, 20), ForeColor = Theme.Dim, BackColor = Theme.Card, Text = st.Why, AutoEllipsis = true };
                st.Button = new Pill { Text = st.FixText, Location = new Point(W - 196, 12), Size = new Size(184, 34), BackColor = Theme.Card };
                var me = st;
                st.Button.Click += (s, e) => RunSteps(new[] { me });
                card.Controls.Add(st.Mark); card.Controls.Add(st.Text); card.Controls.Add(st.Detail); card.Controls.Add(st.Button);
                var tips = new ToolTip { AutoPopDelay = 20000 };
                tips.SetToolTip(st.Detail, st.Why);
                page.Controls.Add(card);
                y += 64;
            }
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(10, 10), BackColor = Theme.Back });
            return page;
        }

        void DefineSteps()
        {
            steps.Clear();
            steps.Add(new Step {
                Title = L("Rust (builds VIctory)", "Rust (compila o VIctory)"),
                Why = L("The compiler VIctory is written for. Installed with winget (rustup).", "O compilador do VIctory. Instalado com o winget (rustup)."),
                FixText = L("Install", "Instalar"),
                // Not needed with a ready-made emulator (a release or the installer put victory.exe here).
                Check = () => NothingToBuild ? -1 : File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "cargo.exe")) || RunCapture("where", "cargo").Contains("cargo") ? 1 : 0,
                Fix = () => Winget("Rustlang.Rustup", null),
            });
            steps.Add(new Step {
                Title = L("Visual Studio Build Tools (C++)", "Visual Studio Build Tools (C++)"),
                Why = L("The C++ linker and Windows SDK Rust needs. A large download (2-6 GB); Windows asks for permission.", "O linker de C++ e o Windows SDK de que o Rust precisa. Download grande (2-6 GB); o Windows pede autorizacao."),
                FixText = L("Install", "Instalar"),
                Check = () => NothingToBuild ? -1 : HasCppTools() ? 1 : 0,
                Fix = () => Winget("Microsoft.VisualStudio.2022.BuildTools", "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"),
            });
            steps.Add(new Step {
                Title = L("Build VIctory", "Compilar o VIctory"),
                Why = L("Compiles the emulator from this folder (several minutes, once; again after an update).", "Compila o emulador a partir desta pasta (alguns minutos, uma vez; de novo depois de atualizar)."),
                FixText = L("Build", "Compilar"),
                Check = () => UseAmdEngine ? (OwnVictoryExe != null ? 1 : -1) : VictoryExe != null ? 1 : 0,
                Fix = BuildBlocking,
            });
            steps.Add(new Step {
                Title = L("AMD alternative engine (Belobug)", "Motor alternativo AMD (Belobug)"),
                Why = L("Only if you chose it in Hardware & ID: VIctory-Belobug-AMD, Belobug's fork for Radeon (GPL-3.0). Downloads its source and builds it once.",
                        "So se o escolheste em Hardware e ID: VIctory-Belobug-AMD, o fork do Belobug para Radeon (GPL-3.0). Descarrega o codigo e compila-o uma vez."),
                FixText = L("Build it", "Compilar"),
                Check = () => !UseAmdEngine ? -1 : VictoryExe != null ? 1 : 0,
                Fix = BuildAmdEngine,
            });
            steps.Add(new Step {
                Title = L("Windows Security: 'Access denied' when building", "Seguranca do Windows: 'Acesso negado' ao compilar"),
                Why = L("Only if a build from source ends with 'Access denied': Controlled folder access blocks freshly built programs. Opens Windows Security; allow victory.exe there yourself. The launcher never changes antivirus settings.",
                        "So se uma compilacao acabar com 'Acesso negado': o Acesso controlado a pastas bloqueia programas acabados de compilar. Abre a Seguranca do Windows; permite la o victory.exe tu mesmo. O launcher nunca muda definicoes do antivirus."),
                FixText = L("Open it", "Abrir"),
                Check = () => -1,
                Auto = false,
                Fix = () => { OpenUrl("windowsdefender://ransomwareprotection"); return false; },
            });
            steps.Add(new Step {
                Title = L("Your game folder", "A pasta do jogo"),
                Why = L("Looks for game_scarlett_master.exe (or another game_scarlett*.exe) in Downloads, Desktop, Documents, Games folders and the root of each drive.", "Procura o game_scarlett_master.exe (ou outro game_scarlett*.exe) em Transferencias, Ambiente de trabalho, Documentos, pastas Games e na raiz de cada disco."),
                FixText = L("Find it", "Encontrar"),
                Check = () => { string g = null; Invoke(new Action(() => g = GameExe)); return File.Exists(g) ? 1 : 0; },
                Fix = FindGame,
            });
            steps.Add(new Step {
                Title = L("Shaders prepared", "Shaders preparados"),
                Why = L("Translates the game's shaders and makes their pipelines once (about 5 minutes), so the game does not stop to do it.", "Traduz os shaders do jogo e prepara as pipelines uma vez (cerca de 5 minutos), para o jogo nao parar para isso."),
                FixText = L("Prepare", "Preparar"),
                Check = () => ShadersReady() ? 1 : 0,
                Fix = PrepareBlocking,
            });
            steps.Add(new Step {
                Title = L("Power: plugged in, best performance", "Energia: ligado a corrente, melhor desempenho"),
                Why = L("Sets Windows' power mode to 'Best performance'. A laptop on battery runs the CPU slower: plug it in (and use the maker's performance mode, e.g. PredatorSense).",
                        "Poe o modo de energia do Windows em 'Melhor desempenho'. Um portatil na bateria corre mais devagar: liga-o a corrente (e usa o modo de desempenho do fabricante, p. ex. PredatorSense)."),
                FixText = L("Set it", "Definir"),
                Check = () => BestPower() && SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline ? 1 : 0,
                Fix = () => { RunWait("powercfg", "/overlaysetactive ded574b5-45a0-4f42-8737-46345c09c238", false); return BestPower(); },
            });
            steps.Add(new Step {
                Title = L("Virtual memory managed by Windows", "Memoria virtual gerida pelo Windows"),
                Why = L("The game and VIctory commit 25+ GB; a fixed, small page file ends them. Windows asks for permission; it counts after a restart.",
                        "O jogo e o VIctory reservam 25+ GB; um ficheiro de paginacao fixo e pequeno fecha-os. O Windows pede autorizacao; conta depois de reiniciar."),
                FixText = L("Set it", "Definir"),
                Check = () => PagefileOk() ? 1 : 0,
                Fix = FixPagefile,
            });
            steps.Add(new Step {
                Title = L("Dedicated graphics card for VIctory", "Placa grafica dedicada para o VIctory"),
                Why = L("On a laptop with two graphics cards, Windows may give VIctory the slow built-in one. Sets 'High performance' for victory.exe (Windows > Graphics settings).",
                        "Num portatil com duas placas, o Windows pode dar ao VIctory a integrada, lenta. Poe 'Alto desempenho' para o victory.exe (Windows > Definicoes de graficos)."),
                FixText = L("Set it", "Definir"),
                Check = () => VictoryExe == null ? -1 : GpuPreferenceSet() || (probeAdapter.Length > 0 && GpuPreferenceOk() && !(hasNvidia && hasRadeon)) ? 1 : 0,
                Fix = () => { bool ok = SetGpuPreference(); ProbeGpu(); return ok; },
            });
            steps.Add(new Step {
                Title = "NVIDIA Smooth Motion",
                Why = L("Driver frame generation for RTX 40/50, written into the driver's profile for victory.exe (no NVIDIA App needed).", "Geracao de frames do driver para RTX 40/50, escrita no perfil do driver para o victory.exe (sem NVIDIA App)."),
                FixText = L("Turn on", "Ligar"),
                Check = () => !rtxFrameGen ? -1 : (SmoothMotion.Status() == "on" ? 1 : 0),
                Applies = () => rtxFrameGen,
                Fix = () => { var err = SmoothMotion.Set(true); smoothState = SmoothMotion.Status(); if (err != null) Log("Smooth Motion: " + err); BeginInvoke(new Action(() => { UpdateBanner(); UpdateSmooth(); })); return err == null; },
            });
            steps.Add(new Step {
                Title = "Resizable BAR",
                Why = L("Lets the CPU write straight into all of the video memory (faster copies). It is switched on in the PC's BIOS/UEFI: the guide shows how.",
                        "Deixa o processador escrever diretamente em toda a VRAM (copias mais rapidas). Liga-se na BIOS/UEFI do PC: o guia mostra como."),
                FixText = L("Show me how", "Mostra-me como"),
                Check = () => rebar ? 1 : RebarMissing() ? 0 : -1,
                Fix = () => { BeginInvoke(new Action(ShowRebarGuide)); return false; },
                Auto = false,
            });
            steps.Add(new Step {
                Title = L("Resizable BAR in the NVIDIA driver", "Resizable BAR no driver da NVIDIA"),
                Why = L("NVIDIA's driver uses Resizable BAR only for the games on its list: this puts victory.exe on it (rBAR Enable, Enable v2, Options in its profile).",
                        "O driver da NVIDIA so usa o Resizable BAR nos jogos da lista dele: isto poe o victory.exe nessa lista (rBAR Enable, Enable v2, Options no perfil)."),
                FixText = L("Turn on", "Ligar"),
                Check = () => !hasNvidia || !rebarBios ? -1 : SmoothMotion.RebarSet() ? 1 : 0,
                Applies = () => hasNvidia && rebarBios,
                Fix = () => {
                    var err = SmoothMotion.SetRebar(true);
                    // The driver keeps its Resizable BAR settings for administrators (-137, NVAPI_INVALID_USER_PRIVILEGE):
                    // without them "Turn on" failed and the step stayed red. Asked once more as administrator.
                    if (err != null && err.Contains("-137"))
                        err = Elevated(Application.ExecutablePath, "--rebar-profile") && SmoothMotion.RebarSet() ? null : L("Windows did not give administrator rights, or the driver refused it.", "O Windows nao deu permissoes de administrador, ou o driver recusou.", "Windows n'a pas donné les droits administrateur, ou le pilote a refusé.");
                    if (err != null) Log("rBAR: " + err); else Log(L("Resizable BAR is on in victory.exe's driver profile.", "Resizable BAR ligado no perfil do driver do victory.exe."));
                    return err == null;
                },
            });
        }

        // ---------------------------------------------------------------- running steps
        void CheckAll()
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (setupBusy) return;
            new Thread(() => {
                foreach (var st in steps)
                {
                    int s;
                    try { s = st.Check(); } catch (Exception) { s = -1; }
                    var me = st;
                    BeginInvoke(new Action(() => PaintStep(me, s)));
                }
            }) { IsBackground = true }.Start();
        }

        void PaintStep(Step st, int state)
        {
            st.State = state;
            st.Mark.Text = state == 1 ? "✓" : state == 0 ? "✗" : "•";
            st.Mark.ForeColor = state == 1 ? Theme.Good : state == 0 ? Theme.Warn : Theme.Dim;
            st.Button.Enabled = !setupBusy;
            st.Button.Invalidate();
        }

        void DoEverything()
        {
            // (Steps for another kind of PC are left out: on an AMD card "Do everything" tried NVIDIA's
            // Smooth Motion and rBAR profile and showed them as failed, 27/11/2026.)
            RunSteps(steps.Where(s => s.Auto && s.State != 1 && (s.Applies == null || s.Applies())).ToArray());
        }

        void RunSteps(Step[] which)
        {
            if (setupBusy || which.Length == 0) return;
            setupBusy = true;
            foreach (var st in steps) { st.Button.Enabled = false; st.Button.Invalidate(); }
            everything.Enabled = false; everything.Invalidate();
            new Thread(() => {
                int i = 0;
                foreach (var st in which)
                {
                    i++;
                    var me = st;
                    int now;
                    try { now = st.Check(); } catch (Exception) { now = -1; }
                    if (now == 1 && which.Length > 1) { BeginInvoke(new Action(() => PaintStep(me, 1))); continue; }
                    Progress(string.Format("{0} ({1}/{2})...", st.Title, i, which.Length), (float)(i - 1) / which.Length);
                    BeginInvoke(new Action(() => { me.Mark.Text = "…"; me.Mark.ForeColor = Theme.Cyan; }));
                    bool ok;
                    try { ok = st.Fix(); } catch (Exception e) { ok = false; Log(st.Title + ": " + e.Message); }
                    try { now = st.Check(); } catch (Exception) { now = -1; }
                    BeginInvoke(new Action(() => PaintStep(me, now)));
                    Log((now == 1 ? "✓ " : ok ? "• " : "✗ ") + st.Title);
                    // Nothing after a build can work without it.
                    if (now == 0 && st.Fix == (Func<bool>)BuildBlocking && which.Length > 1) break;
                }
                Progress(L("Done.", "Feito."), 1f);
                Thread.Sleep(800);
                setupBusy = false;
                BeginInvoke(new Action(() => {
                    setupBar.Visible = false;
                    if (loadBar != null) loadBar.Visible = false;
                    everything.Enabled = true; everything.Invalidate();
                    foreach (var st in steps) { st.Button.Enabled = true; st.Button.Invalidate(); }
                    Refresh2();
                }));
            }) { IsBackground = true }.Start();
        }

        float setupProgress;

        // Both bars (Setup page and Play page) show the same work; the Shaders page mirrors them.
        void Progress(string text, float part)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, float>(Progress), text, part); return; }
            setupProgress = part;
            foreach (var bar in new[] { setupBar, loadBar })
                if (bar != null) { bar.Visible = true; bar.Set(part, text); }
        }

        // ---------------------------------------------------------------- the steps' work
        bool Winget(string id, string overrideArgs)
        {
            if (string.IsNullOrEmpty(RunCapture("where", "winget")))
            {
                Log(L("winget is missing (App Installer from the Microsoft Store). Opening the download page.", "Falta o winget (App Installer da Microsoft Store). A abrir a pagina de download."));
                OpenUrl(id.StartsWith("Rust") ? "https://rustup.rs" : "https://visualstudio.microsoft.com/visual-cpp-build-tools/");
                return false;
            }
            var args = "install --id " + id + " -e --silent --accept-source-agreements --accept-package-agreements";
            if (overrideArgs != null) args += " --override \"" + overrideArgs + "\"";
            Log("winget " + args);
            return RunWait("winget", args, false) == 0;
        }

        static bool HasCppTools()
        {
            var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vswhere)) return false;
            var s = RunCapture(vswhere, "-products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath");
            return !string.IsNullOrEmpty(s.Trim());
        }

        // A ready-made emulator is here (a release, or the installer): nothing has to be compiled.
        bool NothingToBuild { get { return !UseAmdEngine && (File.Exists(Path.Combine(root, "victory.exe")) || File.Exists(Path.Combine(AppDir, "victory.exe"))); } }

        // Where this repository's source is: beside the launcher (or a folder above it), or the copy
        // the launcher downloaded. A launcher copied on its own to Downloads has none beside it
        // ("could not find Cargo.toml in ...\Downloads"); then the source is fetched first.
        string sourceSeen;

        string SourceDir
        {
            get
            {
                Func<string, bool> isSource = d => d != null && File.Exists(Path.Combine(d, "Cargo.toml")) && File.Exists(Path.Combine(d, ".victory-root"));
                for (var d = new DirectoryInfo(root); d != null; d = d.Parent)
                    if (isSource(d.FullName))
                    {
                        // Remembered, so a copy of the launcher elsewhere (Desktop, Downloads) finds it again.
                        if (sourceSeen != d.FullName) { sourceSeen = d.FullName; if (ReadSetting("source") != d.FullName) WriteSetting("source", d.FullName); }
                        return d.FullName;
                    }
                var known = ReadSetting("source");
                if (isSource(known)) return known;
                var mine = Path.Combine(Path.GetDirectoryName(dataRoot), "source", "cold-project-vi");
                return File.Exists(Path.Combine(mine, "Cargo.toml")) ? mine : null;
            }
        }

        // The edupode branch's source from GitHub's zip (no git needed) into %LOCALAPPDATA%\VIctory\source.
        bool EnsureSource()
        {
            if (SourceDir != null) return true;
            var to = Path.Combine(Path.GetDirectoryName(dataRoot), "source", "cold-project-vi");
            Log(L("The launcher is not inside the source folder: downloading the source (main branch)...", "O launcher nao esta dentro da pasta do codigo: a descarregar o codigo (ramo main)..."));
            return DownloadZipInto("https://github.com/felicienradio/cold-project-vi/archive/refs/heads/main.zip", to, L("the source", "o codigo"));
        }

        // A GitHub source zip (one folder inside) unpacked as `to`.
        bool DownloadZipInto(string url, string to, string what)
        {
            var zip = Path.Combine(Path.GetTempPath(), "vi-" + Guid.NewGuid().ToString("N") + ".zip");
            Progress(string.Format(L("Downloading {0}...", "A descarregar {0}..."), what), 0.02f);
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
                using (var web = new System.Net.WebClient()) { web.Headers["User-Agent"] = "Julias-Launcher"; web.DownloadFile(url, zip); }
                var tmp = to + ".new";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, tmp);
                var inner = Directory.GetDirectories(tmp).Single();
                if (Directory.Exists(to)) Directory.Delete(to, true);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                Directory.Move(inner, to);
                Directory.Delete(tmp, true);
                Log(string.Format(L("Downloaded {0} into {1}", "{0} descarregado para {1}"), what, to));
                return true;
            }
            catch (Exception e)
            {
                Log(string.Format(L("{0} could not be downloaded: {1}", "Nao deu para descarregar {0}: {1}"), what, e.Message));
                return false;
            }
            finally { try { File.Delete(zip); } catch (Exception) { } }
        }

        bool BuildBlocking()
        {
            if (!EnsureSource()) return false;
            return BuildIn(SourceDir, "VIctory", "build.log");
        }

        // Belobug's AMD fork at the commit the launcher knows, from GitHub's source zip (no git
        // needed), into engines\amd; then built like this repository.
        bool BuildAmdEngine()
        {
            var dir = AmdEngineDir;
            if (!File.Exists(Path.Combine(dir, "Cargo.toml")))
            {
                if (!DownloadZipInto(AmdRepo + "/archive/" + AmdCommit + ".zip", dir, L("Belobug's AMD engine", "o motor AMD do Belobug"))) return false;
                var example = Path.Combine(dir, "victory.example.toml");
                if (File.Exists(example) && !File.Exists(Path.Combine(dir, "victory.toml"))) File.Copy(example, Path.Combine(dir, "victory.toml"));
            }
            return BuildIn(dir, L("the AMD engine (Belobug)", "o motor AMD (Belobug)"), "build-amd.log");
        }

        bool BuildIn(string folder, string what, string logName)
        {
            var log = Path.Combine(dataRoot, "logs", logName);
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            int crates = 0;
            var psi = new ProcessStartInfo(CargoExe, "build --locked --profile diag -p victory --features diagnostics") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = folder };
            var cargoBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin");
            psi.EnvironmentVariables["PATH"] = cargoBin + ";" + Environment.GetEnvironmentVariable("PATH");
            // A storage folder on another drive (Storage.cs): the build goes there, not into the source folder.
            if (StorageRoot != null) psi.EnvironmentVariables["CARGO_TARGET_DIR"] = BuildTarget(folder);
            Log(string.Format(L("Building {0}... Log: ", "A compilar {0}... Log: "), what) + log);
            using (var writer = new StreamWriter(log, false))
            {
                Process p;
                try { p = Process.Start(psi); }
                catch (Exception e) { Log(L("Could not start cargo: ", "Nao deu para iniciar o cargo: ") + e.Message); return false; }
                DataReceivedEventHandler sink = (s, e) => {
                    if (e.Data == null) return;
                    lock (writer) writer.WriteLine(e.Data);
                    var m = Regex.Match(e.Data, @"^\s*Compiling (\S+)");
                    if (m.Success)
                    {
                        crates++;
                        Progress(string.Format(L("Building {2}: {0} ({1} parts)", "A compilar {2}: {0} ({1} partes)"), m.Groups[1].Value, crates, what), Math.Min(0.95f, crates / 330f));
                    }
                };
                p.OutputDataReceived += sink; p.ErrorDataReceived += sink;
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
                Log(p.ExitCode == 0 ? what + L(": built.", ": compilado.") : L("The build failed: see ", "A compilacao falhou: ve ") + log);
                BeginInvoke(new Action(() => { Refresh2(); ProbeGpu(); }));
                if (p.ExitCode == 0) EnsureAgility();
                if (p.ExitCode != 0)
                {
                    writer.Flush();
                    TellErrors(log, 0);
                    BeginInvoke(new Action(() => MessageBox.Show(this, L("The build failed. The reason is in the Activity box and in ", "A compilacao falhou. O motivo esta na caixa Atividade e em ") + log
                        + L("\r\n\r\nHelp > Send errors to Discord makes a package with it.", "\r\n\r\nAjuda > Enviar erros no Discord faz um pacote com isto."), "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                }
                return p.ExitCode == 0;
            }
        }

        bool PrepareBlocking()
        {
            string dir = null, game = null;
            Invoke(new Action(() => { SaveSettings(); dir = gameBox.Text.Trim(); game = GameExe; }));
            if (VictoryExe == null || !File.Exists(game)) { Log(L("Build VIctory and choose the game folder first.", "Compila o VIctory e escolhe a pasta do jogo primeiro.")); return false; }
            if (!RoomForCache()) return false;
            var log = Path.Combine(dataRoot, "logs", "prepare.log");
            long mark = 0;
            try { if (File.Exists(log)) mark = new FileInfo(log).Length; } catch (Exception) { }
            Log(L("Translating the game's shaders and preparing pipelines (about 5 minutes)... Log: ", "A traduzir os shaders do jogo e a preparar pipelines (cerca de 5 minutos)... Log: ") + log);
            Progress(L("Translating the game's shaders (about 5 minutes)...", "A traduzir os shaders do jogo (cerca de 5 minutos)..."), 0.05f);
            WriteSetting("shaders", "0");
            Stage(0, 0f, L("Shaders: starting...", "Shaders: a comecar..."), true);
            Stage(1, 0f, L("Pipelines: after the shaders", "Pipelines: depois dos shaders"), true);
            // `progress <read> of <total> MB, <n> shaders, <s> s` (victory shaders): the game's files read so far.
            int translated = -1;
            var p = Start("shaders \"" + dir + "\"", log, line => {
                var done = Regex.Match(line, @"^(\d+) shaders in the cache");
                if (done.Success) translated = int.Parse(done.Groups[1].Value);
                var m = Regex.Match(line, @"^progress (\d+) of (\d+) MB, (\d+) shaders, (\d+) s");
                if (!m.Success) return;
                float part = float.Parse(m.Groups[1].Value) / Math.Max(1f, float.Parse(m.Groups[2].Value));
                var text = string.Format(L("Shaders: {0:N0} translated · {1:0}% of the game's files · {2} s", "Shaders: {0:N0} traduzidos · {1:0}% dos ficheiros do jogo · {2} s"),
                    int.Parse(m.Groups[3].Value), part * 100, m.Groups[4].Value);
                Stage(0, part, text, true);
                Progress(text, 0.05f + 0.55f * part);
            });
            p.WaitForExit();
            Stage(0, 1f, p.ExitCode == 0 ? L("Shaders: done", "Shaders: feito") : L("Shaders: failed (see the errors below)", "Shaders: falhou (ve os erros em baixo)"), true);
            if (p.ExitCode != 0)
            {
                Log(string.Format(L("Translating the shaders failed (code {0}).", "A traducao dos shaders falhou (codigo {0})."), p.ExitCode));
                TellErrors(log, mark);
                return false;
            }
            // A dump gives about 25,000 shaders; a dozen means the game's files were not read (a folder of other
            // files, or a damaged dump). It used to say "Shaders ready" anyway (a player's report, 27/11/2026).
            if (translated >= 0 && translated < 1000)
            {
                Stage(0, 1f, L("Shaders: failed (see the errors below)", "Shaders: falhou (ve os erros em baixo)"), false);
                Log(string.Format(L("Only {0:N0} shaders were translated (a complete dump gives about 25,000): this folder does not hold the game's files, or the dump is damaged.",
                    "So {0:N0} shaders foram traduzidos (um dump completo da cerca de 25 000): esta pasta nao tem os ficheiros do jogo, ou o dump esta estragado.",
                    "Seulement {0:N0} shaders ont été traduits (un dump complet en donne environ 25 000) : ce dossier ne contient pas les fichiers du jeu, ou le dump est abîmé."), translated));
                if (MissingDumpFiles(dir).Length > 0) Log(MissingDumpText(dir));
                else Log(L("Download or extract the dump again, into a folder of its own.", "Volta a descarregar ou extrair o dump, para uma pasta so dele.", "Télécharge ou extrais le dump à nouveau, dans un dossier à lui."));
                TellErrors(log, mark);
                return false;
            }
            Progress(L("Preparing pipelines...", "A preparar pipelines..."), 0.6f);
            var w = Start("warm --used", log, line => {
                var m = Regex.Match(line, @"^(\d+) of (\d+), (\d+) s");
                if (m.Success)
                {
                    int n = int.Parse(m.Groups[1].Value), of = int.Parse(m.Groups[2].Value);
                    Progress(string.Format(L("Preparing pipelines: {0} of {1}", "A preparar pipelines: {0} de {1}"), n, of), 0.6f + 0.4f * n / Math.Max(1, of));
                    Stage(1, (float)n / Math.Max(1, of), string.Format(L("Pipelines: {0:N0} of {1:N0} made · {2} s", "Pipelines: {0:N0} de {1:N0} feitas · {2} s"), n, of, m.Groups[3].Value), true);
                }
                var none = Regex.Match(line, @"; 0 to make");
                if (none.Success) Stage(1, 1f, L("Pipelines: all ready", "Pipelines: todas prontas"), true);
            });
            w.WaitForExit();
            Stage(1, 1f, L("Pipelines: done", "Pipelines: feito"), false);
            Stage(0, 1f, L("Shaders: done", "Shaders: feito"), false);
            if (w.ExitCode != 0)
            {
                // The shaders are there; pipelines are made again before each game (and after it).
                Log(string.Format(L("Preparing pipelines stopped (code {0}); the shaders are ready, PLAY makes the pipelines.", "A preparacao das pipelines parou (codigo {0}); os shaders estao prontos, o JOGAR faz as pipelines."), w.ExitCode));
                TellErrors(log, mark);
            }
            Log(L("Shaders ready. You can press PLAY.", "Shaders prontos. Podes carregar em JOGAR."));
            WriteSetting("shaders", "1");
            WriteSetting("shaders_driver", driverVersion);
            return true;
        }

        // The shader cache's drive needs room: a full disk leaves half a cache behind.
        bool RoomForCache()
        {
            const long need = 12L << 30;
            try
            {
                Directory.CreateDirectory(ActiveCacheDir);
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(ActiveCacheDir)));
                if (drive.AvailableFreeSpace >= need) return true;
                Log(string.Format(L("Only {0:0.0} GB free on {1}: the shaders need about 12 GB. Free some space and try again.",
                    "So {0:0.0} GB livres em {1}: os shaders precisam de cerca de 12 GB. Liberta espaco e tenta de novo."), drive.AvailableFreeSpace / 1073741824.0, drive.Name));
                return false;
            }
            catch (Exception) { return true; }
        }

        // The lines of a log written since `mark` that say what went wrong (at most 8).
        void TellErrors(string log, long mark)
        {
            try
            {
                using (var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    f.Seek(Math.Min(mark, f.Length), SeekOrigin.Begin);
                    var lines = new StreamReader(f).ReadToEnd().Split('\n')
                        .Select(s => s.TrimEnd('\r'))
                        .Where(s => Regex.IsMatch(s, @"(?i)\b(error|panicked|failed|denied|not found|no space)\b"))
                        .Reverse().Take(8).Reverse().ToList();
                    foreach (var line in lines) Log("   " + (line.Length > 220 ? line.Substring(0, 220) + "..." : line));
                    if (lines.Count == 0) Log(L("   (no error lines; the whole log: ", "   (sem linhas de erro; o log todo: ") + log + ")");
                }
            }
            catch (Exception) { }
        }

        // Ready: a finished preparation (launcher.ini's shaders=1, written only when it ended well)
        // whose cache is still there, or a cache as large as one (made by another way of starting VIctory).
        bool ShadersReady()
        {
            // The game has ~25,000 shaders: a handful means the preparation saw almost nothing (a folder
            // with only the executable showed 12 as "ready", 27/11/2026). Counted folder by folder: one walk of
            // the whole tree ended at the first entry the game or the warm-up was writing or renaming, and the
            // step showed "not prepared" (and Fix it) on PCs with 25,000+ shaders.
            return CountUpTo(Path.Combine(ActiveCacheDir, "shaders"), 1000) >= 1000;
        }

        static int CountUpTo(string dir, int most)
        {
            int n = 0;
            var pending = new Stack<string>();
            pending.Push(dir);
            while (pending.Count > 0 && n < most)
            {
                var d = pending.Pop();
                try { foreach (var f in Directory.EnumerateFiles(d)) if (++n >= most) break; } catch (Exception) { }
                try { foreach (var s in Directory.EnumerateDirectories(d)) pending.Push(s); } catch (Exception) { }
            }
            return n;
        }

        bool FindGame()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var places = new List<string> {
                Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(home, "Games"),
            };
            foreach (var d in DriveInfo.GetDrives())
                if (d.DriveType == DriveType.Fixed && d.IsReady) { places.Add(d.RootDirectory.FullName); places.Add(Path.Combine(d.RootDirectory.FullName, "Games")); }
            Progress(L("Looking for game_scarlett*.exe...", "A procurar o game_scarlett*.exe..."), 0.3f);
            // A complete dump first (the game's files beside the executable), anywhere in these places; a lone
            // executable only when no complete dump is found (a player's Downloads held the .exe among other files).
            string loose = null;
            foreach (var place in places.Distinct())
            {
                var found = Search(place, 4, ref loose);
                if (found != null) return UseGameFolder(Path.GetDirectoryName(found));
            }
            if (loose != null)
            {
                UseGameFolder(Path.GetDirectoryName(loose));
                Log(MissingDumpText(Path.GetDirectoryName(loose)));
                return false;
            }
            Log(L("The game was not found: choose its folder with Browse on the Play page.", "O jogo nao foi encontrado: escolhe a pasta com Procurar na pagina Jogar."));
            return false;
        }

        bool UseGameFolder(string dir)
        {
            Invoke(new Action(() => { gameBox.Text = dir; SaveSettings(); }));
            Log(L("Game found: ", "Jogo encontrado: ") + dir);
            return true;
        }

        // The game's own files that every dump has beside game_scarlett_master.exe. Without them the folder holds
        // only the executable (or it sits among other files): the shaders do not prepare (12 of ~25,000) and the
        // game ends at once.
        static readonly string[] DumpFiles = { "shaders.rpf", "levels.rpf" };

        static string[] MissingDumpFiles(string dir)
        {
            try { return DumpFiles.Where(f => !File.Exists(Path.Combine(dir, f))).ToArray(); }
            catch (Exception) { return DumpFiles; }
        }

        static string MissingDumpText(string dir)
        {
            return string.Format(L("The game's files are not beside the executable in {0} ({1} missing). Move the whole dump (game_scarlett_master.exe with its .rpf files and its data and pack folders) into a folder of its own, for example C:\\Games\\GTA6, and choose that folder on the Play page.",
                "Os ficheiros do jogo nao estao ao lado do executavel em {0} (falta {1}). Move o dump inteiro (o game_scarlett_master.exe com os ficheiros .rpf e as pastas data e pack) para uma pasta so dele, por exemplo C:\\Games\\GTA6, e escolhe essa pasta na pagina Jogar.",
                "Les fichiers du jeu ne sont pas à côté de l'exécutable dans {0} ({1} manquant). Déplace tout le dump (game_scarlett_master.exe avec ses fichiers .rpf et ses dossiers data et pack) dans un dossier à lui, par exemple C:\\Games\\GTA6, et choisis ce dossier dans la page Jouer."),
                dir, string.Join(", ", MissingDumpFiles(dir)));
        }

        // The first folder under dir with a game_scarlett*.exe and the game's files beside it; loose is the first
        // executable seen without them.
        static string Search(string dir, int depth, ref string loose)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                var f = Path.Combine(dir, "game_scarlett_master.exe");
                // Dumps with another build's name (game_scarlett_*.exe): the Play page lets the player pick.
                var other = GameExes(dir);
                var exe = File.Exists(f) ? f : other.Length > 0 ? Path.Combine(dir, other[0]) : null;
                if (exe != null)
                {
                    if (MissingDumpFiles(dir).Length == 0) return exe;
                    if (loose == null) loose = exe;
                }
                if (depth == 0) return null;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith("$") || name.Equals("Windows", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Program", StringComparison.OrdinalIgnoreCase) || name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;
                    var r = Search(sub, depth - 1, ref loose);
                    if (r != null) return r;
                }
            }
            catch (Exception) { }
            return null;
        }

        static bool BestPower()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes"))
                {
                    var v = k == null ? null : k.GetValue("ActiveOverlayAcPowerScheme") as string;
                    return v != null && v.Equals("ded574b5-45a0-4f42-8737-46345c09c238", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception) { return false; }
        }

        // Virtual memory the game can grow into: managed by Windows on all drives ("?:\pagefile.sys"),
        // system-managed size on a drive ("C:\pagefile.sys 0 0"), or fixed sizes of 32 GB and more.
        // (The old check only took the first: a system-managed C: showed as off.)
        public static bool PagefileOk()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
                {
                    var v = k == null ? null : k.GetValue("PagingFiles") as string[];
                    if (v == null) return false;
                    long most = 0;
                    foreach (var entry in v)
                    {
                        var parts = entry.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0) continue;
                        if (parts[0].StartsWith("?:", StringComparison.Ordinal)) return true;
                        if (parts.Length == 1 || (parts.Length >= 3 && parts[1] == "0" && parts[2] == "0")) return true;
                        long max;
                        if (parts.Length >= 3 && long.TryParse(parts[2], out max)) most += max;
                    }
                    return most >= 32768;
                }
            }
            catch (Exception) { return false; }
        }

        // The script both the Setup step and the installer run as administrator: Windows manages the
        // page file on every drive. Set-CimInstance fails on some PCs ("Generic failure"), so the
        // registry value it stands for is written too.
        // Done by the launcher itself, run as administrator (`--pagefile`), with no PowerShell: an encoded
        // PowerShell command run hidden is what antivirus programs look for in malware, and Windows flagged
        // the launcher for it (27/11/2026). Set through WMI, and the registry value it stands for is written
        // too (WMI fails on some PCs with "Generic failure").
        public static bool SetManagedPagefile()
        {
            bool done = false;
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        o["AutomaticManagedPagefile"] = true;
                        o.Put();
                        done = true;
                    }
            }
            catch (Exception) { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true))
                    if (k != null) { k.SetValue("PagingFiles", new[] { @"?:\pagefile.sys" }, RegistryValueKind.MultiString); done = true; }
            }
            catch (Exception) { }
            return done;
        }

        bool FixPagefile()
        {
            bool ok = Elevated(Application.ExecutablePath, "--pagefile");
            if (ok && PagefileOk())
            {
                Log(L("Virtual memory is now managed by Windows. Restart Windows for it to count.", "A memoria virtual passou a ser gerida pelo Windows. Reinicia o Windows para contar."));
                return true;
            }
            Log(L("Virtual memory could not be set. By hand: Windows key > 'Advanced system settings' > Performance: Settings > Advanced > Virtual memory: Change > tick 'Automatically manage paging file size for all drives' > OK, then restart.",
                  "A memoria virtual nao ficou definida. A mao: tecla Windows > 'Definicoes avancadas do sistema' > Desempenho: Definicoes > Avancadas > Memoria virtual: Alterar > marca 'Gerir automaticamente o tamanho do ficheiro de paginacao para todas as unidades' > OK e reinicia."));
            try { Process.Start("SystemPropertiesPerformance.exe"); } catch (Exception) { }
            return false;
        }

        // Runs a program with administrator rights (the UAC prompt) and waits; false if refused or failed.
        bool Elevated(string file, string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(file, args) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch (Win32Exception) { Log(L("Administrator rights were not given.", "Nao foram dadas permissoes de administrador.")); return false; }
        }

        static int RunWait(string file, string args, bool window)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = !window });
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (Exception) { return -1; }
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch (Exception) { }
        }

        void WriteSetting(string key, string value)
        {
            try
            {
                var lines = File.Exists(settingsFile) ? File.ReadAllLines(settingsFile).Where(l => !l.StartsWith(key + "=")).ToList() : new List<string>();
                lines.Add(key + "=" + value);
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                File.WriteAllLines(settingsFile, lines);
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- Resizable BAR
        // Whether the card can have Resizable BAR at all: GTX 10/16 and RTX 20 cannot; early RTX 30
        // cards need NVIDIA's firmware update first.
        int RebarCardSupport()
        {
            // GTX of any series, RTX 2050-2080 (not the "RTX 2000 Ada" pro card), the TITANs before Ampere, Quadro
            // before RTX, MX laptop chips; Radeon RX 400/500 (Polaris), Vega and older.
            var n = probeAdapter.Length > 0 ? probeAdapter : gpuName;
            if (Regex.IsMatch(n, @"\bGTX\b|RTX\s*20[5-8]0|TITAN\s*(X|V|RTX)|\bMX\s*\d|Quadro\s+[KMP]\d|\bRX\s*[45][5-9]0\b|\bVega\b|\bR[79]\s", RegexOptions.IgnoreCase)) return 0;
            if (Regex.IsMatch(n, @"RTX\s*30\d\d", RegexOptions.IgnoreCase)) return 2;
            return 1;
        }

        // Resizable BAR as a warning ("Running without", the Fix it button): only when the player can switch it on.
        // Not on cards that cannot have it (a GTX 1650 was told to fix it on every start), not when the BIOS has it on
        // and only Windows' D3D12 does not offer it to games (Windows 10, older Windows 11: nothing to switch), and not
        // before anything was read (a Radeon showed "ReBAR Off" until `victory gpu` answered, then the warning went).
        bool RebarMissing()
        {
            if (rebar || rebarBios || ReadSetting("rebar_ack") == "1" || integratedOnly || RebarCardSupport() == 0) return false;
            // NVIDIA: nvidia-smi's BAR1 was read and says 256 MB, and D3D12 does not offer it either.
            if (hasNvidia && barRead) return rebarHeap != 1;
            // Other cards: D3D12 knows GPU upload heaps (a Windows new enough to say) and says no.
            return rebarHeap == 0 && probeOptions16;
        }

        // The menus by motherboard maker (from the board's own name in Windows).
        string[] RebarSteps(string maker)
        {
            var m = (maker ?? "").ToUpperInvariant();
            if (m.Contains("ASUS")) return new[] {
                L("ASUS: Advanced > PCI Subsystem Settings > Above 4G Decoding: Enabled, then Re-Size BAR Support: Auto/Enabled.", "ASUS: Advanced > PCI Subsystem Settings > Above 4G Decoding: Enabled, depois Re-Size BAR Support: Auto/Enabled."),
                L("Boot > CSM (Compatibility Support Module) > Launch CSM: Disabled.", "Boot > CSM (Compatibility Support Module) > Launch CSM: Disabled.") };
            if (m.Contains("MICRO-STAR") || m.Contains("MSI")) return new[] {
                L("MSI: Settings > Advanced > PCI Subsystem Settings > Above 4G memory/Crypto Currency mining: Enabled, then Re-Size BAR Support: Enabled", "MSI: Settings > Advanced > PCI Subsystem Settings > Above 4G memory/Crypto Currency mining: Enabled, depois Re-Size BAR Support: Enabled"),
                L("(newer boards: the 'Smart Access Memory / Re-Size BAR' button on the EZ Mode page). Turn CSM off under Boot.", "(placas recentes: o botao 'Smart Access Memory / Re-Size BAR' na pagina EZ Mode). Desliga o CSM em Boot.") };
            if (m.Contains("GIGABYTE")) return new[] {
                L("Gigabyte/AORUS: Settings > IO Ports > Above 4G Decoding: Enabled, then Re-Size BAR Support: Auto.", "Gigabyte/AORUS: Settings > IO Ports > Above 4G Decoding: Enabled, depois Re-Size BAR Support: Auto."),
                L("Boot > CSM Support: Disabled.", "Boot > CSM Support: Disabled.") };
            if (m.Contains("ASROCK")) return new[] {
                L("ASRock: Advanced > PCI Configuration > Above 4G Decoding: Enabled, then Re-Size BAR Support: Enabled.", "ASRock: Advanced > PCI Configuration > Above 4G Decoding: Enabled, depois Re-Size BAR Support: Enabled."),
                L("Boot > CSM (Compatibility Support Module): Disabled.", "Boot > CSM (Compatibility Support Module): Disabled.") };
            return new[] {
                L("Look for 'Above 4G Decoding' and 'Re-Size BAR' (or 'Resizable BAR', 'Smart Access Memory') under Advanced > PCI, and turn CSM off under Boot.",
                  "Procura 'Above 4G Decoding' e 'Re-Size BAR' (ou 'Resizable BAR', 'Smart Access Memory') em Advanced > PCI, e desliga o CSM em Boot."),
                L("Laptops (Acer, Lenovo, HP, Dell...): usually no switch; it comes with the maker's BIOS update for your model.", "Portateis (Acer, Lenovo, HP, Dell...): normalmente nao ha opcao; vem com a atualizacao de BIOS do fabricante para o teu modelo.") };
        }

        void ShowRebarGuide()
        {
            bool uefi = Uefi();
            string maker = hw != null ? hw.BoardMaker : "", board = hw != null ? hw.Board : "";
            if (hw == null)
                try { maker = Wmi("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer"); board = Wmi("SELECT Product FROM Win32_BaseBoard", "Product"); } catch (Exception) { }
            int card = RebarCardSupport();
            var f = new Form { Text = "Resizable BAR", ClientSize = new Size(820, 660), BackColor = Theme.Back, ForeColor = Theme.Text, Font = Theme.Ui(9.75f), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Icon = Icon };
            f.Controls.Add(new Heading(L("Resizable BAR", "Resizable BAR")) { Location = new Point(20, 16), Width = 780 });
            Func<bool, string, string> line = (ok, what) => (ok ? "✓  " : "✗  ") + what;
            var lines = new List<string> {
                line(rebar, rebarHeap == 1 ? L("The game gets it: D3D12 offers mapped video memory.", "O jogo tem-no: o D3D12 oferece memoria de video mapeada.")
                    : rebarHeap == 0 && rebarBios ? L("On in the BIOS and on the card, but Windows does not offer it to games (see 3).", "Ligado na BIOS e na placa, mas o Windows nao o oferece aos jogos (ve o 3).")
                    : L("The game does not get it.", "O jogo nao o tem.")),
                line(rebarBios || !hasNvidia, hasNvidia ? (rebarBios ? L("The card reports a large BAR (nvidia-smi: BAR1 of 1 GB or more).", "A placa indica um BAR grande (nvidia-smi: BAR1 de 1 GB ou mais).") : L("The card reports a 256 MB BAR: off in the BIOS, or the card's firmware lacks it.", "A placa indica um BAR de 256 MB: desligado na BIOS, ou o firmware da placa nao o tem.")) : L("Not an NVIDIA card: only the game's side is checked.", "Nao e uma placa NVIDIA: so o lado do jogo e verificado.")),
                line(uefi, uefi ? L("The PC starts in UEFI mode (needed).", "O PC arranca em modo UEFI (necessario).") : L("The PC starts in Legacy/CSM mode: Resizable BAR needs UEFI (convert the disk with mbr2gpt first).", "O PC arranca em Legacy/CSM: o Resizable BAR precisa de UEFI (converte o disco com o mbr2gpt primeiro).")),
                line(card != 0, card == 0 ? string.Format(L("{0} cannot use Resizable BAR (only RTX 30 and newer, RX 5000 and newer).", "A {0} nao suporta Resizable BAR (so RTX 30 ou mais recente, RX 5000 ou mais recente)."), gpuName)
                    : card == 2 ? string.Format(L("{0}: early RTX 30 cards need NVIDIA's Resizable BAR firmware update (see 2).", "{0}: as primeiras RTX 30 precisam da atualizacao de firmware Resizable BAR da NVIDIA (ve o 2)."), gpuName)
                    : string.Format(L("{0} supports it.", "A {0} suporta-o."), gpuName)),
            };
            var state = new Label { Location = new Point(20, 50), Size = new Size(780, 92), Font = Theme.Mono(8.75f), BackColor = Theme.Back, ForeColor = rebar ? Theme.Good : Theme.Warn, Text = string.Join("\r\n", lines.ToArray()) };
            f.Controls.Add(state);
            var steps = new List<string>();
            steps.Add(L("1.  Motherboard: ", "1.  Placa-mae: ") + (maker + " " + board).Trim() + L(". Update its BIOS first, then in the BIOS:", ". Atualiza a BIOS primeiro, depois na BIOS:"));
            foreach (var s in RebarSteps(maker)) steps.Add("       " + s);
            steps.Add(L("       Save and exit (usually F10). Back in Windows, press 'Check again'.", "       Guarda e sai (normalmente F10). De volta ao Windows, carrega em 'Verificar de novo'."));
            steps.Add(L("2.  Graphics card: update the driver. RTX 3060/3060 Ti/3070/3080/3090 bought before April 2021 may need NVIDIA's", "2.  Placa grafica: atualiza o driver. RTX 3060/3060 Ti/3070/3080/3090 compradas antes de abril de 2021 podem precisar da"));
            steps.Add(L("       Resizable BAR firmware update (button below; from your card maker's site for some models).", "       atualizacao de firmware Resizable BAR da NVIDIA (botao em baixo; no site do fabricante da placa para alguns modelos)."));
            steps.Add(L("3.  Windows: games get it through D3D12's GPU upload heaps: Windows 11 24H2 or newer and a recent driver.", "3.  Windows: os jogos tem-no pelos GPU upload heaps do D3D12: Windows 11 24H2 ou mais recente e um driver recente."));
            steps.Add(L("       On but not offered: Windows Update (to 24H2+) and the newest NVIDIA/AMD driver, then restart.", "       Ligado mas nao oferecido: Windows Update (para 24H2+) e o driver NVIDIA/AMD mais recente, depois reinicia."));
            steps.Add("");
            steps.Add(L("If you cannot turn it on (older card, laptop without the option, Windows 10):", "Se nao o conseguires ligar (placa antiga, portatil sem a opcao, Windows 10):"));
            steps.Add(L("   •  The game still works: the launcher leaves the direct video-memory writes off on its own.", "   •  O jogo funciona na mesma: o launcher deixa a escrita direta na VRAM desligada sozinho."));
            steps.Add(L("   •  Pick Modest PC or Ultra Low, keep 'Keep the most video memory' on, and close apps on the Performance page:", "   •  Escolhe PC modesto ou Ultra baixo, deixa 'Guardar o maximo de VRAM' ligado e fecha apps na pagina Desempenho:"));
            steps.Add(L("      without Resizable BAR the copies to the card are slower, so less to copy helps most.", "      sem Resizable BAR as copias para a placa sao mais lentas, por isso copiar menos e o que mais ajuda."));
            steps.Add(L("   •  'I can't turn it on' stops the launcher warning you about it.", "   •  'Nao o consigo ligar' faz o launcher deixar de te avisar."));
            var text = new Label { Location = new Point(20, 150), Size = new Size(780, 430), ForeColor = Theme.Text, BackColor = Theme.Back, Text = string.Join("\r\n", steps.ToArray()) };
            f.Controls.Add(text);
            int by = 600;
            var bios = new Pill { Main = true, Text = L("Restart into the BIOS", "Reiniciar para a BIOS"), Location = new Point(20, by), Size = new Size(200, 40), Enabled = uefi && !rebar };
            bios.Click += (s, e) => {
                var ask = L("Save your work first. The PC restarts in 5 seconds and opens its BIOS/UEFI settings. Restart now?", "Guarda o teu trabalho primeiro. O PC reinicia em 5 segundos e abre as definicoes da BIOS/UEFI. Reiniciar agora?");
                if (MessageBox.Show(f, ask, "Resizable BAR", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    new Thread(() => Elevated("shutdown", "/r /fw /t 5")) { IsBackground = true }.Start();
            };
            f.Controls.Add(bios);
            var vbios = new Pill { Text = L("NVIDIA firmware update", "Firmware da NVIDIA"), Location = new Point(230, by + 3), Size = new Size(180, 34), Visible = card == 2 };
            vbios.Click += (s, e) => OpenUrl("https://www.nvidia.com/en-us/geforce/news/geforce-rtx-30-series-resizable-bar-support/");
            f.Controls.Add(vbios);
            var again = new Pill { Text = L("Check again", "Verificar de novo"), Location = new Point(card == 2 ? 420 : 230, by + 3), Size = new Size(150, 34) };
            again.Click += (s, e) => { f.Close(); DetectSystem(); ProbeGpu(); CheckAll(); };
            f.Controls.Add(again);
            var cant = new Pill { Text = L("I can't turn it on", "Nao o consigo ligar"), Location = new Point(card == 2 ? 580 : 390, by + 3), Size = new Size(170, 34), Visible = !rebar };
            cant.Click += (s, e) => { WriteSetting("rebar_ack", "1"); Log(L("Resizable BAR: no more warnings. The game runs without it.", "Resizable BAR: sem mais avisos. O jogo corre sem ele.")); f.Close(); CheckAll(); Refresh2(); };
            f.Controls.Add(cant);
            f.ShowDialog(this);
        }
    }

    // The installer's one administrator pass (installer\VI.iss runs `Julia's Launcher.exe --admin-setup
    // <install folder>`): the Windows settings the Setup page would ask for one by one.
    static class AdminTasks
    {
        public static int Run(string[] args)
        {
            var dir = args.SkipWhile(a => a != "--admin-setup").Skip(1).FirstOrDefault() ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var log = new List<string>();
            // Best performance power mode.
            try
            {
                var p = Process.Start(new ProcessStartInfo("powercfg", "/overlaysetactive ded574b5-45a0-4f42-8737-46345c09c238") { UseShellExecute = false, CreateNoWindow = true });
                p.WaitForExit(60000);
                log.Add("power: " + (p.ExitCode == 0 ? "ok" : "code " + p.ExitCode));
            }
            catch (Exception e) { log.Add("power: " + e.Message); }
            // Virtual memory managed by Windows (counts after a restart).
            if (!LauncherForm.PagefileOk()) log.Add("virtual memory: " + (LauncherForm.SetManagedPagefile() ? "ok" : "failed"));
            // (No Defender exception any more: the launcher never changes antivirus settings.)
            // The high-performance graphics card for victory.exe.
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                    foreach (var exe in new[] { Path.Combine(dir, "victory.exe"), Path.Combine(dir, "engines", "amd", "target", "diag", "victory.exe") })
                        k.SetValue(exe, "GpuPreference=2;");
                log.Add("gpu preference: ok");
            }
            catch (Exception e) { log.Add("gpu preference: " + e.Message); }
            // Smooth Motion in the driver's profile (RTX 40/50; ignored elsewhere).
            try { var err = SmoothMotion.Set(true); log.Add("smooth motion: " + (err ?? "ok")); } catch (Exception e) { log.Add("smooth motion: " + e.Message); }
            // Resizable BAR in victory.exe's driver profile (counts only where the BIOS has it on).
            try { var err = SmoothMotion.SetRebar(true); log.Add("rbar profile: " + (err ?? "ok")); } catch (Exception e) { log.Add("rbar profile: " + e.Message); }
            try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "vi-admin-setup.txt"), log); } catch (Exception) { }
            return 0;
        }
    }
}
