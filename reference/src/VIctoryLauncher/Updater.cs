using System;
using System.Linq;

namespace VIctoryLauncher;

internal static class Updater
{
	public const string Version = "0.3.16";

	public const string ManifestUrl = "https://github.com/felicienradio/cold-project-vi/releases/latest/download/manifest.json";

	public const string SignatureUrl = "https://github.com/felicienradio/cold-project-vi/releases/latest/download/manifest.sig";

	public const string DownloadPrefix = "https://github.com/felicienradio/cold-project-vi/releases/download/";

	public const string ReleasesApi = "https://api.github.com/repos/felicienradio/cold-project-vi/releases?per_page=15";

	public static readonly string[] Allowed = new string[4] { "Julia's Launcher.exe", "victory.exe", "tools/nvsmooth30/version.dll", "D3D12/D3D12Core.dll" };

	public static Version Number(string v)
	{
		string text = new string((v ?? "").TrimStart('v').TakeWhile((char c) => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
		if (!System.Version.TryParse((text.Count((char c) => c == '.') == 0) ? (text + ".0") : text, out var result))
		{
			return new Version(0, 0);
		}
		return result;
	}
}
