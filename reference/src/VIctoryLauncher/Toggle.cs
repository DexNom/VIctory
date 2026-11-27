using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class Toggle : CheckBox
{
	private readonly Mix hv;

	private readonly Mix dn;

	private readonly Mix kn;

	public Toggle()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		hv = new Mix(this);
		dn = new Mix(this);
		kn = new Mix(this);
		AutoSize = false;
		Height = 30;
		Cursor = Cursors.Hand;
		BackColor = Color.Transparent;
		ForeColor = Theme.Text;
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		hv.To(1f, 140);
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hv.To(0f, 200);
		dn.To(0f, 120);
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		dn.To(1f, 70);
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		dn.To(0f, 140);
		base.OnMouseUp(e);
	}

	protected override void OnCheckedChanged(EventArgs e)
	{
		kn.To(Checked ? 1f : 0f, 190);
		base.OnCheckedChanged(e);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		Invalidate();
		base.OnEnabledChanged(e);
	}

	protected override void OnTextChanged(EventArgs e)
	{
		Invalidate();
		base.OnTextChanged(e);
	}

	protected override void OnGotFocus(EventArgs e)
	{
		Invalidate();
		base.OnGotFocus(e);
	}

	protected override void OnLostFocus(EventArgs e)
	{
		Invalidate();
		base.OnLostFocus(e);
	}

	public static void DrawSwitch(Graphics g, RectangleF track, bool on, bool hover, bool enabled)
	{
		DrawSwitch(g, track, on ? 1f : 0f, hover ? 1f : 0f, enabled, 0f);
	}

	public static void DrawSwitch(Graphics g, RectangleF track, bool on, bool hover, bool enabled, bool pressed)
	{
		DrawSwitch(g, track, on ? 1f : 0f, hover ? 1f : 0f, enabled, pressed ? 1f : 0f);
	}

	public static void DrawSwitch(Graphics g, RectangleF track, float on, float hover, bool enabled, float pressed)
	{
		Color a = Fx.Lerp(Theme.TrackOff, Theme.TrackHover, hover);
		Color b = Fx.Lerp(Fx.Lerp(Theme.Cyan, Theme.AccentHover, hover), Theme.Violet, pressed);
		using (GraphicsPath path = Theme.Round(track, track.Height / 2f))
		{
			using SolidBrush brush = new SolidBrush((!enabled) ? Theme.TrackDisabled : Fx.Lerp(a, b, on));
			g.FillPath(brush, path);
		}
		float num = track.Height - 4f;
		float num2 = (enabled ? (num + 3f * pressed) : num);
		float num3 = track.X + 2f + (track.Width - num2 - 4f) * on;
		if (enabled)
		{
			using SolidBrush brush2 = new SolidBrush(Theme.Shadow);
			g.FillEllipse(brush2, num3, track.Y + 3f, num2, num);
		}
		using SolidBrush brush3 = new SolidBrush((!enabled) ? Theme.KnobDisabled : Fx.Lerp(Theme.Knob, Theme.OnAccent, on));
		g.FillEllipse(brush3, num3, track.Y + 2f, num2, num);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.ClearBack(this, graphics);
		Theme.Smooth(graphics);
		if (!kn.Running)
		{
			kn.Set(Checked ? 1f : 0f);
		}
		RectangleF track = new RectangleF(2f, (Height - 20) / 2, 36f, 20f);
		if (Focused && ShowFocusCues && Enabled)
		{
			Theme.FocusRing(graphics, new RectangleF(track.X - 2f, track.Y - 2f, track.Width + 4f, track.Height + 4f), 12f, Theme.Text);
		}
		DrawSwitch(graphics, track, kn.V, hv.V, Enabled, dn.V);
		RectangleF layoutRectangle = new RectangleF(48f, 0f, Math.Max(0, Width - 50), Height);
		using SolidBrush brush = new SolidBrush(Enabled ? ForeColor : Theme.Dim);
		StringFormat stringFormat = new StringFormat();
		stringFormat.LineAlignment = StringAlignment.Center;
		stringFormat.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat.HotkeyPrefix = HotkeyPrefix.None;
		using StringFormat format = stringFormat;
		graphics.DrawString(Text ?? "", Font, brush, layoutRectangle, format);
	}
}
