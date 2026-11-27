// The launcher's own drawn controls, in the Cold Project style: a dark navy window, panels one
// shade up with a 1 px border, small corners, one cyan accent, monospaced labels and numbers.
// Plain GDI+, .NET Framework 4, C# 5.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(19, 23, 31);
        public static readonly Color Side = Color.FromArgb(15, 19, 26);
        public static readonly Color Card = Color.FromArgb(26, 31, 41);
        public static readonly Color CardHover = Color.FromArgb(33, 40, 53);
        public static readonly Color Selected = Color.FromArgb(24, 44, 58);
        public static readonly Color Line = Color.FromArgb(44, 52, 66);
        // The one accent (the names stay from the older theme: Pink is the accent, Violet its darker side).
        public static readonly Color Pink = Color.FromArgb(56, 189, 248);
        public static readonly Color Violet = Color.FromArgb(14, 145, 210);
        public static readonly Color Cyan = Color.FromArgb(56, 189, 248);
        public static readonly Color Text = Color.FromArgb(226, 232, 240);
        public static readonly Color Dim = Color.FromArgb(136, 146, 162);
        public static readonly Color Good = Color.FromArgb(74, 222, 128);
        public static readonly Color Warn = Color.FromArgb(251, 191, 36);
        public static readonly Color Bad = Color.FromArgb(248, 113, 113);

        static string mono;
        // Cascadia Mono where Windows has it, Consolas otherwise.
        public static string MonoName
        {
            get
            {
                if (mono != null) return mono;
                mono = "Consolas";
                try
                {
                    using (var fonts = new InstalledFontCollection())
                        foreach (var f in fonts.Families)
                            if (f.Name == "Cascadia Mono") { mono = f.Name; break; }
                }
                catch (Exception) { }
                return mono;
            }
        }
        public static Font Mono(float size) { return new Font(MonoName, size); }
        public static Font Ui(float size) { return new Font("Segoe UI", size); }
        public static Font UiBold(float size) { return new Font("Segoe UI Semibold", size); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void Panel(Graphics g, RectangleF r, Color fill, Color border)
        {
            using (var path = Round(r, 5))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }
        }
    }

    // A switch with its text to the right; Checked as a CheckBox.
    class Toggle : CheckBox
    {
        bool hover;
        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = false;
            Height = 30;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        public static void DrawSwitch(Graphics g, RectangleF track, bool on, bool hover, bool enabled)
        {
            using (var path = Theme.Round(track, track.Height / 2))
            using (var b = new SolidBrush(!enabled ? Theme.Line : on ? Theme.Cyan : hover ? Theme.CardHover : Theme.Line)) g.FillPath(b, path);
            float k = track.Height - 4;
            float kx = on ? track.Right - k - 2 : track.X + 2;
            using (var b = new SolidBrush(on ? Color.White : Color.FromArgb(200, 206, 216))) g.FillEllipse(b, kx, track.Y + 2, k, k);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            DrawSwitch(g, new RectangleF(1, (Height - 20) / 2f, 36, 20), Checked, hover, Enabled);
            var text = new RectangleF(46, 0, Width - 46, Height);
            using (var b = new SolidBrush(Enabled ? ForeColor : Theme.Dim))
            using (var f = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Text, Font, b, text, f);
        }
    }

    // A launch option as in the reference: a bordered panel, the switch, a title and one line.
    class OptionCard : CheckBox
    {
        public string Line = "";
        bool hover;
        public OptionCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Size = new Size(400, 58);
            Cursor = Cursors.Hand;
            Font = Theme.UiBold(10f);
            ForeColor = Theme.Text;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            Theme.Panel(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), hover ? Theme.CardHover : Theme.Card, Theme.Line);
            Toggle.DrawSwitch(g, new RectangleF(14, (Height - 20) / 2f, 34, 20), Checked, hover, Enabled);
            using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.Dim))
            using (var f = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Text, Font, b, new RectangleF(60, 9, Width - 70, 20), f);
            using (var lf = Theme.Ui(8.75f))
            using (var b = new SolidBrush(Theme.Dim))
            using (var f = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Line ?? "", lf, b, new RectangleF(60, 31, Width - 70, 18), f);
        }
    }

    // A preset to pick: name, the resolution in the accent colour, a grey line.
    class ModeCard : Control
    {
        public string Title, Tag2, Line;
        bool selected, hover;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public ModeCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Size = new Size(150, 74);
            Font = Theme.Mono(8.5f);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            Theme.Panel(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), selected ? Theme.Selected : hover ? Theme.CardHover : Theme.Card, selected ? Theme.Cyan : Theme.Line);
            var nf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var tf = Theme.UiBold(10.5f))
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(Title ?? "", tf, b, new RectangleF(10, 8, Width - 16, 22), nf);
            using (var b = new SolidBrush(Theme.Cyan)) g.DrawString(Tag2 ?? "", Font, b, new RectangleF(11, 31, Width - 16, 16), nf);
            using (var lf = Theme.Ui(8.5f))
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(Line ?? "", lf, b, new RectangleF(10, 49, Width - 14, 18), nf);
        }
    }

    // A button: the accent fill for the main action, a bordered panel otherwise.
    class Pill : Button
    {
        public bool Main;
        bool hover, down;
        public Pill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            Height = 34;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (!Enabled) Theme.Panel(g, r, Theme.Card, Theme.Line);
            else if (Main)
            {
                var fill = down ? Theme.Violet : hover ? Color.FromArgb(110, 207, 250) : Theme.Cyan;
                Theme.Panel(g, r, fill, fill);
            }
            else Theme.Panel(g, r, down ? Theme.Back : hover ? Theme.CardHover : Theme.Card, hover || down ? Theme.Cyan : Theme.Line);
            // Keyboard focus (Tab): a ring inside the edge.
            if (Focused && ShowFocusCues && Enabled)
                using (var p = new Pen(Main ? Color.FromArgb(8, 20, 30) : Theme.Cyan, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                using (var path = Theme.Round(new RectangleF(3.5f, 3.5f, Width - 7.5f, Height - 7.5f), 4)) g.DrawPath(p, path);
            using (var b = new SolidBrush(Enabled ? (Main ? Color.FromArgb(8, 20, 30) : ForeColor) : Theme.Dim))
            using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, HotkeyPrefix = HotkeyPrefix.None })
            {
                // A label longer than the button (Portuguese and French ones often are) gets a smaller font
                // before it is cut with "...".
                var font = Font;
                Font smaller = null;
                for (float size = Font.Size; size > 7.5f && g.MeasureString(Text, font).Width > r.Width - 12; size -= 0.5f)
                {
                    if (smaller != null) smaller.Dispose();
                    font = smaller = new Font(Font.FontFamily, size - 0.5f, Font.Style);
                }
                g.DrawString(Text, font, b, r, f);
                if (smaller != null) smaller.Dispose();
            }
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // A key (or a controller button) drawn as a key cap: "Left Ctrl / C" becomes two caps with a slash between.
    class KeyCap : Control
    {
        public bool Pad;
        public KeyCap()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Theme.Mono(8.75f);
        }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            var text = Text ?? "";
            if (text == "-" || text.Length == 0)
            {
                using (var b = new SolidBrush(Theme.Dim)) g.DrawString("—", Font, b, 4, 5);
                return;
            }
            float x = 0;
            var parts = text.Split(new[] { " / ", " + " }, StringSplitOptions.None);
            var joins = new List<string>();
            for (int i = 0, at = 0; i < parts.Length - 1; i++)
            {
                at = text.IndexOf(parts[i], at, StringComparison.Ordinal) + parts[i].Length;
                joins.Add(text.Substring(at, 3).Trim());
            }
            for (int i = 0; i < parts.Length; i++)
            {
                var w = g.MeasureString(parts[i], Font).Width + 14;
                if (x + w > Width) w = Math.Max(20, Width - x - 1);
                var r = new RectangleF(x + 0.5f, 1.5f, w, Height - 4f);
                Theme.Panel(g, r, Pad ? Theme.Back : Theme.Card, Pad ? Theme.Line : Theme.Cyan);
                using (var b = new SolidBrush(Pad ? Theme.Dim : Theme.Text))
                using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(parts[i], Font, b, r, f);
                x += w + 4;
                if (i < joins.Count)
                {
                    using (var b = new SolidBrush(Theme.Dim)) g.DrawString(joins[i], Font, b, x, 6);
                    x += g.MeasureString(joins[i], Font).Width + 2;
                }
                if (x >= Width) break;
            }
        }
    }
    // A button of the side bar: icon and name; the active one has a lighter fill and a cyan bar.
    class NavButton : Control
    {
        public string Glyph;
        bool active, hover;
        public bool Active { get { return active; } set { active = value; Invalidate(); } }
        public NavButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
            Size = new Size(196, 38);
            Font = Theme.Ui(10.5f);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Side);
            Theme.Smooth(g);
            var r = new RectangleF(8, 2, Width - 16, Height - 4);
            if (active || hover)
                using (var path = Theme.Round(r, 5))
                using (var b = new SolidBrush(active ? Color.FromArgb(28, 36, 48) : Color.FromArgb(22, 28, 37))) g.FillPath(b, path);
            if (active)
                using (var b = new SolidBrush(Theme.Cyan)) g.FillRectangle(b, 8, 6, 3, Height - 12);
            using (var gf = new Font("Segoe MDL2 Assets", 11f))
            using (var b = new SolidBrush(active ? Theme.Text : Theme.Dim))
                g.DrawString(Glyph ?? "", gf, b, 22, Height / 2f - 8);
            using (var b = new SolidBrush(active ? Theme.Text : Theme.Dim))
            using (var f = new StringFormat { LineAlignment = StringAlignment.Center })
                g.DrawString(Text, Font, b, new RectangleF(50, 0, Width - 54, Height), f);
        }
    }

    // The head of the Play page: a drawn "VI" mark, a line about the project, the hardware chips.
    class Banner : Control
    {
        public string Title = "", Subtitle = "", Accent = "";
        public string[] Chips = new string[0];
        public Color[] ChipColors = new Color[0];
        public Banner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 110;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            // The mark: "VI" in a sunset gradient over a dark tile.
            var tile = new RectangleF(2, 4, 132, 96);
            using (var path = Theme.Round(tile, 8))
            using (var b = new LinearGradientBrush(tile, Color.FromArgb(34, 18, 52), Color.FromArgb(12, 34, 52), 60f)) g.FillPath(b, path);
            using (var f = new Font("Segoe UI Black", 40f, FontStyle.Italic))
            using (var b = new LinearGradientBrush(tile, Color.FromArgb(255, 92, 170), Color.FromArgb(70, 200, 255), 30f))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("VI", f, b, tile, sf);
            float x = 160;
            using (var tf = Theme.Ui(12f))
            {
                // Three runs of text one after the other, measured without GDI+'s padding.
                float tx = x;
                var parts = new[] { Title, Accent, Subtitle };
                var colors = new[] { Theme.Text, Theme.Cyan, Theme.Text };
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.IsNullOrEmpty(parts[i])) continue;
                    using (var b = new SolidBrush(colors[i])) g.DrawString(parts[i], tf, b, tx, 22, StringFormat.GenericTypographic);
                    tx += g.MeasureString(parts[i].Replace(' ', ' '), tf, 2000, StringFormat.GenericTypographic).Width;
                }
            }
            using (var cf = Theme.Mono(8.5f))
            {
                float cx = x + 2;
                for (int i = 0; i < Chips.Length; i++)
                {
                    var c = i < ChipColors.Length ? ChipColors[i] : Theme.Dim;
                    using (var b = new SolidBrush(c)) g.DrawString(Chips[i], cf, b, cx, 58);
                    cx += g.MeasureString(Chips[i], cf).Width + 6;
                    if (i + 1 < Chips.Length)
                    {
                        using (var b = new SolidBrush(Theme.Line)) g.DrawString("·", cf, b, cx, 58);
                        cx += 16;
                    }
                }
            }
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    // A row of checks ("✓ Dump verified  ✓ Binary ready ..."), drawn in a bordered panel.
    class StatusStrip : Control
    {
        public string[] Items = new string[0];
        public int[] States = new int[0];   // 1 good, 0 bad, -1 neutral
        public StatusStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 52;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            Theme.Panel(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Card, Theme.Line);
            float x = 14;
            using (var f = Theme.Mono(8.75f))
                for (int i = 0; i < Items.Length; i++)
                {
                    int s = i < States.Length ? States[i] : -1;
                    var mark = s == 1 ? "✓" : s == 0 ? "✗" : "•";
                    using (var b = new SolidBrush(s == 1 ? Theme.Text : s == 0 ? Theme.Warn : Theme.Dim))
                    {
                        g.DrawString(mark + " " + Items[i], f, b, x, Height / 2f - 8);
                        x += g.MeasureString(mark + " " + Items[i], f).Width + 14;
                    }
                }
        }
    }

    // A loading bar with its text.
    class LoadBar : Control
    {
        float done;
        public LoadBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Mono(8.75f);
        }
        public void Set(float part, string text) { done = Math.Max(0f, Math.Min(1f, part)); Text = text; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            var track = new RectangleF(0, Height - 7, Width - 1, 6);
            using (var path = Theme.Round(track, 3f))
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
            if (done > 0)
            {
                var fill = new RectangleF(0, Height - 7, Math.Max(6, (Width - 1) * done), 6);
                using (var path = Theme.Round(fill, 3f))
                using (var b = new SolidBrush(Theme.Cyan)) g.FillPath(b, path);
            }
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(Text, Font, b, 0, 0);
            var pct = ((int)(done * 100)).ToString() + "%";
            var size = g.MeasureString(pct, Font);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(pct, Font, b, Width - size.Width, 0);
        }
    }

    // A section title in monospaced capitals ("RENDER PRESET"), with an optional grey note at the right.
    class Heading : Control
    {
        public string Note = "";
        public Heading(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = text;
            Height = 26;
            Font = Theme.Mono(10f);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            Theme.Smooth(g);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString((Text ?? "").ToUpperInvariant(), Font, b, 0, 3);
            if (!string.IsNullOrEmpty(Note))
                using (var nf = Theme.Mono(8.25f))
                using (var b = new SolidBrush(Theme.Dim))
                {
                    var size = g.MeasureString(Note, nf);
                    g.DrawString(Note, nf, b, Width - size.Width, 5);
                }
        }
    }
}
