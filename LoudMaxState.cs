using System;
using System.IO;

namespace WindowsAudioManager;

internal static class LoudMaxState
{
    private static string StateFile => Path.Combine(AppContext.BaseDirectory, "loudmax.state");

    public static bool IsOn
    {
        get
        {
            try { return File.Exists(StateFile) && File.ReadAllText(StateFile).Trim() == "on"; }
            catch { return false; }
        }
        set
        {
            try { File.WriteAllText(StateFile, value ? "on" : "off"); }
            catch { }
        }
    }
}
