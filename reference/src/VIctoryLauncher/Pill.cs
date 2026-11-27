using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class Pill : Button
{
	public bool Link;

	private bool main;

	private bool shine;

	private bool shineReg;

	private float sweep = -1f;

	private readonly Mix hv;

	private readonly Mix dn;

	private readonly Mix mn;

	public bool Main
	{
		get
		{
			return main;
		}
		set
		{
			if (main != value)
			{
				main = value;
				mn.To(value ? 1f : 0f, 170);
				Invalidate();
				if (shine)
				{
					Anim.Poke(this);
				}
			}
		}
	}

	public bool Shine
	{
		get
		{
			return shine;
		}
		set
		{
			if (shine != value)
			{
				shine = value;
				if (value && !shineReg)
				{
					shineReg = true;
					Anim.Register(this, needsFocus: true, Frame);
				}
			}
		}
	}

	public Pill()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		hv = new Mix(this);
		dn = new Mix(this);
		mn = new Mix(this);
		FlatStyle = FlatStyle.Flat;
		FlatAppearance.BorderSize = 0;
		Cursor = Cursors.Hand;
		ForeColor = Theme.Text;
		Font = Theme.Ui(9.5f);
		Height = 34;
	}

	private int Frame(double t)
	{
		if (!Enabled || !main || Link)
		{
			if (sweep >= 0f)
			{
				sweep = -1f;
				Invalidate();
			}
			return 5000;
		}
		double num = t % 5.5;
		if (num < 1.05)
		{
			sweep = (float)(num / 1.05);
			Invalidate();
			return 33;
		}
		if (sweep >= 0f)
		{
			sweep = -1f;
			Invalidate();
		}
		return (int)((5.5 - num) * 1000.0) + 5;
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
		dn.To(0f, 150);
		base.OnMouseUp(e);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Space)
		{
			dn.To(1f, 70);
		}
		base.OnKeyDown(e);
	}

	protected override void OnKeyUp(KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Space)
		{
			dn.To(0f, 150);
		}
		base.OnKeyUp(e);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		if (!Enabled)
		{
			hv.Set(0f);
			dn.Set(0f);
			sweep = -1f;
		}
		else if (shine)
		{
			Anim.Poke(this);
		}
		Invalidate();
		base.OnEnabledChanged(e);
	}

	protected override void OnTextChanged(EventArgs e)
	{
		Invalidate();
		base.OnTextChanged(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.ClearBack(this, graphics);
		Theme.Smooth(graphics);
		float v = hv.V;
		float v2 = dn.V;
		float v3 = mn.V;
		float t = Math.Max(v, v2);
		float num = v2 * 1.4f;
		RectangleF r = new RectangleF(0.5f + num, 0.5f + num * 0.7f, (float)Width - 1.5f - 2f * num, (float)Height - 1.5f - 1.4f * num);
		float radius = r.Height / 2f;
		string s = Text ?? "";
		if (Link)
		{
			using (SolidBrush brush = new SolidBrush((!Enabled) ? Theme.KnobDisabled : Fx.Lerp(Fx.Lerp(Theme.Dim, Theme.Text, t), Theme.Cyan, v3)))
			{
				StringFormat stringFormat = new StringFormat();
				stringFormat.Alignment = StringAlignment.Center;
				stringFormat.LineAlignment = StringAlignment.Center;
				stringFormat.Trimming = StringTrimming.EllipsisCharacter;
				stringFormat.FormatFlags = StringFormatFlags.NoWrap;
				stringFormat.HotkeyPrefix = HotkeyPrefix.None;
				using StringFormat format = stringFormat;
				bool flag = v3 >= 0.5f;
				Font font = (flag ? new Font(Font.FontFamily, Font.Size, FontStyle.Bold) : Font);
				graphics.DrawString(s, font, brush, new RectangleF(0f, 0f, Width, Height - 2), format);
				float num2 = Math.Max(v3, v);
				if (num2 > 0.01f && Enabled)
				{
					float num3 = Math.Min(Width - 2, Theme.TextW(graphics, s, font)) * Fx.Out(num2);
					using SolidBrush brush2 = new SolidBrush(Fx.Lerp(Theme.LineStrong, Theme.Cyan, v3));
					graphics.FillRectangle(brush2, ((float)Width - num3) / 2f, Height - 5, num3, 2f);
				}
				if (flag)
				{
					font.Dispose();
				}
			}
			if (Focused && ShowFocusCues && Enabled)
			{
				Theme.FocusRing(graphics, new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f), 6f, Theme.Cyan);
			}
			return;
		}
		if (!Enabled)
		{
			Theme.Panel(graphics, r, Theme.Wash, Color.FromArgb(60, 255, 255, 255), radius);
		}
		else
		{
			Color a = Color.FromArgb((int)(22f * v + 20f * v2), 255, 255, 255);
			Color a2 = Fx.Lerp(Theme.Outline, Color.FromArgb(235, 255, 255, 255), t);
			Color b = Fx.Lerp(Fx.Lerp(Theme.Cyan, Theme.AccentHover, v), Theme.Violet, v2);
			Theme.Panel(graphics, r, Fx.Lerp(a, b, v3), Fx.Lerp(a2, b, v3), radius);
			if (sweep >= 0f && v3 > 0.9f)
			{
				using GraphicsPath path = Theme.Round(r, radius);
				GraphicsState gstate = graphics.Save();
				graphics.SetClip(path, CombineMode.Intersect);
				float num4 = r.Height * 1.7f;
				float num5 = r.X - num4 + (r.Width + num4) * Fx.InOut(sweep);
				ColorBlend colorBlend = new ColorBlend(3);
				colorBlend.Positions = new float[3] { 0f, 0.5f, 1f };
				colorBlend.Colors = new Color[3]
				{
					Color.FromArgb(0, 255, 255, 255),
					Color.FromArgb(104, 255, 255, 255),
					Color.FromArgb(0, 255, 255, 255)
				};
				ColorBlend interpolationColors = colorBlend;
				LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new RectangleF(num5, r.Y, num4, r.Height), Color.White, Color.White, LinearGradientMode.Horizontal);
				linearGradientBrush.InterpolationColors = interpolationColors;
				using (LinearGradientBrush brush3 = linearGradientBrush)
				{
					graphics.FillRectangle(brush3, num5, r.Y - 1f, num4, r.Height + 2f);
				}
				graphics.Restore(gstate);
			}
		}
		if (Focused && ShowFocusCues && Enabled)
		{
			Theme.FocusRing(graphics, new RectangleF(3.5f, 3.5f, (float)Width - 7.5f, (float)Height - 7.5f), ((float)Height - 7.5f) / 2f, main ? Theme.OnAccent : Theme.Cyan);
		}
		using SolidBrush brush4 = new SolidBrush(Enabled ? Fx.Lerp(ForeColor, Theme.OnAccent, v3) : Theme.Dim);
		StringFormat stringFormat2 = new StringFormat();
		stringFormat2.Alignment = StringAlignment.Center;
		stringFormat2.LineAlignment = StringAlignment.Center;
		stringFormat2.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat2.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat2.HotkeyPrefix = HotkeyPrefix.None;
		using StringFormat format2 = stringFormat2;
		Font font2 = Font;
		Font font3 = null;
		float num6 = Math.Max(10f, r.Width - Math.Min(24f, r.Height * 0.6f));
		float num7 = Font.Size - 0.5f;
		while (num7 >= 7.5f && graphics.MeasureString(s, font2).Width > num6)
		{
			font3?.Dispose();
			font2 = (font3 = new Font(Font.FontFamily, num7, Font.Style));
			num7 -= 0.5f;
		}
		graphics.DrawString(s, font2, brush4, new RectangleF(r.X + 6f, r.Y, Math.Max(10f, r.Width - 12f), r.Height), format2);
		font3?.Dispose();
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
}
