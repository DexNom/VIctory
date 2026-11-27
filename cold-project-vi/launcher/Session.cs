// Julia's Launcher: the game session (what keeps PLAY on or off, a stuck session closed) and the
// choice of the game's executable when a dump has more than one.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    enum GameState { None, Running, Stuck }

    partial class LauncherForm
    {
        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hwnd);

        // The game this launcher started, and the pipelines of its session made after it closed.
        Process gameProc, afterWarm;
        // When the game this launcher started lost its window (its process still there).
        DateTime? windowGone;
        bool gameHadWindow;

        // What is running now. A game is a victory.exe with a window, or the one this launcher
        // started; tools (replay, warm, shader translation) are victory.exe too, with no window.
        // Stuck: a window that has not answered for 5 seconds (a crash, an error dialog), or the
        // game this launcher started still there 15 seconds after its window closed.
        GameState State()
        {
            var state = GameState.None;
            foreach (var p in Process.GetProcessesByName("victory"))
            {
                try
                {
                    bool ours = gameProc != null && !gameProc.HasExited && p.Id == gameProc.Id;
                    var window = p.MainWindowHandle;
                    if (ours)
                    {
                        if (window != IntPtr.Zero) { gameHadWindow = true; windowGone = null; }
                        else if (gameHadWindow && windowGone == null) windowGone = DateTime.Now;
                    }
                    if (!ours && window == IntPtr.Zero) continue;
                    bool stuck = (window != IntPtr.Zero && IsHungAppWindow(window))
                        || (ours && windowGone != null && (DateTime.Now - windowGone.Value).TotalSeconds > 15);
                    if (stuck) return GameState.Stuck;
                    state = GameState.Running;
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return state;
        }

        // Ends every game (and what it started) and the pipelines being made after a session.
        void CloseGames()
        {
            var ids = new List<int>();
            foreach (var p in Process.GetProcessesByName("victory"))
            {
                try
                {
                    bool ours = gameProc != null && !gameProc.HasExited && p.Id == gameProc.Id;
                    bool tool = afterWarm != null && !afterWarm.HasExited && p.Id == afterWarm.Id;
                    if (ours || tool || p.MainWindowHandle != IntPtr.Zero) ids.Add(p.Id);
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            foreach (var id in ids)
            {
                RunWait("taskkill", "/PID " + id + " /T /F", false);
                Log(string.Format(L("Closed VIctory (process {0}).", "VIctory fechado (processo {0})."), id));
            }
            gameProc = null; afterWarm = null; windowGone = null; gameHadWindow = false;
        }

        // ---------------------------------------------------------------- what is ready
        // Checked on a worker thread every few seconds; the window shows the result (Refresh2).
        volatile string exeProblem;
        volatile int shaderFiles;
        volatile bool pagefileOk = true, powerOk = true, gpuPrefOk = true, shadersOk = true;
        bool warnedThisSession;

        void StartChecks()
        {
            new Thread(() => {
                while (true)
                {
                    try { RunChecks(); } catch (Exception) { }
                    try { BeginInvoke(new Action(Refresh2)); } catch (Exception) { return; }
                    Thread.Sleep(8000);
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        void RunChecks()
        {
            string game = null;
            Invoke(new Action(() => game = GameExe));
            exeProblem = ExeProblem(game);
            // Folder by folder (Setup.cs, CountUpTo): one entry renamed mid-walk no longer makes it 0.
            shaderFiles = CountUpTo(Path.Combine(ActiveCacheDir, "shaders"), int.MaxValue);
            shadersOk = ShadersReady();
            pagefileOk = PagefileOk();
            powerOk = BestPower() && SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline;
            gpuPrefOk = GpuPreferenceOk();
        }

        // Why the game's executable cannot be run, or null: a file that stopped downloading, a
        // placeholder, an encrypted package (VIctory's "malformed image: Could not read file magic").
        string ExeProblem(string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return null;
                var info = new FileInfo(path);
                var head = new byte[2];
                int got;
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) got = f.Read(head, 0, 2);
                if (info.Length < (1L << 20) || got < 2 || head[0] != (byte)'M' || head[1] != (byte)'Z')
                    return string.Format(L("{0} is not a working program ({1:N0} bytes): the dump is incomplete or still encrypted. Download or extract it again.",
                        "{0} nao e um programa que funcione ({1:N0} bytes): o dump esta incompleto ou ainda encriptado. Volta a descarregar ou extrair."), Path.GetFileName(path), info.Length);
                // Only the executable, not the game's data (the whole dump is ~100 GB): the game ends at once
                // with code 1 (an RTX 3060 player's "Desktop\EXE" folder held 122 MB, 27/11/2026).
                long seen = 0;
                foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(path), "*", SearchOption.AllDirectories))
                {
                    try { seen += new FileInfo(f).Length; } catch (Exception) { }
                    if (seen >= (20L << 30)) break;
                }
                if (seen < (20L << 30))
                    return string.Format(L("This game folder holds only {0:0.0} GB: the game's data is not in it (the whole dump is about 100 GB). Choose the folder of the complete dump, with all its files beside game_scarlett_master.exe.",
                        "Esta pasta do jogo so tem {0:0.0} GB: os dados do jogo nao estao nela (o dump completo tem cerca de 100 GB). Escolhe a pasta do dump completo, com todos os ficheiros ao lado do game_scarlett_master.exe."), seen / 1073741824.0);
                // Big enough, but the game's own files are not beside the executable (it sits in Downloads among
                // other files): the shaders do not prepare and the game cannot start.
                if (MissingDumpFiles(Path.GetDirectoryName(path)).Length > 0) return MissingDumpText(Path.GetDirectoryName(path));
                // Empty program libraries beside it end the game the same way.
                var empty = Directory.GetFiles(Path.GetDirectoryName(path), "*.dll").Where(d => new FileInfo(d).Length < 64).Select(Path.GetFileName).Take(3).ToArray();
                if (empty.Length > 0)
                    return string.Format(L("The dump has empty files ({0}): download or extract it again.", "O dump tem ficheiros vazios ({0}): volta a descarregar ou extrair."), string.Join(", ", empty));
            }
            catch (Exception) { }
            return null;
        }

        // What the game would run without, in words for the warning.
        List<string> Missing()
        {
            var m = new List<string>();
            if (integratedOnly) m.Add(L("a graphics card with 4 GB or more (only integrated graphics found)", "uma placa grafica com 4 GB ou mais (so ha grafica integrada)", "une carte graphique de 4 Go ou plus (seulement un graphique intégré)"));
            if (!pagefileOk) m.Add(L("virtual memory managed by Windows", "memoria virtual gerida pelo Windows"));
            if (!powerOk) m.Add(L("best performance power (plugged in)", "energia em melhor desempenho (ligado a corrente)"));
            if (RebarMissing()) m.Add("Resizable BAR");
            if (!gpuPrefOk) m.Add(L("the dedicated graphics card for VIctory", "a placa grafica dedicada para o VIctory"));
            if (!shadersOk) m.Add(L("prepared shaders", "shaders preparados"));
            return m;
        }

        // ---------------------------------------------------------------- memory, and a game that cannot start
        // What Windows can still promise (RAM plus virtual memory not yet committed), in GB.
        static double CommitFreeGb()
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT FreeVirtualMemory FROM Win32_OperatingSystem"))
                    foreach (System.Management.ManagementObject o in s.Get())
                        return Convert.ToDouble(o["FreeVirtualMemory"]) / (1024.0 * 1024.0);
            }
            catch (Exception) { }
            return -1;
        }

        // How much more Windows can promise by growing a page file it manages itself ("?:\pagefile.sys"
        // or "C:\pagefile.sys 0 0"), in GB: it grows on demand up to 3x the RAM, an eighth of the drive
        // and what the drive has free. FreeVirtualMemory leaves that out, so with a managed page file the
        // warning came at nearly every PLAY on a PC with 18 GB of RAM free (27/11/2026).
        double PagefileGrowthGb()
        {
            try
            {
                if (!PagefileOk()) return 0;
                double allocatedMb = 0;
                string drive = "C:\\";
                using (var s = new System.Management.ManagementObjectSearcher("SELECT Name, AllocatedBaseSize FROM Win32_PageFileUsage"))
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        allocatedMb += Convert.ToDouble(o["AllocatedBaseSize"]);
                        var name = Convert.ToString(o["Name"]);
                        if (!string.IsNullOrEmpty(name) && name.Length >= 3) drive = name.Substring(0, 3);
                    }
                var d = new DriveInfo(drive);
                double gb = 1024.0 * 1024 * 1024;
                double most = Math.Min(Math.Max(3 * ramGb, 4), d.TotalSize / 8.0 / gb);
                double byDisk = allocatedMb / 1024.0 + Math.Max(0, d.AvailableFreeSpace / gb - 2);
                return Math.Max(0, Math.Min(most, byDisk) - allocatedMb / 1024.0);
            }
            catch (Exception) { return 0; }
        }

        // The game reserves about 24 GB as it starts: with less to promise, D3D12 refuses the device
        // ("not enough memory resources", 0x8007000E) and the window stays white.
        const double GameCommitGb = 24;

        bool EnoughMemory()
        {
            var now = CommitFreeGb();
            if (now < 0 || now >= GameCommitGb) return true;
            var free = now + PagefileGrowthGb();
            if (free >= GameCommitGb) return true;
            var ask = string.Format(L("Windows can only promise {0:0.0} GB more memory (RAM plus virtual memory); the game needs about {1:0} GB as it starts. With less, its window stays white.\r\n\r\n" +
                                      "•  Close apps (Performance page: what holds memory).\r\n•  Let Windows manage virtual memory (Setup), then restart the PC.\r\n\r\nPlay anyway?",
                                      "O Windows so pode prometer mais {0:0.0} GB de memoria (RAM mais memoria virtual); o jogo precisa de cerca de {1:0} GB ao arrancar. Com menos, a janela fica branca.\r\n\r\n" +
                                      "•  Fecha apps (pagina Desempenho: o que ocupa memoria).\r\n•  Deixa o Windows gerir a memoria virtual (Preparar) e reinicia o PC.\r\n\r\nJogar na mesma?",
                                      "Windows ne peut garantir que {0:0.0} Go de mémoire en plus (RAM et mémoire virtuelle) ; le jeu a besoin d'environ {1:0} Go au démarrage. Avec moins, sa fenêtre reste blanche.\r\n\r\n" +
                                      "•  Ferme des applis (page Performances : ce qui occupe la mémoire).\r\n•  Laisse Windows gérer la mémoire virtuelle (Installation), puis redémarre le PC.\r\n\r\nJouer quand même ?"), free, GameCommitGb);
            return MessageBox.Show(this, ask, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        // Reads the session's log for the first minute: when the game cannot start (no D3D12 device,
        // a broken game file, a crash) the player is told why instead of looking at a white window.
        void WatchStart(string log, Process game)
        {
            new Thread(() => {
                var until = DateTime.Now.AddSeconds(90);
                while (DateTime.Now < until && !game.HasExited)
                {
                    Thread.Sleep(2000);
                    string text;
                    try
                    {
                        using (var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (f.Length > (8L << 20)) f.Seek(-(8L << 20), SeekOrigin.End);
                            text = new StreamReader(f).ReadToEnd();
                        }
                    }
                    catch (Exception) { continue; }
                    if (text.Contains("presenting to a window")) return;
                    string why = null;
                    if (text.Contains("no host D3D12 device") && text.Contains("8007000E"))
                        why = L("Windows ran out of memory to start the graphics (0x8007000E), so the window stays white.\r\n\r\nClose other apps, let Windows manage virtual memory (Setup) and restart the PC, then play again.",
                                "O Windows ficou sem memoria para iniciar os graficos (0x8007000E), por isso a janela fica branca.\r\n\r\nFecha outras apps, deixa o Windows gerir a memoria virtual (Preparar) e reinicia o PC, depois joga de novo.");
                    else if (text.Contains("no host D3D12 device"))
                        why = L("The graphics card could not start D3D12, so the window stays white. Update the graphics driver and restart the PC.",
                                "A placa grafica nao conseguiu iniciar o D3D12, por isso a janela fica branca. Atualiza o driver grafico e reinicia o PC.");
                    else if (text.Contains("malformed image"))
                        why = L("The game's files are damaged or incomplete (malformed image). Download or extract the dump again.",
                                "Os ficheiros do jogo estao estragados ou incompletos (malformed image). Volta a descarregar ou extrair o dump.");
                    if (why == null) continue;
                    try
                    {
                        BeginInvoke(new Action(() => {
                            Log(why.Replace("\r\n", " "));
                            if (MessageBox.Show(this, why + L("\r\n\r\nClose the game now?", "\r\n\r\nFechar o jogo agora?"), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                            {
                                CloseGames();
                                Refresh2();
                            }
                        }));
                    }
                    catch (Exception) { }
                    return;
                }
            }) { IsBackground = true }.Start();
        }

        // ---------------------------------------------------------------- the game's executable
        ComboBox exeBox;
        // The one chosen (launcher.ini's exe=), when the folder has several.
        string exeChoice;
        string exesOf;
        bool fillingExes;

        // The folder's game_scarlett*.exe, game_scarlett_master.exe first.
        static string[] GameExes(string dir)
        {
            try
            {
                if (dir.Length == 0 || !Directory.Exists(dir)) return new string[0];
                return Directory.GetFiles(dir, "game_scarlett*.exe")
                    .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .OrderBy(n => n.Equals("game_scarlett_master.exe", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception) { return new string[0]; }
        }

        string GameExe
        {
            get
            {
                var dir = gameBox.Text.Trim();
                var all = GameExes(dir);
                var pick = exeChoice != null && all.Contains(exeChoice, StringComparer.OrdinalIgnoreCase) ? exeChoice
                    : all.Length > 0 ? all[0] : "game_scarlett_master.exe";
                return Path.Combine(dir, pick);
            }
        }

        ComboBox ExeBox(int x, int y, int width)
        {
            exeBox = new ComboBox {
                Location = new Point(x, y), Width = width, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Card, ForeColor = Theme.Text, Visible = false,
            };
            exeBox.SelectedIndexChanged += (s, e) => {
                if (fillingExes || exeBox.SelectedItem == null) return;
                var name = (string)exeBox.SelectedItem;
                if (string.Equals(name, exeChoice, StringComparison.OrdinalIgnoreCase)) return;
                exeChoice = name;
                SaveSettings();
                Log(L("Game executable: ", "Executavel do jogo: ") + name);
                Refresh2();
            };
            return exeBox;
        }

        // The list follows the folder: shown only when there is a choice to make.
        void RefreshExes()
        {
            if (exeBox == null) return;
            var dir = gameBox.Text.Trim();
            var all = GameExes(dir);
            var key = dir + "|" + string.Join("|", all) + "|" + exeChoice;
            if (key == exesOf) return;
            bool listChanged = exesOf == null || !exesOf.StartsWith(dir + "|" + string.Join("|", all) + "|");
            exesOf = key;
            fillingExes = true;
            exeBox.Items.Clear();
            foreach (var n in all) exeBox.Items.Add(n);
            exeBox.Visible = all.Length > 1;
            if (all.Length > 0) exeBox.SelectedItem = Path.GetFileName(GameExe);
            fillingExes = false;
            if (all.Length > 1 && listChanged)
                Log(string.Format(L("The game folder has {0} executables; using {1} (change it beside the folder).",
                    "A pasta do jogo tem {0} executaveis; a usar o {1} (muda-o ao lado da pasta)."), all.Length, Path.GetFileName(GameExe)));
        }
    }
}
