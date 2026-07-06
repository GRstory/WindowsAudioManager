using Microsoft.UI.Xaml;

namespace WindowsAudioManager;

public partial class App : Application
{
    private readonly bool _startInTray;
    public MainWindow? MainWindow { get; private set; }
    public TrayManager? Tray { get; private set; }

    public App() : this(false) { }

    public App(bool startInTray)
    {
        _startInTray = startInTray;
        InitializeComponent();
    }

    public static new App Current => (App)Application.Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();

        bool trayActive = _startInTray || AutoStart.IsEnabled();
        if (trayActive)
        {
            EnableTray();
        }

        if (!_startInTray)
        {
            MainWindow.Activate();
        }
    }

    public void EnableTray()
    {
        if (Tray == null) Tray = new TrayManager(ShowMainWindow);
        MainWindow?.SetHideOnClose(true);
    }

    public void DisableTray()
    {
        Tray?.Dispose();
        Tray = null;
        MainWindow?.SetHideOnClose(false);
    }

    public void ShowMainWindow()
    {
        MainWindow?.DispatcherQueue.TryEnqueue(() => MainWindow?.Activate());
    }

    public void ExitApp()
    {
        Tray?.Dispose();
        Tray = null;
        MainWindow?.DispatcherQueue.TryEnqueue(() => Exit());
    }
}
