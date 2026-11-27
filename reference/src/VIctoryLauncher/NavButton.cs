using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class NavButton : Control
{
	public string Glyph;

	private bool active;

	private readonly Mix hv;

	private readonly Mix dn;

	private readonly Mix act;

	public bool Active
	{
		get
		{
			return active;
		}
		set
		{
			if (active != value)
			{
				active = value;
				act.To(value ? 1f : 0f, 220);
				Invalidate();
			}
		}
	}

	public NavButton()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		hv = new Mix(this);
		dn = new Mix(this);
		act = new Mix(this);
		Cursor = Cursors.Hand;
		Size = new Size(196, 38);
		Font = Theme.Ui(10.5f);
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

	protected override void OnTextChanged(EventArgs e)
	{
		Invalidate();
		base.OnTextChanged(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(Theme.Side);
		Theme.Smooth(graphics);
		if (!act.Running)
		{
			act.Set(active ? 1f : 0f);
		}
		float v = act.V;
		float num = Math.Max(hv.V, dn.V);
		float num2 = (float)Height / 2f;
		float num3 = 6f * Fx.Clamp(v, 0f, 1f);
		if (num3 > 0.2f)
		{
			using SolidBrush brush = new SolidBrush(Theme.Cyan);
			graphics.FillEllipse(brush, 15f - num3 / 2f, num2 - num3 / 2f, num3, num3);
		}
		if (num > 0.02f && v < 0.98f)
		{
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb((int)(90f * num * (1f - v)), Theme.Cyan));
			graphics.FillEllipse(brush2, 12f, num2 - 3f, 6f, 6f);
		}
		float num4 = 2f * num * (1f - v);
		using (Font font = Theme.Mono(8f))
		{
			using SolidBrush brush3 = new SolidBrush(Fx.Lerp(Fx.Lerp(Theme.Faint, Theme.Muted, num), Theme.Cyan, v));
			StringFormat stringFormat = new StringFormat();
			stringFormat.Alignment = StringAlignment.Near;
			stringFormat.LineAlignment = StringAlignment.Center;
			stringFormat.FormatFlags = StringFormatFlags.NoWrap;
			using StringFormat format = stringFormat;
			graphics.DrawString(Glyph ?? "", font, brush3, new RectangleF(26f + num4, 0f, 26f, Height), format);
		}
		Font font2 = ((v >= 0.5f) ? Theme.UiBold(10.5f) : null);
		using (SolidBrush brush4 = new SolidBrush(Fx.Lerp(Fx.Lerp(Theme.Muted, Theme.Text, num), Theme.Cyan, v)))
		{
			StringFormat stringFormat2 = new StringFormat();
			stringFormat2.LineAlignment = StringAlignment.Center;
			stringFormat2.Trimming = StringTrimming.EllipsisCharacter;
			stringFormat2.FormatFlags = StringFormatFlags.NoWrap;
			stringFormat2.HotkeyPrefix = HotkeyPrefix.None;
			using StringFormat format2 = stringFormat2;
			graphics.DrawString(Text ?? "", font2 ?? Font, brush4, new RectangleF(54f + num4, 0f, Math.Max(10, Width - 60), Height), format2);
		}
		font2?.Dispose();
	}
}
