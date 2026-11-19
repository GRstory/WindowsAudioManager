using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    private readonly IntPtr _hwnd;

    public void SetHideOnClose(bool value) => _hideOnClose = value;

    public void ShowAndActivate()
    {
        DebugLog.WindowState("ShowAndActivate start", _hwnd, (_appWindow.Presenter as OverlappedPresenter)?.State);
        _appWindow.Show(activateWindow: true);
        DebugLog.WindowState("after Show", _hwnd, (_appWindow.Presenter as OverlappedPresenter)?.State);

        if (_appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
            DebugLog.WindowState("after Restore", _hwnd, presenter.State);
        }

        Activate();
        DebugLog.WindowState("after Activate", _hwnd, (_appWindow.Presenter as OverlappedPresenter)?.State);
        ForceForeground();
        DebugLog.WindowState("after ForceForeground", _hwnd, (_appWindow.Presenter as OverlappedPresenter)?.State);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => DebugLog.WindowState("settled", _hwnd, (_appWindow.Presenter as OverlappedPresenter)?.State));
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    // 트레이 클릭 시 포커스가 explorer(트레이 오버플로 창)에 남아 창이 뒤에 깔리는 문제 대응
    private void ForceForeground()
    {
        if (SetForegroundWindow(_hwnd) && GetForegroundWindow() == _hwnd) return;

        var fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        var myThread = GetCurrentThreadId();
        if (fgThread == 0 || fgThread == myThread) return;

        AttachThreadInput(myThread, fgThread, true);
        SetForegroundWindow(_hwnd);
        AttachThreadInput(myThread, fgThread, false);
    }

    public MainWindow()
    {
        InitializeComponent();
        Title = "Windows Audio Manager";

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hwnd = hwnd;
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
#if DEBUG
        _appWindow.Changed += (_, a) =>
        {
            if (a.DidPresenterChange || a.DidVisibilityChange || a.DidPositionChange || a.DidSizeChange)
                DebugLog.WindowState($"Changed(vis={a.DidVisibilityChange} pos={a.DidPositionChange} size={a.DidSizeChange}) pos={_appWindow.Position.X},{_appWindow.Position.Y}", _hwnd, (_appWindow.Presenter as OverlappedPresenter)?.State);
        };
        Activated += (_, a) => DebugLog.Write($"Activated: {a.WindowActivationState}");
#endif
        _appWindow.Resize(new SizeInt32(1200, 900));
        try { _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "display.ico")); } catch { }

        _appWindow.Closing += (_, args) =>
        {
            DebugLog.WindowState($"Closing hideOnClose={_hideOnClose}", _hwnd, (_appWindow.Presenter as OverlappedPresenter)?.State);
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
        AutoStartToggle.IsOn = AutoStart.IsEnabled();
        SetActiveTab(isSettings: false);

        if (Content is FrameworkElement root)
        {
            root.Loaded += (_, _) =>
            {
                UpdateLoudMaxButtons();
                RefreshDevices();
            };
        }

        _refreshDebounce = DispatcherQueue.CreateTimer();
        _refreshDebounce.Interval = TimeSpan.FromMilliseconds(120);
        _refreshDebounce.IsRepeating = false;
        _refreshDebounce.Tick += (_, _) => RefreshDevices();

        _notificationClient = new DeviceNotificationClient(() => DispatcherQueue.TryEnqueue(ScheduleRefresh));
        _notificationEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    private readonly DispatcherQueueTimer _refreshDebounce;
    private bool _refreshRunning;
    private bool _refreshPending;

    // 장치 변경 알림은 역할(Console/Multimedia/Communications)마다 연달아 오므로 120ms 모아서 한 번만 새로고침
    private void ScheduleRefresh()
    {
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    private async void RefreshDevices()
    {
        if (_refreshRunning)
        {
            _refreshPending = true;
            return;
        }

        _refreshRunning = true;
        try
        {
            do
            {
                _refreshPending = false;
                await RefreshDevicesAsync();
            } while (_refreshPending);
        }
        finally
        {
            _refreshRunning = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

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
        LogText.Text = status;

        _resizeSettleSize = default;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ResizeWindowToContent(0));
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

    private Windows.Foundation.Size _resizeSettleSize;

    private void ResizeWindowToContent(int attempt)
    {
        if (Content is not FrameworkElement root) return;

        var inf = new Windows.Foundation.Size(4000, 4000);
        root.InvalidateMeasure();
        root.UpdateLayout();
        root.Measure(inf);
        var s = root.DesiredSize;
        double scale = root.XamlRoot?.RasterizationScale ?? 1.0;

        int w = Math.Max(480, (int)Math.Ceiling(s.Width * scale) + 4);
        int h = Math.Max(220, (int)Math.Ceiling(s.Height * scale) + 4);
        _appWindow.ResizeClient(new SizeInt32(w, h));

        bool stable = Math.Abs(s.Width - _resizeSettleSize.Width) < 0.5
                   && Math.Abs(s.Height - _resizeSettleSize.Height) < 0.5;
        _resizeSettleSize = s;

        if (!stable && attempt < 10)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ResizeWindowToContent(attempt + 1));
        }
#if DEBUG
        Title = $"DBG a={attempt} stable={stable} sW={s.Width:F0} sH={s.Height:F0} scale={scale:F2} w={w} outAW={OutputList.ActualWidth:F0} inAW={InputList.ActualWidth:F0} outDW={OutputList.DesiredSize.Width:F0} caAW={ContentArea.ActualWidth:F0} navAW={NavSidebar.ActualWidth:F0}";
#endif
    }

    private readonly System.Threading.SemaphoreSlim _batLock = new(1, 1);

    private async Task RunBatAsync(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                LogText.Text = $"{fileName} 파일을 찾을 수 없습니다.";
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
            await _batLock.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    using var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                });
            }
            finally
            {
                _batLock.Release();
            }
            LogText.Text = $"{DateTime.Now:HH:mm:ss}  {fileName} 실행 완료";
        }
        catch (Exception ex)
        {
            LogText.Text = "실행 오류: " + ex.Message;
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

    public async void SetLoudMax(bool on)
    {
        LoudMaxState.IsOn = on;
        UpdateLoudMaxButtons();
        App.Current.Tray?.UpdateLoudMax(on);
        await RunBatAsync(on ? "LoudMax_On.bat" : "LoudMax_Off.bat");
    }

    private void LoudMaxOnButton_Click(object sender, RoutedEventArgs e) => SetLoudMax(true);

    private void LoudMaxOffButton_Click(object sender, RoutedEventArgs e) => SetLoudMax(false);

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => App.Current.ExitApp();

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

    private void MainTabButton_Click(object sender, RoutedEventArgs e) => SetActiveTab(isSettings: false);

    private void SettingsTabButton_Click(object sender, RoutedEventArgs e) => SetActiveTab(isSettings: true);

    private void SetActiveTab(bool isSettings)
    {
        MainTabContent.Opacity = isSettings ? 0 : 1;
        MainTabContent.IsHitTestVisible = !isSettings;
        SettingsTabContent.Opacity = isSettings ? 1 : 0;
        SettingsTabContent.IsHitTestVisible = isSettings;

        MainTabIndicator.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
        SettingsTabIndicator.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
        MainTabSelectedFill.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
        SettingsTabSelectedFill.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void DeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id || string.IsNullOrEmpty(id)) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            await Task.Run(() => PolicyConfig.SetDefaultDevice(id));
            LogText.Text = "기본 장치 + 통신 장치 설정 완료";
        }
        catch (Exception ex)
        {
            LogText.Text = "설정 오류: " + ex.Message;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        // 장치 변경 알림과 합쳐서 한 번만 새로고침 (완료 시 오버레이 숨김)
        ScheduleRefresh();
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
