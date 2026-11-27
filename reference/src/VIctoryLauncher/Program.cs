using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VIctoryLauncher;

internal static class Program
{
	public static readonly string ErrorFile = Path.Combine(Path.GetTempPath(), "vi-launcher-error.txt");

	[STAThread]
	private static int Main(string[] args)
	{
		AppDomain.CurrentDomain.UnhandledException += (object s, UnhandledExceptionEventArgs e) =>
		{
			try
			{
				File.AppendAllText(ErrorFile, string.Concat(DateTime.Now, "  ", e.ExceptionObject, Environment.NewLine));
			}
			catch (Exception)
			{
			}
		};
		Application.ThreadException += (object s, ThreadExceptionEventArgs e) =>
		{
			try
			{
				File.AppendAllText(ErrorFile, string.Concat(DateTime.Now, "  ", e.Exception, Environment.NewLine));
			}
			catch (Exception)
			{
			}
			MessageBox.Show("Julia's Launcher: " + e.Exception.Message + "\r\n\r\n(Help > Send errors to Discord)", "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		};
		if (args.Contains("--admin-setup"))
		{
			return AdminTasks.Run(args);
		}
		if (args.Contains("--pagefile"))
		{
			if (!LauncherForm.SetManagedPagefile())
			{
				return 1;
			}
			return 0;
		}
		if (args.Contains("--rebar-profile"))
		{
			if (SmoothMotion.SetRebar(on: true) != null)
			{
				return 1;
			}
			return 0;
		}
		int num = Array.IndexOf(args, "--wait-pid");
		if (num >= 0 && num + 1 < args.Length && int.TryParse(args[num + 1], out var result))
		{
			try
			{
				Process.GetProcessById(result).WaitForExit(15000);
			}
			catch (Exception)
			{
			}
		}
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "VI-Launcher-edupode", out createdNew))
		{
			if (!createdNew)
			{
				Process[] processesByName = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Application.ExecutablePath));
				foreach (Process process in processesByName)
				{
					try
					{
						if (process.Id != Process.GetCurrentProcess().Id && process.MainWindowHandle != IntPtr.Zero)
						{
							SetForegroundWindow(process.MainWindowHandle);
						}
					}
					catch (Exception)
					{
					}
				}
				return 0;
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.Run(new LauncherForm());
		}
		return 0;
	}

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hwnd);
}
