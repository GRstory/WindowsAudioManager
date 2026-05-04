using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WindowsAudioManager;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool startInTray = Array.IndexOf(args, "--tray") >= 0;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(ctx);
            _ = new App(startInTray);
        });
        return 0;
    }
}
