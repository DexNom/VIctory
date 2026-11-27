using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class ModeCard : Control
{
	public string Title;

	public string Tag2;

	public string Line;

	private bool selected;

	private readonly Mix hv;

	private readonly Mix dn;

	private readonly Mix sel;

	public bool Selected
	{
		get
		{
			return selected;
		}
		set
		{
			if (selected != value)
			{
				selected = value;
				sel.To(value ? 1f : 0f, 240);
				Invalidate();
			}
		}
	}

	public ModeCard()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		hv = new Mix(this);
		dn = new Mix(this);
		sel = new Mix(this);
		Cursor = Cursors.Hand;
		Size = new Size(120, 78);
		Font = Theme.Mono(8.5f);
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

	protected override void OnEnabledChanged(EventArgs e)
	{
		Invalidate();
		base.OnEnabledChanged(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.ClearBack(this, graphics);
		Theme.Smooth(graphics);
		if (!sel.Running)
		{
			sel.Set(selected ? 1f : 0f);
		}
		float v = sel.V;
		float num = Math.Max(hv.V, dn.V);
		using (SolidBrush brush = new SolidBrush(Fx.Lerp(Theme.Rule, Theme.LineStrong, num)))
		{
			graphics.FillRectangle(brush, 0, Height - 1, Width, 1);
		}
		if (v > 0.01f)
		{
			using SolidBrush brush2 = new SolidBrush(Theme.Cyan);
			graphics.FillRectangle(brush2, 0f, Height - 3, (float)Width * v, 3f);
		}
		StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
		stringFormat.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat.HotkeyPrefix = HotkeyPrefix.None;
		using StringFormat format = stringFormat;
		float num2 = 14f;
		Font font = Theme.UiBold(num2);
		while (num2 > 9f && Theme.TextW(graphics, Title ?? "", font) > (float)(Width - 4))
		{
			font.Dispose();
			num2 -= 0.5f;
			font = Theme.UiBold(num2);
		}
		using (font)
		{
			using SolidBrush brush3 = new SolidBrush((!Enabled) ? Theme.Dim : Fx.Lerp(Fx.Lerp(Theme.Dim, Theme.Text, num), Theme.Cyan, v));
			graphics.DrawString(Title ?? "", font, brush3, new RectangleF(1f, 6f - num * 1f, Math.Max(10, Width - 2), 26f), format);
		}
		using (Font font2 = Theme.Ui(8.75f))
		{
			using SolidBrush brush4 = new SolidBrush(Fx.Lerp(Theme.Muted, Theme.Soft, Math.Max(v, num * 0.6f)));
			graphics.DrawString(Tag2 ?? "", font2, brush4, new RectangleF(1f, 35f, Math.Max(10, Width - 2), 16f), format);
		}
		using Font font3 = Theme.Ui(8f);
		using SolidBrush brush5 = new SolidBrush(Theme.Faint);
		graphics.DrawString(Line ?? "", font3, brush5, new RectangleF(1f, 53f, Math.Max(10, Width - 2), 16f), format);
	}
}
