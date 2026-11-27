// Julia's Launcher: the Custom preset (Advanced Tweaks) and its profile codes. A code ("VI1-...") is the
// profile's values, each held to the values the page offers, with a checksum: a pasted code can set
// nothing else and runs nothing.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    class CustomProfile
    {
        public string Scale = "0.6666667";
        public int TextureMax = 2048, CardBuffersMb = 1536, RipeBlocks = 4, StageMb = 384, Fps = 60;
        public bool Guards, LevelsOnDemand;

        // What each value may be (the page's choices); a code with anything else is refused.
        public static readonly string[] Scales = { "1.0", "0.75", "0.6666667", "0.5", "0.4" };
        public static readonly int[] TextureMaxes = { 0, 2048, 1024, 512 };
        public static readonly int[] Buffers = { 512, 768, 1024, 1536, 2048 };
        public static readonly int[] Ripes = { 2, 4 };
        public static readonly int[] Stages = { 64, 128, 384 };
        public static readonly int[] Fpses = { 30, 60 };

        public CustomProfile Copy() { return (CustomProfile)MemberwiseClone(); }

        public string Text()
        {
            return string.Format("s={0};t={1};b={2};r={3};g={4};f={5};m={6};l={7}", Scale, TextureMax, CardBuffersMb, RipeBlocks, StageMb, Fps, Guards ? 1 : 0, LevelsOnDemand ? 1 : 0);
        }

        static string Check(string body)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.ASCII.GetBytes(body)).Take(3).Select(b => b.ToString("x2")).ToArray());
        }

        public string Code()
        {
            var body = Convert.ToBase64String(Encoding.ASCII.GetBytes(Text())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return "VI1-" + body + "-" + Check(body);
        }

        // The profile a code holds, or null with the reason.
        public static CustomProfile FromCode(string code, out string why)
        {
            why = null;
            code = (code ?? "").Trim();
            var parts = code.Split('-');
            if (parts.Length < 3 || parts[0] != "VI1") { why = "not a VI1 profile code"; return null; }
            var check = parts[parts.Length - 1];
            var body = string.Join("-", parts.Skip(1).Take(parts.Length - 2).ToArray());
            if (Check(body) != check) { why = "the code is damaged (checksum)"; return null; }
            string text;
            try
            {
                var b64 = body.Replace('-', '+').Replace('_', '/');
                while (b64.Length % 4 != 0) b64 += "=";
                text = Encoding.ASCII.GetString(Convert.FromBase64String(b64));
            }
            catch (Exception) { why = "the code is damaged"; return null; }
            return FromText(text, out why);
        }

        public static CustomProfile FromText(string text, out string why)
        {
            why = null;
            var p = new CustomProfile();
            foreach (var pair in (text ?? "").Split(';'))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2) continue;
                int n;
                bool num = int.TryParse(kv[1], out n);
                switch (kv[0])
                {
                    case "s": if (!Scales.Contains(kv[1])) { why = "unknown resolution"; return null; } p.Scale = kv[1]; break;
                    case "t": if (!num || !TextureMaxes.Contains(n)) { why = "unknown texture size"; return null; } p.TextureMax = n; break;
                    case "b": if (!num || !Buffers.Contains(n)) { why = "unknown buffer size"; return null; } p.CardBuffersMb = n; break;
                    case "r": if (!num || !Ripes.Contains(n)) { why = "unknown value"; return null; } p.RipeBlocks = n; break;
                    case "g": if (!num || !Stages.Contains(n)) { why = "unknown value"; return null; } p.StageMb = n; break;
                    case "f": if (!num || !Fpses.Contains(n)) { why = "unknown FPS clock"; return null; } p.Fps = n; break;
                    case "m": p.Guards = kv[1] == "1"; break;
                    case "l": p.LevelsOnDemand = kv[1] == "1"; break;
                    default: why = "unknown setting " + kv[0]; return null;
                }
            }
            return p;
        }
    }

    partial class LauncherForm
    {
        CustomProfile custom = new CustomProfile();
        ComboBox cScale, cTex, cBuf, cRipe, cStage, cFps;
        CheckBox cGuards, cLevels;
        TextBox pasteBox, codeBox;
        bool fillingCustom;

        string CustomTag()
        {
            double sc;
            double.TryParse(custom.Scale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sc);
            return sc < 0.999 ? (int)Math.Round(1080 * sc) + "p" : "1080p";
        }

        void LoadCustom()
        {
            string why;
            var p = CustomProfile.FromText(ReadSetting("custom"), out why);
            if (p != null) custom = p;
        }

        void SaveCustom() { WriteSetting("custom", custom.Text()); }

        // The player's values over the preset's (Environment2 calls it last).
        void ApplyCustom(Dictionary<string, string> e)
        {
            var p = custom;
            if (p.Scale == "1.0") { e.Remove("VICTORY_RENDER_SCALE"); e.Remove("VICTORY_OWN_UPSCALER"); }
            else { e["VICTORY_RENDER_SCALE"] = p.Scale; e["VICTORY_OWN_UPSCALER"] = "1"; }
            if (p.TextureMax == 0) e.Remove("VICTORY_TEXTURE_MAX"); else e["VICTORY_TEXTURE_MAX"] = p.TextureMax.ToString();
            e["VICTORY_CARD_BUFFERS_MB"] = p.CardBuffersMb.ToString();
            e["VICTORY_RIPE_BLOCKS"] = p.RipeBlocks.ToString();
            e["VICTORY_TEXTURE_STAGE_MB"] = p.StageMb.ToString();
            if (p.Fps == 60) e["VICTORY_FPS_MAX"] = "60"; else e.Remove("VICTORY_FPS_MAX");
            if (p.Guards) { e.Remove("VICTORY_PRESSURE_FREE_MB"); e.Remove("VICTORY_PRESSURE_CLEAR_MB"); e.Remove("VICTORY_TEXTURE_RAM_GUARD"); }
            e["VICTORY_LEVELS_ON_DEMAND"] = p.LevelsOnDemand ? "1" : "0";
            if (p.CardBuffersMb <= 768) e.Remove("VICTORY_WINDOWS_BAR_MB");
        }

        Panel CustomPage()
        {
            LoadCustom();
            var page = Page();
            const int X = 28, W = 900;
            int y = 18;
            page.Controls.Add(new Heading(L("Advanced Tweaks (Custom preset)", "Ajustes avancados (predefinicao Personalizado)")) { Location = new Point(X, y), Width = W });
            y += 32;
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(W, 36), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f),
                Text = L("Your own values. They count when the Custom card is picked on the Play page. Lower values use less memory and give a softer picture.",
                         "Os teus proprios valores. Contam quando o cartao Personalizado esta escolhido na pagina Jogar. Valores mais baixos usam menos memoria e dao uma imagem mais suave.") });
            y += 44;
            Func<string, string, object[], ComboBox> row = (label, note, items) => {
                page.Controls.Add(new Label { Location = new Point(X, y + 4), Size = new Size(250, 22), Text = label, ForeColor = Theme.Text, BackColor = Theme.Back, Font = Theme.UiBold(9.5f) });
                var c = new ComboBox { Location = new Point(X + 260, y), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Mono(9f) };
                c.Items.AddRange(items);
                c.SelectedIndexChanged += (s, e) => ReadCustomFromPage();
                page.Controls.Add(c);
                page.Controls.Add(new Label { Location = new Point(X + 496, y + 4), Size = new Size(W - 496, 22), Text = note, ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(8.75f) });
                y += 36;
                return c;
            };
            cScale = row(L("World resolution", "Resolucao do mundo"), L("rebuilt to 1080p by the game's upscaler", "reconstruida para 1080p pelo upscaler do jogo"),
                new object[] { "1080p (1.0)", "810p (0.75)", "720p (0.667)", "540p (0.5)", "432p (0.4)" });
            cTex = row(L("Largest texture", "Maior textura"), L("smaller: less video memory", "menor: menos VRAM"), new object[] { L("no cap", "sem limite"), "2048", "1024", "512" });
            cBuf = row(L("Game buffers on the card", "Buffers do jogo na placa"), L("more: crowds and fights hold up", "mais: multidoes e lutas aguentam melhor"), new object[] { "512 MB", "768 MB", "1024 MB", "1536 MB", "2048 MB" });
            cRipe = row(L("Texture blocks at once", "Blocos de texturas de cada vez"), L("2: less memory, 4: faster", "2: menos memoria, 4: mais rapido"), new object[] { "2", "4" });
            cStage = row(L("Texture batch size", "Tamanho do lote de texturas"), L("smaller: less RAM", "menor: menos RAM"), new object[] { "64 MB", "128 MB", "384 MB" });
            cFps = row(L("FPS clock", "Relogio de FPS"), L("60 lets the game pass 30 FPS", "60 deixa o jogo passar dos 30 FPS"), new object[] { "30", "60" });
            cGuards = new OptionCard { Text = L("Memory guards", "Protecoes de memoria"), Line = L("Hold texture prefetch back early (16 GB of RAM or less)", "Travam cedo o pre-carregamento de texturas (16 GB de RAM ou menos)"), Location = new Point(X, y), Size = new Size((W - 10) / 2, 58) };
            cLevels = new OptionCard { Text = L("Texture levels on demand", "Niveis de texturas a pedido"), Line = L("Less video memory, more pop-in", "Menos VRAM, mais texturas a aparecer tarde"), Location = new Point(X + (W - 10) / 2 + 10, y), Size = new Size((W - 10) / 2, 58) };
            cGuards.CheckedChanged += (s, e) => ReadCustomFromPage();
            cLevels.CheckedChanged += (s, e) => ReadCustomFromPage();
            page.Controls.Add(cGuards); page.Controls.Add(cLevels);
            y += 70;
            var use = new Pill { Main = true, Text = L("Play with these (Custom)", "Jogar com estes (Personalizado)"), Location = new Point(X, y), Size = new Size(250, 38) };
            use.Click += (s, e) => { SelectMode(ModeCustom); SaveSettings(); Show(PagePlay); };
            page.Controls.Add(use);
            var back = new Pill { Text = L("Back to Play", "Voltar a Jogar"), Location = new Point(X + 260, y + 2), Size = new Size(150, 34) };
            back.Click += (s, e) => Show(PagePlay);
            page.Controls.Add(back);
            Func<int, string> name = k => modes[k].Name;
            var from = new ComboBox { Location = new Point(X + W - 260, y + 6), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text };
            from.Items.Add(L("Start from a preset...", "Partir de uma predefinicao..."));
            foreach (var k in new[] { 0, 1, 2, ModeUltraLow, 3 }) from.Items.Add(name(k));
            from.SelectedIndex = 0;
            from.SelectedIndexChanged += (s, e) => {
                if (from.SelectedIndex <= 0) return;
                var k = new[] { 0, 1, 2, ModeUltraLow, 3 }[from.SelectedIndex - 1];
                custom = PresetProfile(k);
                SaveCustom(); RefreshCustom();
                from.SelectedIndex = 0;
            };
            page.Controls.Add(from);
            y += 56;

            page.Controls.Add(new Heading(L("Share a profile", "Partilhar um perfil")) { Location = new Point(X, y), Width = W });
            y += 32;
            codeBox = new TextBox { Location = new Point(X, y + 4), Width = W - 200, ReadOnly = true, BackColor = Theme.Card, ForeColor = Theme.Cyan, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(9.5f) };
            page.Controls.Add(codeBox);
            var copy = new Pill { Text = L("Copy my profile", "Copiar o meu perfil"), Location = new Point(X + W - 190, y), Size = new Size(190, 34) };
            copy.Click += (s, e) => { Clipboard.SetText(custom.Code()); Log(L("Profile code copied: ", "Codigo do perfil copiado: ") + custom.Code()); };
            page.Controls.Add(copy);
            y += 46;
            page.Controls.Add(Small(L("Paste a profile code here:", "Cola aqui um codigo de perfil:"), X, y, W));
            y += 22;
            pasteBox = new TextBox { Location = new Point(X, y + 4), Width = W - 200, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(9.5f) };
            page.Controls.Add(pasteBox);
            var apply = new Pill { Main = true, Text = L("Apply", "Aplicar"), Location = new Point(X + W - 190, y), Size = new Size(190, 34) };
            apply.Click += (s, e) => ApplyPasted();
            page.Controls.Add(apply);
            y += 50;
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(W, 40), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f),
                Text = L("A code only holds the values on this page; the launcher shows what changes before it applies one.", "Um codigo so tem os valores desta pagina; o launcher mostra o que muda antes de o aplicar.") });
            return page;
        }

        // A preset's values as a profile, to start from.
        CustomProfile PresetProfile(int k)
        {
            var p = new CustomProfile();
            var m = modes[k];
            p.Scale = m.Scale ?? "1.0";
            p.Fps = m.UltraLow ? 30 : 60;
            if (m.UltraLow) { p.TextureMax = 512; p.CardBuffersMb = 512; p.RipeBlocks = 2; p.StageMb = 64; p.Guards = true; p.LevelsOnDemand = true; }
            else if (m.Modest) { p.TextureMax = 1024; p.CardBuffersMb = 768; p.RipeBlocks = 2; p.StageMb = 128; p.Guards = true; p.LevelsOnDemand = false; }
            else { p.TextureMax = 2048; p.CardBuffersMb = 1536; p.RipeBlocks = 4; p.StageMb = 384; p.Guards = ramGb > 0 && ramGb < 24; p.LevelsOnDemand = false; }
            return p;
        }

        void RefreshCustom()
        {
            if (cScale == null) return;
            fillingCustom = true;
            cScale.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Scales, custom.Scale));
            cTex.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.TextureMaxes, custom.TextureMax));
            cBuf.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Buffers, custom.CardBuffersMb));
            cRipe.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Ripes, custom.RipeBlocks));
            cStage.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Stages, custom.StageMb));
            cFps.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Fpses, custom.Fps));
            cGuards.Checked = custom.Guards;
            cLevels.Checked = custom.LevelsOnDemand;
            codeBox.Text = custom.Code();
            fillingCustom = false;
        }

        void ReadCustomFromPage()
        {
            if (fillingCustom || cScale.SelectedIndex < 0) return;
            custom.Scale = CustomProfile.Scales[cScale.SelectedIndex];
            custom.TextureMax = CustomProfile.TextureMaxes[Math.Max(0, cTex.SelectedIndex)];
            custom.CardBuffersMb = CustomProfile.Buffers[Math.Max(0, cBuf.SelectedIndex)];
            custom.RipeBlocks = CustomProfile.Ripes[Math.Max(0, cRipe.SelectedIndex)];
            custom.StageMb = CustomProfile.Stages[Math.Max(0, cStage.SelectedIndex)];
            custom.Fps = CustomProfile.Fpses[Math.Max(0, cFps.SelectedIndex)];
            custom.Guards = cGuards.Checked;
            custom.LevelsOnDemand = cLevels.Checked;
            codeBox.Text = custom.Code();
            SaveCustom();
            Refresh2();
        }

        // A pasted code: what it changes first, then the values on a yes.
        void ApplyPasted()
        {
            string why;
            var p = CustomProfile.FromCode(pasteBox.Text, out why);
            if (p == null) { MessageBox.Show(this, L("This code cannot be used: ", "Este codigo nao serve: ") + why + ".", "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var changes = new List<string>();
            Action<string, string, string> diff = (what, a, b) => { if (a != b) changes.Add("  •  " + what + ": " + a + "  →  " + b); };
            diff(L("World resolution", "Resolucao do mundo"), custom.Scale, p.Scale);
            diff(L("Largest texture", "Maior textura"), custom.TextureMax == 0 ? "-" : custom.TextureMax.ToString(), p.TextureMax == 0 ? "-" : p.TextureMax.ToString());
            diff(L("Game buffers on the card", "Buffers do jogo na placa"), custom.CardBuffersMb + " MB", p.CardBuffersMb + " MB");
            diff(L("Texture blocks at once", "Blocos de texturas de cada vez"), custom.RipeBlocks.ToString(), p.RipeBlocks.ToString());
            diff(L("Texture batch size", "Tamanho do lote de texturas"), custom.StageMb + " MB", p.StageMb + " MB");
            diff(L("FPS clock", "Relogio de FPS"), custom.Fps.ToString(), p.Fps.ToString());
            diff(L("Memory guards", "Protecoes de memoria"), custom.Guards ? "on" : "off", p.Guards ? "on" : "off");
            diff(L("Texture levels on demand", "Niveis de texturas a pedido"), custom.LevelsOnDemand ? "on" : "off", p.LevelsOnDemand ? "on" : "off");
            if (changes.Count == 0) { MessageBox.Show(this, L("This profile is the same as yours.", "Este perfil e igual ao teu."), "Julia's Launcher"); return; }
            var ask = L("This code changes:\r\n\r\n", "Este codigo muda:\r\n\r\n") + string.Join("\r\n", changes.ToArray()) + L("\r\n\r\nApply it and play with Custom?", "\r\n\r\nAplicar e jogar com Personalizado?");
            if (MessageBox.Show(this, ask, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            custom = p;
            SaveCustom();
            RefreshCustom();
            SelectMode(ModeCustom);
            SaveSettings();
            pasteBox.Text = "";
            Log(L("Profile applied: ", "Perfil aplicado: ") + custom.Code());
        }
    }
}
