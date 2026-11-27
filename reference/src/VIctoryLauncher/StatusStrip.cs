using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class StatusStrip : Control
{
	public string[] Items = new string[0];

	public int[] States = new int[0];

	public string ReadyText = "Ready to play";

	public string NotReadyText = "Not ready yet";

	public int RightRoom = 232;

	public StatusStrip()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Height = 52;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.ClearBack(this, graphics);
		Theme.Smooth(graphics);
		Theme.Panel(graphics, new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f), Theme.Card, Theme.Line, 14f);
		bool flag = Items.Length > 0;
		for (int i = 0; i < Items.Length; i++)
		{
			if (((i < States.Length) ? States[i] : (-1)) != 1)
			{
				flag = false;
			}
		}
		Color color = (flag ? Theme.Good : Theme.Warn);
		using (SolidBrush brush = new SolidBrush(Color.FromArgb(60, color)))
		{
			graphics.FillEllipse(brush, 14f, (float)Height / 2f - 8f, 16f, 16f);
		}
		using (SolidBrush brush2 = new SolidBrush(color))
		{
			graphics.FillEllipse(brush2, 17f, (float)Height / 2f - 5f, 10f, 10f);
		}
		float num = 38f;
		float num2 = Width - RightRoom;
		using (Font font = Theme.UiBold(10.5f))
		{
			using SolidBrush brush3 = new SolidBrush(Theme.Text);
			StringFormat stringFormat = new StringFormat();
			stringFormat.LineAlignment = StringAlignment.Center;
			stringFormat.Trimming = StringTrimming.EllipsisCharacter;
			stringFormat.FormatFlags = StringFormatFlags.NoWrap;
			stringFormat.HotkeyPrefix = HotkeyPrefix.None;
			using StringFormat format = stringFormat;
			string s = (flag ? ReadyText : NotReadyText);
			float val = (float)Math.Ceiling(graphics.MeasureString(s, font).Width);
			float num3 = Math.Max(40f, Math.Min(val, num2 - num));
			graphics.DrawString(s, font, brush3, new RectangleF(num, 0f, num3, Height), format);
			num += num3 + 14f;
		}
		using Font font2 = Theme.Ui(9f);
		StringFormat stringFormat2 = new StringFormat();
		stringFormat2.LineAlignment = StringAlignment.Center;
		stringFormat2.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat2.HotkeyPrefix = HotkeyPrefix.None;
		using StringFormat format2 = stringFormat2;
		for (int j = 0; j < Items.Length; j++)
		{
			int num4 = ((j < States.Length) ? States[j] : (-1));
			string s2 = num4 switch
			{
				0 => "!", 
				1 => "✓", 
				_ => "•", 
			} + "  " + Items[j];
			float num5 = (float)Math.Ceiling(graphics.MeasureString(s2, font2).Width);
			if (!(num + num5 > num2))
			{
				using (SolidBrush brush4 = new SolidBrush(num4 switch
				{
					0 => Theme.Warn, 
					1 => Theme.Soft, 
					_ => Theme.Dim, 
				}))
				{
					graphics.DrawString(s2, font2, brush4, new RectangleF(num, 0f, num5 + 4f, Height), format2);
				}
				num += num5 + 12f;
				continue;
			}
			break;
		}
	}
}
