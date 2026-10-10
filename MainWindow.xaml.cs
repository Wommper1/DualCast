using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DualBluetooth.Audio;
using DualBluetooth.Models;
using DualBluetooth.Services;

namespace DualBluetooth;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private readonly DualAudioRouter _router = new();
    private readonly SettingsService _settingsService = new();
    private readonly AudioDeviceWatcher _deviceWatcher = new();
    private readonly DispatcherTimer _diagnosticTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _reconnectTimer;
    private readonly DispatcherTimer _deviceChangeTimer;
    private AppSettings _settings = new();
    private bool _initializing = true;
    private bool _refreshing;
    private bool _starting;
    private bool _closeRequested;
    private bool _wantedRunning;
    private bool _calibrating;
    private IReadOnlyList<AudioDeviceInfo> _devices = [];
    private CancellationTokenSource? _calibrationCancellation;

    public MainWindow()
    {
        InitializeComponent();
        _diagnosticTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, DiagnosticTimer_Tick, Dispatcher);
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, SaveTimer_Tick, Dispatcher) { IsEnabled = false };
        _reconnectTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, ReconnectTimer_Tick, Dispatcher) { IsEnabled = false };
        _deviceChangeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, DeviceChangeTimer_Tick, Dispatcher) { IsEnabled = false };
        _router.Faulted += Router_Faulted;
        _deviceWatcher.DevicesChanged += DeviceWatcher_DevicesChanged;
        SourceInitialized += MainWindow_SourceInitialized;
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        ApplyAccessibilityTheme();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();
        SelectTaggedItem(ThemeCombo, _settings.Theme, "Graphite");
        SelectTaggedItem(AccentCombo, _settings.Accent, "Ocean");
        SelectTaggedItem(TextSizeCombo, _settings.TextSize.ToString(), "16");
        HighVisibilityCheck.IsChecked = _settings.HighVisibility;
        AlwaysOnTopCheck.IsChecked = _settings.AlwaysOnTop;
        ApplyAccessibilityTheme();
        DelayASlider.Value = Math.Clamp(_settings.DelayAMilliseconds, 0, 2000);
        DelayBSlider.Value = Math.Clamp(_settings.DelayBMilliseconds, 0, 2000);
        VolumeASlider.Value = Math.Clamp(_settings.VolumeAPercent, 0, 100);
        VolumeBSlider.Value = Math.Clamp(_settings.VolumeBPercent, 0, 100);
        AutoReconnectCheck.IsChecked = _settings.AutoReconnect;
        ShowUnavailableCheck.IsChecked = _settings.ShowUnavailableDevices;
        SelectLatency(_settings.RenderLatencyMilliseconds);
        try { RefreshDeviceLists(); }
        catch (Exception ex)
        {
            // Windows audio may not be answering yet; the rest of the window must still finish setting up.
            AppLog.Error("Device list failed at start-up", ex);
            SetStatus("Device scan failed", ex.Message, "#FF6B7A");
        }
        UpdateValueLabels();
        _initializing = false;
        _diagnosticTimer.Start();
        SaveSettings();
    }

    private void RefreshDeviceLists()
    {
        var devices = DualAudioRouter.GetRenderDevices(_settings.FavoriteDeviceIds, ShowUnavailableCheck.IsChecked == true);
        _devices = devices;
        // The saved choice wins over the on-screen fallback so a temporarily missing device is not forgotten.
        var oldA = _settings.OutputADeviceId ?? SelectedId(OutputACombo);
        var oldB = _settings.OutputBDeviceId ?? SelectedId(OutputBCombo);
        _refreshing = true;
        try
        {
            OutputACombo.ItemsSource = devices;
            OutputBCombo.ItemsSource = devices;
            var outputAId = ExistingId(devices, oldA)
                ?? devices.FirstOrDefault(device => device.IsActive && device.IsDefault)?.Id
                ?? devices.FirstOrDefault(device => device.IsActive)?.Id;
            var outputBId = ExistingId(devices, oldB)
                ?? devices.FirstOrDefault(device => device.IsActive && device.Id != outputAId)?.Id;
            OutputACombo.SelectedValue = outputAId;
            OutputBCombo.SelectedValue = outputBId;
        }
        finally { _refreshing = false; }
        UpdateFavoriteButtons();
        var activeCount = devices.Count(device => device.IsActive);
        var unavailableCount = devices.Count - activeCount;
        DiagnosticText.Text = activeCount == 0
            ? "No connected playback devices were found. Connect the earbuds in Windows Bluetooth settings, then refresh."
            : $"Found {activeCount} connected device{(activeCount == 1 ? "" : "s")}" +
              (unavailableCount > 0 ? $" and {unavailableCount} disconnected device{(unavailableCount == 1 ? "" : "s")}." : ".");
    }

    private static string? ExistingId(IReadOnlyList<AudioDeviceInfo> devices, string? id) =>
        id is not null && devices.Any(device => device.Id == id) ? id : null;

    private static string? SelectedId(ComboBox comboBox) => comboBox.SelectedValue as string;

    private static AudioDeviceInfo? SelectedDevice(ComboBox comboBox) => comboBox.SelectedItem as AudioDeviceInfo;

    private void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        try { RefreshDeviceLists(); QueueSave(); }
        catch (Exception ex) { SetStatus("Device scan failed", ex.Message, "#FF6B7A"); }
    }

    private void ShowUnavailable_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        _settings.ShowUnavailableDevices = ShowUnavailableCheck.IsChecked == true;
        RefreshDeviceLists();
        QueueSave();
    }

    private void FavoriteA_Click(object sender, RoutedEventArgs e) => ToggleFavorite(OutputACombo);

    private void FavoriteB_Click(object sender, RoutedEventArgs e) => ToggleFavorite(OutputBCombo);

    private void ToggleFavorite(ComboBox comboBox)
    {
        var device = SelectedDevice(comboBox);
        if (device is null)
            return;

        if (_settings.FavoriteDeviceIds.Contains(device.Id, StringComparer.Ordinal))
            _settings.FavoriteDeviceIds.RemoveAll(id => string.Equals(id, device.Id, StringComparison.Ordinal));
        else
            _settings.FavoriteDeviceIds.Add(device.Id);

        RefreshDeviceLists();
        QueueSave();
    }

    private void UpdateFavoriteButtons()
    {
        UpdateFavoriteButton(FavoriteAButton, SelectedDevice(OutputACombo));
        UpdateFavoriteButton(FavoriteBButton, SelectedDevice(OutputBCombo));
    }

    private void UpdateFavoriteButton(Button button, AudioDeviceInfo? device)
    {
        button.IsEnabled = device is not null && !_router.IsRunning;
        button.Content = device is not null && _settings.FavoriteDeviceIds.Contains(device.Id, StringComparer.Ordinal)
            ? "★ Favorite"
            : "☆ Favorite";
    }

    private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("Could not open Settings", ex.Message, "#FF6B7A"); }
    }

    private void OpenBluetoothSettings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("Could not open Bluetooth Settings", ex.Message, "#FF6B7A"); }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsPopup.IsOpen = !SettingsPopup.IsOpen;

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => SettingsPopup.IsOpen = false;

    private void AppearanceSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        ReadAppearanceSettings();
        ApplyAccessibilityTheme();
        QueueSave();
    }

    private void AppearanceSettingChanged_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        ReadAppearanceSettings();
        ApplyAccessibilityTheme();
        QueueSave();
    }

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        SelectTaggedItem(ThemeCombo, "Graphite", "Graphite");
        SelectTaggedItem(AccentCombo, "Ocean", "Ocean");
        SelectTaggedItem(TextSizeCombo, "16", "16");
        HighVisibilityCheck.IsChecked = true;
        AlwaysOnTopCheck.IsChecked = false;
        ReadAppearanceSettings();
        ApplyAccessibilityTheme();
        QueueSave();
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await StartStreamingAsync(isReconnect: false);

    private async Task StartStreamingAsync(bool isReconnect)
    {
        RoutingOptions? options;
        string validationMessage;
        if (isReconnect)
        {
            // Go by the saved outputs and a fresh device list, and keep trying while a device is still away.
            var plan = PlanReconnect(out options);
            if (plan.State == ReconnectState.NotConfigured)
            {
                _wantedRunning = false;
                SetStatus("Needs setup", plan.Message, "#FFB84D");
                return;
            }
            if (plan.State == ReconnectState.Waiting)
            {
                SetStatus("Reconnecting", plan.Message, "#FFB84D");
                if (_wantedRunning && AutoReconnectCheck.IsChecked == true) _reconnectTimer.Start();
                return;
            }
        }
        else if (!TryCreateOptions(out options, out validationMessage))
        {
            SetStatus("Needs setup", validationMessage, "#FFB84D");
            return;
        }
        _starting = true;
        try
        {
            StartButton.IsEnabled = false;
            CommitOutputSelection();
            SetStatus("Starting", "Opening both Bluetooth endpoints and the protected system-audio capture path…", "#60D2FF");
            AppLog.Info($"Starting: A delay {options!.DelayAMilliseconds} ms, B delay {options.DelayBMilliseconds} ms, buffer {options.RenderLatencyMilliseconds} ms, reconnect={isReconnect}");
            await _router.StartAsync(options);
            // A fault can land while the start is still finishing; do not announce streaming over a stopped router.
            if (!_router.IsRunning)
                throw new InvalidOperationException("Audio stopped as soon as it started.");
            AppLog.Info("Streaming started");
            _wantedRunning = true;
            _reconnectTimer.Stop();
            SetStreamingUi(true);
            var advice = OutputAdvice.DefaultDeviceEcho(_devices, options.OutputADeviceId, options.OutputBDeviceId);
            var streaming = isReconnect ? "Devices reconnected. Streaming resumed." : "Streaming system audio to both outputs.";
            SetStatus("Streaming", advice is null ? streaming : $"{streaming} {advice}", advice is null ? "#41D99A" : "#FFB84D");
            SaveSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error("Start failed", ex);
            _router.Stop();
            SetStreamingUi(false);
            SetStatus("Could not start", FriendlyAudioError(ex), "#FF6B7A");
            if (_wantedRunning && AutoReconnectCheck.IsChecked == true) _reconnectTimer.Start();
        }
        finally
        {
            _starting = false;
            if (_closeRequested)
                Close();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _wantedRunning = false;
        _reconnectTimer.Stop();
        LogStop("user");
        _router.Stop();
        SetStreamingUi(false);
        SetStatus("Stopped", "The audio devices have been released.", "#6F809D");
        BufferText.Text = "—";
    }

    private async void Calibration_Click(object sender, RoutedEventArgs e)
    {
        if (_calibrating)
        {
            _calibrationCancellation?.Cancel();
            return;
        }
        if (_router.IsRunning)
        {
            SetStatus("Stop first", "Stop streaming before playing the sync test.", "#FFB84D");
            return;
        }
        if (!TryCreateOptions(out var options, out var validationMessage))
        {
            SetStatus("Needs setup", validationMessage, "#FFB84D");
            return;
        }
        _calibrating = true;
        _calibrationCancellation = new CancellationTokenSource();
        CalibrationButton.Content = "■  Stop test";
        StartButton.IsEnabled = false;
        SetStatus("Calibrating", "Listen for four short pulses from both devices.", "#56C7FF");
        try
        {
            CommitOutputSelection();
            SaveSettings();
            await _router.PlayCalibrationAsync(options!, _calibrationCancellation.Token);
            SetStatus("Calibration finished", "If one device sounded first, add delay to that device and test again.", "#41D99A");
        }
        catch (OperationCanceledException) { SetStatus("Calibration stopped", "The calibration test was cancelled.", "#6F809D"); }
        catch (Exception ex) { AppLog.Error("Calibration failed", ex); SetStatus("Calibration failed", FriendlyAudioError(ex), "#FF6B7A"); }
        finally
        {
            _calibrationCancellation?.Dispose();
            _calibrationCancellation = null;
            _calibrating = false;
            CalibrationButton.Content = "▶  Play sync test";
            CalibrationButton.IsEnabled = true;
            StartButton.IsEnabled = true;
        }
    }

    private void LogStop(string reason)
    {
        if (_router.IsRunning)
            AppLog.Info($"Streaming stopped ({reason}); drift corrections: {_router.DriftDropCount}; gaps A/B: {_router.UnderrunCounts.A}/{_router.UnderrunCounts.B}");
    }

    private void ResetCalibration_Click(object sender, RoutedEventArgs e)
    {
        DelayASlider.Value = 0;
        DelayBSlider.Value = 0;
        QueueSave();
    }

    private void Router_Faulted(object? sender, string message)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            // Several callbacks can fault together; only the first one tears down.
            if (!_router.IsRunning)
                return;
            // Stop off the UI thread: it waits on the lifecycle gate, which StartAsync may hold.
            AppLog.Warn($"Router fault: {message}");
            LogStop("fault");
            await Task.Run(_router.Stop);
            SetStreamingUi(false);
            SetStatus("Connection lost", message, "#FF6B7A");
            if (_wantedRunning && AutoReconnectCheck.IsChecked == true)
            {
                DiagnosticText.Text += " Retrying every five seconds…";
                _reconnectTimer.Start();
            }
        });
    }

    private void DeviceWatcher_DevicesChanged(object? sender, EventArgs e)
    {
        // Windows fires several notifications per connect/disconnect; wait for the burst to settle.
        Dispatcher.BeginInvoke(() =>
        {
            _deviceChangeTimer.Stop();
            _deviceChangeTimer.Start();
        });
    }

    private async void DeviceChangeTimer_Tick(object? sender, EventArgs e)
    {
        _deviceChangeTimer.Stop();
        AppLog.Info("Playback devices changed" + (_router.IsRunning ? " (streaming; list not refreshed)" : ""));
        if (_router.IsRunning)
        {
            DiagnosticText.Text = "Windows reported a playback-device change. Monitoring both active streams…";
            return;
        }

        try { RefreshDeviceLists(); }
        catch (Exception ex)
        {
            AppLog.Error("Device list refresh failed", ex);
            SetStatus("Device update failed", ex.Message, "#FF7586");
        }

        // A device that was away may just have come back: try now instead of waiting for the next five-second tick.
        if (_wantedRunning && AutoReconnectCheck.IsChecked == true && !_router.IsRunning && !_starting && !_calibrating)
        {
            _reconnectTimer.Stop();
            await StartStreamingAsync(isReconnect: true);
        }
    }

    private async void ReconnectTimer_Tick(object? sender, EventArgs e)
    {
        _reconnectTimer.Stop();
        if (_wantedRunning && !_router.IsRunning) await StartStreamingAsync(isReconnect: true);
    }

    private void DiagnosticTimer_Tick(object? sender, EventArgs e)
    {
        if (!_router.IsRunning) return;
        var buffers = _router.BufferedMilliseconds;
        var signal = _router.HasRecentAudioSignal ? "signal active" : "waiting for Windows audio";
        var drops = _router.DriftDropCount;
        var underruns = _router.UnderrunCounts;
        BufferText.Text = $"A {buffers.A:0} ms  ·  B {buffers.B:0} ms  ·  {signal}" +
                          (underruns.A + underruns.B > 0 ? $"  ·  gaps A {underruns.A} / B {underruns.B}" : "") +
                          (drops > 0 ? $"  ·  {drops} drift correction{(drops == 1 ? "" : "s")}" : "");
    }

    private ReconnectPlan PlanReconnect(out RoutingOptions? options)
    {
        options = null;
        IReadOnlyList<AudioDeviceInfo> devices;
        try
        {
            devices = DualAudioRouter.GetRenderDevices(_settings.FavoriteDeviceIds, includeUnavailable: true);
            RefreshDeviceLists();
        }
        catch (Exception ex)
        {
            AppLog.Error("Device scan failed while reconnecting", ex);
            return new ReconnectPlan(ReconnectState.Waiting, "Windows audio is not answering yet; trying again shortly…");
        }
        var plan = ReconnectPlanner.Plan(_settings.OutputADeviceId, _settings.OutputBDeviceId, devices);
        if (plan.State == ReconnectState.Ready)
            options = new RoutingOptions(_settings.OutputADeviceId!, _settings.OutputBDeviceId!, (int)DelayASlider.Value,
                (int)DelayBSlider.Value, (float)(VolumeASlider.Value / 100d), (float)(VolumeBSlider.Value / 100d), SelectedLatency());
        return plan;
    }

    private void ShowOutputAdvice()
    {
        if (_router.IsRunning || _starting || _calibrating)
            return;
        var advice = OutputAdvice.DefaultDeviceEcho(_devices, SelectedId(OutputACombo), SelectedId(OutputBCombo));
        if (advice is not null)
            SetStatus("Heads up", advice, "#FFB84D");
    }

    private bool TryCreateOptions(out RoutingOptions? options, out string message)
    {
        options = null;
        var outputA = SelectedId(OutputACombo);
        var outputB = SelectedId(OutputBCombo);
        if (outputA is null || outputB is null)
        {
            message = "Select two connected output devices.";
            return false;
        }
        if (SelectedDevice(OutputACombo)?.IsActive != true || SelectedDevice(OutputBCombo)?.IsActive != true)
        {
            message = "Both selected devices must show as connected. Open Bluetooth devices, connect the earbuds, then refresh.";
            return false;
        }
        if (outputA == outputB)
        {
            message = "Choose two different output devices.";
            return false;
        }
        options = new RoutingOptions(outputA, outputB, (int)DelayASlider.Value, (int)DelayBSlider.Value,
            (float)(VolumeASlider.Value / 100d), (float)(VolumeBSlider.Value / 100d), SelectedLatency());
        message = string.Empty;
        return true;
    }

    private void SetStreamingUi(bool streaming)
    {
        StartButton.IsEnabled = !streaming && !_calibrating;
        StopButton.IsEnabled = streaming;
        CalibrationButton.IsEnabled = !streaming && !_calibrating;
        OutputACombo.IsEnabled = !streaming;
        OutputBCombo.IsEnabled = !streaming;
        FavoriteAButton.IsEnabled = !streaming && SelectedDevice(OutputACombo) is not null;
        FavoriteBButton.IsEnabled = !streaming && SelectedDevice(OutputBCombo) is not null;
        DelayASlider.IsEnabled = !streaming;
        DelayBSlider.IsEnabled = !streaming;
        ResetCalibrationButton.IsEnabled = !streaming;
        ShowUnavailableCheck.IsEnabled = !streaming;
        LatencyCombo.IsEnabled = !streaming;
    }

    private void SetStatus(string header, string details, string color)
    {
        HeaderStatusText.Text = header;
        StatusDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        DiagnosticText.Text = details;
    }

    private static string FriendlyAudioError(Exception exception) =>
        $"{exception.GetBaseException().Message} Check that both Bluetooth devices are connected in Stereo/A2DP mode, then refresh devices.";

    private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { UpdateValueLabels(); QueueSave(); }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateValueLabels();
        _router.SetVolumes((float)(VolumeASlider?.Value / 100d ?? 1), (float)(VolumeBSlider?.Value / 100d ?? 1));
        QueueSave();
    }

    private void UpdateValueLabels()
    {
        if (DelayAText is null || DelayBText is null || VolumeAText is null || VolumeBText is null) return;
        DelayAText.Text = $"{DelayASlider.Value:0} ms";
        DelayBText.Text = $"{DelayBSlider.Value:0} ms";
        VolumeAText.Text = $"{VolumeASlider.Value:0}%";
        VolumeBText.Text = $"{VolumeBSlider.Value:0}%";
    }

    private void SettingChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFavoriteButtons();
        if (_refreshing || _initializing)
            return;
        if (ReferenceEquals(sender, OutputACombo)) _settings.OutputADeviceId = SelectedId(OutputACombo);
        else if (ReferenceEquals(sender, OutputBCombo)) _settings.OutputBDeviceId = SelectedId(OutputBCombo);
        ShowOutputAdvice();
        QueueSave();
    }

    private void CommitOutputSelection()
    {
        _settings.OutputADeviceId = SelectedId(OutputACombo);
        _settings.OutputBDeviceId = SelectedId(OutputBCombo);
    }
    private void SettingChanged_Click(object sender, RoutedEventArgs e) => QueueSave();

    private void QueueSave()
    {
        if (_initializing || _saveTimer is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e) { _saveTimer.Stop(); SaveSettings(); }

    private void SaveSettings()
    {
        if (_initializing) return;
        _settings.DelayAMilliseconds = (int)DelayASlider.Value;
        _settings.DelayBMilliseconds = (int)DelayBSlider.Value;
        _settings.VolumeAPercent = (int)VolumeASlider.Value;
        _settings.VolumeBPercent = (int)VolumeBSlider.Value;
        _settings.RenderLatencyMilliseconds = SelectedLatency();
        _settings.AutoReconnect = AutoReconnectCheck.IsChecked == true;
        _settings.ShowUnavailableDevices = ShowUnavailableCheck.IsChecked == true;
        ReadAppearanceSettings();
        try { _settingsService.Save(_settings); }
        catch (Exception ex)
        {
            AppLog.Error("Settings save failed", ex);
            DiagnosticText.Text = $"Settings could not be saved: {ex.Message}";
        }
    }

    private int SelectedLatency()
    {
        if (LatencyCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value)) return value;
        return 100;
    }

    private void SelectLatency(int requested)
    {
        var supported = new[] { 50, 100, 150, 250 };
        var closest = supported.MinBy(value => Math.Abs(value - requested));
        LatencyCombo.SelectedItem = LatencyCombo.Items.OfType<ComboBoxItem>()
            .First(item => item.Tag?.ToString() == closest.ToString());
    }

    private static void SelectTaggedItem(ComboBox comboBox, string? requested, string fallback)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), requested, StringComparison.OrdinalIgnoreCase))
            ?? comboBox.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == fallback);
    }

    private void ReadAppearanceSettings()
    {
        _settings.Theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Graphite";
        _settings.Accent = (AccentCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Ocean";
        _settings.TextSize = int.TryParse((TextSizeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var size) ? size : 16;
        _settings.HighVisibility = HighVisibilityCheck.IsChecked == true;
        _settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Stopping the router waits on a lock the in-flight start holds, and that start needs this thread to finish.
        // Defer the close until the start returns instead of blocking here.
        if (_starting)
        {
            _closeRequested = true;
            _wantedRunning = false;
            e.Cancel = true;
            return;
        }
        _wantedRunning = false;
        _calibrationCancellation?.Cancel();
        _diagnosticTimer.Stop();
        _reconnectTimer.Stop();
        _deviceChangeTimer.Stop();
        _saveTimer.Stop();
        SaveSettings();
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        _deviceWatcher.Dispose();
        _router.Dispose();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, Marshal.SizeOf<int>());
        ApplyWindowChromeColors();
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            ApplyAccessibilityTheme();
    }

    private void ApplyAccessibilityTheme()
    {
        var resources = Application.Current.Resources;
        Topmost = _settings.AlwaysOnTop;
        FontSize = _settings.TextSize is 16 ? 16 : 14;
        if (SystemParameters.HighContrast)
        {
            resources["WindowBrush"] = SystemColors.WindowBrush;
            resources["PanelBrush"] = SystemColors.WindowBrush;
            resources["PanelRaisedBrush"] = SystemColors.ControlBrush;
            resources["InputBrush"] = SystemColors.WindowBrush;
            resources["InputHoverBrush"] = SystemColors.ControlBrush;
            resources["BorderBrush"] = SystemColors.ControlDarkBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush;
            resources["AccentStrongBrush"] = SystemColors.HighlightBrush;
            resources["AccentHoverBrush"] = SystemColors.HotTrackBrush;
            resources["TextBrush"] = SystemColors.WindowTextBrush;
            resources["MutedBrush"] = SystemColors.GrayTextBrush;
            return;
        }

        var palette = _settings.Theme switch
        {
            "Graphite" => (Window: "#101214", Panel: "#171A1D", Raised: "#1D2226", Input: "#252B30", Hover: "#2C343A", Border: "#3A454D"),
            "Oled" => (Window: "#000000", Panel: "#07090C", Raised: "#0C1015", Input: "#121820", Hover: "#19222C", Border: "#293542"),
            _ => (Window: "#070B14", Panel: "#0E1625", Raised: "#142034", Input: "#18253A", Hover: "#1D2D46", Border: "#2A3A54")
        };
        var accent = _settings.Accent switch
        {
            "Violet" => (Soft: "#B9A5FF", Strong: "#7655D5", Hover: "#8969E7"),
            "Emerald" => (Soft: "#65E8B7", Strong: "#0D966B", Hover: "#15AC7A"),
            _ => (Soft: "#60D2FF", Strong: "#0A93D0", Hover: "#14A8E8")
        };

        resources["WindowBrush"] = BrushFrom(palette.Window);
        resources["PanelBrush"] = BrushFrom(palette.Panel);
        resources["PanelRaisedBrush"] = BrushFrom(palette.Raised);
        resources["InputBrush"] = BrushFrom(palette.Input);
        resources["InputHoverBrush"] = BrushFrom(palette.Hover);
        resources["BorderBrush"] = BrushFrom(_settings.HighVisibility ? "#60758A" : palette.Border);
        resources["AccentBrush"] = BrushFrom(accent.Soft);
        resources["AccentStrongBrush"] = BrushFrom(accent.Strong);
        resources["AccentHoverBrush"] = BrushFrom(accent.Hover);
        resources["TextBrush"] = BrushFrom("#EAF0F8");
        resources["MutedBrush"] = BrushFrom(_settings.HighVisibility ? "#C4CFDC" : "#A7B5CA");
        ApplyWindowChromeColors();
    }

    private void ApplyWindowChromeColors()
    {
        if (SystemParameters.HighContrast)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var caption = ColorRef((SolidColorBrush)Application.Current.Resources["WindowBrush"]);
        var text = ColorRef((SolidColorBrush)Application.Current.Resources["TextBrush"]);
        var border = ColorRef((SolidColorBrush)Application.Current.Resources["BorderBrush"]);
        _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(handle, DwmTextColor, ref text, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(handle, DwmBorderColor, ref border, Marshal.SizeOf<int>());
    }

    private static int ColorRef(SolidColorBrush brush) =>
        brush.Color.R | (brush.Color.G << 8) | (brush.Color.B << 16);

    private static SolidColorBrush BrushFrom(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int attributeValue, int attributeSize);
}
