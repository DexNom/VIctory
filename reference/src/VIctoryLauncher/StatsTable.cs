using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal class StatsTable : Control
{
	private const int RowH = 30;

	private const int HeadH = 34;

	public string[] Headers = new string[0];

	public float[] Parts = new float[0];

	public List<string[]> Rows = new List<string[]>();

	public int Highlight = -1;

	public bool[] RightAlign = new bool[0];

	public string Empty = "";

	public int Wanted => 34 + Math.Max(1, Rows.Count) * 30 + 8;

	public StatsTable()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Font = Theme.Ui(9.5f);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((Parent != null) ? Parent.BackColor : Theme.Back);
		Theme.Smooth(graphics);
		RectangleF r = new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f);
		Theme.Panel(graphics, r, Theme.Card, Theme.Line, 14f);
		float num = Width - 32;
		float[] array = new float[Parts.Length + 1];
		array[0] = 16f;
		float num2 = Math.Max(1E-06f, Parts.Sum());
		for (int i = 0; i < Parts.Length; i++)
		{
			array[i + 1] = array[i] + num * Parts[i] / num2;
		}
		StringFormat f = new StringFormat
		{
			Trimming = StringTrimming.EllipsisCharacter,
			FormatFlags = StringFormatFlags.NoWrap,
			LineAlignment = StringAlignment.Center
		};
		try
		{
			StringFormat far = new StringFormat
			{
				Trimming = StringTrimming.EllipsisCharacter,
				FormatFlags = StringFormatFlags.NoWrap,
				LineAlignment = StringAlignment.Center,
				Alignment = StringAlignment.Far
			};
			try
			{
				Func<int, StringFormat> func = (int num7) => (num7 >= RightAlign.Length || !RightAlign[num7]) ? f : far;
				using (Font font = Theme.UiBold(8f))
				{
					using SolidBrush brush = new SolidBrush(Theme.Dim);
					for (int num3 = 0; num3 < Headers.Length && num3 < Parts.Length; num3++)
					{
						graphics.DrawString((Headers[num3] ?? "").ToUpperInvariant(), font, brush, new RectangleF(array[num3], 0f, array[num3 + 1] - array[num3] - 8f, 34f), func(num3));
					}
				}
				using (Pen pen = new Pen(Theme.Line))
				{
					graphics.DrawLine(pen, 14f, 34.5f, Width - 14, 34.5f);
				}
				if (Rows.Count == 0)
				{
					using (SolidBrush brush2 = new SolidBrush(Theme.Dim))
					{
						graphics.DrawString(Empty ?? "", Font, brush2, new RectangleF(16f, 34f, num, 30f), f);
						return;
					}
				}
				float num4 = 36f;
				for (int num5 = 0; num5 < Rows.Count; num5++)
				{
					bool flag = num5 == Highlight;
					RectangleF r2 = new RectangleF(8f, num4 + 1f, Width - 16, 28f);
					if (flag)
					{
						using GraphicsPath path = Theme.Round(r2, 10f);
						using SolidBrush brush3 = new SolidBrush(Theme.Selected);
						graphics.FillPath(brush3, path);
					}
					else if (num5 % 2 == 1)
					{
						using GraphicsPath path2 = Theme.Round(r2, 10f);
						using SolidBrush brush4 = new SolidBrush(Theme.Stripe);
						graphics.FillPath(brush4, path2);
					}
					using (Font font2 = (flag ? Theme.UiBold(9.5f) : Theme.Ui(9.5f)))
					{
						for (int num6 = 0; num6 < Rows[num5].Length && num6 < Parts.Length; num6++)
						{
							Color color;
							if (flag)
							{
								color = Theme.Cyan;
							}
							else
							{
								color = ((num6 == 0) ? Theme.Text : Theme.Soft);
							}
							using SolidBrush brush5 = new SolidBrush(color);
							graphics.DrawString(Rows[num5][num6] ?? "", font2, brush5, new RectangleF(array[num6], num4, array[num6 + 1] - array[num6] - 8f, 30f), func(num6));
						}
					}
					num4 += 30f;
				}
			}
			finally
			{
				if (far != null)
				{
					((IDisposable)far).Dispose();
				}
			}
		}
		finally
		{
			if (f != null)
			{
				((IDisposable)f).Dispose();
			}
		}
	}
}
