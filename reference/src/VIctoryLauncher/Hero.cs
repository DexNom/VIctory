using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class Hero : Poster
{
	public const int Rows = 362;

	public const int TopRule = 62;

	public const int BottomRule = 314;

	private const int BN = 64;

	public string Brand = "";

	public string BrandValue = "";

	public string RightLabel = "";

	public string RightValue = "";

	public string Title = "";

	public string Accent = "";

	public string Status = "";

	public string[] FactLabels = new string[0];

	public string[] FactValues = new string[0];

	public Color[] FactColors = new Color[0];

	public string[] StepNames = new string[0];

	public string[] Steps = new string[0];

	private int[] states = new int[0];

	private bool statesSeen;

	private readonly Mix[] dot = new Mix[4];

	private readonly Mix[] burst = new Mix[4];

	private double orbitT;

	private float leanX;

	private float leanY;

	private float tLeanX;

	private float tLeanY;

	private long rippleAt = -100000L;

	private Bitmap blob;

	private readonly byte[] blobBuf = new byte[16384];

	private readonly float[] bx = new float[3];

	private readonly float[] by = new float[3];

	private readonly float[] br2 = new float[3];

	private static readonly Pen ringPen = new Pen(Theme.Ring, 1f);

	private static readonly Pen arcSoft = new Pen(Color.FromArgb(64, 255, 255, 255), 1.6f)
	{
		StartCap = LineCap.Round,
		EndCap = LineCap.Round
	};

	private static readonly Pen arcBright = new Pen(Color.FromArgb(200, 255, 255, 255), 2.2f)
	{
		StartCap = LineCap.Round,
		EndCap = LineCap.Round
	};

	public int[] States
	{
		get
		{
			return states;
		}
		set
		{
			SetStates(value);
		}
	}

	public Hero()
	{
		Height = 362;
		for (int i = 0; i < 4; i++)
		{
			dot[i] = new Mix(InvalOrbit);
			burst[i] = new Mix(InvalOrbit);
		}
		Anim.Register(this, needsFocus: true, Frame);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && blob != null)
		{
			blob.Dispose();
			blob = null;
		}
		base.Dispose(disposing);
	}

	private Rectangle OrbitRect()
	{
		float num = Width - 270;
		return new Rectangle((int)(num - 176f - 120f), 63, 592, 251);
	}

	private void InvalOrbit()
	{
		if (!IsDisposed)
		{
			Invalidate(OrbitRect());
		}
	}

	private void SetStates(int[] value)
	{
		int[] array = states;
		states = value ?? new int[0];
		for (int i = 0; i < 4 && i < states.Length; i++)
		{
			bool flag = states[i] == 1;
			bool flag2 = i < array.Length && array[i] == 1;
			if (!statesSeen)
			{
				dot[i].Set(flag ? 1f : 0f);
				continue;
			}
			dot[i].To(flag ? 1f : 0f, 480);
			if (flag && !flag2)
			{
				burst[i].Set(0f);
				burst[i].To(1f, 950);
			}
		}
		if (states.Length > 0)
		{
			statesSeen = true;
		}
	}

	public void Ripple()
	{
		if (Anim.Live)
		{
			rippleAt = Anim.Now;
			Anim.Poke(this);
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		float num = e.X - (Width - 200);
		float num2 = (float)e.Y - 188f - 38f;
		float num3 = (float)Math.Sqrt(num * num + num2 * num2);
		if (num3 < 1f)
		{
			tLeanX = (tLeanY = 0f);
		}
		else
		{
			float num4 = Math.Min(10f, num3 * 0.03f);
			tLeanX = num / num3 * num4;
			tLeanY = num2 / num3 * num4;
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		tLeanX = (tLeanY = 0f);
		base.OnMouseLeave(e);
	}

	private static int Q(double v)
	{
		return (int)Math.Round(v / 3.0) * 3;
	}

	private int Frame(double t)
	{
		orbitT = t;
		int num = BackKey();
		gBreath = (int)Math.Round(8.0 * Math.Sin(t * (Math.PI * 2.0) / 11.0));
		gMx = Q((double)Width * 0.03 * Math.Sin(t * (Math.PI * 2.0) / 37.0) + (double)Width * 0.012 * Math.Sin(t * (Math.PI * 2.0) / 23.0 + 1.3));
		gMy = Q((double)Height * 0.06 * Math.Sin(t * (Math.PI * 2.0) / 31.0 + 0.7) + (double)Height * 0.02 * Math.Sin(t * (Math.PI * 2.0) / 19.0));
		gPx = Q((double)(-Width) * 0.024 * Math.Sin(t * (Math.PI * 2.0) / 43.0 + 2.1) + (double)Width * 0.01 * Math.Sin(t * (Math.PI * 2.0) / 27.0));
		gPy = Q((double)Height * 0.08 * Math.Sin(t * (Math.PI * 2.0) / 41.0 + 0.2));
		leanX += (tLeanX - leanX) * 0.16f;
		leanY += (tLeanY - leanY) * 0.16f;
		UpdateBlob(t);
		if (BackKey() != num)
		{
			Invalidate();
		}
		else
		{
			Invalidate(OrbitRect());
		}
		return 50;
	}

	private void UpdateBlob(double t)
	{
		if (blob == null)
		{
			blob = new Bitmap(64, 64, PixelFormat.Format32bppPArgb);
		}
		double num = (double)(Anim.Now - rippleAt) / 1000.0;
		float num2 = ((num < 1.8) ? ((float)(Math.Exp((0.0 - num) * 2.4) * Math.Sin(num * 12.0))) : 0f);
		float num3 = leanX / 10f;
		float num4 = leanY / 10f;
		double[] array = new double[3] { 26.0, 19.0, 15.0 };
		double[] array2 = new double[3] { 0.18, 0.33, 0.3 };
		double[] array3 = new double[3] { 0.4, 2.2, 4.1 };
		float[] array4 = new float[3] { 0.45f, 0.35f, 0.27f };
		for (int i = 0; i < 3; i++)
		{
			double num5 = t * 2.0 * Math.PI / array[i] * (double)((i != 2) ? 1 : (-1)) + array3[i];
			bx[i] = (float)(Math.Cos(num5) * array2[i]) + num3 * 0.05f * (float)(i + 1);
			by[i] = (float)(Math.Sin(num5 * 0.85 + (double)i) * array2[i] * 0.9) + num4 * 0.05f * (float)(i + 1);
			float num6 = array4[i] * (1f + 0.05f * (float)Math.Sin(t * 0.9 + (double)i * 2.0) + 0.07f * num2);
			br2[i] = num6 * num6;
		}
		for (int j = 0; j < 64; j++)
		{
			float num7 = ((float)j + 0.5f) / 64f * 2f - 1f;
			for (int k = 0; k < 64; k++)
			{
				float num8 = ((float)k + 0.5f) / 64f * 2f - 1f;
				float num9 = 0f;
				float num10 = 0f;
				float num11 = 0f;
				float num12 = 0f;
				for (int l = 0; l < 3; l++)
				{
					float num13 = num8 - bx[l];
					float num14 = num7 - by[l];
					float num15 = br2[l] / (num13 * num13 + num14 * num14 + 0.0006f);
					num9 += num15;
					float num16 = num15 * num15;
					switch (l)
					{
					case 0:
						num10 = num16;
						break;
					case 1:
						num11 = num16;
						break;
					default:
						num12 = num16;
						break;
					}
				}
				int num17 = (j * 64 + k) * 4;
				float num18 = 1f - Fx.SmoothStep(0.55f, 0.98f, (float)Math.Sqrt(num8 * num8 + num7 * num7));
				float num19 = Fx.SmoothStep(0.8f, 1.12f, num9) * num18;
				float val = Fx.SmoothStep(0.35f, 1f, num9) * 0.13f * num18;
				float num20 = Math.Max(num19, val);
				if (num20 <= 0.002f)
				{
					blobBuf[num17] = (blobBuf[num17 + 1] = (blobBuf[num17 + 2] = (blobBuf[num17 + 3] = 0)));
					continue;
				}
				float num21 = num10 + num11 + num12 + 1E-06f;
				float num22 = (num10 * 255f + num11 * 255f + num12 * 255f) / num21;
				float num23 = (num10 * 246f + num11 * 244f + num12 * 178f) / num21;
				float num24 = (num10 * 252f + num11 * 163f + num12 * 166f) / num21;
				float num25 = Fx.SmoothStep(1.9f, 3.6f, num9) * 0.4f;
				num22 += (255f - num22) * num25;
				num23 += (246f - num23) * num25;
				num24 += (252f - num24) * num25;
				num22 += (255f - num22) * (1f - num19);
				num23 += (178f - num23) * (1f - num19);
				num24 += (166f - num24) * (1f - num19);
				blobBuf[num17] = (byte)(num24 * num20);
				blobBuf[num17 + 1] = (byte)(num23 * num20);
				blobBuf[num17 + 2] = (byte)(num22 * num20);
				blobBuf[num17 + 3] = (byte)(num20 * 255f);
			}
		}
		BitmapData bitmapData = blob.LockBits(new Rectangle(0, 0, 64, 64), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
		Marshal.Copy(blobBuf, 0, bitmapData.Scan0, blobBuf.Length);
		blob.UnlockBits(bitmapData);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Rectangle clipRectangle = e.ClipRectangle;
		PaintBackdrop(graphics, clipRectangle);
		Theme.Smooth(graphics);
		float num = 40f;
		float num2 = Width - 40;
		float num3 = Intro;
		if (clipRectangle.Top < 62)
		{
			Theme.Meta(graphics, Brand, BrandValue, Theme.Muted, new RectangleF(num, 15f, 320f, 34f), StringAlignment.Near);
			if (!string.IsNullOrEmpty(RightLabel))
			{
				Theme.Meta(graphics, RightLabel, RightValue, Theme.Muted, new RectangleF(num2 - 320f, 15f, 320f, 34f), StringAlignment.Far);
			}
		}
		using (Pen pen = new Pen(Theme.HairWhite))
		{
			graphics.DrawLine(pen, 0, 62, Width, 62);
			graphics.DrawLine(pen, 0, 314, Width, 314);
		}
		if (clipRectangle.IntersectsWith(OrbitRect()))
		{
			DrawOrbits(graphics);
		}
		if (clipRectangle.IntersectsWith(new Rectangle(0, 62, Math.Max(10, Width - 440), 252)))
		{
			graphics.TranslateTransform(0f, (1f - num3) * 10f);
			DrawHeadline(graphics, num, num3);
			using (Font font = Theme.Ui(10.5f))
			{
				using SolidBrush brush = new SolidBrush(Fx.Alpha(Theme.Soft, num3));
				StringFormat stringFormat = new StringFormat();
				stringFormat.Trimming = StringTrimming.EllipsisWord;
				stringFormat.FormatFlags = StringFormatFlags.LineLimit;
				stringFormat.HotkeyPrefix = HotkeyPrefix.None;
				using StringFormat format = stringFormat;
				graphics.DrawString(Status ?? "", font, brush, new RectangleF(num, 204f, Math.Max(100f, Math.Min(440f, (float)(Width - 446) - num)), 44f), format);
			}
			graphics.ResetTransform();
		}
		int num4 = FactLabels.Length;
		if (num4 > 0 && clipRectangle.Bottom > 314)
		{
			float num5 = (num2 - num) / (float)num4;
			for (int i = 0; i < num4; i++)
			{
				StringAlignment align = ((i != 0) ? ((i != num4 - 1) ? StringAlignment.Center : StringAlignment.Far) : StringAlignment.Near);
				Color valueColor = ((i < FactColors.Length) ? FactColors[i] : Theme.Muted);
				Theme.Meta(graphics, FactLabels[i], (i < FactValues.Length) ? FactValues[i] : "", valueColor, new RectangleF(num + (float)i * num5, 324f, num5 - 10f, 34f), align);
			}
		}
	}

	private void DrawHeadline(Graphics g, float lx, float k)
	{
		float num = Math.Max(160f, (float)(Width - 446) - lx - 16f);
		float num2 = 44f;
		Font font = Poster.Heavy(num2);
		Font font2 = Poster.Serif(num2);
		while (num2 > 24f && (Theme.TrackedWidth(g, Title ?? "", font, -1.5f) > num || Theme.TextW(g, Accent ?? "", font2) > num))
		{
			num2 -= 2f;
			font.Dispose();
			font2.Dispose();
			font = Poster.Heavy(num2);
			font2 = Poster.Serif(num2);
		}
		float num3 = num2 / 44f;
		float num4 = 130f;
		using (font)
		{
			using (font2)
			{
				using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Text, k)))
				{
					Theme.Tracked(g, Title ?? "", font, b, lx - 1f, num4 - Theme.AscentPx(g, font), -1.5f);
				}
				using SolidBrush brush = new SolidBrush(Fx.Alpha(Theme.Cyan, k));
				StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
				stringFormat.FormatFlags = StringFormatFlags.NoWrap;
				using StringFormat format = stringFormat;
				g.DrawString(Accent ?? "", font2, brush, lx - 1f, num4 + 56f * num3 - Theme.AscentPx(g, font2), format);
			}
		}
	}

	private void DrawOrbits(Graphics g)
	{
		float num = Width - 270;
		float num2 = 192f;
		float num3 = 176f;
		float num4 = 110f;
		double num5 = orbitT;
		GraphicsState gstate = g.Save();
		g.SetClip(new RectangleF(0f, 63f, Width, 251f), CombineMode.Intersect);
		g.DrawEllipse(ringPen, num - num3, num2 - num3, num3 * 2f, num3 * 2f);
		g.DrawEllipse(ringPen, num - num4, num2 - num4, num4 * 2f, num4 * 2f);
		float num6 = (float)(num5 * 4.0 % 360.0);
		float num7 = (float)(150.0 - num5 * 6.5 % 360.0);
		g.DrawArc(arcSoft, num - num3, num2 - num3, num3 * 2f, num3 * 2f, num6, 84f);
		g.DrawArc(arcBright, num - num3, num2 - num3, num3 * 2f, num3 * 2f, num6 + 60f, 24f);
		g.DrawArc(arcSoft, num - num4, num2 - num4, num4 * 2f, num4 * 2f, num7, 70f);
		g.DrawArc(arcBright, num - num4, num2 - num4, num4 * 2f, num4 * 2f, num7 + 48f, 22f);
		if (blob == null)
		{
			UpdateBlob(num5);
		}
		if (blob != null)
		{
			float num8 = num + 70f + leanX;
			float num9 = num2 + 34f + leanY + 3f * (float)Math.Sin(num5 * 0.45);
			InterpolationMode interpolationMode = g.InterpolationMode;
			g.InterpolationMode = InterpolationMode.HighQualityBilinear;
			g.DrawImage(blob, new RectangleF(num8 - 80f, num9 - 80f, 160f, 160f));
			g.InterpolationMode = interpolationMode;
		}
		g.Restore(gstate);
		double[][] array = new double[4][]
		{
			new double[6] { num3, 205.0, -8.0, 11.0, 83.0, 0.0 },
			new double[6] { num4, 335.0, 9.0, 14.0, 71.0, 1.7 },
			new double[6] { num4, 140.0, -9.0, 14.0, 97.0, 3.1 },
			new double[6] { num3, 25.0, 8.0, 8.0, 77.0, 4.4 }
		};
		for (int i = 0; i < array.Length && i < Steps.Length; i++)
		{
			double num10 = (array[i][1] + array[i][3] * Math.Sin(num5 * 2.0 * Math.PI / array[i][4] + array[i][5])) * Math.PI / 180.0;
			float cx = num + (float)(Math.Cos(num10) * array[i][0]);
			float cy = num2 + (float)(Math.Sin(num10) * array[i][0]);
			Chip(g, cx, cy, (float)array[i][2], "#" + (i + 1).ToString("00") + " " + ((i < StepNames.Length) ? StepNames[i] : ""), Steps[i], (i < states.Length) ? states[i] : (-1), (i < 4) ? dot[i].V : 0f, (i < 4) ? burst[i].V : 1f, i < 4 && burst[i].Running);
		}
	}

	private static void Chip(Graphics g, float cx, float cy, float rot, string name, string detail, int state, float fill, float burstK, bool bursting)
	{
		Font chipName = Fx.ChipName;
		Font chipDetail = Fx.ChipDetail;
		float num = Math.Max(Theme.TextW(g, name, chipName), Theme.TextW(g, detail ?? "", chipDetail)) + 52f;
		float num2 = 42f;
		GraphicsState gstate = g.Save();
		g.TranslateTransform(cx, cy);
		g.RotateTransform(rot);
		g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		RectangleF r = new RectangleF((0f - num) / 2f, (0f - num2) / 2f, num, num2);
		using (GraphicsPath path = Theme.Round(r, num2 / 2f))
		{
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(62, 255, 255, 255)))
			{
				g.FillPath(brush, path);
			}
			using Pen pen = new Pen(Color.FromArgb(84, 255, 255, 255));
			g.DrawPath(pen, path);
		}
		Color color = ((state == 0) ? Theme.HotPink : Theme.Dim);
		float num3 = r.X + 18f;
		if (bursting)
		{
			float num4 = 10f + 12f * Fx.Out(burstK);
			using Pen pen2 = new Pen(Fx.Alpha(Theme.Cyan, 1f - burstK), 2f);
			g.DrawEllipse(pen2, num3 - num4, 0f - num4, num4 * 2f, num4 * 2f);
		}
		using (SolidBrush brush2 = new SolidBrush(color))
		{
			g.FillEllipse(brush2, num3 - 10f, -10f, 20f, 20f);
		}
		if (fill > 0.01f)
		{
			float num5 = 20f * fill;
			using SolidBrush brush3 = new SolidBrush(Theme.Cyan);
			g.FillEllipse(brush3, num3 - num5 / 2f, (0f - num5) / 2f, num5, num5);
		}
		StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		using (StringFormat format = stringFormat)
		{
			using (SolidBrush brush4 = new SolidBrush(Theme.Text))
			{
				g.DrawString(name, chipName, brush4, r.X + 38f, (0f - num2) / 2f + 6f, format);
			}
			using SolidBrush brush5 = new SolidBrush(Theme.Soft);
			g.DrawString(detail ?? "", chipDetail, brush5, r.X + 38f, (0f - num2) / 2f + 23f, format);
		}
		g.Restore(gstate);
	}
}
