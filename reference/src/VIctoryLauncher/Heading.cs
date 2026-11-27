using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class Heading : Control
{
	public string Note = "";

	public bool Hair = true;

	public Heading(string text)
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Text = text;
		Height = 26;
		Font = Theme.UiBold(11.5f);
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
		float num = 0f;
		Font font = null;
		if (!string.IsNullOrEmpty(Note))
		{
			font = Theme.Ui(8.5f);
			num = Math.Min((float)Math.Ceiling(graphics.MeasureString(Note, font).Width), Math.Max(0f, (float)Width * 0.5f));
		}
		StringFormat stringFormat = new StringFormat();
		stringFormat.LineAlignment = StringAlignment.Center;
		stringFormat.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat.HotkeyPrefix = HotkeyPrefix.None;
		using (StringFormat format = stringFormat)
		{
			using (SolidBrush brush = new SolidBrush(Theme.Text))
			{
				graphics.DrawString(Text ?? "", Font, brush, new RectangleF(-1f, 0f, Math.Max(10f, (float)Width - num - (float)((num > 0f) ? 12 : 0)), Height - 2), format);
			}
			if (font != null)
			{
				using (SolidBrush brush2 = new SolidBrush(Theme.Muted))
				{
					graphics.DrawString(Note, font, brush2, new RectangleF((float)Width - num - 2f, 0f, num + 2f, Height - 2), format);
				}
				font.Dispose();
			}
		}
		if (Hair)
		{
			using (Pen pen = new Pen(Theme.Rule))
			{
				graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
			}
		}
	}
}
