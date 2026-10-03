using System.Windows;

namespace DualBluetooth;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\DualCast.SingleInstance";
    private const string ActivateEventName = @"Local\DualCast.Activate";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.GetBaseException().Message, "DualCast error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Two copies would fight over the same playback devices, so hand off to the running one instead.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        if (!isFirstInstance)
        {
            _activateEvent.Set();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        _activateWait = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => Dispatcher.BeginInvoke(ActivateMainWindow),
            null, Timeout.Infinite, executeOnlyOnce: false);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activateEvent?.Dispose();
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        // Topmost toggle gets the window in front when Windows blocks plain focus stealing.
        var wasTopmost = window.Topmost;
        window.Topmost = true;
        window.Topmost = wasTopmost;
        window.Focus();
    }
}
