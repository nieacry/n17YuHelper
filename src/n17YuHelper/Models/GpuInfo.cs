using System.Diagnostics;
using System.Runtime.InteropServices;

namespace N17YuHelper.Models;

public enum GpuVendor
{
    None,
    Nvidia,
    Amd,
    Intel,
    Other,
}

/// <summary>Graphics card and its memory (via DXGI — works for NVIDIA, AMD and Intel).</summary>
public sealed record GpuInfo(string Name, GpuVendor Vendor, long VramBytes, string? NvidiaDriver)
{
    public double VramGb => VramBytes / 1024.0 / 1024 / 1024;

    public override string ToString() =>
        Vendor == GpuVendor.None
            ? "No dedicated GPU found"
            : $"{Name} · {VramGb:0.#} GB" + (NvidiaDriver is { } d ? $" · driver {d}" : "");

    /// <summary>llama.cpp build for this graphics card: cuda-13, cuda-12, vulkan or cpu.</summary>
    public string LlamaBuild
    {
        get
        {
            if (Vendor == GpuVendor.Nvidia)
            {
                if (NvidiaDriver is { } d && int.TryParse(d.Split('.')[0], out int major))
                    return major >= 580 ? "cuda-13" : "cuda-12";
                return "cuda-12";
            }
            return Vendor is GpuVendor.Amd or GpuVendor.Intel or GpuVendor.Other ? "vulkan" : "cpu";
        }
    }

    private static GpuInfo? _cached;

    public static GpuInfo Detect(bool refresh = false)
    {
        if (_cached is not null && !refresh) return _cached;
        GpuInfo result;
        try
        {
            result = DetectDxgi();
        }
        catch (Exception ex)
        {
            Log.Error("GPU detection (DXGI) failed", ex);
            result = new GpuInfo("Unknown", GpuVendor.None, 0, null);
        }
        // Fallback for NVIDIA: nvidia-smi knows the exact amount of memory.
        if (result.Vendor == GpuVendor.None && DetectNvidiaSmi() is { } nv) result = nv;
        Log.Info("GPU: " + result);
        return _cached = result;
    }

    private static GpuInfo DetectDxgi()
    {
        var iid = typeof(IDXGIFactory1).GUID;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factoryObj));
        var factory = (IDXGIFactory1)factoryObj;

        DXGI_ADAPTER_DESC1? best = null;
        try
        {
            for (uint i = 0; ; i++)
            {
                int hr = factory.EnumAdapters1(i, out var adapter);
                if (hr != 0 || adapter is null) break;
                try
                {
                    adapter.GetDesc1(out var desc);
                    bool software = (desc.Flags & 2) != 0;               // DXGI_ADAPTER_FLAG_SOFTWARE
                    if (!software && (best is null || (long)desc.DedicatedVideoMemory > (long)best.Value.DedicatedVideoMemory))
                        best = desc;
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

        if (best is not { } b || (long)b.DedicatedVideoMemory < 512L * 1024 * 1024)
            return new GpuInfo(best?.Description ?? "Integrated graphics", GpuVendor.None, (long)(best?.DedicatedVideoMemory ?? 0), null);

        var vendor = b.VendorId switch
        {
            0x10DE => GpuVendor.Nvidia,
            0x1002 => GpuVendor.Amd,
            0x8086 => GpuVendor.Intel,
            _ => GpuVendor.Other,
        };
        return new GpuInfo(b.Description.Trim(), vendor, (long)b.DedicatedVideoMemory, vendor == GpuVendor.Nvidia ? NvidiaDriverVersion() : null);
    }

    private static GpuInfo? DetectNvidiaSmi()
    {
        var line = RunNvidiaSmi("--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits");
        if (line is null) return null;
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !long.TryParse(parts[1], out var mib)) return null;
        return new GpuInfo(parts[0], GpuVendor.Nvidia, mib * 1024 * 1024, parts[2]);
    }

    private static string? NvidiaDriverVersion() => RunNvidiaSmi("--query-gpu=driver_version --format=csv,noheader");

    private static string? RunNvidiaSmi(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var line = p.StandardOutput.ReadLine()?.Trim();
            p.WaitForExit(3000);
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch
        {
            return null;
        }
    }

    // ---------------- DXGI ----------------

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    // Methods are listed in vtable order, including those inherited from IDXGIObject / IDXGIFactory.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
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
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1? adapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }
}
