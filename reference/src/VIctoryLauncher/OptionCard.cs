using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class OptionCard : CheckBox
{
	public string Line = "";

	private readonly Mix hv;

	private readonly Mix dn;

	private readonly Mix kn;

	public OptionCard()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		hv = new Mix(this);
		dn = new Mix(this);
		kn = new Mix(this);
		AutoSize = false;
		Size = new Size(400, 70);
		Cursor = Cursors.Hand;
		Font = Theme.UiBold(10f);
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

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.ClearBack(this, graphics);
		Theme.Smooth(graphics);
		if (!kn.Running)
		{
			kn.Set(Checked ? 1f : 0f);
		}
		float num = (Enabled ? hv.V : 0f);
		float num2 = (Enabled ? dn.V : 0f);
		if (num > 0.01f || num2 > 0.01f)
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb((int)(12f * num + 10f * num2), 255, 255, 255));
			graphics.FillRectangle(brush, 0, 0, Width, Height - 1);
		}
		using (Pen pen = new Pen(Fx.Lerp(Theme.Rule, Theme.LineStrong, num * 0.5f)))
		{
			graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
		}
		if (Focused && ShowFocusCues && Enabled)
		{
			Theme.FocusRing(graphics, new RectangleF(1.5f, 1.5f, (float)Width - 3f, (float)Height - 4f), 6f, Theme.Cyan);
		}
		Toggle.DrawSwitch(graphics, new RectangleF(Width - 38 - 4, (Height - 1 - 22) / 2, 38f, 22f), kn.V, num, Enabled, num2);
		string text = Text ?? "";
		bool flag = text.StartsWith("★");
		if (flag)
		{
			text = text.TrimStart('★', ' ');
		}
		float num3 = Math.Max(20, Width - 38 - 4 - 16);
		using (SolidBrush brush2 = new SolidBrush(Enabled ? Theme.Text : Theme.Dim))
		{
			StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
			stringFormat.Trimming = StringTrimming.EllipsisCharacter;
			stringFormat.FormatFlags = StringFormatFlags.NoWrap;
			stringFormat.LineAlignment = StringAlignment.Near;
			stringFormat.HotkeyPrefix = HotkeyPrefix.None;
			using StringFormat format = stringFormat;
			using Font f = Theme.UiBold(6.75f);
			string text2 = (flag ? Theme.TestedText.ToUpperInvariant() : "");
			float num4 = (flag ? (Theme.TrackedWidth(graphics, text2, f, 1.1f) + 14f) : 0f);
			float num5 = Math.Min(Theme.TextW(graphics, text, Font), num3 - num4);
			graphics.DrawString(text, Font, brush2, new RectangleF(2f, 10f, Math.Max(10f, num5 + 2f), 22f), format);
			if (flag && Enabled)
			{
				float num6 = 2f + num5 + 9f;
				using (SolidBrush brush3 = new SolidBrush(Theme.Cyan))
				{
					graphics.FillEllipse(brush3, num6, 15.5f, 5f, 5f);
				}
				using SolidBrush b = new SolidBrush(Theme.Cyan);
				Theme.Tracked(graphics, text2, f, b, num6 + 10f, 14f, 1.1f);
			}
		}
		using Font font = Theme.Ui(8.5f);
		using SolidBrush brush4 = new SolidBrush(Enabled ? Fx.Lerp(Theme.Muted, Theme.Soft, num * 0.6f) : Theme.KnobDisabled);
		StringFormat stringFormat2 = new StringFormat();
		stringFormat2.Trimming = StringTrimming.EllipsisWord;
		stringFormat2.FormatFlags = StringFormatFlags.LineLimit;
		using StringFormat format2 = stringFormat2;
		graphics.DrawString(Line ?? "", font, brush4, new RectangleF(2f, 32f, num3, Math.Max(10, Height - 35)), format2);
	}
}
