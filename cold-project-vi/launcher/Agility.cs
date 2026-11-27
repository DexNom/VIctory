// Julia's Launcher: the D3D12 Agility SDK beside victory.exe (D3D12\D3D12Core.dll, Microsoft's redistributable).
// Windows 10's own D3D12 refused the engine's root signatures and the game never started there
// (apps\victory\src\agility.rs says how victory.exe asks for it). The installer puts it in place; for a launcher
// updated from 0.3.6 or older, whose update list may carry only the files it knew, the launcher fetches it from
// the release, checked against the SHA-256 written here, and puts it beside every victory.exe of this repository
// it may start. Where Windows' own D3D12 is newer, Windows keeps using its own.
using System;
using System.IO;
using System.Net;
using System.Threading;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        // Microsoft.Direct3D.D3D12 1.620.2 (NuGet), build\native\bin\x64\D3D12Core.dll.
        public const string AgilityUrl = "https://github.com/felicienradio/cold-project-vi/releases/download/v0.3.7/D3D12Core.dll";
        public const string AgilitySha256 = "70a6d47d335b4a15180544c01a35b84474522a369dc582d85fe057313a3aeaa0";

        static readonly object agilityLock = new object();

        // In the background at start and before PLAY; quiet unless it had to fetch the file.
        void EnsureAgility()
        {
            new Thread(() => {
                lock (agilityLock)
                {
                    try
                    {
                        string have = null;
                        foreach (var exe in OwnExeCandidates)
                        {
                            if (!File.Exists(exe)) continue;
                            var dll = Path.Combine(Path.GetDirectoryName(exe), "D3D12", "D3D12Core.dll");
                            if (File.Exists(dll) && Sha256(dll) == AgilitySha256) { have = have ?? dll; continue; }
                            if (have == null) have = FetchAgility();
                            if (have == null) return;
                            Directory.CreateDirectory(Path.GetDirectoryName(dll));
                            File.Copy(have, dll, true);
                            Log(L("D3D12 Agility SDK placed beside victory.exe (needed on Windows 10).", "D3D12 Agility SDK posto ao lado do victory.exe (necessario no Windows 10).", "D3D12 Agility SDK placé à côté de victory.exe (nécessaire sous Windows 10)."));
                        }
                    }
                    catch (Exception e) { try { File.AppendAllText(Program.ErrorFile, DateTime.Now + "  agility: " + e + Environment.NewLine); } catch (Exception) { } }
                }
            }) { IsBackground = true }.Start();
        }

        // Downloads the release's D3D12Core.dll into the data folder; null unless its SHA-256 matches.
        string FetchAgility()
        {
            var to = Path.Combine(Path.GetDirectoryName(dataRoot), "agility", "D3D12Core.dll");
            if (File.Exists(to) && Sha256(to) == AgilitySha256) return to;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            var part = to + ".part";
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Julias-Launcher";
                web.DownloadFile(AgilityUrl, part);
            }
            if (Sha256(part) != AgilitySha256) { File.Delete(part); return null; }
            if (File.Exists(to)) File.Delete(to);
            File.Move(part, to);
            return to;
        }
    }
}
