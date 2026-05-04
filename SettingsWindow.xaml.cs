using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WindowsAudioManager;

public sealed partial class SettingsWindow : Window
{
    private const int GWLP_HWNDPARENT = -8;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private readonly AppWindow _appWindow;

    public SettingsWindow(Window owner)
    {
        InitializeComponent();
        Title = "설정";

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        _appWindow.Resize(new SizeInt32(480, 320));
        try { _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "display.ico")); } catch { }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        if (_appWindow.TitleBar is { } tb)
        {
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, ownerHwnd);

        PositionRelativeTo(owner);

        AutoStartToggle.IsOn = AutoStart.IsEnabled();
    }

    private void PositionRelativeTo(Window owner)
    {
        var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var ownerId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd);
        var ownerAw = AppWindow.GetFromWindowId(ownerId);

        var op = ownerAw.Position;
        var os = ownerAw.Size;
        var ms = _appWindow.Size;

        int x = op.X + (os.Width - ms.Width) / 2;
        int y = op.Y + 60;
        _appWindow.Move(new PointInt32(x, y));
    }

    private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AutoStart.SetEnabled(AutoStartToggle.IsOn);
        if (AutoStartToggle.IsOn)
        {
            App.Current.EnableTray();
        }
        else
        {
            App.Current.DisableTray();
        }
    }
}
