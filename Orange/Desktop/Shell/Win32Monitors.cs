using System.Runtime.InteropServices;
using Orange.Core;
using Orange.Desktop.Layout;
using Orange.Desktop.Model;
using static Orange.Input.Native;

namespace Orange.Desktop.Shell;

/// <summary>Monitors in physical pixels with a stable key from the display configuration.</summary>
internal sealed class Win32Monitors : IMonitors
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var names = DeviceNames();
        var result = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(h, ref info)) return true;

            uint dpi = GetDpiForMonitor(h, MDT_EFFECTIVE_DPI, out var dx, out _) == 0 ? dx : 96;
            var key = names.TryGetValue(info.szDevice, out var n)
                ? new MonitorKey(n.Path, string.IsNullOrWhiteSpace(n.Friendly) ? info.szDevice : n.Friendly)
                : new MonitorKey(info.szDevice, info.szDevice);   // fallback: \\.\DISPLAY2 (not stable across GPU changes)

            result.Add(new MonitorInfo(key, ToRect(info.rcMonitor), ToRect(info.rcWork), (int)dpi,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static PixelRect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    /// <summary>GDI device name (\\.\DISPLAY1) → monitor device path and friendly name.</summary>
    private static Dictionary<string, (string Path, string Friendly)> DeviceNames()
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0) return map;
            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            var modes = new DISPLAYCONFIG_MODE_INFO[nm];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return map;

            foreach (var p in paths.Take((int)np))
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = p.sourceInfo.adapterId,
                    id = p.sourceInfo.id,
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = p.targetInfo.adapterId,
                    id = p.targetInfo.id,
                };
                if (DisplayConfigGetDeviceInfo(ref target) != 0 || string.IsNullOrEmpty(target.monitorDevicePath)) continue;

                // Cloned displays share a source; the first target names it.
                map.TryAdd(source.viewGdiDeviceName, (target.monitorDevicePath, target.monitorFriendlyDeviceName));
            }
        }
        catch (Exception ex)
        {
            JobRunner.Log($"Desktop: display config unavailable: {ex.Message}");
        }
        return map;
    }
}
