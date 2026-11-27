using System;
using System.Drawing;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class Eyebrow : Control
{
	public bool Hair = true;

	public Color Tint = Theme.Muted;

	public Eyebrow(string text)
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Text = text;
		Height = 24;
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
		using (Font f = Theme.UiBold(7.5f))
		{
			using SolidBrush b = new SolidBrush(Tint);
			Theme.Tracked(graphics, (Text ?? "").ToUpperInvariant(), f, b, 0f, 2f, 1.3f);
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
