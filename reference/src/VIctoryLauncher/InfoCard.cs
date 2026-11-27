using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class InfoCard : Control
{
	public string Empty = "";

	public InfoCard()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnTextChanged(EventArgs e)
	{
		Invalidate();
		base.OnTextChanged(e);
	}

	private string[] Lines()
	{
		return (Text ?? "").Replace("\r", "").Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
	}

	private static void Split(string l, out string k, out string v)
	{
		int num = l.IndexOf(':');
		k = ((num > 0) ? l.Substring(0, num).Trim() : "");
		v = ((num > 0) ? l.Substring(num + 1).Trim() : l.Trim());
	}

	private List<float> MeasureRows(Graphics g, out float labelW)
	{
		List<float> list = new List<float>();
		labelW = 0f;
		string[] array = Lines();
		if (array.Length == 0)
		{
			return list;
		}
		using Font font = Theme.Ui(9f);
		using Font font2 = Theme.UiBold(9.5f);
		string[] array2 = array;
		foreach (string l in array2)
		{
			Split(l, out var k, out var _);
			labelW = Math.Max(labelW, g.MeasureString(k, font).Width);
		}
		labelW = Math.Min(labelW + 20f, (float)Width * 0.36f);
		string[] array3 = array;
		foreach (string l2 in array3)
		{
			Split(l2, out var _, out var v2);
			list.Add(Math.Max(30f, g.MeasureString(v2, font2, (int)Math.Max(60f, (float)(Width - 32) - labelW)).Height + 12f));
		}
		return list;
	}

	public int Fit()
	{
		using Bitmap image = new Bitmap(1, 1);
		using Graphics g = Graphics.FromImage(image);
		List<float> list = MeasureRows(g, out var _);
		return (list.Count == 0) ? 52 : ((int)Math.Ceiling(list.Sum() + 16f));
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((Parent != null) ? Parent.BackColor : Theme.Back);
		Theme.Smooth(graphics);
		Theme.Panel(graphics, new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f), Theme.Card, Theme.Line, 14f);
		List<float> list = MeasureRows(graphics, out var labelW);
		string[] array = Lines();
		if (array.Length == 0)
		{
			using (SolidBrush brush = new SolidBrush(Theme.Dim))
			{
				StringFormat stringFormat = new StringFormat();
				stringFormat.LineAlignment = StringAlignment.Center;
				stringFormat.Trimming = StringTrimming.EllipsisCharacter;
				stringFormat.FormatFlags = StringFormatFlags.NoWrap;
				using StringFormat format = stringFormat;
				graphics.DrawString(Empty ?? "", Font, brush, new RectangleF(16f, 0f, Math.Max(10, Width - 32), Height), format);
				return;
			}
		}
		float num = 8f;
		using Font font = Theme.Ui(9f);
		using Font font2 = Theme.UiBold(9.5f);
		using SolidBrush brush2 = new SolidBrush(Theme.Dim);
		using SolidBrush brush3 = new SolidBrush(Theme.Text);
		using Pen pen = new Pen(Theme.Line);
		StringFormat stringFormat2 = new StringFormat();
		stringFormat2.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat2.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat2.LineAlignment = StringAlignment.Center;
		using StringFormat format2 = stringFormat2;
		StringFormat stringFormat3 = new StringFormat();
		stringFormat3.LineAlignment = StringAlignment.Center;
		using StringFormat format3 = stringFormat3;
		for (int i = 0; i < array.Length; i++)
		{
			Split(array[i], out var k, out var v);
			float num2 = list[i];
			if (i > 0)
			{
				graphics.DrawLine(pen, 16f, (float)Math.Round(num) + 0.5f, Width - 16, (float)Math.Round(num) + 0.5f);
			}
			graphics.DrawString(k, font, brush2, new RectangleF(16f, num, labelW - 8f, num2), format2);
			graphics.DrawString(v, font2, brush3, new RectangleF(16f + labelW, num, (float)(Width - 32) - labelW, num2), format3);
			num += num2;
		}
	}
}
