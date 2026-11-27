using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class StatusMark : Control
{
	public int Kind = 4;

	public bool Round = true;

	public bool Node;

	public bool Flat;

	public string Number = "";

	private readonly Mix fill;

	private readonly Mix burst;

	private bool pulseReg;

	private float pulse = -1f;

	public StatusMark()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Size = new Size(30, 30);
		Font = Theme.UiBold(8f);
		fill = new Mix(this);
		burst = new Mix(this);
	}

	public void Set(int kind, string text)
	{
		bool flag = Kind == 1;
		Kind = kind;
		Text = text;
		if (Node)
		{
			fill.To((kind == 1) ? 1f : 0f, 380);
			if (kind == 1 && !flag)
			{
				burst.Set(0f);
				burst.To(1f, 750);
			}
			if (kind == 2 && !pulseReg)
			{
				pulseReg = true;
				Anim.Register(this, needsFocus: false, Pulse);
			}
			else if (pulseReg)
			{
				Anim.Poke(this);
			}
		}
		Invalidate();
	}

	private int Pulse(double t)
	{
		if (Kind != 2)
		{
			if (pulse >= 0f)
			{
				pulse = -1f;
				Invalidate();
			}
			return 4000;
		}
		pulse = (float)(0.5 + 0.5 * Math.Sin(t * 2.0 * Math.PI / 1.5));
		Invalidate();
		return 66;
	}

	public static Color ColorOf(int kind)
	{
		return kind switch
		{
			5 => Theme.Bad, 
			2 => Theme.Cyan, 
			0 => Theme.Warn, 
			1 => Theme.Good, 
			_ => Theme.Dim, 
		};
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((Parent != null) ? Parent.BackColor : Theme.Back);
		Theme.Smooth(graphics);
		Color color = ColorOf(Kind);
		if (Node)
		{
			float num = Math.Min(Width, Height) - 2;
			RectangleF rectangleF = new RectangleF(((float)Width - num) / 2f, ((float)Height - num) / 2f, num, num);
			if (!fill.Running)
			{
				fill.Set((Kind == 1) ? 1f : 0f);
			}
			float v = fill.V;
			if (Kind == 2 && pulse >= 0f)
			{
				using SolidBrush brush = new SolidBrush(Color.FromArgb((int)(18f + 52f * pulse), color));
				graphics.FillEllipse(brush, rectangleF);
			}
			if (v < 0.999f)
			{
				using (SolidBrush brush2 = new SolidBrush((Kind == 2 && pulse >= 0f) ? Fx.Lerp(Theme.Back, Theme.Plum, pulse * 0.6f) : Theme.Back))
				{
					graphics.FillEllipse(brush2, rectangleF.X + 2f, rectangleF.Y + 2f, num - 4f, num - 4f);
				}
				using Pen pen = new Pen(color, (Kind == 2) ? (2f + 0.8f * Math.Max(0f, pulse)) : 1.5f);
				graphics.DrawEllipse(pen, rectangleF.X + 1f, rectangleF.Y + 1f, num - 2f, num - 2f);
			}
			if (v > 0.01f)
			{
				float num2 = num * Fx.Out(v);
				using SolidBrush brush3 = new SolidBrush(Theme.Good);
				graphics.FillEllipse(brush3, rectangleF.X + (num - num2) / 2f, rectangleF.Y + (num - num2) / 2f, num2, num2);
			}
			if (burst.Running)
			{
				float v2 = burst.V;
				float num3 = num * (0.8f + 0.2f * v2);
				using Pen pen2 = new Pen(Fx.Alpha(Theme.Good, 1f - v2), 2.5f);
				graphics.DrawEllipse(pen2, rectangleF.X + (num - num3) / 2f, rectangleF.Y + (num - num3) / 2f, num3, num3);
			}
			using Font font = Theme.UiBold(8f);
			using SolidBrush brush4 = new SolidBrush(Fx.Lerp(color, Theme.OnAccent, v));
			StringFormat stringFormat = new StringFormat();
			stringFormat.Alignment = StringAlignment.Center;
			stringFormat.LineAlignment = StringAlignment.Center;
			stringFormat.FormatFlags = StringFormatFlags.NoWrap;
			using StringFormat format = stringFormat;
			graphics.DrawString(Number ?? "", font, brush4, rectangleF, format);
			return;
		}
		if (Flat)
		{
			using (SolidBrush brush5 = new SolidBrush(color))
			{
				using Font font2 = Theme.UiBold(8.5f);
				StringFormat stringFormat2 = new StringFormat();
				stringFormat2.LineAlignment = StringAlignment.Center;
				stringFormat2.FormatFlags = StringFormatFlags.NoWrap;
				stringFormat2.Trimming = StringTrimming.EllipsisCharacter;
				using StringFormat format2 = stringFormat2;
				graphics.DrawString(Text ?? "", font2, brush5, new RectangleF(0f, 0f, Width, Height), format2);
				return;
			}
		}
		if (!Round)
		{
			RectangleF rectangleF2 = new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f);
			Theme.Panel(graphics, rectangleF2, Color.FromArgb((Kind == 1 || Kind == 2) ? 40 : 28, color), Color.FromArgb((Kind == 3 || Kind == 4) ? 70 : 130, color), rectangleF2.Height / 2f);
			using SolidBrush brush6 = new SolidBrush(color);
			StringFormat stringFormat3 = new StringFormat();
			stringFormat3.Alignment = StringAlignment.Center;
			stringFormat3.LineAlignment = StringAlignment.Center;
			stringFormat3.FormatFlags = StringFormatFlags.NoWrap;
			stringFormat3.Trimming = StringTrimming.EllipsisCharacter;
			using StringFormat format3 = stringFormat3;
			graphics.DrawString(Text ?? "", Font, brush6, rectangleF2, format3);
			return;
		}
		float num4 = Math.Min(Width, Height) - 2;
		RectangleF rect = new RectangleF(((float)Width - num4) / 2f, ((float)Height - num4) / 2f, num4, num4);
		float num5 = rect.X + num4 / 2f;
		float num6 = rect.Y + num4 / 2f;
		float num7 = num4 / 30f;
		if (Kind == 1)
		{
			using (SolidBrush brush7 = new SolidBrush(color))
			{
				graphics.FillEllipse(brush7, rect);
			}
			Pen pen3 = new Pen(Theme.OnAccent, Math.Max(1.6f, 2.4f * num7));
			pen3.StartCap = LineCap.Round;
			pen3.EndCap = LineCap.Round;
			pen3.LineJoin = LineJoin.Round;
			using Pen pen4 = pen3;
			graphics.DrawLines(pen4, new PointF[3]
			{
				new PointF(num5 - 6f * num7, num6 + 0.5f * num7),
				new PointF(num5 - 1.5f * num7, num6 + 5f * num7),
				new PointF(num5 + 6.5f * num7, num6 - 5f * num7)
			});
			return;
		}
		using (Pen pen5 = new Pen(color, Math.Max(1.4f, 1.8f * num7)))
		{
			graphics.DrawEllipse(pen5, rect.X + 1f, rect.Y + 1f, num4 - 2f, num4 - 2f);
		}
		Pen pen6 = new Pen(color, Math.Max(1.4f, 2.2f * num7));
		pen6.StartCap = LineCap.Round;
		pen6.EndCap = LineCap.Round;
		using Pen pen7 = pen6;
		using SolidBrush brush8 = new SolidBrush(color);
		if (Kind == 0)
		{
			graphics.DrawLine(pen7, num5, num6 - 6f * num7, num5, num6 + 1f * num7);
			graphics.FillEllipse(brush8, num5 - 1.6f * num7, num6 + 4.5f * num7, 3.2f * num7, 3.2f * num7);
		}
		else if (Kind == 5)
		{
			graphics.DrawLine(pen7, num5 - 4.5f * num7, num6 - 4.5f * num7, num5 + 4.5f * num7, num6 + 4.5f * num7);
			graphics.DrawLine(pen7, num5 - 4.5f * num7, num6 + 4.5f * num7, num5 + 4.5f * num7, num6 - 4.5f * num7);
		}
		else if (Kind == 2)
		{
			for (int i = -1; i <= 1; i++)
			{
				graphics.FillEllipse(brush8, num5 + (float)(i * 6) * num7 - 1.8f * num7, num6 - 1.8f * num7, 3.6f * num7, 3.6f * num7);
			}
		}
		else
		{
			graphics.DrawLine(pen7, num5 - 4f * num7, num6, num5 + 4f * num7, num6);
		}
	}
}
