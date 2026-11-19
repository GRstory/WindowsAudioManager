using System;
using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace WindowsAudioManager;

public sealed class TrayManager : IDisposable
{
    private readonly TrayHostWindow _host;
    private readonly TaskbarIcon _icon;
    private readonly ToggleMenuFlyoutItem _loudMaxOn;
    private readonly ToggleMenuFlyoutItem _loudMaxOff;

    public TrayManager(App app)
    {
        _host = new TrayHostWindow();
        _icon = _host.TrayIcon;

        _icon.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Icons/display.ico"));
        _icon.ToolTipText = "Windows Audio Manager";
        _icon.NoLeftClickDelay = true;
        _icon.LeftClickCommand = new RelayCommand(app.ShowMainWindow);

        _loudMaxOn = new ToggleMenuFlyoutItem { Text = "LoudMax On", Command = new RelayCommand(() => app.SetLoudMax(true)) };
        _loudMaxOff = new ToggleMenuFlyoutItem { Text = "LoudMax Off", Command = new RelayCommand(() => app.SetLoudMax(false)) };
        UpdateLoudMax(LoudMaxState.IsOn);

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "열기", Command = new RelayCommand(app.ShowMainWindow) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_loudMaxOn);
        menu.Items.Add(_loudMaxOff);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "종료", Command = new RelayCommand(app.ExitApp) });
        _icon.ContextMenuMode = ContextMenuMode.PopupMenu;
        _icon.ContextFlyout = menu;

        _host.Activate();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_host);
        var hostId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(hostId).MoveAndResize(new RectInt32(-32000, -32000, 1, 1));

        _icon.ForceCreate();
    }

    public void UpdateLoudMax(bool on)
    {
        _loudMaxOn.IsChecked = on;
        _loudMaxOff.IsChecked = !on;
    }

    public void Dispose()
    {
        _icon.Dispose();
        _host.Close();
    }
}

internal sealed class TrayHostWindow : Window
{
    public TaskbarIcon TrayIcon { get; }

    public TrayHostWindow()
    {
        TrayIcon = new TaskbarIcon();
        Content = TrayIcon;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var aw = AppWindow.GetFromWindowId(id);
        aw.IsShownInSwitchers = false;
        aw.Resize(new SizeInt32(1, 1));
        if (aw.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.SetBorderAndTitleBar(false, false);
        }
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action _exec;
    public RelayCommand(Action exec) { _exec = exec; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _exec();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
