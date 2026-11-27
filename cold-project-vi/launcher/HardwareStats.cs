// Julia's Launcher, Hardware & ID: the reference PC (the laptop the presets are measured on) and the players'
// PCs as GitHub publishes them every 6 hours (docs/hardware-stats.json, averages only: never a row or an ID,
// a group of fewer than 3 PCs counts as "other"), as a table and bar charts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    // Horizontal bars, one per line: a name, a bar as long as its share, and the number.
    class BarChart : Control
    {
        public List<Tuple<string, double, string>> Items = new List<Tuple<string, double, string>>();
        public string Empty = "";
        public BarChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Ui(9f);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            Theme.Smooth(g);
            if (Items.Count == 0)
            {
                using (var b = new SolidBrush(Theme.Dim)) g.DrawString(Empty, Font, b, 10, 10);
                return;
            }
            double most = Math.Max(1e-9, Items.Max(i => i.Item2));
            const int line = 24, nameW = 200, numW = 90;
            int y = 8;
            foreach (var it in Items)
            {
                using (var b = new SolidBrush(Theme.Text))
                using (var f = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center })
                    g.DrawString(it.Item1, Font, b, new RectangleF(10, y, nameW - 14, line - 4), f);
                float full = Width - nameW - numW - 20;
                float w = (float)(full * it.Item2 / most);
                using (var path = Theme.Round(new RectangleF(nameW, y + 4, Math.Max(3, w), line - 12), 3))
                using (var b = new SolidBrush(Theme.Cyan)) g.FillPath(b, path);
                using (var b = new SolidBrush(Theme.Dim))
                using (var f = new StringFormat { LineAlignment = StringAlignment.Center })
                    g.DrawString(it.Item3, Font, b, new RectangleF(nameW + w + 8, y, numW + full - w, line - 4), f);
                y += line;
            }
        }
        public int Wanted { get { return Math.Max(40, Items.Count * 24 + 16); } }
    }

    partial class LauncherForm
    {
        public const string StatsJsonUrl = "https://raw.githubusercontent.com/felicienradio/cold-project-vi/main/docs/hardware-stats.json";
        public const string StatsPageUrl = "https://github.com/felicienradio/cold-project-vi/blob/main/docs/HARDWARE-STATS.md";

        // The laptop every preset is measured on (edupode's), for players to compare their PC with.
        int ReferencePcSection(Panel page, int X, int y, int W)
        {
            page.Controls.Add(new Heading(L("Reference PC (the presets are measured on it)", "PC de referencia (as predefinicoes sao medidas nele)", "PC de référence (les préréglages sont mesurés dessus)")) { Location = new Point(X, y), Width = W });
            y += 32;
            var lines = new[] {
                L("Laptop:  Acer Predator Helios Neo 16 (PHN16-73), plugged in, performance mode", "Portatil:  Acer Predator Helios Neo 16 (PHN16-73), ligado a corrente, modo desempenho", "Portable :  Acer Predator Helios Neo 16 (PHN16-73), sur secteur, mode performance"),
                L("Graphics: NVIDIA GeForce RTX 5070 Ti Laptop GPU, 12 GB, Resizable BAR on", "Grafica:  NVIDIA GeForce RTX 5070 Ti Laptop GPU, 12 GB, Resizable BAR ligado", "Graphique : NVIDIA GeForce RTX 5070 Ti Laptop GPU, 12 Go, Resizable BAR activé"),
                L("CPU:      Intel Core Ultra 9 275HX (24 cores)", "CPU:      Intel Core Ultra 9 275HX (24 nucleos)", "CPU :      Intel Core Ultra 9 275HX (24 cœurs)"),
                L("Memory:   32 GB RAM, 1 TB NVMe SSD, Windows 11", "Memoria:  32 GB de RAM, SSD NVMe de 1 TB, Windows 11", "Mémoire :  32 Go de RAM, SSD NVMe de 1 To, Windows 11"),
                L("Recommended (720p → 1080p): about 22-26 FPS in the open world, 35+ in cutscenes, 6-8 GB of video memory, ~20 GB of RAM.",
                  "Recomendado (720p → 1080p): cerca de 22-26 FPS no mundo aberto, 35+ nas cutscenes, 6-8 GB de VRAM, ~20 GB de RAM.",
                  "Recommandé (720p → 1080p) : environ 22-26 FPS en monde ouvert, 35+ en cinématiques, 6-8 Go de VRAM, ~20 Go de RAM."),
            };
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(W, lines.Length * 16 + 18), Padding = new Padding(10, 8, 10, 8), BackColor = Theme.Card, ForeColor = Theme.Text,
                Font = Theme.Mono(9f), Text = string.Join("\r\n", lines) });
            return y + lines.Length * 16 + 30;
        }

        // The players' PCs: a summary line, the graphics cards as a table, and charts. Filled when GitHub answers.
        int PlayersPcsSection(Panel page, int X, int y, int W)
        {
            page.Controls.Add(new Heading(L("Players' PCs (updated every 6 hours)", "PCs dos jogadores (atualizado de 6 em 6 horas)", "PC des joueurs (mis à jour toutes les 6 heures)")) { Location = new Point(X, y), Width = W });
            y += 32;
            var head = new Label { Location = new Point(X, y), Size = new Size(W - 170, 40), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f),
                Text = L("Reading the averages from GitHub...", "A ler as medias do GitHub...", "Lecture des moyennes sur GitHub...") };
            page.Controls.Add(head);
            var open = new Pill { Text = L("Open on GitHub", "Abrir no GitHub", "Ouvrir sur GitHub"), Location = new Point(X + W - 160, y), Size = new Size(160, 32) };
            open.Click += (s, e) => OpenUrl(StatsPageUrl);
            page.Controls.Add(open);
            y += 46;
            var table = new ListView { Location = new Point(X, y), Size = new Size(W, 220), View = View.Details, FullRowSelect = true, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
                Font = Theme.Ui(9.5f), HeaderStyle = ColumnHeaderStyle.Nonclickable, OwnerDraw = true };
            table.Columns.Add(L("Graphics card", "Placa grafica", "Carte graphique"), 360);
            table.Columns.Add(L("PCs", "PCs", "PC"), 90);
            table.Columns.Add(L("Video memory", "VRAM", "VRAM"), 140);
            // The last column fills the rest, so no light strip is left beside the header.
            table.Columns.Add(L("Resizable BAR for the game", "Resizable BAR para o jogo", "Resizable BAR pour le jeu"), W - 360 - 90 - 140 - 4);
            DarkHeader(table);
            page.Controls.Add(table);
            y += 230;
            var charts = new List<Tuple<string, BarChart>>();
            foreach (var title in new[] {
                L("Graphics card makers", "Fabricantes de placas", "Fabricants de cartes"),
                L("RAM", "RAM", "RAM"),
                L("Windows", "Windows", "Windows"),
                L("How many have it", "Quantos tem", "Combien l'ont") })
            {
                var c = new BarChart { Location = new Point(X, y + 24), Size = new Size(W, 60), Empty = "..." };
                var h = new Label { Location = new Point(X, y), Size = new Size(W, 22), Text = title, ForeColor = Theme.Text, BackColor = Theme.Back, Font = Theme.UiBold(9.5f) };
                page.Controls.Add(h); page.Controls.Add(c);
                charts.Add(Tuple.Create(title, c));
                y += 24 + 60 + 14;
            }
            int bottom = y;
            new Thread(() => {
                Dictionary<string, object> s = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    using (var web = new WebClient())
                    {
                        web.Headers[HttpRequestHeader.UserAgent] = "Julias-Launcher";
                        s = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(web.DownloadData(StatsJsonUrl))) as Dictionary<string, object>;
                    }
                }
                catch (Exception) { }
                try { BeginInvoke(new Action(() => FillStats(s, head, table, charts))); } catch (Exception) { }
            }) { IsBackground = true }.Start();
            return bottom;
        }

        static double Num(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToDouble(v) : 0; }
        static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }
        static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> d, string k)
        {
            object v;
            var list = d != null && d.TryGetValue(k, out v) ? v as object[] : null;
            return list == null ? Enumerable.Empty<Dictionary<string, object>>() : list.OfType<Dictionary<string, object>>();
        }

        void FillStats(Dictionary<string, object> s, Label head, ListView table, List<Tuple<string, BarChart>> charts)
        {
            if (s == null)
            {
                head.Text = L("GitHub could not be reached: the averages show when it can.", "Sem ligacao ao GitHub: as medias aparecem quando houver.", "GitHub injoignable : les moyennes s'afficheront plus tard.");
                foreach (var c in charts) { c.Item2.Empty = "—"; c.Item2.Invalidate(); }
                return;
            }
            var avg = Obj(s, "average");
            var pct = Obj(s, "percent");
            object updated;
            DateTime when;
            var at = s.TryGetValue("updated", out updated) && DateTime.TryParse(updated as string, null, System.Globalization.DateTimeStyles.RoundtripKind, out when) ? when.ToLocalTime().ToString("g") : "?";
            double pcs = Num(s, "pcs");
            head.Text = string.Format(L("{0:N0} PCs share their hardware. On average: {1:0.#} GB of RAM, {2:0.#} GB of video memory, {3:0} CPU threads. Updated {4}.",
                                        "{0:N0} PCs partilham o hardware. Em media: {1:0.#} GB de RAM, {2:0.#} GB de VRAM, {3:0} threads de CPU. Atualizado a {4}.",
                                        "{0:N0} PC partagent leur matériel. En moyenne : {1:0.#} Go de RAM, {2:0.#} Go de VRAM, {3:0} threads CPU. Mis à jour le {4}."),
                pcs, Num(avg, "ram_gb"), Num(avg, "vram_gb"), Num(avg, "cpu_threads"), at);
            table.Items.Clear();
            foreach (var r in Rows(s, "gpus"))
            {
                var name = r["name"] as string;
                if (name == "other") name = L("other (fewer than 3 PCs each)", "outras (menos de 3 PCs cada)", "autres (moins de 3 PC chacune)");
                table.Items.Add(new ListViewItem(new[] { name, Num(r, "pcs").ToString("N0"),
                    r.ContainsKey("vram_gb") ? Num(r, "vram_gb").ToString("0") + " GB" : "",
                    r.ContainsKey("rebar_percent") ? Num(r, "rebar_percent").ToString("0") + " %" : "" }));
            }
            Func<string, List<Tuple<string, double, string>>> share = key => Rows(s, key)
                .Select(r => Tuple.Create(key == "ram_gb" && (r["name"] as string) != "other" ? r["name"] + " GB" : r["name"] as string, Num(r, "pcs"), Num(r, "pcs").ToString("N0") + "  (" + (pcs > 0 ? Num(r, "pcs") / pcs * 100 : 0).ToString("0") + " %)"))
                .ToList();
            var sets = new List<List<Tuple<string, double, string>>> {
                share("gpu_vendors"),
                share("ram_gb"),
                share("windows"),
                new List<Tuple<string, double, string>> {
                    Tuple.Create(L("Resizable BAR for the game", "Resizable BAR para o jogo", "Resizable BAR pour le jeu"), Num(pct, "rebar_for_the_game"), Num(pct, "rebar_for_the_game").ToString("0") + " %"),
                    Tuple.Create(L("Resizable BAR in the BIOS (NVIDIA)", "Resizable BAR na BIOS (NVIDIA)", "Resizable BAR dans le BIOS (NVIDIA)"), Num(pct, "rebar_in_bios"), Num(pct, "rebar_in_bios").ToString("0") + " %"),
                    Tuple.Create(L("8 GB of video memory or less", "8 GB de VRAM ou menos", "8 Go de VRAM ou moins"), Num(pct, "vram_8_gb_or_less"), Num(pct, "vram_8_gb_or_less").ToString("0") + " %"),
                    Tuple.Create(L("16 GB of RAM or less", "16 GB de RAM ou menos", "16 Go de RAM ou moins"), Num(pct, "ram_16_gb_or_less"), Num(pct, "ram_16_gb_or_less").ToString("0") + " %"),
                    Tuple.Create(L("Laptops", "Portateis", "Portables"), Num(pct, "laptops"), Num(pct, "laptops").ToString("0") + " %"),
                    Tuple.Create(L("SSD", "SSD", "SSD"), Num(pct, "ssd"), Num(pct, "ssd").ToString("0") + " %"),
                },
            };
            // Each chart takes the room its lines need; what is below moves down by the difference.
            for (int i = 0; i < charts.Count; i++)
            {
                var c = charts[i].Item2;
                c.Items = sets[i];
                int grow = c.Wanted - c.Height;
                c.Height = c.Wanted;
                if (grow != 0)
                    foreach (Control other in c.Parent.Controls)
                        if (other != c && other.Top > c.Top) other.Top += grow;
                c.Invalidate();
            }
        }
    }
}
