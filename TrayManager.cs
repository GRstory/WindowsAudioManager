using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NAudio.CoreAudioApi;
using Windows.Graphics;

namespace WindowsAudioManager;

public sealed class TrayManager : IDisposable
{
    private readonly TrayHostWindow _host;
    private readonly TaskbarIcon _icon;
    private readonly Action _showMainWindow;
    private readonly Action _exitApp;
    private readonly MenuFlyout _menu;
    private readonly ToggleMenuFlyoutItem _apoOnItem;
    private readonly ToggleMenuFlyoutItem _apoOffItem;
    private readonly MenuFlyoutSeparator _devicesTopSeparator;
    private readonly MenuFlyoutItem _devicesHeader;
    private readonly MenuFlyoutSeparator _devicesBottomSeparator;
    private readonly MenuFlyoutItem _openItem;
    private readonly MenuFlyoutItem _exitItem;

    public TrayManager(Action showMainWindow, Action exitApp)
    {
        _showMainWindow = showMainWindow;
        _exitApp = exitApp;

        _host = new TrayHostWindow();
        _icon = _host.TrayIcon;

        _icon.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Icons/display.ico"));
        _icon.ToolTipText = "Windows Audio Manager";
        _icon.NoLeftClickDelay = true;
        _icon.LeftClickCommand = new RelayCommand(() => _showMainWindow());

        _menu = new MenuFlyout();

        _apoOnItem = new ToggleMenuFlyoutItem
        {
            Text = "LoudMax On",
            IsChecked = LoudMaxState.IsOn,
            Command = new RelayCommand(() =>
            {
                RunBat("LoudMax_On.bat");
                LoudMaxState.IsOn = true;
                App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                    App.Current.MainWindow?.UpdateLoudMaxButtons());
            })
        };
        _menu.Items.Add(_apoOnItem);

        _apoOffItem = new ToggleMenuFlyoutItem
        {
            Text = "LoudMax Off",
            IsChecked = !LoudMaxState.IsOn,
            Command = new RelayCommand(() =>
            {
                RunBat("LoudMax_Off.bat");
                LoudMaxState.IsOn = false;
                App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                    App.Current.MainWindow?.UpdateLoudMaxButtons());
            })
        };
        _menu.Items.Add(_apoOffItem);

        _devicesTopSeparator = new MenuFlyoutSeparator();
        _menu.Items.Add(_devicesTopSeparator);

        _devicesHeader = new MenuFlyoutItem { Text = "출력 장치", IsEnabled = false };
        _menu.Items.Add(_devicesHeader);

        _devicesBottomSeparator = new MenuFlyoutSeparator();
        _menu.Items.Add(_devicesBottomSeparator);

        _openItem = new MenuFlyoutItem
        {
            Text = "창 열기",
            Command = new RelayCommand(() => _showMainWindow())
        };
        _menu.Items.Add(_openItem);

        _exitItem = new MenuFlyoutItem
        {
            Text = "종료",
            Command = new RelayCommand(() => _exitApp())
        };
        _menu.Items.Add(_exitItem);

        RefreshDynamicItems();

        _host.Activate();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_host);
        var hostId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(hostId).MoveAndResize(new RectInt32(-32000, -32000, 1, 1));

        _icon.ContextFlyout = _menu;
        _icon.RightClickCommand = new RelayCommand(RefreshDynamicItems);
        _icon.ForceCreate();
    }

    private void RefreshDynamicItems()
    {
        _apoOnItem.IsChecked = LoudMaxState.IsOn;
        _apoOffItem.IsChecked = !LoudMaxState.IsOn;

        int headerIndex = _menu.Items.IndexOf(_devicesHeader);
        int bottomIndex = _menu.Items.IndexOf(_devicesBottomSeparator);
        for (int i = bottomIndex - 1; i > headerIndex; i--)
        {
            _menu.Items.RemoveAt(i);
        }

        try
        {
            var enumerator = new MMDeviceEnumerator();
            string defaultId = "";
            try { defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; } catch { }

            int insertIndex = _menu.Items.IndexOf(_devicesHeader) + 1;
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                       .OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase))
            {
                var id = d.ID;
                var name = d.FriendlyName;
                var item = new ToggleMenuFlyoutItem
                {
                    Text = name,
                    IsChecked = id == defaultId,
                    Command = new RelayCommand(() =>
                    {
                        try { PolicyConfig.SetDefaultDevice(id); } catch { }
                    })
                };
                _menu.Items.Insert(insertIndex++, item);
            }
        }
        catch { }
    }

    private static void RunBat(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path)) return;
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{path}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { }
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
