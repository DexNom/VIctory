using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace VIctoryLauncher;

internal static class AdminTasks
{
	public static int Run(string[] args)
	{
		string text = args.SkipWhile((string a) => a != "--admin-setup").Skip(1).FirstOrDefault() ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
		List<string> list = new List<string>();
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo("powercfg", "/overlaysetactive ded574b5-45a0-4f42-8737-46345c09c238");
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
			Process process = Process.Start(processStartInfo);
			process.WaitForExit(60000);
			list.Add("power: " + ((process.ExitCode == 0) ? "ok" : ("code " + process.ExitCode)));
		}
		catch (Exception ex)
		{
			list.Add("power: " + ex.Message);
		}
		if (!LauncherForm.PagefileOk())
		{
			list.Add("virtual memory: " + (LauncherForm.SetManagedPagefile() ? "ok" : "failed"));
		}
		try
		{
			using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\DirectX\\UserGpuPreferences"))
			{
				string[] array = new string[2]
				{
					Path.Combine(text, "victory.exe"),
					Path.Combine(text, "engines", "amd", "target", "diag", "victory.exe")
				};
				foreach (string name in array)
				{
					registryKey.SetValue(name, "GpuPreference=2;");
				}
			}
			list.Add("gpu preference: ok");
		}
		catch (Exception ex2)
		{
			list.Add("gpu preference: " + ex2.Message);
		}
		try
		{
			string text2 = SmoothMotion.Set(on: true);
			list.Add("smooth motion: " + (text2 ?? "ok"));
		}
		catch (Exception ex3)
		{
			list.Add("smooth motion: " + ex3.Message);
		}
		try
		{
			string text3 = SmoothMotion.SetRebar(on: true);
			list.Add("rbar profile: " + (text3 ?? "ok"));
		}
		catch (Exception ex4)
		{
			list.Add("rbar profile: " + ex4.Message);
		}
		try
		{
			File.WriteAllLines(Path.Combine(Path.GetTempPath(), "vi-admin-setup.txt"), list);
		}
		catch (Exception)
		{
		}
		return 0;
	}
}
