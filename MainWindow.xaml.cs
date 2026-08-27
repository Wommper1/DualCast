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
    private AppSettings _settings = new();
    private bool _initializing = true;
    private bool _wantedRunning;
    private bool _calibrating;
    private CancellationTokenSource? _calibrationCancellation;

    public MainWindow()
    {
        InitializeComponent();
        _diagnosticTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, DiagnosticTimer_Tick, Dispatcher);
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, SaveTimer_Tick, Dispatcher) { IsEnabled = false };
        _reconnectTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, ReconnectTimer_Tick, Dispatcher) { IsEnabled = false };
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
        SelectLatency(_settings.RenderLatencyMilliseconds);
        RefreshDeviceLists();
        UpdateValueLabels();
        _initializing = false;
        _diagnosticTimer.Start();
        SaveSettings();
    }

    private void RefreshDeviceLists()
    {
        var devices = DualAudioRouter.GetActiveRenderDevices();
        var oldA = SelectedId(OutputACombo) ?? _settings.OutputADeviceId;
        var oldB = SelectedId(OutputBCombo) ?? _settings.OutputBDeviceId;
        OutputACombo.ItemsSource = devices;
        OutputBCombo.ItemsSource = devices;
        var outputAId = ExistingId(devices, oldA) ?? devices.FirstOrDefault(device => device.IsDefault)?.Id ?? devices.FirstOrDefault()?.Id;
        var outputBId = ExistingId(devices, oldB) ?? devices.FirstOrDefault(device => device.Id != outputAId)?.Id;
        OutputACombo.SelectedValue = outputAId;
        OutputBCombo.SelectedValue = outputBId;
        DiagnosticText.Text = devices.Count == 0
            ? "No active playback devices were found. Pair and connect both Bluetooth devices in Windows, then refresh."
            : $"Found {devices.Count} active playback device{(devices.Count == 1 ? "" : "s")}.";
    }

    private static string? ExistingId(IReadOnlyList<AudioDeviceInfo> devices, string? id) =>
        id is not null && devices.Any(device => device.Id == id) ? id : null;

    private static string? SelectedId(ComboBox comboBox) => comboBox.SelectedValue as string;

    private void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        try { RefreshDeviceLists(); QueueSave(); }
        catch (Exception ex) { SetStatus("Device scan failed", ex.Message, "#FF6B7A"); }
    }

    private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("Could not open Settings", ex.Message, "#FF6B7A"); }
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
        if (!TryCreateOptions(out var options, out var validationMessage))
        {
            SetStatus("Needs setup", validationMessage, "#FFB84D");
            return;
        }
        try
        {
            StartButton.IsEnabled = false;
            SetStatus("Starting", "Opening both Bluetooth endpoints and the protected system-audio capture path…", "#60D2FF");
            await _router.StartAsync(options!);
            _wantedRunning = true;
            _reconnectTimer.Stop();
            SetStreamingUi(true);
            SetStatus("Streaming", isReconnect ? "Devices reconnected. Streaming resumed." : "Streaming system audio to both outputs.", "#41D99A");
            SaveSettings();
        }
        catch (Exception ex)
        {
            _router.Stop();
            SetStreamingUi(false);
            SetStatus("Could not start", FriendlyAudioError(ex), "#FF6B7A");
            if (_wantedRunning && AutoReconnectCheck.IsChecked == true) _reconnectTimer.Start();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _wantedRunning = false;
        _reconnectTimer.Stop();
        _router.Stop();
        SetStreamingUi(false);
        SetStatus("Stopped", "The audio devices have been released.", "#6F809D");
        BufferText.Text = "—";
    }

    private async void Calibration_Click(object sender, RoutedEventArgs e)
    {
        if (_calibrating) return;
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
        CalibrationButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        SetStatus("Calibrating", "Listen for four short pulses from both devices.", "#56C7FF");
        try
        {
            SaveSettings();
            await _router.PlayCalibrationAsync(options!, _calibrationCancellation.Token);
            SetStatus("Calibration finished", "If one device sounded first, add delay to that device and test again.", "#41D99A");
        }
        catch (OperationCanceledException) { SetStatus("Calibration stopped", "The calibration test was cancelled.", "#6F809D"); }
        catch (Exception ex) { SetStatus("Calibration failed", FriendlyAudioError(ex), "#FF6B7A"); }
        finally
        {
            _calibrationCancellation?.Dispose();
            _calibrationCancellation = null;
            _calibrating = false;
            CalibrationButton.IsEnabled = true;
            StartButton.IsEnabled = true;
        }
    }

    private void ResetCalibration_Click(object sender, RoutedEventArgs e)
    {
        DelayASlider.Value = 0;
        DelayBSlider.Value = 0;
        QueueSave();
    }

    private void Router_Faulted(object? sender, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _router.Stop();
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
        Dispatcher.BeginInvoke(() =>
        {
            if (_router.IsRunning)
            {
                DiagnosticText.Text = "Windows reported a playback-device change. Monitoring both active streams…";
                return;
            }

            try { RefreshDeviceLists(); }
            catch (Exception ex) { SetStatus("Device update failed", ex.Message, "#FF7586"); }
        });
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
        BufferText.Text = $"A {buffers.A:0} ms  ·  B {buffers.B:0} ms  ·  {signal}";
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

    private void SettingChanged(object sender, SelectionChangedEventArgs e) => QueueSave();
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
        _settings.OutputADeviceId = SelectedId(OutputACombo);
        _settings.OutputBDeviceId = SelectedId(OutputBCombo);
        _settings.DelayAMilliseconds = (int)DelayASlider.Value;
        _settings.DelayBMilliseconds = (int)DelayBSlider.Value;
        _settings.VolumeAPercent = (int)VolumeASlider.Value;
        _settings.VolumeBPercent = (int)VolumeBSlider.Value;
        _settings.RenderLatencyMilliseconds = SelectedLatency();
        _settings.AutoReconnect = AutoReconnectCheck.IsChecked == true;
        ReadAppearanceSettings();
        try { _settingsService.Save(_settings); }
        catch (Exception ex) { DiagnosticText.Text = $"Settings could not be saved: {ex.Message}"; }
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
        _wantedRunning = false;
        _calibrationCancellation?.Cancel();
        _diagnosticTimer.Stop();
        _reconnectTimer.Stop();
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
