using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MediaMonkeyZidooSync;

/// <summary>Owns one notification-area icon for the lifetime of the main window.</summary>
internal static class ApplicationBranding
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string text);

    internal static Func<bool> AttachTray(Window window)
    {
        IntPtr icon = IntPtr.Zero;
        HwndSource? source = null;
        NotifyData data = default;
        bool added = false;
        uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        const int callback = 0x8000 + 71;
        IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if ((uint)message == taskbarCreated && icon != IntPtr.Zero)
                added = Shell_NotifyIcon(0, ref data);
            if (message == callback && lparam.ToInt32() == 0x203)
            {
                window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
                handled = true;
            }
            return IntPtr.Zero;
        }
        window.SourceInitialized += (_, _) =>
        {
            icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Resources", "Branding", "Application.ico"), 1, 0, 0, 0x10 | 0x40);
            if (icon == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("Application tray artwork could not be loaded.");
                return;
            }
            var hwnd = new WindowInteropHelper(window).Handle;
            source = HwndSource.FromHwnd(hwnd);
            source.AddHook(Hook);
            data = new NotifyData
            {
                Size = (uint)Marshal.SizeOf<NotifyData>(), Window = hwnd, Id = 1,
                Flags = 1 | 2 | 4, Callback = callback, Icon = icon,
                Tip = "MM Zidoo Sync", Info = "", InfoTitle = ""
            };
            added = Shell_NotifyIcon(0, ref data);
        };
        window.Closed += (_, _) =>
        {
            if (added) Shell_NotifyIcon(2, ref data);
            added = false;
            source?.RemoveHook(Hook);
            if (icon != IntPtr.Zero) { DestroyIcon(icon); icon = IntPtr.Zero; }
        };
        return () => added;
    }
}
