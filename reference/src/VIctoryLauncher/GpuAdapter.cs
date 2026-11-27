namespace VIctoryLauncher;

internal class GpuAdapter
{
	public string Name = "";

	public uint Vendor;

	public long DedicatedMb;

	public long SharedMb;

	public bool Integrated;

	public string VendorName
	{
		get
		{
			if (Vendor != 4318)
			{
				if (Vendor != 4098)
				{
					if (Vendor != 32902)
					{
						return "0x" + Vendor.ToString("X4");
					}
					return "Intel";
				}
				return "AMD";
			}
			return "NVIDIA";
		}
	}
}
