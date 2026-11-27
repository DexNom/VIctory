using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VIctoryLauncher;

internal static class Gpus
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct AdapterDesc1
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;

		public uint VendorId;

		public uint DeviceId;

		public uint SubSysId;

		public uint Revision;

		public UIntPtr DedicatedVideoMemory;

		public UIntPtr DedicatedSystemMemory;

		public UIntPtr SharedSystemMemory;

		public uint LuidLow;

		public int LuidHigh;

		public uint Flags;
	}

	[ComImport]
	[Guid("29038f61-3839-4626-91fd-086879011a05")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDXGIAdapter1
	{
		void SetPrivateData();

		void SetPrivateDataInterface();

		void GetPrivateData();

		void GetParent();

		void EnumOutputs();

		void GetDesc();

		void CheckInterfaceSupport();

		[PreserveSig]
		int GetDesc1(out AdapterDesc1 desc);
	}

	[ComImport]
	[Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDXGIFactory1
	{
		void SetPrivateData();

		void SetPrivateDataInterface();

		void GetPrivateData();

		void GetParent();

		void EnumAdapters();

		void MakeWindowAssociation();

		void GetWindowAssociation();

		void CreateSwapChain();

		void CreateSoftwareAdapter();

		[PreserveSig]
		int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
	}

	private const uint SoftwareAdapter = 2u;

	[DllImport("dxgi.dll")]
	private static extern int CreateDXGIFactory1([In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);

	public static List<GpuAdapter> List()
	{
		List<GpuAdapter> list = new List<GpuAdapter>();
		HashSet<long> hashSet = new HashSet<long>();
		try
		{
			Guid riid = typeof(IDXGIFactory1).GUID;
			if (CreateDXGIFactory1(ref riid, out var factory) != 0 || factory == null)
			{
				return list;
			}
			try
			{
				IDXGIAdapter1 adapter;
				for (uint num = 0u; num < 16 && factory.EnumAdapters1(num, out adapter) == 0; num++)
				{
					if (adapter == null)
					{
						break;
					}
					try
					{
						if (adapter.GetDesc1(out var d) == 0 && (d.Flags & 2) == 0 && d.VendorId != 5140 && hashSet.Add(((long)d.LuidHigh << 32) | d.LuidLow) && !list.Exists((GpuAdapter o) => o.Name == (d.Description ?? "").Trim() && o.DedicatedMb == (long)(d.DedicatedVideoMemory.ToUInt64() >> 20)) && (d.VendorId == 4318 || d.VendorId == 4098 || d.VendorId == 32902))
						{
							GpuAdapter gpuAdapter = new GpuAdapter();
							gpuAdapter.Name = (d.Description ?? "").Trim();
							gpuAdapter.Vendor = d.VendorId;
							gpuAdapter.DedicatedMb = (long)(d.DedicatedVideoMemory.ToUInt64() >> 20);
							gpuAdapter.SharedMb = (long)(d.SharedSystemMemory.ToUInt64() >> 20);
							GpuAdapter gpuAdapter2 = gpuAdapter;
							gpuAdapter2.Integrated = IsIntegrated(gpuAdapter2);
							list.Add(gpuAdapter2);
						}
					}
					finally
					{
						Marshal.ReleaseComObject(adapter);
					}
				}
			}
			finally
			{
				Marshal.ReleaseComObject(factory);
			}
		}
		catch (Exception)
		{
		}
		return list;
	}

	private static bool IsIntegrated(GpuAdapter g)
	{
		if (g.Vendor == 4318)
		{
			return false;
		}
		if (Regex.IsMatch(g.Name, "\\bArc\\b|Radeon\\s*(RX|Pro|VII)", RegexOptions.IgnoreCase))
		{
			return false;
		}
		return g.DedicatedMb < 3072;
	}
}
