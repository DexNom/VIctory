using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class KeyCap : Control
{
	public bool Pad;

	public KeyCap()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Font = Theme.Mono(8.75f);
	}

	protected override void OnTextChanged(EventArgs e)
	{
		Invalidate();
		base.OnTextChanged(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.ClearBack(this, graphics);
		Theme.Smooth(graphics);
		string text = Text ?? "";
		StringFormat stringFormat = new StringFormat();
		stringFormat.LineAlignment = StringAlignment.Center;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat.HotkeyPrefix = HotkeyPrefix.None;
		using StringFormat format = stringFormat;
		StringFormat stringFormat2 = new StringFormat();
		stringFormat2.Alignment = StringAlignment.Center;
		stringFormat2.LineAlignment = StringAlignment.Center;
		stringFormat2.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat2.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat2.HotkeyPrefix = HotkeyPrefix.None;
		using StringFormat format2 = stringFormat2;
		if (text == "-" || text.Length == 0)
		{
			using (SolidBrush brush = new SolidBrush(Theme.Dim))
			{
				graphics.DrawString("—", Font, brush, new RectangleF(4f, 0f, Math.Max(10, Width - 4), Height), format);
				return;
			}
		}
		float num = 0f;
		string[] array = text.Split(new string[2] { " / ", " + " }, StringSplitOptions.None);
		List<string> list = new List<string>();
		int i = 0;
		int num2 = 0;
		for (; i < array.Length - 1; i++)
		{
			int num3 = text.IndexOf(array[i], num2, StringComparison.Ordinal);
			num2 = ((num3 < 0) ? num2 : num3) + array[i].Length;
			list.Add((num2 + 3 <= text.Length) ? text.Substring(num2, 3).Trim() : "/");
		}
		for (int j = 0; j < array.Length; j++)
		{
			float num4 = (float)Math.Ceiling(graphics.MeasureString(array[j], Font).Width) + 12f;
			if (num + num4 > (float)Width)
			{
				num4 = Math.Max(20f, (float)Width - num - 1f);
			}
			RectangleF rectangleF = new RectangleF(num + 0.5f, 1.5f, num4, (float)Height - 4f);
			Theme.Panel(graphics, rectangleF, Pad ? Theme.Back : Theme.Card, Pad ? Theme.Line : Theme.Cyan, 7f);
			using (SolidBrush brush2 = new SolidBrush(Pad ? Theme.Dim : Theme.Text))
			{
				graphics.DrawString(array[j], Font, brush2, rectangleF, format2);
			}
			num += num4 + 4f;
			if (j < list.Count)
			{
				float num5 = graphics.MeasureString(list[j], Font).Width;
				using (SolidBrush brush3 = new SolidBrush(Theme.Dim))
				{
					graphics.DrawString(list[j], Font, brush3, new RectangleF(num, 0f, num5 + 4f, Height), format);
				}
				num += num5 + 2f;
			}
			if (num >= (float)Width)
			{
				break;
			}
		}
	}
}
