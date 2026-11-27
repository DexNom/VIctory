using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class Poster : Control, IBackdrop
{
	private Bitmap bg;

	private int bgW;

	private int bgH;

	private int bgKey = int.MinValue;

	protected int gMx;

	protected int gMy;

	protected int gPx;

	protected int gPy;

	protected int gBreath;

	protected readonly Mix intro;

	protected float Intro => intro.V;

	public Poster()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Width = 900;
		intro = new Mix(this);
		intro.Set(1f);
	}

	public void PlayIntro()
	{
		intro.Set(0f);
		intro.To(1f, 240);
	}

	protected int BackKey()
	{
		return (gMx * 73856093) ^ (gMy * 19349663) ^ (gPx * 83492791) ^ (gPy * 2654435) ^ (gBreath * 97);
	}

	private void Rebuild(int key)
	{
		if (bg == null || bgW != Width || bgH != Height)
		{
			if (bg != null)
			{
				bg.Dispose();
			}
			bg = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
			bgW = Width;
			bgH = Height;
		}
		bgKey = key;
		float num = (float)gBreath / 8f;
		float num2 = 1f + 0.05f * num;
		float num3 = 1f + 0.03f * num;
		float num4 = 1f - 0.05f * num;
		float num5 = 1f - 0.03f * num;
		using Graphics graphics = Graphics.FromImage(bg);
		graphics.Clear(Theme.Back);
		Theme.Smooth(graphics);
		Theme.Glow(graphics, (float)Width * 0.2f + (float)gMx, (float)Height * 0.62f + (float)gMy, (float)Width * 0.55f * num3, (float)Height * 0.98f * num3, Theme.GlowMagenta, (int)(170f * num2));
		Theme.Glow(graphics, (float)Width * 0.17f + (float)gMx * 1.5f, (float)Height * 0.68f + (float)gMy * 1.5f, (float)Width * 0.26f * num3, (float)Height * 0.6f * num3, Color.FromArgb(246, 74, 196), (int)(120f * num2));
		Theme.Glow(graphics, (float)Width * 0.98f + (float)gPx, (float)Height * 0.02f + (float)gPy, (float)Width * 0.32f * num5, (float)Height * 0.8f * num5, Theme.GlowPeach, (int)(112f * num4));
		int num6 = Math.Min(96, Height / 2);
		using LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(0, Height - num6, Math.Max(1, Width), num6 + 1), Color.FromArgb(0, Theme.Back), Theme.Back, 90f);
		graphics.FillRectangle(brush, 0, Height - num6, Width, num6 + 1);
	}

	public virtual void PaintBackdrop(Graphics g, Rectangle area)
	{
		if (Width > 0 && Height > 0)
		{
			int num = BackKey();
			if (bg == null || bgW != Width || bgH != Height || bgKey != num)
			{
				Rebuild(num);
			}
			Rectangle destRect = Rectangle.Intersect(area, new Rectangle(0, 0, Width, Height));
			if (destRect.Width > 0 && destRect.Height > 0)
			{
				g.DrawImage(bg, destRect, destRect.X, destRect.Y, destRect.Width, destRect.Height, GraphicsUnit.Pixel);
			}
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && bg != null)
		{
			bg.Dispose();
			bg = null;
		}
		base.Dispose(disposing);
	}

	public static Font Heavy(float pt)
	{
		return new Font("Segoe UI Black", pt);
	}

	public static Font Serif(float pt)
	{
		return new Font("Georgia", pt, FontStyle.Italic);
	}
}
