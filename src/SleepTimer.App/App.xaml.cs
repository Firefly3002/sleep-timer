using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using SleepTimer.Core;

namespace SleepTimer.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _pipeCancellation;
    private TrayController? _tray;
    private PromptWindow? _prompt;
    private DesktopWidgetWindow? _widget;
    private DispatcherTimer? _displayTimer;
    private readonly SettingsStore _settingsStore = new();

    internal AppSettings Settings { get; private set; } = new();
    internal TimerEngine Engine { get; private set; } = null!;
    internal MainWindow MainView { get; private set; } = null!;
    internal bool IsExiting { get; private set; }
    internal bool IsWidgetVisible => _widget?.IsVisible == true;
    private string PipeName => "SleepTimer-" + SanitizePipePart(Environment.UserName);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mutexName = "Local\\SleepTimer_" + SanitizePipePart(Environment.UserName);
        _instanceMutex = new Mutex(initiallyOwned: true, mutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        Settings = _settingsStore.Load();
        Engine = new TimerEngine(new SystemMonotonicClock(), new ThreadPoolOneShotScheduler());
        Engine.SnapshotChanged += snapshot => Dispatcher.BeginInvoke(() => HandleSnapshot(snapshot));
        Engine.PowerActionRequested += action => Dispatcher.BeginInvoke(() => PerformPowerAction(action));

        _pipeCancellation = new CancellationTokenSource();
        _ = ListenForSecondLaunchAsync(_pipeCancellation.Token);

        MainView = new MainWindow();
        MainView.Show();
        _tray = new TrayController(
            ShowMainWindow,
            CancelTimer,
            RestartTimer,
            OpenSettings,
            RequestExitFromTray,
            ToggleWidget);

        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _displayTimer.Tick += (_, _) => RefreshLiveDisplay();
        _displayTimer.Start();

        if (Settings.ShowWidgetOnStartup) ShowWidget();
        if (Settings.StartTimerOnLaunch) StartTimer();
        else MainView.SetNotice("Ready when you are. Pick a quick timer or start your saved timer.");
    }

    internal void StartTimer(int? durationMinutes = null)
    {
        var minutes = durationMinutes ?? Settings.InitialTimerMinutes;
        Engine.Start(
            TimeSpan.FromMinutes(minutes),
            Settings.ShowWarning ? TimeSpan.FromSeconds(Settings.WarningSeconds) : TimeSpan.Zero,
            TimeSpan.FromMinutes(Settings.SnoozeMinutes),
            new PowerActionRequest(
                Settings.PowerAction,
                Settings.CloseAppProcessName,
                Settings.CustomProgramPath,
                Settings.CustomProgramArguments));
        MainView.SetNotice(durationMinutes is null
            ? "Timer started with your saved settings."
            : $"Your {FormatDuration(minutes)} timer has started.");
        ShowMainWindow();
    }

    internal void StartPresetTimer(int durationMinutes)
    {
        if (Engine.GetSnapshot().Phase != TimerPhase.Idle)
        {
            if (!ConfirmAction(
                "A fresh start",
                "Replace the active timer with a new timer?",
                $"Your current timer will be replaced by a {FormatDuration(durationMinutes)} timer.",
                "Replace timer")) return;
        }
        StartTimer(durationMinutes);
    }

    internal void RestartTimer()
    {
        if (Engine.GetSnapshot().Phase != TimerPhase.Idle)
        {
            if (!ConfirmAction(
                "Restart timer",
                "Start a fresh timer from your saved settings?",
                "The current countdown will be replaced with your saved duration and power action.",
                "Restart timer")) return;
        }
        StartTimer();
    }

    internal void CancelTimer()
    {
        if (Engine.GetSnapshot().Phase == TimerPhase.Idle) return;
        _widget?.PrepareForCancellation();
        Engine.Cancel();
        ClosePrompt();
        MainView.SetNotice("Timer canceled. No power action is scheduled.");
        ShowMainWindow();
    }

    internal void SaveSettings(AppSettings settings)
    {
        settings.Normalize();
        try
        {
            _settingsStore.Save(settings);
            Settings = settings.Clone();
            _widget?.ApplySettings(Settings);
            MainView.SetNotice("Settings saved on this PC. Restart an active timer to apply timer changes.");
            MainView.RefreshWidgetState();
        }
        catch (Exception exception)
        {
            MainView.SetError($"Settings could not be saved. {exception.Message}");
        }
    }

    internal void SavePromptPosition(AppSettings settings)
    {
        settings.Normalize();
        try
        {
            _settingsStore.Save(settings);
            Settings = settings;
        }
        catch (Exception exception)
        {
            MainView.SetError($"Window position could not be saved. {exception.Message}");
        }
    }

    internal void SaveWidgetLayout(AppSettings settings)
    {
        settings.Normalize();
        try
        {
            _settingsStore.Save(settings);
            Settings = settings.Clone();
        }
        catch (Exception exception)
        {
            MainView?.SetError($"Widget layout could not be saved. {exception.Message}");
        }
    }

    internal void ToggleWidget()
    {
        if (IsWidgetVisible) HideWidget();
        else ShowWidget();
    }

    internal void ShowWidget()
    {
        if (_widget is null)
        {
            _widget = new DesktopWidgetWindow(this, Settings);
            _widget.Closed += (_, _) =>
            {
                _widget = null;
                MainView?.RefreshWidgetState();
                _tray?.SetWidgetVisible(false);
            };
        }
        _widget.ShowWidgetWindow();
        MainView?.RefreshWidgetState();
        _tray?.SetWidgetVisible(true);
    }

    internal void HideWidget()
    {
        _widget?.HideWidgetWindow();
        MainView?.RefreshWidgetState();
        _tray?.SetWidgetVisible(false);
    }

    internal void NotifyWidgetHidden()
    {
        MainView?.RefreshWidgetState();
        _tray?.SetWidgetVisible(false);
    }

    internal void PreviewWidgetOpacity(double opacity) => _widget?.SetOpacity(opacity);

    internal void OpenSettings()
    {
        ShowMainWindow();
        MainView.OpenSettings();
    }

    internal void ShowMainWindow()
    {
        if (MainView is null) return;
        if (!MainView.IsVisible) MainView.Show();
        if (MainView.WindowState == WindowState.Minimized) MainView.WindowState = WindowState.Normal;
        MainView.Activate();
    }

    internal void RequestExitFromTray()
    {
        if (Engine.GetSnapshot().Phase != TimerPhase.Idle)
        {
            if (!ConfirmAction(
                "Stop timer and exit?",
                "Your timer only runs while Sleep Timer is open.",
                "Exiting now will cancel the active timer and its upcoming power action.",
                "Exit and stop")) return;
            Engine.Cancel();
        }

        ClosePrompt();
        _widget?.CloseFromApp();
        _widget = null;
        IsExiting = true;
        _tray?.Dispose();
        _tray = null;
        MainView.AllowAppClose();
        MainView.Close();
        Shutdown();
    }

    private void HandleSnapshot(TimerSnapshot snapshot)
    {
        if (MainView is null) return;
        MainView.Refresh(snapshot);
        _tray?.Update(snapshot);
        _widget?.Refresh(snapshot);

        if (snapshot.Phase == TimerPhase.Warning)
        {
            if (_prompt is null)
            {
                _prompt = new PromptWindow(this, Settings, snapshot);
                _prompt.Closed += (_, _) => _prompt = null;
                _prompt.Show();
            }
            _prompt.Refresh(Engine.GetSnapshot());
        }
        else
        {
            ClosePrompt();
        }
    }

    private void RefreshLiveDisplay()
    {
        if (Engine is null) return;
        var snapshot = Engine.GetSnapshot();
        MainView?.Refresh(snapshot);
        _tray?.Update(snapshot);
        _prompt?.Refresh(snapshot);
        _widget?.Refresh(snapshot);
    }

    private async void PerformPowerAction(PowerActionRequest action)
    {
        ClosePrompt();
        _widget?.CompleteProgress();
        try
        {
            if (action.Action == PowerAction.CloseApp)
            {
                var processName = Path.GetFileNameWithoutExtension(action.CloseAppProcessName?.Trim());
                var progress = $"Asking {processName} to close gracefully. Waiting up to 30 seconds…";
                MainView.SetNotice(progress);
                if (!MainView.IsVisible) _tray?.ShowNotice(progress);
            }

            var result = await PowerActions.ExecuteAsync(action);
            MainView.SetNotice(result);
            if (action.Action == PowerAction.CloseApp && !MainView.IsVisible) _tray?.ShowNotice(result);
        }
        catch (Exception exception)
        {
            MainView.SetPowerError(exception.Message);
            ShowMainWindow();
        }
    }

    private void ClosePrompt()
    {
        if (_prompt is null) return;
        _prompt.CloseFromApp();
        _prompt = null;
    }

    private bool ConfirmAction(string title, string heading, string message, string confirmText)
    {
        var dialog = new TimerConfirmationWindow(title, heading, message, confirmText);
        if (MainView?.IsVisible == true)
        {
            dialog.Owner = MainView;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        return dialog.ShowDialog() == true;
    }

    private void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine("START_OR_SHOW");
            writer.Flush();
        }
        catch
        {
            // The first instance may still be starting. Its timer remains active even if it cannot be focused.
        }
    }

    private async Task ListenForSecondLaunchAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (message == "START_OR_SHOW")
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (Engine.GetSnapshot().Phase == TimerPhase.Idle && Settings.StartTimerOnLaunch) StartTimer();
                        else ShowMainWindow();
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (IOException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }

    private static string SanitizePipePart(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? WindowsIdentity.GetCurrent().User?.Value?.Replace('-', '_') ?? "Default" : new string(chars);
    }

    private static string FormatDuration(int minutes)
    {
        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return $"{hours}-hour";
        }
        return $"{minutes}-minute";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _displayTimer?.Stop();
        _displayTimer = null;
        _pipeCancellation?.Cancel();
        _pipeCancellation?.Dispose();
        _widget?.CloseFromApp();
        _widget = null;
        _tray?.Dispose();
        Engine?.Dispose();
        if (_ownsMutex && _instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _ownsMutex = false;
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

