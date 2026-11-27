using System;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal sealed class Mix
{
	public float V;

	private float a;

	private float b;

	private long t0;

	private int dur;

	private bool run;

	private readonly Action inval;

	public bool Running => run;

	public float Target => b;

	public Mix(Control owner)
	{
		Action action = () =>
		{
			if (!owner.IsDisposed)
			{
				owner.Invalidate();
			}
		};
		inval = action;
	}

	public Mix(Action invalidate)
	{
		inval = invalidate;
	}

	public void Set(float v)
	{
		V = (a = (b = v));
		run = false;
	}

	public void To(float target, int ms)
	{
		if (target == b && (run || V == target))
		{
			return;
		}
		b = target;
		if (ms <= 0 || !Anim.Live)
		{
			V = target;
			run = false;
			inval();
			return;
		}
		a = V;
		t0 = Anim.Now;
		dur = ms;
		if (!run)
		{
			run = true;
			Anim.Add(this);
		}
		Anim.Wake();
	}

	internal bool Step(long now)
	{
		if (!run)
		{
			return false;
		}
		float num = Math.Min(1f, (float)(now - t0) / (float)dur);
		V = a + (b - a) * Fx.Out(num);
		inval();
		if (num >= 1f)
		{
			V = b;
			run = false;
			return false;
		}
		return true;
	}

	internal void Finish()
	{
		if (V != b || run)
		{
			V = b;
			run = false;
			inval();
		}
	}
}
