using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal static class Theme
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

	public static readonly Color TrackOff = Color.FromArgb(96, 58, 90);

	public static readonly Color TrackHover = Color.FromArgb(116, 72, 108);

	public static readonly Color TrackDisabled = Color.FromArgb(62, 34, 57);

	public static readonly Color KnobDisabled = Color.FromArgb(124, 98, 118);

	public static readonly Color Quiet = Color.FromArgb(74, 36, 66);

	public static readonly Color LineStrong = Color.FromArgb(150, 112, 142);

	public static readonly Color BadgeBack = Color.FromArgb(92, 28, 76);

	public static readonly Color BadgeText = Color.FromArgb(255, 168, 222);

	public static readonly Color NavHover = Color.FromArgb(44, 16, 40);

	public static readonly Color CardOn = Color.FromArgb(150, 92, 134);

	public static readonly Color Lilac = Color.FromArgb(206, 164, 255);

	public static readonly Color Wash = Color.FromArgb(34, 255, 255, 255);

	public static readonly Color Stripe = Color.FromArgb(14, 255, 255, 255);

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

	public static string TestedText = "Tested";

	private static string mono;

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
				using InstalledFontCollection installedFontCollection = new InstalledFontCollection();
				FontFamily[] families = installedFontCollection.Families;
				foreach (FontFamily fontFamily in families)
				{
					if (fontFamily.Name == "Cascadia Mono")
					{
						mono = fontFamily.Name;
						break;
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
		if (c.Parent is IBackdrop backdrop)
		{
			GraphicsState gstate = g.Save();
			g.TranslateTransform(-c.Left, -c.Top);
			backdrop.PaintBackdrop(g, c.Bounds);
			g.Restore(gstate);
		}
		else
		{
			g.Clear((c.Parent != null) ? c.Parent.BackColor : Back);
		}
	}

	public static void Glow(Graphics g, float cx, float cy, float rx, float ry, Color c, int alpha)
	{
		using GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddEllipse(cx - rx, cy - ry, rx * 2f, ry * 2f);
		PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath);
		pathGradientBrush.CenterColor = Color.FromArgb(alpha, c);
		pathGradientBrush.SurroundColors = new Color[1] { Color.FromArgb(0, c) };
		using PathGradientBrush brush = pathGradientBrush;
		g.FillPath(brush, graphicsPath);
	}

	public static float TextW(Graphics g, string text, Font f)
	{
		if (string.IsNullOrEmpty(text))
		{
			return 0f;
		}
		StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
		stringFormat.FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
		using StringFormat stringFormat2 = stringFormat;
		return g.MeasureString(text, f, PointF.Empty, stringFormat2).Width;
	}

	public static float TrackedWidth(Graphics g, string text, Font f, float spacing)
	{
		float num = 0f;
		string text2 = text ?? "";
		for (int i = 0; i < text2.Length; i++)
		{
			num += TextW(g, text2[i].ToString(), f) + spacing;
		}
		return num;
	}

	public static float Tracked(Graphics g, string text, Font f, Brush b, float x, float y, float spacing)
	{
		StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
		stringFormat.FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
		using StringFormat stringFormat2 = stringFormat;
		string text2 = text ?? "";
		for (int i = 0; i < text2.Length; i++)
		{
			string text3 = text2[i].ToString();
			g.DrawString(text3, f, b, x, y, stringFormat2);
			x += g.MeasureString(text3, f, PointF.Empty, stringFormat2).Width + spacing;
		}
		return x;
	}

	public static float AscentPx(Graphics g, Font f)
	{
		FontFamily fontFamily = f.FontFamily;
		return f.SizeInPoints * g.DpiY / 72f * (float)fontFamily.GetCellAscent(f.Style) / (float)fontFamily.GetEmHeight(f.Style);
	}

	public static void Meta(Graphics g, string label, string value, Color valueColor, RectangleF box, StringAlignment align)
	{
		Font metaFont = Fx.MetaFont;
		StringFormat stringFormat = new StringFormat();
		stringFormat.Alignment = align;
		stringFormat.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat.HotkeyPrefix = HotkeyPrefix.None;
		using StringFormat format = stringFormat;
		using (SolidBrush brush = new SolidBrush(Text))
		{
			g.DrawString(label ?? "", metaFont, brush, new RectangleF(box.X, box.Y, box.Width, 16f), format);
		}
		using SolidBrush brush2 = new SolidBrush(valueColor);
		g.DrawString(value ?? "", metaFont, brush2, new RectangleF(box.X, box.Y + 16f, box.Width, 16f), format);
	}

	public static void FocusRing(Graphics g, RectangleF r, float radius, Color c)
	{
		using Pen pen = new Pen(Color.FromArgb(210, c), 1.5f);
		using GraphicsPath path = Round(r, radius);
		g.DrawPath(pen, path);
	}

	public static void DrawOrbits(Graphics g, RectangleF r)
	{
		Action<float, float, float, Color, int> action = (float cx, float cy, float radius, Color color, int alpha) =>
		{
			using GraphicsPath graphicsPath = new GraphicsPath();
			graphicsPath.AddEllipse(cx - radius, cy - radius, radius * 2f, radius * 2f);
			using PathGradientBrush brush3 = new PathGradientBrush(graphicsPath)
			{
				CenterColor = Color.FromArgb(alpha, color),
				SurroundColors = new Color[1] { Color.FromArgb(0, color) }
			};
			g.FillPath(brush3, graphicsPath);
		};
		action(r.X + r.Width * 0.16f, r.Y + r.Height * 0.7f, r.Height * 2.2f, GlowMagenta, 120);
		action(r.Right - r.Width * 0.04f, r.Y + r.Height * 0.05f, r.Height * 1.3f, GlowPeach, 80);
		float num = r.X + r.Width * 0.8f;
		float num2 = r.Y + r.Height * 0.62f;
		float[] array = new float[2]
		{
			r.Height * 1.05f,
			r.Height * 0.66f
		};
		using (Pen pen = new Pen(Ring, 1f))
		{
			float[] array2 = array;
			foreach (float num4 in array2)
			{
				g.DrawEllipse(pen, num - num4, num2 - num4, num4 * 2f, num4 * 2f);
			}
		}
		Tuple<float, float, Color>[] array3 = new Tuple<float, float, Color>[4]
		{
			Tuple.Create(array[0], 200f, HotPink),
			Tuple.Create(array[1], 300f, Plum),
			Tuple.Create(array[1], 150f, Peach),
			Tuple.Create(array[0], 330f, Cyan)
		};
		Tuple<float, float, Color>[] array4 = array3;
		foreach (Tuple<float, float, Color> tuple in array4)
		{
			double num6 = (double)tuple.Item2 * Math.PI / 180.0;
			float num7 = num + (float)Math.Cos(num6) * tuple.Item1;
			float num8 = num2 + (float)Math.Sin(num6) * tuple.Item1;
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
			{
				g.FillEllipse(brush, num7 - 9f, num8 - 9f, 18f, 18f);
			}
			using SolidBrush brush2 = new SolidBrush(tuple.Item3);
			g.FillEllipse(brush2, num7 - 6f, num8 - 6f, 12f, 12f);
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
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
		if (num <= 0f)
		{
			graphicsPath.AddRectangle(r);
			return graphicsPath;
		}
		graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
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
		using GraphicsPath path = Round(r, radius);
		using (SolidBrush brush = new SolidBrush(fill))
		{
			g.FillPath(brush, path);
		}
		using Pen pen = new Pen(border);
		g.DrawPath(pen, path);
	}
}
