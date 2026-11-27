using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class LoadBar : Control
{
	private float done;

	private float sheen = -1f;

	private readonly Mix shown;

	public LoadBar()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Font = Theme.Mono(8.75f);
		shown = new Mix(this);
		Anim.Register(this, needsFocus: false, Frame);
	}

	public void Set(float part, string text)
	{
		float num = done;
		done = Math.Max(0f, Math.Min(1f, part));
		Text = text;
		if (!Visible || done < num - 0.001f)
		{
			shown.Set(done);
			Invalidate();
		}
		else
		{
			shown.To(done, 280);
		}
		Anim.Poke(this);
	}

	private int Frame(double t)
	{
		if (shown.V > 0.002f && shown.V < 0.998f && done < 0.999f)
		{
			sheen = (float)(t % 1.7 / 1.7);
			Invalidate(new Rectangle(0, Math.Max(0, Height - 9), Width, 9));
			return 40;
		}
		if (sheen >= 0f)
		{
			sheen = -1f;
			Invalidate(new Rectangle(0, Math.Max(0, Height - 9), Width, 9));
		}
		return 5000;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.ClearBack(this, graphics);
		Theme.Smooth(graphics);
		if (!shown.Running && shown.V != done)
		{
			shown.Set(done);
		}
		float v = shown.V;
		float num = Math.Max(6, Width - 1);
		RectangleF r = new RectangleF(0f, Height - 7, num, 6f);
		using (GraphicsPath path = Theme.Round(r, 3f))
		{
			using SolidBrush brush = new SolidBrush(Theme.Card);
			graphics.FillPath(brush, path);
		}
		if (v > 0.001f)
		{
			RectangleF r2 = new RectangleF(0f, Height - 7, Math.Min(num, Math.Max(6f, num * v)), 6f);
			using GraphicsPath path2 = Theme.Round(r2, 3f);
			using (SolidBrush brush2 = new SolidBrush(Theme.Cyan))
			{
				graphics.FillPath(brush2, path2);
			}
			if (sheen >= 0f)
			{
				GraphicsState gstate = graphics.Save();
				graphics.SetClip(path2, CombineMode.Intersect);
				float num2 = 90f;
				float num3 = r2.X - num2 + (r2.Width + num2) * sheen;
				ColorBlend colorBlend = new ColorBlend(3);
				colorBlend.Positions = new float[3] { 0f, 0.5f, 1f };
				colorBlend.Colors = new Color[3]
				{
					Color.FromArgb(0, 255, 255, 255),
					Color.FromArgb(140, 255, 255, 255),
					Color.FromArgb(0, 255, 255, 255)
				};
				ColorBlend interpolationColors = colorBlend;
				LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new RectangleF(num3, r2.Y, num2, r2.Height), Color.White, Color.White, LinearGradientMode.Horizontal);
				linearGradientBrush.InterpolationColors = interpolationColors;
				using (LinearGradientBrush brush3 = linearGradientBrush)
				{
					graphics.FillRectangle(brush3, num3, r2.Y - 1f, num2, r2.Height + 2f);
				}
				graphics.Restore(gstate);
			}
		}
		string s = (int)(v * 100f + 0.5f) + "%";
		float num4 = (float)Math.Ceiling(graphics.MeasureString(s, Font).Width);
		float num5 = Math.Max(10, Height - 9);
		StringFormat stringFormat = new StringFormat();
		stringFormat.LineAlignment = StringAlignment.Center;
		stringFormat.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat.HotkeyPrefix = HotkeyPrefix.None;
		using StringFormat format = stringFormat;
		using (SolidBrush brush4 = new SolidBrush(Theme.Text))
		{
			graphics.DrawString(Text ?? "", Font, brush4, new RectangleF(0f, 0f, Math.Max(10f, (float)Width - num4 - 10f), num5), format);
		}
		using SolidBrush brush5 = new SolidBrush(Theme.Dim);
		StringFormat stringFormat2 = new StringFormat(format);
		stringFormat2.Alignment = StringAlignment.Far;
		using StringFormat format2 = stringFormat2;
		graphics.DrawString(s, Font, brush5, new RectangleF((float)Width - num4 - 4f, 0f, num4 + 4f, num5), format2);
	}
}
