using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal static class Kit
{
	private const TextFormatFlags Wrap = TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(IntPtr hwnd, string app, string list);

	public static void Dark(Control c, string theme)
	{
		Action apply = () =>
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
		c.HandleCreated += (object s, EventArgs e) =>
		{
			apply();
		};
	}

	public static int TextH(string text, Font f, int width)
	{
		return TextRenderer.MeasureText(text ?? "", f, new Size(Math.Max(20, width), 100000), TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 4;
	}

	public static int TextW(string text, Font f)
	{
		return TextRenderer.MeasureText(text ?? "", f, new Size(100000, 100), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
	}

	public static int PillW(string text, float pt, int min)
	{
		using Font f = new Font("Segoe UI", pt);
		return Math.Max(min, TextW(text, f) + 40);
	}

	public static Label Text(string text, Font f, Color color, Color back, int x, int y, int w)
	{
		Label label = new Label();
		label.Text = text;
		label.Font = f;
		label.ForeColor = color;
		label.BackColor = back;
		label.UseMnemonic = false;
		label.AutoSize = false;
		label.Location = new Point(x, y);
		label.Size = new Size(w, TextH(text, f, w));
		return label;
	}

	public static int Row(Control parent, int x, int y, int maxW, params Pill[] pills)
	{
		int num = x;
		int num2 = 0;
		foreach (Pill pill in pills)
		{
			int num3 = Math.Max(pill.Width, PillW(pill.Text, pill.Font.Size, 110));
			if (num > x && num + num3 > x + maxW)
			{
				num = x;
				y += num2 + 10;
				num2 = 0;
			}
			pill.Location = new Point(num, y);
			pill.Size = new Size(num3, pill.Height);
			parent.Controls.Add(pill);
			num += num3 + 10;
			num2 = Math.Max(num2, pill.Height);
		}
		return y + num2;
	}

	public static string Clip(string text, Font f, int width)
	{
		text = text ?? "";
		if (TextW(text, f) <= width)
		{
			return text;
		}
		int num = 0;
		int num2 = text.Length;
		while (num < num2)
		{
			int num3 = (num + num2 + 1) / 2;
			if (TextW(text.Substring(0, num3) + "...", f) <= width)
			{
				num = num3;
			}
			else
			{
				num2 = num3 - 1;
			}
		}
		return text.Substring(0, num).TrimEnd() + "...";
	}

	public static RoundCard Field(Control parent, TextBox tb, int x, int y, int w)
	{
		return Field(parent, tb, x, y, w, 36);
	}

	public static RoundCard Field(Control parent, TextBox tb, int x, int y, int w, int h)
	{
		tb.BorderStyle = BorderStyle.None;
		RoundCard card = new RoundCard
		{
			Location = new Point(x, y),
			Size = new Size(w, h),
			BackColor = Theme.Back,
			Edge = Theme.Line,
			Radius = 10f
		};
		tb.BackColor = card.BackColor;
		tb.Width = w - 28;
		if (tb.Multiline)
		{
			tb.Size = new Size(w - 28, h - 20);
			tb.Location = new Point(14, 10);
			Dark(tb, "DarkMode_Explorer");
		}
		else
		{
			tb.Location = new Point(14, (card.Height - tb.Height) / 2);
		}
		tb.GotFocus += (object s, EventArgs e) =>
		{
			card.Edge = Theme.Cyan;
			card.Invalidate();
		};
		tb.LostFocus += (object s, EventArgs e) =>
		{
			card.Edge = Theme.Line;
			card.Invalidate();
		};
		card.Click += (object s, EventArgs e) =>
		{
			tb.Focus();
		};
		card.Controls.Add(tb);
		parent.Controls.Add(card);
		return card;
	}
}
