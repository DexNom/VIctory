using System;
using System.Drawing;

namespace VIctoryLauncher;

internal static class Fx
{
	private static Font meta;

	private static Font chipName;

	private static Font chipDetail;

	public static Font MetaFont
	{
		get
		{
			if (meta == null)
			{
				meta = Theme.Ui(8.5f);
			}
			return meta;
		}
	}

	public static Font ChipName
	{
		get
		{
			if (chipName == null)
			{
				chipName = Theme.UiBold(9f);
			}
			return chipName;
		}
	}

	public static Font ChipDetail
	{
		get
		{
			if (chipDetail == null)
			{
				chipDetail = Theme.Ui(8f);
			}
			return chipDetail;
		}
	}

	public static Color Lerp(Color a, Color b, float t)
	{
		if (t <= 0f)
		{
			return a;
		}
		if (t >= 1f)
		{
			return b;
		}
		return Color.FromArgb((int)((float)(int)a.A + (float)(b.A - a.A) * t), (int)((float)(int)a.R + (float)(b.R - a.R) * t), (int)((float)(int)a.G + (float)(b.G - a.G) * t), (int)((float)(int)a.B + (float)(b.B - a.B) * t));
	}

	public static Color Alpha(Color c, float k)
	{
		return Color.FromArgb(Math.Max(0, Math.Min(255, (int)((float)(int)c.A * k))), c.R, c.G, c.B);
	}

	public static float Clamp(float v, float lo, float hi)
	{
		if (!(v < lo))
		{
			if (!(v > hi))
			{
				return v;
			}
			return hi;
		}
		return lo;
	}

	public static float SmoothStep(float e0, float e1, float x)
	{
		float num = Clamp((x - e0) / (e1 - e0), 0f, 1f);
		return num * num * (3f - 2f * num);
	}

	public static float Out(float p)
	{
		float num = 1f - p;
		return 1f - num * num * num;
	}

	public static float InOut(float p)
	{
		return p * p * (3f - 2f * p);
	}
}
