using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VIctoryLauncher;

internal static class SmoothMotion
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr QueryInterface(uint id);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int NoArgs();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int OutHandle(out IntPtr handle);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int OneHandle(IntPtr session);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int FindApp(IntPtr session, IntPtr name, out IntPtr profile, IntPtr app);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int FindProfile(IntPtr session, IntPtr name, out IntPtr profile);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int CreateProfile(IntPtr session, IntPtr info, out IntPtr profile);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int CreateApp(IntPtr session, IntPtr profile, IntPtr app);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint id, IntPtr setting);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int SetSettingEx(IntPtr session, IntPtr profile, IntPtr setting, uint a, uint b);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int GetSettingEx(IntPtr session, IntPtr profile, uint id, IntPtr setting, out uint extra);

	private const uint EnableId = 2966652096u;

	private const uint ApisId = 2966161525u;

	private const string Exe = "victory.exe";

	private const string ProfileName = "VIctory (edu launcher)";

	private const int Str = 4096;

	private const int ProfileSize = 4116;

	private const int AppSize = 20492;

	private const int SettingSize = 12320;

	private const uint RebarEnableId = 983226u;

	private const uint RebarEnableV2Id = 784929u;

	private const uint RebarOptionsId = 983227u;

	private static QueryInterface query;

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibrary(string name);

	[DllImport("kernel32", CharSet = CharSet.Ansi)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);

	private static T Fn<T>(uint id) where T : class
	{
		IntPtr intPtr = query(id);
		if (intPtr == IntPtr.Zero)
		{
			throw new Exception("NVAPI function 0x" + id.ToString("X8") + " is missing");
		}
		return Marshal.GetDelegateForFunctionPointer(intPtr, typeof(T)) as T;
	}

	private static int SetSettingAny(IntPtr session, IntPtr profile, IntPtr setting)
	{
		IntPtr intPtr = query(2318202357u);
		if (intPtr != IntPtr.Zero)
		{
			return ((SetSettingEx)Marshal.GetDelegateForFunctionPointer(intPtr, typeof(SetSettingEx)))(session, profile, setting, 0u, 0u);
		}
		return Fn<SetSettingFn>(1467863554u)(session, profile, setting);
	}

	private static int GetSettingAny(IntPtr session, IntPtr profile, uint id, IntPtr setting)
	{
		IntPtr intPtr = query(3935914381u);
		uint extra;
		if (intPtr != IntPtr.Zero)
		{
			return ((GetSettingEx)Marshal.GetDelegateForFunctionPointer(intPtr, typeof(GetSettingEx)))(session, profile, id, setting, out extra);
		}
		return Fn<GetSettingFn>(1941930808u)(session, profile, id, setting);
	}

	private static bool Load()
	{
		if (query != null)
		{
			return true;
		}
		IntPtr intPtr = LoadLibrary("nvapi64.dll");
		if (intPtr == IntPtr.Zero)
		{
			return false;
		}
		IntPtr procAddress = GetProcAddress(intPtr, "nvapi_QueryInterface");
		if (procAddress == IntPtr.Zero)
		{
			return false;
		}
		query = (QueryInterface)Marshal.GetDelegateForFunctionPointer(procAddress, typeof(QueryInterface));
		return Fn<NoArgs>(22079528u)() == 0;
	}

	private static IntPtr Buffer(int size, int version)
	{
		IntPtr intPtr = Marshal.AllocHGlobal(size);
		Marshal.Copy(new byte[size], 0, intPtr, size);
		Marshal.WriteInt32(intPtr, size | (version << 16));
		return intPtr;
	}

	private static void PutString(IntPtr at, string s)
	{
		byte[] bytes = Encoding.Unicode.GetBytes(s + "\0");
		Marshal.Copy(bytes, 0, at, Math.Min(bytes.Length, 4096));
	}

	private static IntPtr Text(string s)
	{
		IntPtr intPtr = Marshal.AllocHGlobal(4096);
		Marshal.Copy(new byte[4096], 0, intPtr, 4096);
		PutString(intPtr, s);
		return intPtr;
	}

	public static string Status()
	{
		try
		{
			if (!Load())
			{
				return "no NVIDIA driver";
			}
			if (Fn<OutHandle>(110417198u)(out var handle) != 0)
			{
				return "no driver session";
			}
			try
			{
				Fn<OneHandle>(928890219u)(handle);
				if (!FindOurProfile(handle, out var profile))
				{
					return "not set";
				}
				IntPtr intPtr = Buffer(12320, 1);
				try
				{
					if (GetSettingAny(handle, profile, 2966652096u, intPtr) != 0)
					{
						return "not set";
					}
					return (Marshal.ReadInt32(intPtr, 8220) == 1) ? "on" : "off";
				}
				finally
				{
					Marshal.FreeHGlobal(intPtr);
				}
			}
			finally
			{
				Fn<OneHandle>(3671707640u)(handle);
			}
		}
		catch (Exception ex)
		{
			return "unknown (" + ex.Message + ")";
		}
	}

	private static bool FindOurProfile(IntPtr session, out IntPtr profile)
	{
		IntPtr intPtr = Text("victory.exe");
		IntPtr intPtr2 = Buffer(20492, 4);
		try
		{
			if (Fn<FindApp>(4008011442u)(session, intPtr, out profile, intPtr2) == 0)
			{
				return true;
			}
			IntPtr intPtr3 = Text("VIctory (edu launcher)");
			try
			{
				return Fn<FindProfile>(2118818315u)(session, intPtr3, out profile) == 0;
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr3);
			}
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr);
			Marshal.FreeHGlobal(intPtr2);
		}
	}

	private static int SetDword(IntPtr session, IntPtr profile, uint id, uint value)
	{
		IntPtr intPtr = Buffer(12320, 1);
		try
		{
			Marshal.WriteInt32(intPtr, 4100, (int)id);
			Marshal.WriteInt32(intPtr, 4104, 0);
			Marshal.WriteInt32(intPtr, 8220, (int)value);
			return SetSettingAny(session, profile, intPtr);
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr);
		}
	}

	public static string Set(bool on)
	{
		return Write(on, (IntPtr session, IntPtr profile) =>
		{
			int num = SetDword(session, profile, 2966652096u, on ? 1u : 0u);
			if (num != 0)
			{
				return "the setting was refused (" + num + ")";
			}
			if (on)
			{
				SetDword(session, profile, 2966161525u, 1u);
			}
			return (string)null;
		});
	}

	public static string SetRebar(bool on)
	{
		return Write(on, (IntPtr session, IntPtr profile) =>
		{
			int num = SetDword(session, profile, 983226u, on ? 1u : 0u);
			if (num != 0)
			{
				return "rBAR - Enable was refused (" + num + ")";
			}
			SetDword(session, profile, 784929u, on ? 2u : 0u);
			if (on)
			{
				SetDword(session, profile, 983227u, 1u);
			}
			return (string)null;
		});
	}

	public static bool RebarSet()
	{
		try
		{
			if (!Load())
			{
				return false;
			}
			if (Fn<OutHandle>(110417198u)(out var handle) != 0)
			{
				return false;
			}
			try
			{
				Fn<OneHandle>(928890219u)(handle);
				if (!FindOurProfile(handle, out var profile))
				{
					return false;
				}
				IntPtr intPtr = Buffer(12320, 1);
				try
				{
					return GetSettingAny(handle, profile, 983226u, intPtr) == 0 && Marshal.ReadInt32(intPtr, 8220) == 1;
				}
				finally
				{
					Marshal.FreeHGlobal(intPtr);
				}
			}
			finally
			{
				Fn<OneHandle>(3671707640u)(handle);
			}
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static string Write(bool create, Func<IntPtr, IntPtr, string> write)
	{
		try
		{
			if (!Load())
			{
				return "no NVIDIA driver (nvapi64.dll)";
			}
			if (Fn<OutHandle>(110417198u)(out var handle) != 0)
			{
				return "no driver session";
			}
			try
			{
				int num = Fn<OneHandle>(928890219u)(handle);
				if (num != 0)
				{
					return "the driver's settings did not load (" + num + ")";
				}
				if (!FindOurProfile(handle, out var profile))
				{
					if (!create)
					{
						return null;
					}
					IntPtr intPtr = Buffer(4116, 1);
					PutString(intPtr + 4, "VIctory (edu launcher)");
					try
					{
						num = Fn<CreateProfile>(3424084072u)(handle, intPtr, out profile);
						if (num != 0)
						{
							return "the profile could not be made (" + num + ")";
						}
					}
					finally
					{
						Marshal.FreeHGlobal(intPtr);
					}
					IntPtr intPtr2 = Buffer(20492, 4);
					PutString(intPtr2 + 8, "victory.exe");
					PutString(intPtr2 + 8 + 4096, "VIctory");
					try
					{
						num = Fn<CreateApp>(1128770014u)(handle, profile, intPtr2);
						if (num != 0)
						{
							return "victory.exe could not be added to the profile (" + num + ")";
						}
					}
					finally
					{
						Marshal.FreeHGlobal(intPtr2);
					}
				}
				string text = write(handle, profile);
				if (text != null)
				{
					return text;
				}
				num = Fn<OneHandle>(4240211476u)(handle);
				if (num != 0)
				{
					return "the driver did not save (" + num + "): run the launcher as administrator once";
				}
				return null;
			}
			finally
			{
				Fn<OneHandle>(3671707640u)(handle);
			}
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}
}
