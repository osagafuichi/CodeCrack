using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace CodeCrackApp.Services;

/// <summary>Windows light/dark follow. Read <see cref="AppsUseLightTheme"/>; subscribe to changes.</summary>
public static class WindowsTheme
{
    private const int WM_SETTINGCHANGE = 0x001A;

    public static event EventHandler? SystemThemeChanged;

    public static bool AppsUseLightTheme
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int i ? i != 0 : true; // default light
        }
    }

    /// <summary>Call once from a Window (e.g. OnSourceInitialized) to relay WM_SETTINGCHANGE.</summary>
    public static void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var source = HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero)
        {
            string? area = System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam);
            if (area == "ImmersiveColorSet")
                SystemThemeChanged?.Invoke(null, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }
}
