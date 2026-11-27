using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class Dropdown : ComboBox
{
	private bool hover;

	public int Face => ItemHeight + 6;

	public Dropdown()
	{
		DropDownStyle = ComboBoxStyle.DropDownList;
		FlatStyle = FlatStyle.Flat;
		DrawMode = DrawMode.OwnerDrawFixed;
		ItemHeight = 24;
		BackColor = Theme.Card;
		ForeColor = Theme.Text;
		Font = Theme.Ui(9.5f);
		Kit.Dark(this, "DarkMode_CFD");
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		hover = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hover = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnDrawItem(DrawItemEventArgs e)
	{
		if (e.Index >= 0)
		{
			bool flag = (e.State & DrawItemState.ComboBoxEdit) == 0 && (e.State & DrawItemState.Selected) != 0;
			using (SolidBrush brush = new SolidBrush(flag ? Theme.Selected : Theme.Card))
			{
				e.Graphics.FillRectangle(brush, e.Bounds);
			}
			Rectangle bounds = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height);
			TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, bounds, flag ? Theme.Cyan : Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
	}

	protected override void WndProc(ref Message m)
	{
		base.WndProc(ref m);
		if (m.Msg != 15 || Width < 30 || Height < 10)
		{
			return;
		}
		try
		{
			using Graphics graphics = Graphics.FromHwnd(Handle);
			graphics.Clear((Parent != null) ? Parent.BackColor : Theme.Back);
			Theme.Smooth(graphics);
			RectangleF rectangleF = new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f);
			Graphics graphics2 = graphics;
			RectangleF rectangleF2 = rectangleF;
			Color color;
			if (Enabled)
			{
				color = ((hover || DroppedDown) ? Theme.CardHover : Theme.Card);
			}
			else
			{
				color = Theme.Back;
			}
			Graphics g = graphics2;
			RectangleF r = rectangleF2;
			Color fill = color;
			Color border;
			if (DroppedDown)
			{
				border = Theme.Cyan;
			}
			else
			{
				border = (hover ? Theme.LineStrong : Theme.Line);
			}
			Theme.Panel(g, r, fill, border, 10f);
			string text = ((SelectedIndex >= 0) ? GetItemText(SelectedItem) : "");
			TextRenderer.DrawText(graphics, text, Font, new Rectangle(12, 0, Width - 38, Height), Enabled ? Theme.Text : Theme.Dim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			float num = Width - 18;
			float num2 = (float)Height / 2f;
			Pen pen = new Pen(Enabled ? Theme.Cyan : Theme.Dim, 1.8f);
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			using Pen pen2 = pen;
			graphics.DrawLines(pen2, new PointF[3]
			{
				new PointF(num - 4f, num2 - 2f),
				new PointF(num, num2 + 2f),
				new PointF(num + 4f, num2 - 2f)
			});
		}
		catch (Exception)
		{
		}
	}
}
