namespace VIctoryLauncher;

internal class Mode
{
	public string Name;

	public string Short;

	public string Tag;

	public string Description;

	public string Scale;

	public bool LightTextures;

	public bool Modest;

	public bool UltraLow;

	public bool Custom;

	public Mode(string name, string tag, string shortLine, string description, string scale, bool light, bool modest)
	{
		Name = name;
		Tag = tag;
		Short = shortLine;
		Description = description;
		Scale = scale;
		LightTextures = light;
		Modest = modest;
	}

	public override string ToString()
	{
		return Name + " (" + Tag + ")";
	}
}
