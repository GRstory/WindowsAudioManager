using System.Diagnostics;
using Microsoft.Win32;

namespace WindowsAudioManager;

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "WindowsAudioManager";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(ValueName) is null) return false;

        // 작업 관리자/설정에서 끈 경우 첫 바이트가 홀수(예: 3)
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        if (enabled)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue(ValueName, $"\"{exePath}\" --tray");
            }

            // 작업 관리자에서 '사용 안 함' 처리된 기록 제거
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
