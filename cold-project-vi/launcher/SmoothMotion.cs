// NVIDIA Smooth Motion (the driver's frame generation) turned on for victory.exe in the driver's
// own profile store (NVAPI DRS), as NVIDIA Profile Inspector does: no NVIDIA App needed.
// Setting ids are the community's names for them (Profile Inspector): 0xB0D384C0 "Smooth Motion -
// Enable" (1 on), 0xB0CC0875 "Smooth Motion - Enabled APIs" (1: Direct3D 12). Function ids and
// struct sizes as AnyXboxX's launcher uses them (tested without administrator rights, driver 621).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VIctoryLauncher
{
    static class SmoothMotion
    {
        const uint EnableId = 0xB0D384C0;
        const uint ApisId = 0xB0CC0875;
        const string Exe = "victory.exe";
        const string ProfileName = "VIctory (edu launcher)";

        const int Str = 4096;                    // NvAPI_UnicodeString: 2048 UTF-16 units
        const int ProfileSize = 4 + Str + 16;    // NVDRS_PROFILE_V1
        const int AppSize = 8 + 4 * Str + 4 + Str; // NVDRS_APPLICATION_V4
        const int SettingSize = 4 + Str + 20 + 2 * 4100; // NVDRS_SETTING_V1

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr QueryInterface(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NoArgs();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OutHandle(out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OneHandle(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FindApp(IntPtr session, IntPtr name, out IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FindProfile(IntPtr session, IntPtr name, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CreateProfile(IntPtr session, IntPtr info, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CreateApp(IntPtr session, IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetSettingFn(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        // The newer calls take two more words (SetSetting: two zeros; GetSetting: a word it fills).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetSettingEx(IntPtr session, IntPtr profile, IntPtr setting, uint a, uint b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetSettingEx(IntPtr session, IntPtr profile, uint id, IntPtr setting, out uint extra);

        static QueryInterface query;

        static T Fn<T>(uint id) where T : class
        {
            var p = query(id);
            if (p == IntPtr.Zero) throw new Exception("NVAPI function 0x" + id.ToString("X8") + " is missing");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        // Recent drivers know some settings (Smooth Motion's among them) only through the newer
        // SetSetting/GetSetting (0x8A2CF5F5 / 0xEA99498D, five arguments); the older ones
        // (0x577DD202 / 0x73BF8338) answer -160, setting not found. The newer first, as AnyXboxX's
        // launcher does, the older where a driver lacks it.
        static int SetSettingAny(IntPtr session, IntPtr profile, IntPtr setting)
        {
            var p = query(0x8A2CF5F5);
            if (p != IntPtr.Zero)
                return ((SetSettingEx)Marshal.GetDelegateForFunctionPointer(p, typeof(SetSettingEx)))(session, profile, setting, 0, 0);
            return Fn<SetSettingFn>(0x577DD202)(session, profile, setting);
        }

        static int GetSettingAny(IntPtr session, IntPtr profile, uint id, IntPtr setting)
        {
            var p = query(0xEA99498D);
            if (p != IntPtr.Zero)
            {
                uint extra;
                return ((GetSettingEx)Marshal.GetDelegateForFunctionPointer(p, typeof(GetSettingEx)))(session, profile, id, setting, out extra);
            }
            return Fn<GetSettingFn>(0x73BF8338)(session, profile, id, setting);
        }

        static bool Load()
        {
            if (query != null) return true;
            var lib = LoadLibrary("nvapi64.dll");
            if (lib == IntPtr.Zero) return false;
            var qi = GetProcAddress(lib, "nvapi_QueryInterface");
            if (qi == IntPtr.Zero) return false;
            query = (QueryInterface)Marshal.GetDelegateForFunctionPointer(qi, typeof(QueryInterface));
            return Fn<NoArgs>(0x0150E828)() == 0;
        }

        static IntPtr Buffer(int size, int version)
        {
            var b = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, b, size);
            Marshal.WriteInt32(b, size | (version << 16));
            return b;
        }

        static void PutString(IntPtr at, string s)
        {
            var bytes = Encoding.Unicode.GetBytes(s + "\0");
            Marshal.Copy(bytes, 0, at, Math.Min(bytes.Length, Str));
        }

        static IntPtr Text(string s)
        {
            var b = Marshal.AllocHGlobal(Str);
            Marshal.Copy(new byte[Str], 0, b, Str);
            PutString(b, s);
            return b;
        }

        // What the driver holds for victory.exe: "on", "off", "not set", or why it cannot say.
        public static string Status()
        {
            try
            {
                if (!Load()) return "no NVIDIA driver";
                IntPtr session;
                if (Fn<OutHandle>(0x0694D52E)(out session) != 0) return "no driver session";
                try
                {
                    Fn<OneHandle>(0x375DBD6B)(session);
                    IntPtr profile;
                    if (!FindOurProfile(session, out profile)) return "not set";
                    var setting = Buffer(SettingSize, 1);
                    try
                    {
                        if (GetSettingAny(session, profile, EnableId, setting) != 0) return "not set";
                        // currentValue (u32) at 4 + Str + 20 + 4100
                        return Marshal.ReadInt32(setting, 4 + Str + 20 + 4100) == 1 ? "on" : "off";
                    }
                    finally { Marshal.FreeHGlobal(setting); }
                }
                finally { Fn<OneHandle>(0xDAD9CFF8)(session); }
            }
            catch (Exception e) { return "unknown (" + e.Message + ")"; }
        }

        static bool FindOurProfile(IntPtr session, out IntPtr profile)
        {
            var name = Text(Exe);
            var app = Buffer(AppSize, 4);
            try
            {
                if (Fn<FindApp>(0xEEE566B2)(session, name, out profile, app) == 0) return true;
                var pname = Text(ProfileName);
                try { return Fn<FindProfile>(0x7E4A9A0B)(session, pname, out profile) == 0; }
                finally { Marshal.FreeHGlobal(pname); }
            }
            finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(app); }
        }

        static int SetDword(IntPtr session, IntPtr profile, uint id, uint value)
        {
            var s = Buffer(SettingSize, 1);
            try
            {
                Marshal.WriteInt32(s, 4 + Str, unchecked((int)id));        // settingId
                Marshal.WriteInt32(s, 4 + Str + 4, 0);                     // NVDRS_DWORD_TYPE
                Marshal.WriteInt32(s, 4 + Str + 20 + 4100, unchecked((int)value)); // currentValue
                return SetSettingAny(session, profile, s);
            }
            finally { Marshal.FreeHGlobal(s); }
        }

        // Writes Smooth Motion on (or off) for victory.exe; returns null when done, else what failed.
        public static string Set(bool on)
        {
            return Write(on, (session, profile) => {
                int r = SetDword(session, profile, EnableId, on ? 1u : 0u);
                if (r != 0) return "the setting was refused (" + r + ")";
                if (on) SetDword(session, profile, ApisId, 1u);
                return null;
            });
        }

        // Resizable BAR in victory.exe's profile, as NVIDIA's own list does for the games it allows
        // it in (NVIDIA Profile Inspector's names): "rBAR - Enable" 0x000F00BA, "rBAR - Enable v2"
        // 0x000BFA21 (driver 616.56+; 2 Enabled, 0 Disabled), "rBAR - Options" 0x000F00BB (1, the
        // value NVIDIA's allowed games carry). Needs Resizable BAR on in the PC's BIOS.
        const uint RebarEnableId = 0x000F00BA, RebarEnableV2Id = 0x000BFA21, RebarOptionsId = 0x000F00BB;

        public static string SetRebar(bool on)
        {
            return Write(on, (session, profile) => {
                int r = SetDword(session, profile, RebarEnableId, on ? 1u : 0u);
                if (r != 0) return "rBAR - Enable was refused (" + r + ")";
                // Older drivers do not know v2; that is not a failure.
                SetDword(session, profile, RebarEnableV2Id, on ? 2u : 0u);
                if (on) SetDword(session, profile, RebarOptionsId, 1u);
                return null;
            });
        }

        public static bool RebarSet()
        {
            try
            {
                if (!Load()) return false;
                IntPtr session;
                if (Fn<OutHandle>(0x0694D52E)(out session) != 0) return false;
                try
                {
                    Fn<OneHandle>(0x375DBD6B)(session);
                    IntPtr profile;
                    if (!FindOurProfile(session, out profile)) return false;
                    var setting = Buffer(SettingSize, 1);
                    try { return GetSettingAny(session, profile, RebarEnableId, setting) == 0 && Marshal.ReadInt32(setting, 4 + Str + 20 + 4100) == 1; }
                    finally { Marshal.FreeHGlobal(setting); }
                }
                finally { Fn<OneHandle>(0xDAD9CFF8)(session); }
            }
            catch (Exception) { return false; }
        }

        // Opens the driver's settings, finds (or with `create`, makes) victory.exe's profile, lets
        // `write` change it and saves; null when done, else what failed.
        static string Write(bool create, Func<IntPtr, IntPtr, string> write)
        {
            try
            {
                if (!Load()) return "no NVIDIA driver (nvapi64.dll)";
                IntPtr session;
                if (Fn<OutHandle>(0x0694D52E)(out session) != 0) return "no driver session";
                try
                {
                    int r = Fn<OneHandle>(0x375DBD6B)(session);
                    if (r != 0) return "the driver's settings did not load (" + r + ")";
                    IntPtr profile;
                    if (!FindOurProfile(session, out profile))
                    {
                        if (!create) return null;
                        var info = Buffer(ProfileSize, 1);
                        PutString(info + 4, ProfileName);
                        try
                        {
                            r = Fn<CreateProfile>(0xCC176068)(session, info, out profile);
                            if (r != 0) return "the profile could not be made (" + r + ")";
                        }
                        finally { Marshal.FreeHGlobal(info); }
                        var app = Buffer(AppSize, 4);
                        PutString(app + 8, Exe);              // appName
                        PutString(app + 8 + Str, "VIctory");  // userFriendlyName
                        try
                        {
                            r = Fn<CreateApp>(0x4347A9DE)(session, profile, app);
                            if (r != 0) return "victory.exe could not be added to the profile (" + r + ")";
                        }
                        finally { Marshal.FreeHGlobal(app); }
                    }
                    var refused = write(session, profile);
                    if (refused != null) return refused;
                    r = Fn<OneHandle>(0xFCBC7E14)(session);
                    if (r != 0) return "the driver did not save (" + r + "): run the launcher as administrator once";
                    return null;
                }
                finally { Fn<OneHandle>(0xDAD9CFF8)(session); }
            }
            catch (Exception e) { return e.Message; }
        }
    }
}
