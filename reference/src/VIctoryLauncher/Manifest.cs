using System.Collections.Generic;

namespace VIctoryLauncher;

internal class Manifest
{
	public string Version = "";

	public string Notes = "";

	public string NotesPt = "";

	public List<UpdateFile> Files = new List<UpdateFile>();
}
