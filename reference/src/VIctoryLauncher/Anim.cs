using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VIctoryLauncher;

internal static class Anim
{
	private sealed class Ambient
	{
		public Control C;

		public bool NeedsFocus;

		public Func<double, int> Step;

		public long Due;
	}

	public static bool UserOn = true;

	private static bool systemOn = true;

	public static volatile bool GameRunning;

	private static Form host;

	private static Timer clock;

	private static readonly Stopwatch watch = Stopwatch.StartNew();

	private static readonly List<Mix> mixes = new List<Mix>();

	private static readonly List<Ambient> ambients = new List<Ambient>();

	public static long Now => watch.ElapsedMilliseconds;

	public static bool Live
	{
		get
		{
			if (UserOn && systemOn && host != null && host.IsHandleCreated && host.Visible && host.WindowState != FormWindowState.Minimized)
			{
				return !GameRunning;
			}
			return false;
		}
	}

	private static bool Foreground => Form.ActiveForm == host;

	[DllImport("user32.dll")]
	private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

	private static void ReadSystem()
	{
		try
		{
			int value = 1;
			systemOn = (SystemParametersInfo(4162u, 0u, ref value, 0u) ? (value != 0) : SystemInformation.UIEffectsEnabled);
		}
		catch (Exception)
		{
			systemOn = true;
		}
	}

	public static void Attach(Form f, bool userOn)
	{
		host = f;
		UserOn = userOn;
		ReadSystem();
		f.Activated += (object s, EventArgs e) =>
		{
			Wake();
		};
		f.Deactivate += (object s, EventArgs e) =>
		{
			Wake();
		};
		f.Resize += (object s, EventArgs e) =>
		{
			Wake();
		};
		f.VisibleChanged += (object s, EventArgs e) =>
		{
			Wake();
		};
		try
		{
			SystemEvents.UserPreferenceChanged += (object s, UserPreferenceChangedEventArgs e) =>
			{
				try
				{
					if (!f.IsDisposed && f.IsHandleCreated)
					{
						f.BeginInvoke((Action)(() =>
						{
							ReadSystem();
							Wake();
						}));
					}
				}
				catch (Exception)
				{
				}
			};
		}
		catch (Exception)
		{
		}
	}

	internal static void Add(Mix m)
	{
		mixes.Add(m);
	}

	public static void Register(Control c, bool needsFocus, Func<double, int> step)
	{
		Ambient a = new Ambient
		{
			C = c,
			NeedsFocus = needsFocus,
			Step = step,
			Due = 0L
		};
		ambients.Add(a);
		c.Disposed += (object s, EventArgs e) =>
		{
			ambients.Remove(a);
		};
		Wake();
	}

	public static void Poke(Control c)
	{
		foreach (Ambient ambient in ambients)
		{
			if (ambient.C == c)
			{
				ambient.Due = 0L;
			}
		}
		Wake();
	}

	public static void Wake()
	{
		try
		{
			if (host == null || host.IsDisposed)
			{
				return;
			}
			if (!Live)
			{
				Settle();
				return;
			}
			if (clock == null)
			{
				Timer timer = new Timer();
				timer.Interval = 16;
				clock = timer;
				clock.Tick += Tick;
			}
			if (!clock.Enabled)
			{
				clock.Interval = 16;
				clock.Start();
			}
		}
		catch (Exception)
		{
		}
	}

	private static void Stop()
	{
		if (clock != null && clock.Enabled)
		{
			clock.Stop();
		}
	}

	private static void Settle()
	{
		Mix[] array = mixes.ToArray();
		foreach (Mix mix in array)
		{
			mix.Finish();
		}
		mixes.Clear();
		Stop();
	}

	private static void Tick(object sender, EventArgs e)
	{
		try
		{
			if (!Live)
			{
				Settle();
				return;
			}
			long now = Now;
			int num = int.MaxValue;
			for (int num2 = mixes.Count - 1; num2 >= 0; num2--)
			{
				if (!mixes[num2].Step(now))
				{
					mixes.RemoveAt(num2);
				}
			}
			if (mixes.Count > 0)
			{
				num = 16;
			}
			bool foreground = Foreground;
			for (int i = 0; i < ambients.Count; i++)
			{
				Ambient ambient = ambients[i];
				if (ambient.C.IsDisposed || !ambient.C.Visible || (ambient.NeedsFocus && !foreground))
				{
					continue;
				}
				if (now < ambient.Due)
				{
					num = Math.Min(num, (int)(ambient.Due - now));
					continue;
				}
				int num3 = ambient.Step((double)now / 1000.0);
				if (num3 <= 0)
				{
					num3 = 50;
				}
				ambient.Due = now + num3;
				num = Math.Min(num, num3);
			}
			if (num == int.MaxValue)
			{
				Stop();
			}
			else
			{
				clock.Interval = Math.Max(15, Math.Min(num, 5000));
			}
		}
		catch (Exception)
		{
			Stop();
		}
	}
}
