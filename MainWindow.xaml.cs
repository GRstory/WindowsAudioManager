using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Windows.Graphics;

namespace WindowsAudioManager;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<AudioDevice> _outputs = new();
    private readonly ObservableCollection<AudioDevice> _inputs = new();
    private readonly AppWindow _appWindow;
    private readonly MMDeviceEnumerator _notificationEnumerator = new();
    private readonly DeviceNotificationClient _notificationClient;

    private bool _hideOnClose;

    public void SetHideOnClose(bool value) => _hideOnClose = value;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Windows Audio Manager";

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        _appWindow.Resize(new SizeInt32(1200, 900));
        try { _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "display.ico")); } catch { }

        _appWindow.Closing += (_, args) =>
        {
            if (_hideOnClose)
            {
                args.Cancel = true;
                _appWindow.Hide();
            }
        };

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
        }

        OutputList.ItemsSource = _outputs;
        InputList.ItemsSource = _inputs;

        if (Content is FrameworkElement root)
        {
            root.Loaded += (_, _) =>
            {
                UpdateLoudMaxButtons();
                RefreshDevices();
            };
        }

        _notificationClient = new DeviceNotificationClient(() => DispatcherQueue.TryEnqueue(RefreshDevices));
        _notificationEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    private void RefreshDevices() => _ = RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        var outputs = new List<AudioDevice>();
        var inputs = new List<AudioDevice>();
        string status;

        try
        {
            await Task.Run(() =>
            {
                var enumerator = new MMDeviceEnumerator();
                string defaultOutId = TryGetDefaultId(enumerator, DataFlow.Render, Role.Multimedia);
                string defaultInId = TryGetDefaultId(enumerator, DataFlow.Capture, Role.Multimedia);

                foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                           .OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase))
                {
                    outputs.Add(new AudioDevice
                    {
                        Id = d.ID,
                        Name = d.FriendlyName,
                        IsDefault = d.ID == defaultOutId,
                        DataFlow = DataFlow.Render
                    });
                }

                foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                           .OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase))
                {
                    inputs.Add(new AudioDevice
                    {
                        Id = d.ID,
                        Name = d.FriendlyName,
                        IsDefault = d.ID == defaultInId,
                        DataFlow = DataFlow.Capture
                    });
                }
            });

            status = $"마지막 새로고침: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ·  출력 {outputs.Count}개 / 입력 {inputs.Count}개";
        }
        catch (Exception ex)
        {
            status = "장치 열거 오류: " + ex.Message;
        }

        _outputs.Clear();
        foreach (var o in outputs) _outputs.Add(o);
        _inputs.Clear();
        foreach (var i in inputs) _inputs.Add(i);
        StatusText.Text = status;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ResizeWindowToContent);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ResizeWindowToContent);
        });
    }

    private static string TryGetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(flow, role).ID;
        }
        catch
        {
            return "";
        }
    }

    private void ResizeWindowToContent()
    {
        if (Content is not FrameworkElement root) return;

        OutputList.MinWidth = 0;
        InputList.MinWidth = 0;
        var inf = new Windows.Foundation.Size(4000, 4000);
        OutputList.Measure(inf);
        InputList.Measure(inf);
        var colWidth = Math.Max(OutputList.DesiredSize.Width, InputList.DesiredSize.Width);
        OutputList.MinWidth = colWidth;
        InputList.MinWidth = colWidth;

        root.InvalidateMeasure();
        root.UpdateLayout();
        root.Measure(inf);
        var s = root.DesiredSize;
        double scale = root.XamlRoot?.RasterizationScale ?? 1.0;
        int w = Math.Max(480, (int)Math.Ceiling(s.Width * scale) + 4);
        int h = Math.Max(220, (int)Math.Ceiling(s.Height * scale) + 4);
        _appWindow.ResizeClient(new SizeInt32(w, h));
    }

    private void RunBat(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                LoudMaxStatusText.Text = $"{fileName} 파일을 찾을 수 없습니다.";
                return;
            }
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
            LoudMaxStatusText.Text = $"{DateTime.Now:HH:mm:ss}  {fileName} 실행 완료";
        }
        catch (Exception ex)
        {
            LoudMaxStatusText.Text = "실행 오류: " + ex.Message;
        }
    }

    public void UpdateLoudMaxButtons()
    {
        var accent = (Style)Application.Current.Resources["AccentButtonStyle"];
        if (LoudMaxState.IsOn)
        {
            LoudMaxOnButton.Style = accent;
            LoudMaxOffButton.ClearValue(Button.StyleProperty);
        }
        else
        {
            LoudMaxOnButton.ClearValue(Button.StyleProperty);
            LoudMaxOffButton.Style = accent;
        }
    }

    private void LoudMaxOnButton_Click(object sender, RoutedEventArgs e)
    {
        RunBat("LoudMax_On.bat");
        LoudMaxState.IsOn = true;
        UpdateLoudMaxButtons();
    }

    private void LoudMaxOffButton_Click(object sender, RoutedEventArgs e)
    {
        RunBat("LoudMax_Off.bat");
        LoudMaxState.IsOn = false;
        UpdateLoudMaxButtons();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => App.Current.ExitApp();

    private SettingsWindow? _settingsWindow;
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    private async void DeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id || string.IsNullOrEmpty(id)) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            await Task.Run(() => PolicyConfig.SetDefaultDevice(id));
            StatusText.Text = "기본 장치 + 통신 장치 설정 완료";
        }
        catch (Exception ex)
        {
            StatusText.Text = "설정 오류: " + ex.Message;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        await RefreshDevicesAsync();
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }
}

internal sealed class DeviceNotificationClient : IMMNotificationClient
{
    private readonly Action _onChanged;
    public DeviceNotificationClient(Action onChanged) => _onChanged = onChanged;
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChanged();
    public void OnDeviceAdded(string pwstrDeviceId) => _onChanged();
    public void OnDeviceRemoved(string deviceId) => _onChanged();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => _onChanged();
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
