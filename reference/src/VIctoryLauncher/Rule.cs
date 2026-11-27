using System.Windows.Forms;

namespace VIctoryLauncher;

internal class Rule : Control
{
	public Rule()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Height = 1;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(Theme.Rule);
	}
}
