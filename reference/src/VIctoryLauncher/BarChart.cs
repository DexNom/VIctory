using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class BarChart : Control
{
	private const int line = 28;

	public List<Tuple<string, double, string>> Items = new List<Tuple<string, double, string>>();

	public string Empty = "";

	public Func<string, bool> Mine;

	public static string YouText = "you";

	private static readonly Color[] Colours = new Color[4]
	{
		Theme.HotPink,
		Theme.Peach,
		Theme.Cyan,
		Theme.Lilac
	};

	public int Wanted => Math.Max(44, Items.Count * 28 + 20);

	public BarChart()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Font = Theme.Ui(9f);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((Parent != null) ? Parent.BackColor : Theme.Back);
		Theme.Smooth(graphics);
		Theme.Panel(graphics, new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f), Theme.Card, Theme.Line, 14f);
		if (Items.Count == 0)
		{
			using (SolidBrush brush = new SolidBrush(Theme.Dim))
			{
				StringFormat stringFormat = new StringFormat();
				stringFormat.LineAlignment = StringAlignment.Center;
				stringFormat.Trimming = StringTrimming.EllipsisCharacter;
				stringFormat.FormatFlags = StringFormatFlags.NoWrap;
				using StringFormat format = stringFormat;
				graphics.DrawString(Empty ?? "", Font, brush, new RectangleF(16f, 10f, Math.Max(10, Width - 32), 28f), format);
				return;
			}
		}
		double num = Math.Max(1E-09, Items.Max((Tuple<string, double, string> i) => i.Item2));
		float num2 = Math.Min(210f, (float)Width * 0.3f);
		float num3 = 0f;
		foreach (Tuple<string, double, string> item in Items)
		{
			num3 = Math.Max(num3, graphics.MeasureString(item.Item3 ?? "", Font).Width);
		}
		float num4;
		using (Font font = Theme.UiBold(7.5f))
		{
			num4 = (float)Math.Ceiling(graphics.MeasureString(YouText ?? "", font).Width) + 8f;
		}
		num3 = Math.Min(num3 + 4f + num4 + 6f, (float)Width * 0.4f);
		float num5 = Math.Max(20f, (float)Width - num2 - num3 - 28f);
		int num6 = 10;
		int num7 = 0;
		foreach (Tuple<string, double, string> item2 in Items)
		{
			bool flag = Mine != null && Mine(item2.Item1);
			using (SolidBrush brush2 = new SolidBrush(flag ? Theme.Text : Theme.Soft))
			{
				using Font font2 = (flag ? Theme.UiBold(9f) : Theme.Ui(9f));
				StringFormat stringFormat2 = new StringFormat();
				stringFormat2.Trimming = StringTrimming.EllipsisCharacter;
				stringFormat2.FormatFlags = StringFormatFlags.NoWrap;
				stringFormat2.LineAlignment = StringAlignment.Center;
				using StringFormat format2 = stringFormat2;
				graphics.DrawString(item2.Item1 ?? "", font2, brush2, new RectangleF(16f, num6, num2 - 22f, 28f), format2);
			}
			float val = (float)((double)num5 * item2.Item2 / num);
			using (GraphicsPath path = Theme.Round(new RectangleF(num2, num6 + 9, num5, 10f), 5f))
			{
				using SolidBrush brush3 = new SolidBrush(Theme.Quiet);
				graphics.FillPath(brush3, path);
			}
			using (GraphicsPath path2 = Theme.Round(new RectangleF(num2, num6 + 9, Math.Min(num5, Math.Max(10f, val)), 10f), 5f))
			{
				using SolidBrush brush4 = new SolidBrush(Colours[num7 % Colours.Length]);
				graphics.FillPath(brush4, path2);
			}
			float num8 = num2 + num5 + 12f;
			float num9 = (float)Math.Ceiling(graphics.MeasureString(item2.Item3 ?? "", Font).Width);
			using (SolidBrush brush5 = new SolidBrush(Theme.Dim))
			{
				StringFormat stringFormat3 = new StringFormat();
				stringFormat3.LineAlignment = StringAlignment.Center;
				stringFormat3.FormatFlags = StringFormatFlags.NoWrap;
				stringFormat3.Trimming = StringTrimming.EllipsisCharacter;
				using StringFormat format3 = stringFormat3;
				graphics.DrawString(item2.Item3 ?? "", Font, brush5, new RectangleF(num8, num6, Math.Max(10f, Math.Min(num9 + 2f, (float)Width - num8 - 8f)), 28f), format3);
			}
			if (flag)
			{
				using Font font3 = Theme.UiBold(7.5f);
				float num10 = (float)Math.Ceiling(graphics.MeasureString(YouText ?? "", font3).Width) + 8f;
				RectangleF rectangleF = new RectangleF(Math.Min(num8 + num9 + 4f, (float)Width - num10 - 12f), num6 + 6, num10, 16f);
				using (GraphicsPath path3 = Theme.Round(rectangleF, rectangleF.Height / 2f))
				{
					using SolidBrush brush6 = new SolidBrush(Theme.Cyan);
					graphics.FillPath(brush6, path3);
				}
				using SolidBrush brush7 = new SolidBrush(Theme.OnAccent);
				StringFormat stringFormat4 = new StringFormat();
				stringFormat4.Alignment = StringAlignment.Center;
				stringFormat4.LineAlignment = StringAlignment.Center;
				stringFormat4.FormatFlags = StringFormatFlags.NoWrap;
				using StringFormat format4 = stringFormat4;
				graphics.DrawString(YouText ?? "", font3, brush7, rectangleF, format4);
			}
			num6 += 28;
			num7++;
		}
	}
}
