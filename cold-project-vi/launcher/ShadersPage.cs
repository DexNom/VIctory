// Julia's Launcher: the Shaders page. What the shader cache holds, read again every second while the
// page is open (translated shaders, pipelines made, the ones that failed, disk room, the last errors),
// and the ways to fix it: prepare, repair, rebuild.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        Label[] shaderMarks, shaderTexts;
        TextBox shaderErrors;
        LoadBar shaderBar, pipeBar;
        // A stage being worked on (0 shaders, 1 pipelines) shows its live progress; else the cache's state.
        readonly bool[] stageLive = new bool[2];
        System.Windows.Forms.Timer shaderTimer;
        volatile bool shaderReading;

        Panel ShadersPage()
        {
            var page = Page();
            const int X = 28, W = 900;
            int y = 18;
            page.Controls.Add(new Heading(L("Emulator", "Emulador")) { Location = new Point(X, y), Width = W });
            y += 32;
            buildStatus = new Label { Location = new Point(X, y + 8), Size = new Size(W - 330, 20), ForeColor = Theme.Dim, BackColor = Theme.Back, AutoEllipsis = true, Font = Theme.Mono(8.75f) };
            page.Controls.Add(buildStatus);
            buildButton = new Pill { Text = L("Build VIctory", "Compilar VIctory"), Location = new Point(X + W - 310, y), Width = 150 };
            buildButton.Click += (s, e) => Build();
            page.Controls.Add(buildButton);
            prepareButton = new Pill { Main = true, Text = L("Prepare shaders", "Preparar shaders"), Location = new Point(X + W - 150, y), Width = 150 };
            prepareButton.Click += (s, e) => Prepare();
            page.Controls.Add(prepareButton);
            y += 50;

            page.Controls.Add(new Heading(L("Shader and pipeline health (live)", "Saude dos shaders e pipelines (ao vivo)")) { Location = new Point(X, y), Width = W });
            y += 32;
            const int rows = 8;
            shaderMarks = new Label[rows]; shaderTexts = new Label[rows];
            var card = new Panel { Location = new Point(X, y), Size = new Size(W, rows * 32 + 12), BackColor = Theme.Card };
            card.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
            for (int i = 0; i < rows; i++)
            {
                shaderMarks[i] = new Label { Location = new Point(14, 8 + i * 32), Size = new Size(26, 26), Font = Theme.UiBold(12f), ForeColor = Theme.Dim, BackColor = Theme.Card, Text = "•" };
                shaderTexts[i] = new Label { Location = new Point(44, 12 + i * 32), Size = new Size(W - 60, 22), Font = Theme.Mono(9f), ForeColor = Theme.Text, BackColor = Theme.Card, AutoEllipsis = true };
                card.Controls.Add(shaderMarks[i]); card.Controls.Add(shaderTexts[i]);
            }
            page.Controls.Add(card);
            y += card.Height + 10;
            shaderBar = new LoadBar { Location = new Point(X, y), Size = new Size(W, 28) };
            page.Controls.Add(shaderBar);
            y += 36;
            pipeBar = new LoadBar { Location = new Point(X, y), Size = new Size(W, 28) };
            page.Controls.Add(pipeBar);
            y += 40;
            var repair = new Pill { Text = L("Repair", "Reparar"), Location = new Point(X, y), Size = new Size(150, 36) };
            repair.Click += (s, e) => RepairShaders(false);
            page.Controls.Add(repair);
            var rebuild = new Pill { Text = L("Rebuild everything", "Refazer tudo"), Location = new Point(X + 160, y), Size = new Size(180, 36) };
            rebuild.Click += (s, e) => RepairShaders(true);
            page.Controls.Add(rebuild);
            var open = new Pill { Text = L("Open the cache folder", "Abrir a pasta da cache"), Location = new Point(X + 350, y), Size = new Size(200, 36) };
            open.Click += (s, e) => { Directory.CreateDirectory(ActiveCacheDir); OpenUrl(ActiveCacheDir); };
            page.Controls.Add(open);
            y += 50;
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(W, 36), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f),
                Text = L("Repair: makes the missing shaders and retries the pipelines that failed. Rebuild everything: clears the cache and prepares it again (about 5 minutes).",
                         "Reparar: faz os shaders que faltam e tenta de novo as pipelines que falharam. Refazer tudo: limpa a cache e prepara-a de novo (cerca de 5 minutos).") });
            y += 42;
            y = StorageSection(page, X, y, W);
            page.Controls.Add(new Heading(L("Last errors (prepare.log)", "Ultimos erros (prepare.log)")) { Location = new Point(X, y), Width = W });
            y += 30;
            shaderErrors = new TextBox { Location = new Point(X, y), Size = new Size(W, 150), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Card, ForeColor = Theme.Dim, Font = Theme.Mono(8.5f), ScrollBars = ScrollBars.Vertical };
            page.Controls.Add(shaderErrors);
            page.Controls.Add(new Label { Location = new Point(X, y + 160), Size = new Size(10, 10), BackColor = Theme.Back });

            shaderTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            shaderTimer.Tick += (s, e) => { if (page.Visible) RefreshShaders(); };
            shaderTimer.Start();
            return page;
        }

        // The AMD engine keeps a cache of its own (Hardware.cs, AmdEnvironment).
        string ActiveCacheDir { get { return UseAmdEngine ? Path.Combine(dataRoot, "cache-amd") : cacheDir; } }

        // A stage's live progress on the Shaders page (0 shaders, 1 pipelines); `live` false hands
        // the bar back to the cache's state (RefreshShaders).
        void Stage(int stage, float part, string text, bool live)
        {
            if (InvokeRequired) { try { BeginInvoke(new Action<int, float, string, bool>(Stage), stage, part, text, live); } catch (Exception) { } return; }
            stageLive[stage] = live;
            var bar = stage == 0 ? shaderBar : pipeBar;
            if (bar != null) bar.Set(part, text);
        }

        static int Lines(string file)
        {
            try { return File.Exists(file) ? File.ReadLines(file).Count(l => l.Trim().Length > 0) : 0; } catch (Exception) { return 0; }
        }

        void RefreshShaders()
        {
            if (shaderTexts == null || shaderReading) return;
            shaderReading = true;
            var cache = ActiveCacheDir;
            var log = Path.Combine(dataRoot, "logs", "prepare.log");
            new Thread(() => {
                var rows = new List<Tuple<int, string>>();
                string errors = "";
                int shaders = 0, done = 0, bad = 0, used = 0;
                bool prepared = false;
                try
                {
                    long bytes = 0;
                    var sdir = Path.Combine(cache, "shaders");
                    if (Directory.Exists(sdir))
                        foreach (var f in new DirectoryInfo(sdir).EnumerateFiles("*", SearchOption.AllDirectories)) { shaders++; bytes += f.Length; }
                    var pdir = Path.Combine(cache, "pipelines");
                    done = Lines(Path.Combine(pdir, "warm-done.txt")); bad = Lines(Path.Combine(pdir, "warm-bad.txt")); used = Lines(Path.Combine(pdir, "early-pipelines.txt"));
                    long free = -1;
                    try { Directory.CreateDirectory(cache); free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cache))).AvailableFreeSpace; } catch (Exception) { }
                    prepared = ReadSetting("shaders") == "1";
                    var pathOk = cache.All(c => c < 128);
                    rows.Add(Tuple.Create(shaders >= 1000 ? 1 : 0, string.Format(L("Shaders translated: {0:N0}  ({1:0.0} GB)", "Shaders traduzidos: {0:N0}  ({1:0.0} GB)"), shaders, bytes / 1073741824.0)));
                    rows.Add(Tuple.Create(prepared || shaders >= 1000 ? 1 : 0, prepared ? L("Last preparation finished well", "A ultima preparacao acabou bem")
                        : shaders >= 1000 ? L("Prepared (by another way of starting VIctory, or before this launcher)", "Preparado (por outra forma de arrancar o VIctory, ou antes deste launcher)")
                        : L("Not prepared yet, or not since the last update (press Prepare shaders)", "Ainda nao preparado, ou nao desde o ultimo update (carrega em Preparar shaders)")));
                    rows.Add(Tuple.Create(done > 0 ? 1 : -1, string.Format(L("Pipelines made for the driver: {0:N0}  (used by your sessions: {1:N0})", "Pipelines feitas para o driver: {0:N0}  (usadas nas tuas sessoes: {1:N0})"), done, used)));
                    rows.Add(Tuple.Create(bad == 0 ? 1 : 0, string.Format(L("Pipelines that failed: {0:N0}", "Pipelines que falharam: {0:N0}"), bad) + (bad > 0 ? L("  (Repair tries them again)", "  (Reparar tenta de novo)") : "")));
                    var lastDriver = ReadSetting("shaders_driver") ?? "";
                    rows.Add(Tuple.Create(lastDriver.Length == 0 || lastDriver == driverVersion ? 1 : -1, driverVersion.Length == 0 ? L("Graphics driver: not read", "Driver grafico: nao lido")
                        : lastDriver.Length > 0 && lastDriver != driverVersion ? string.Format(L("Driver changed ({0} → {1}): PLAY remakes the pipelines once", "Driver mudou ({0} → {1}): o JOGAR refaz as pipelines uma vez"), lastDriver, driverVersion)
                        : string.Format(L("Graphics driver {0}", "Driver grafico {0}"), driverVersion)));
                    rows.Add(Tuple.Create(free < 0 ? -1 : free >= (12L << 30) ? 1 : 0, free < 0 ? L("Disk room: not read", "Espaco em disco: nao lido") : string.Format(L("Free on the cache's disk: {0:0.0} GB  (12 GB needed to prepare)", "Livre no disco da cache: {0:0.0} GB  (12 GB para preparar)"), free / 1073741824.0)));
                    rows.Add(Tuple.Create(pathOk ? 1 : 0, pathOk ? L("Cache path is plain (no accents or symbols)", "Caminho da cache simples (sem acentos nem simbolos)") : L("The cache path has accents or symbols: move it (launcher.ini cache=)", "O caminho da cache tem acentos ou simbolos: muda-o (launcher.ini cache=)")));
                    rows.Add(Tuple.Create(-1, L("Cache: ", "Cache: ") + Scrub(cache) + "  ·  " + EngineName));
                    if (File.Exists(log))
                        using (var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (f.Length > (4L << 20)) f.Seek(-(4L << 20), SeekOrigin.End);
                            var lines = new StreamReader(f).ReadToEnd().Split('\n').Select(s => s.TrimEnd('\r'))
                                .Where(s => Regex.IsMatch(s, @"(?i)\b(error|panicked|failed|denied|not found|no space|do not translate)\b")).Reverse().Take(30).Reverse();
                            errors = string.Join("\r\n", lines.ToArray());
                        }
                }
                catch (Exception) { }
                try
                {
                    BeginInvoke(new Action(() => {
                        for (int i = 0; i < shaderTexts.Length && i < rows.Count; i++)
                        {
                            var st = rows[i].Item1;
                            shaderMarks[i].Text = st == 1 ? "✓" : st == 0 ? "✗" : "•";
                            shaderMarks[i].ForeColor = st == 1 ? Theme.Good : st == 0 ? Theme.Warn : Theme.Dim;
                            shaderTexts[i].Text = rows[i].Item2;
                        }
                        var t = errors.Length == 0 ? L("(no errors)", "(sem erros)") : Scrub(errors);
                        if (shaderErrors.Text != t) shaderErrors.Text = t;
                        if (!stageLive[0])
                            shaderBar.Set(shaders >= 1000 || prepared ? 1f : 0f, shaders >= 1000 || prepared
                                ? string.Format(L("Shaders: {0:N0} translated, ready", "Shaders: {0:N0} traduzidos, prontos"), shaders)
                                : L("Shaders: not prepared yet (press Prepare shaders)", "Shaders: ainda nao preparados (carrega em Preparar shaders)"));
                        if (!stageLive[1])
                            pipeBar.Set(used > 0 ? Math.Min(1f, (float)done / used) : done > 0 ? 1f : 0f,
                                string.Format(L("Pipelines: {0:N0} made for the driver (your sessions used {1:N0})", "Pipelines: {0:N0} feitas para o driver (as tuas sessoes usaram {1:N0})"), done, used));
                        shaderReading = false;
                    }));
                }
                catch (Exception) { shaderReading = false; }
            }) { IsBackground = true }.Start();
        }

        // Repair: the failed pipelines' list goes (they are tried again) and Prepare runs. Rebuild:
        // the whole cache goes first.
        void RepairShaders(bool all)
        {
            if (State() != GameState.None) { MessageBox.Show(this, L("Close the game first.", "Fecha o jogo primeiro."), "Julia's Launcher"); return; }
            var cache = ActiveCacheDir;
            try
            {
                if (all)
                {
                    if (MessageBox.Show(this, L("Delete the shader cache and prepare it again (about 5 minutes)?", "Apagar a cache de shaders e prepara-la de novo (cerca de 5 minutos)?"),
                        "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    foreach (var d in new[] { "shaders", "pipelines" })
                    {
                        var p = Path.Combine(cache, d);
                        if (Directory.Exists(p)) Directory.Delete(p, true);
                    }
                    WriteSetting("shaders", "0");
                    Log(L("Shader cache cleared.", "Cache de shaders limpa."));
                }
                else
                {
                    var bad = Path.Combine(cache, "pipelines", "warm-bad.txt");
                    if (File.Exists(bad)) File.Delete(bad);
                }
            }
            catch (Exception e) { Log(L("Could not clear the cache: ", "Nao deu para limpar a cache: ") + e.Message); return; }
            Prepare();
        }
    }
}

