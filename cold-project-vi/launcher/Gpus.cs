// Every graphics adapter Windows has, read through DXGI (not only NVIDIA's nvidia-smi): vendor, dedicated and
// shared memory. An Intel Iris Xe has no memory of its own (it borrows RAM): nvidia-smi found nothing, the
// video memory stayed "0 GB" and the launcher treated it like a 12 GB card, turning on Crowds and fights and
// frame generation; the game then ran out of memory within minutes (felicienradio's report, 27/11/2026).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VIctoryLauncher
{
    class GpuAdapter
    {
        public string Name = "";
        public uint Vendor;
        public long DedicatedMb, SharedMb;
        public bool Integrated;
        public string VendorName { get { return Vendor == 0x10DE ? "NVIDIA" : Vendor == 0x1002 ? "AMD" : Vendor == 0x8086 ? "Intel" : "0x" + Vendor.ToString("X4"); } }
    }

    static class Gpus
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct AdapterDesc1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint VendorId, DeviceId, SubSysId, Revision;
            public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public uint LuidLow; public int LuidHigh;
            public uint Flags;
        }

        // Only the slots called are given their real shape; the others keep the table's order.
        [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIAdapter1
        {
            void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
            void EnumOutputs(); void GetDesc(); void CheckInterfaceSupport();
            [PreserveSig] int GetDesc1(out AdapterDesc1 desc);
        }

        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIFactory1
        {
            void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
            void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter();
            [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
        }

        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory1([In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);

        const uint SoftwareAdapter = 2;   // DXGI_ADAPTER_FLAG_SOFTWARE (WARP, Microsoft Basic Render)

        // The hardware adapters; empty when DXGI cannot be read.
        public static List<GpuAdapter> List()
        {
            var list = new List<GpuAdapter>();
            // One entry per adapter: DXGI lists a card again for each output a capture tool or virtual display hangs
            // on it (an RTX 4080 SUPER showed four times, 27/11/2026).
            var seen = new HashSet<long>();
            try
            {
                var iid = typeof(IDXGIFactory1).GUID;
                IDXGIFactory1 factory;
                if (CreateDXGIFactory1(ref iid, out factory) != 0 || factory == null) return list;
                try
                {
                    for (uint i = 0; i < 16; i++)
                    {
                        IDXGIAdapter1 a;
                        if (factory.EnumAdapters1(i, out a) != 0 || a == null) break;
                        try
                        {
                            AdapterDesc1 d;
                            if (a.GetDesc1(out d) != 0 || (d.Flags & SoftwareAdapter) != 0 || d.VendorId == 0x1414) continue;
                            if (!seen.Add(((long)d.LuidHigh << 32) | d.LuidLow)) continue;
                            if (list.Exists(o => o.Name == (d.Description ?? "").Trim() && o.DedicatedMb == (long)(d.DedicatedVideoMemory.ToUInt64() >> 20))) continue;
                            // Virtual displays (Parsec, emulators' adapters) are not graphics cards.
                            if (d.VendorId != 0x10DE && d.VendorId != 0x1002 && d.VendorId != 0x8086) continue;
                            var g = new GpuAdapter
                            {
                                Name = (d.Description ?? "").Trim(),
                                Vendor = d.VendorId,
                                DedicatedMb = (long)(d.DedicatedVideoMemory.ToUInt64() >> 20),
                                SharedMb = (long)(d.SharedSystemMemory.ToUInt64() >> 20),
                            };
                            g.Integrated = IsIntegrated(g);
                            list.Add(g);
                        }
                        finally { Marshal.ReleaseComObject(a); }
                    }
                }
                finally { Marshal.ReleaseComObject(factory); }
            }
            catch (Exception) { }
            return list;
        }

        // NVIDIA: always a card of its own. Intel and AMD: integrated unless the name says a card of its own
        // (Arc, Radeon RX / Pro) or it has 3 GB or more of its own memory (an APU's carve-out is 0.5-2 GB).
        static bool IsIntegrated(GpuAdapter g)
        {
            if (g.Vendor == 0x10DE) return false;
            if (Regex.IsMatch(g.Name, @"\bArc\b|Radeon\s*(RX|Pro|VII)", RegexOptions.IgnoreCase)) return false;
            return g.DedicatedMb < 3 * 1024;
        }
    }
}
