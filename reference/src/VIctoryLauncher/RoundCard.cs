using System.Drawing;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class RoundCard : Panel
{
	public Color Edge = Theme.Rule;

	public float Radius = 10f;

	public bool Flat;

	public RoundCard()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Theme.Card;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((Parent != null) ? Parent.BackColor : Theme.Back);
		Theme.Smooth(graphics);
		if (Flat)
		{
			using (SolidBrush brush = new SolidBrush(Theme.Rule))
			{
				graphics.FillRectangle(brush, 0, 0, Width, 1);
				graphics.FillRectangle(brush, 0, Height - 1, Width, 1);
				return;
			}
		}
		Theme.Panel(graphics, new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f), BackColor, Edge, Radius);
	}
}
