using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Switchit;

internal static class MonitorArea
{
    public sealed record SavedPosition(string DeviceName, int Right, int Bottom, int OffsetRight, int OffsetBottom);

    public static SavedPosition? Capture(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return null;
        var info = ReadMonitor(MonitorFromWindow(handle, 2));
        return info is null ? null : new SavedPosition(info.Value.DeviceName, rect.Right, rect.Bottom,
            rect.Right - info.Value.Monitor.Left, rect.Bottom - info.Value.Monitor.Top);
    }

    public static void Restore(Window window, SavedPosition position)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return;
        MonitorInfo? matched = null;
        MonitorEnum callback = (IntPtr monitor, IntPtr dc, ref NativeRect bounds, IntPtr data) =>
        {
            var info = ReadMonitor(monitor);
            if (info is { } found && string.Equals(found.DeviceName, position.DeviceName, StringComparison.OrdinalIgnoreCase))
                matched = found;
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        var target = matched ?? ReadMonitor(MonitorFromPoint(new NativePoint(position.Right - 1, position.Bottom - 1), 2));
        if (target is not { } screen) return;
        var right = matched.HasValue ? screen.Monitor.Left + position.OffsetRight : position.Right;
        var bottom = matched.HasValue ? screen.Monitor.Top + position.OffsetBottom : position.Bottom;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = Math.Clamp(right - width, screen.Work.Left, Math.Max(screen.Work.Left, screen.Work.Right - width));
        var y = Math.Clamp(bottom - height, screen.Work.Top, Math.Max(screen.Work.Top, screen.Work.Bottom - height));
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }

    private static MonitorInfo? ReadMonitor(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), DeviceName = "" };
        return GetMonitorInfo(monitor, ref info) ? info : null;
    }

    public static Rect For(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice;
        var topLeft = new Point(info.Work.Left, info.Work.Top);
        var bottomRight = new Point(info.Work.Right, info.Work.Bottom);
        if (transform is { } t) { topLeft = t.Transform(topLeft); bottomRight = t.Transform(bottomRight); }
        return new Rect(topLeft, bottomRight);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y) { public readonly int X = x, Y = y; }
    private delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref NativeRect bounds, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
