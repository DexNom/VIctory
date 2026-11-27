using System;
using System.Drawing;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class FootMeta : Control
{
	public string State = "";

	public string Preset = "";

	public string Engine = "";

	public string Hardware = "";

	public string Why = "";

	public string[] Labels = new string[5] { "State", "Preset", "Engine", "Hardware", "Next step" };

	public Color StateColor = Theme.Good;

	public FootMeta()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	public void Set(string state, Color stateColor, string preset, string engine, string hardware, string why)
	{
		State = state;
		StateColor = stateColor;
		Preset = preset;
		Engine = engine;
		Hardware = hardware;
		Why = why ?? "";
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(Theme.Back);
		Theme.Smooth(graphics);
		float num = 13f;
		Theme.Meta(graphics, Labels[0], State, StateColor, new RectangleF(40f, num, 140f, 34f), StringAlignment.Near);
		if (Why.Length > 0)
		{
			Theme.Meta(graphics, Labels[4], Why, Theme.Dim, new RectangleF(200f, num, Math.Max(40, Width - 200 - 12), 34f), StringAlignment.Near);
			return;
		}
		float num2 = Width - 200 - 12;
		float num3 = Math.Min(240f, num2 * 0.34f);
		float num4 = Math.Min(190f, num2 * 0.26f);
		Theme.Meta(graphics, Labels[1], Preset, Theme.Dim, new RectangleF(200f, num, num3 - 14f, 34f), StringAlignment.Near);
		Theme.Meta(graphics, Labels[2], Engine, Theme.Dim, new RectangleF(200f + num3, num, num4 - 14f, 34f), StringAlignment.Near);
		if (num2 > 560f)
		{
			Theme.Meta(graphics, Labels[3], Hardware, Theme.Dim, new RectangleF(200f + num3 + num4, num, num2 - num3 - num4, 34f), StringAlignment.Near);
		}
	}
}
