using System;
using System.Drawing;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class PageHead : Poster
{
	public string Eye = "";

	public string EyeValue = "";

	public string RightLabel = "";

	public string RightValue = "";

	public Color RightColor = Theme.Muted;

	public string Title = "";

	public string Accent = "";

	public string Sub = "";

	public int SubWidth = 540;

	public PageHead()
	{
		Height = 150;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		PaintBackdrop(graphics, e.ClipRectangle);
		Theme.Smooth(graphics);
		float num = 40f;
		float num2 = Width - 40;
		float num3 = Intro;
		float num4 = (1f - num3) * 10f;
		Theme.Meta(graphics, Eye, EyeValue, Theme.Muted, new RectangleF(num, 15f, 320f, 34f), StringAlignment.Near);
		if (!string.IsNullOrEmpty(RightLabel))
		{
			Theme.Meta(graphics, RightLabel, RightValue, RightColor, new RectangleF(num2 - 320f, 15f, 320f, 34f), StringAlignment.Far);
		}
		using (Pen pen = new Pen(Theme.HairWhite))
		{
			graphics.DrawLine(pen, 0, 62, Width, 62);
		}
		float num5 = Math.Max(200f, (float)Width - 2f * num - (float)((Controls.Count > 0) ? 250 : 0));
		float num6 = 38f;
		Font font = Poster.Heavy(num6);
		Font font2 = Poster.Serif(num6);
		while (num6 > 22f && Theme.TrackedWidth(graphics, Title ?? "", font, -1.5f) + 14f + Theme.TextW(graphics, Accent ?? "", font2) > num5)
		{
			num6 -= 2f;
			font.Dispose();
			font2.Dispose();
			font = Poster.Heavy(num6);
			font2 = Poster.Serif(num6);
		}
		float num7 = 122f + num4;
		using (font)
		{
			using (font2)
			{
				float num8;
				using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Text, num3)))
				{
					num8 = Theme.Tracked(graphics, Title ?? "", font, b, num - 1f, num7 - Theme.AscentPx(graphics, font), -1.5f);
				}
				using SolidBrush brush = new SolidBrush(Fx.Alpha(Theme.Cyan, num3));
				StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
				stringFormat.FormatFlags = StringFormatFlags.NoWrap;
				using StringFormat format = stringFormat;
				graphics.DrawString(Accent ?? "", font2, brush, num8 + 12f, num7 - Theme.AscentPx(graphics, font2), format);
			}
		}
		if (!string.IsNullOrEmpty(Sub))
		{
			using (Font font3 = Theme.Ui(10f))
			{
				TextRenderer.DrawText(graphics, Sub, font3, new Rectangle((int)num, (int)(142f + num4), SubWidth, Math.Max(20, Height - 142)), Fx.Lerp(Theme.Back, Theme.Soft, num3), TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
			}
		}
	}
}
