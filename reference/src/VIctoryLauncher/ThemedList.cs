using System.Drawing;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class ThemedList : ListBox
{
	public ThemedList()
	{
		DrawMode = DrawMode.OwnerDrawFixed;
		ItemHeight = 32;
		BorderStyle = BorderStyle.None;
		BackColor = Theme.Card;
		ForeColor = Theme.Text;
		Font = Theme.Ui(9.5f);
		IntegralHeight = false;
		Kit.Dark(this, "DarkMode_Explorer");
	}

	protected override void OnDrawItem(DrawItemEventArgs e)
	{
		Graphics graphics = e.Graphics;
		using (SolidBrush brush = new SolidBrush(BackColor))
		{
			graphics.FillRectangle(brush, e.Bounds);
		}
		if (e.Index >= 0)
		{
			bool flag = (e.State & DrawItemState.Selected) != 0;
			Theme.Smooth(graphics);
			if (flag)
			{
				Theme.Panel(graphics, new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 5, e.Bounds.Height - 5), Theme.Selected, Theme.Selected, 8f);
			}
			TextRenderer.DrawText(graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height), flag ? Theme.Cyan : Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
	}
}
