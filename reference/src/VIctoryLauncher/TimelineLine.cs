using System;
using System.Drawing;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class TimelineLine : Control
{
	private readonly Mix fill;

	public TimelineLine()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		fill = new Mix(this);
	}

	public void SetFill(float px)
	{
		fill.To(px, 520);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((Parent != null) ? Parent.BackColor : Theme.Back);
		using (SolidBrush brush = new SolidBrush(Theme.Line))
		{
			graphics.FillRectangle(brush, 1, 0, 1, Height);
		}
		if (!fill.Running && fill.V > (float)Height)
		{
			fill.Set(Height);
		}
		float num = Math.Min(Height, fill.V);
		if (num > 0.5f)
		{
			using (SolidBrush brush2 = new SolidBrush(Theme.Good))
			{
				graphics.FillRectangle(brush2, 0f, 0f, 3f, num);
			}
		}
	}
}
