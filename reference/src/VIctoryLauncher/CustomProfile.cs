using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VIctoryLauncher;

internal class CustomProfile
{
	public string Scale = "0.6666667";

	public int TextureMax = 2048;

	public int CardBuffersMb = 1536;

	public int RipeBlocks = 4;

	public int StageMb = 384;

	public int Fps = 60;

	public bool Guards;

	public bool LevelsOnDemand;

	public static readonly string[] Scales = new string[5] { "1.0", "0.75", "0.6666667", "0.5", "0.4" };

	public static readonly int[] TextureMaxes = new int[4] { 0, 2048, 1024, 512 };

	public static readonly int[] Buffers = new int[5] { 512, 768, 1024, 1536, 2048 };

	public static readonly int[] Ripes = new int[2] { 2, 4 };

	public static readonly int[] Stages = new int[3] { 64, 128, 384 };

	public static readonly int[] Fpses = new int[2] { 30, 60 };

	public CustomProfile Copy()
	{
		return (CustomProfile)MemberwiseClone();
	}

	public string Text()
	{
		return $"s={Scale};t={TextureMax};b={CardBuffersMb};r={RipeBlocks};g={StageMb};f={Fps};m={(Guards ? 1 : 0)};l={(LevelsOnDemand ? 1 : 0)}";
	}

	private static string Check(string body)
	{
		using SHA256 sHA = SHA256.Create();
		return string.Concat((from b in sHA.ComputeHash(Encoding.ASCII.GetBytes(body)).Take(3)
			select b.ToString("x2")).ToArray());
	}

	public string Code()
	{
		string text = Convert.ToBase64String(Encoding.ASCII.GetBytes(Text())).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');
		return "VI1-" + text + "-" + Check(text);
	}

	public static CustomProfile FromCode(string code, out string why)
	{
		why = null;
		code = (code ?? "").Trim();
		string[] array = code.Split('-');
		if (array.Length < 3 || array[0] != "VI1")
		{
			why = "not a VI1 profile code";
			return null;
		}
		string text = array[array.Length - 1];
		string text2 = string.Join("-", array.Skip(1).Take(array.Length - 2).ToArray());
		if (Check(text2) != text)
		{
			why = "the code is damaged (checksum)";
			return null;
		}
		string text4;
		try
		{
			string text3 = text2.Replace('-', '+').Replace('_', '/');
			while (text3.Length % 4 != 0)
			{
				text3 += "=";
			}
			text4 = Encoding.ASCII.GetString(Convert.FromBase64String(text3));
		}
		catch (Exception)
		{
			why = "the code is damaged";
			return null;
		}
		return FromText(text4, out why);
	}

	public static CustomProfile FromText(string text, out string why)
	{
		why = null;
		CustomProfile customProfile = new CustomProfile();
		string[] array = (text ?? "").Split(';');
		foreach (string text2 in array)
		{
			string[] array2 = text2.Split('=');
			if (array2.Length != 2)
			{
				continue;
			}
			bool flag = int.TryParse(array2[1], out var result);
			switch (array2[0])
			{
			case "s":
				if (!Scales.Contains(array2[1]))
				{
					why = "unknown resolution";
					return null;
				}
				customProfile.Scale = array2[1];
				break;
			case "t":
				if (!flag || !TextureMaxes.Contains(result))
				{
					why = "unknown texture size";
					return null;
				}
				customProfile.TextureMax = result;
				break;
			case "b":
				if (!flag || !Buffers.Contains(result))
				{
					why = "unknown buffer size";
					return null;
				}
				customProfile.CardBuffersMb = result;
				break;
			case "r":
				if (!flag || !Ripes.Contains(result))
				{
					why = "unknown value";
					return null;
				}
				customProfile.RipeBlocks = result;
				break;
			case "g":
				if (!flag || !Stages.Contains(result))
				{
					why = "unknown value";
					return null;
				}
				customProfile.StageMb = result;
				break;
			case "f":
				if (!flag || !Fpses.Contains(result))
				{
					why = "unknown FPS clock";
					return null;
				}
				customProfile.Fps = result;
				break;
			case "m":
				customProfile.Guards = array2[1] == "1";
				break;
			case "l":
				customProfile.LevelsOnDemand = array2[1] == "1";
				break;
			default:
				why = "unknown setting " + array2[0];
				return null;
			}
		}
		return customProfile;
	}
}
