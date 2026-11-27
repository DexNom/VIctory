using System.Drawing;

namespace VIctoryLauncher;

internal interface IBackdrop
{
	void PaintBackdrop(Graphics g, Rectangle area);
}
