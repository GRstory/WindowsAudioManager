using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WindowsAudioManager;

internal static class DebugLog
{
    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "debug.log");

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    [Conditional("DEBUG")]
    public static void Write(string message)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"); }
        catch { }
    }

    [Conditional("DEBUG")]
    public static void WindowState(string tag, IntPtr hwnd, object? presenterState)
    {
        Write($"{tag}: presenter={presenterState} iconic={IsIconic(hwnd)} visible={IsWindowVisible(hwnd)} foreground={(GetForegroundWindow() == hwnd ? "self" : GetForegroundWindow().ToString("X"))}");
    }
}
