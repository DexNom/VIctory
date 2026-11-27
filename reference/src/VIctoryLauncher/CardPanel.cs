using System.Drawing;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class CardPanel : Panel
{
	public CardPanel()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Padding = new Padding(14, 10, 8, 10);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((Parent != null) ? Parent.BackColor : Theme.Back);
		Theme.Smooth(graphics);
		Theme.Panel(graphics, new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f), Theme.Card, Theme.Line, 14f);
	}
}
