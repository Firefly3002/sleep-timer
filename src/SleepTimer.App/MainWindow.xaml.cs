using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SleepTimer.Core;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace SleepTimer.Desktop;

public partial class MainWindow : Window
{
    private readonly App _app = (App)System.Windows.Application.Current;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings(_app.Settings);
        Refresh(_app.Engine.GetSnapshot());
        RefreshWidgetState();
    }

    public void Refresh(TimerSnapshot snapshot)
    {
        var active = snapshot.Phase != TimerPhase.Idle;
        var action = active ? snapshot.Action : _app.Settings.PowerAction;
        var actionTitle = ActionPresentation.Title(action);
        PrimaryTimerButton.Content = active ? "Restart timer" : "Start saved timer";
        CancelButton.IsEnabled = active;

        if (!active)
        {
            StatusLabel.Text = "READY WHEN YOU ARE";
            StatusDot.Fill = (WpfBrush)FindResource("Mint");
            TimeCaption.Text = "SAVED TIMER";
            TimeLabel.Text = FormatRemaining(TimeSpan.FromMinutes(_app.Settings.InitialTimerMinutes));
            TimeDescriptor.Text = "your next timer";
            HeroTitleText.Text = "Settle in. We’ll handle the rest.";
            DetailLabel.Text = "Choose a quick timer or start your saved one. You can cancel whenever you like.";
            ActionLabel.Text = $"SAVED · {actionTitle}";
            UpdateTimerRing(0, (WpfBrush)FindResource("AccentViolet"));
            return;
        }

        if (snapshot.Phase == TimerPhase.Warning)
        {
            StatusLabel.Text = "GENTLE CHECK-IN";
            StatusDot.Fill = (WpfBrush)FindResource("MoonGold");
            TimeCaption.Text = "ACTION BEGINS IN";
            TimeLabel.Text = FormatRemaining(snapshot.Remaining);
            TimeDescriptor.Text = ActionPresentation.TimerDescription(action);
            HeroTitleText.Text = "Still watching?";
            DetailLabel.Text = "Snooze for a little more time, or cancel to keep your computer on.";
            ActionLabel.Text = $"{actionTitle} · FINAL CHECK-IN";
            var warningProgress = snapshot.PhaseDuration <= TimeSpan.Zero
                ? 0
                : Math.Clamp(1 - snapshot.Remaining.TotalMilliseconds / snapshot.PhaseDuration.TotalMilliseconds, 0, 1);
            UpdateTimerRing(warningProgress, (WpfBrush)FindResource("MoonGold"));
            return;
        }

        StatusLabel.Text = "TIMER ACTIVE";
        StatusDot.Fill = (WpfBrush)FindResource("Mint");
        TimeCaption.Text = "TIME REMAINING";
        TimeLabel.Text = FormatRemaining(snapshot.Remaining);
        TimeDescriptor.Text = ActionPresentation.TimerDescription(action);
        HeroTitleText.Text = "Your night is in good hands.";
        DetailLabel.Text = snapshot.WarningEnabled
            ? "We’ll give you a gentle warning before anything happens."
            : "No warning this run. The selected action will start as soon as the timer ends.";
        ActionLabel.Text = snapshot.WarningEnabled
            ? $"{actionTitle} · WARNING IN {FormatShortDuration(snapshot.Remaining)}"
            : $"{actionTitle} · NO WARNING";
        var progress = snapshot.PhaseDuration <= TimeSpan.Zero
            ? 0
            : Math.Clamp(1 - snapshot.Remaining.TotalMilliseconds / snapshot.PhaseDuration.TotalMilliseconds, 0, 1);
        UpdateTimerRing(progress, (WpfBrush)FindResource("AccentViolet"));
    }

    public void RefreshWidgetState() => WidgetToggleButton.Content = _app.IsWidgetVisible ? "◉  Hide widget" : "◉  Widget";

    private void UpdateTimerRing(double progress, WpfBrush color)
    {
        TimerArc.Stroke = color;
        TimerArc.Data = CreateRingGeometry(progress);
    }

    private static Geometry CreateRingGeometry(double progress)
    {
        const double bounds = 194;
        const double stroke = 11;
        var center = bounds / 2;
        var radius = center - stroke / 2;
        progress = Math.Clamp(progress, 0, 1);
        if (progress <= 0) return Geometry.Empty;

        var start = new WpfPoint(center, center - radius);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        if (progress >= 1)
        {
            figure.Segments.Add(new ArcSegment(new WpfPoint(center, center + radius), new WpfSize(radius, radius), 0,
                false, SweepDirection.Clockwise, true));
            figure.Segments.Add(new ArcSegment(start, new WpfSize(radius, radius), 0,
                false, SweepDirection.Clockwise, true));
        }
        else
        {
            var angle = (-90 + progress * 360) * Math.PI / 180;
            var end = new WpfPoint(center + radius * Math.Cos(angle), center + radius * Math.Sin(angle));
            figure.Segments.Add(new ArcSegment(end, new WpfSize(radius, radius), 0,
                progress > 0.5, SweepDirection.Clockwise, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    public void SetNotice(string message)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        NoticeText.Text = message;
        NoticeText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetPowerError(string message) => SetError($"Power action could not be completed. {message}");

    public void SetError(string message)
    {
        NoticeText.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    public void OpenSettings()
    {
        DashboardPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        FadeIn(SettingsPanel);
        SettingsToggleButton.Content = "⌂  Home";
        LoadSettings(_app.Settings);
        Activate();
    }

    public void AllowAppClose() => _allowClose = true;

    private void LoadSettings(AppSettings settings)
    {
        TimerHoursBox.Text = (settings.InitialTimerMinutes / 60).ToString();
        TimerMinutesPartBox.Text = (settings.InitialTimerMinutes % 60).ToString();
        WarningSecondsBox.Text = settings.WarningSeconds.ToString();
        SnoozeMinutesBox.Text = settings.SnoozeMinutes.ToString();
        PowerActionCombo.SelectedIndex = (int)settings.PowerAction;
        RefreshRunningApps(settings.CloseAppProcessName);
        CustomProgramPathBox.Text = settings.CustomProgramPath;
        CustomProgramArgumentsBox.Text = settings.CustomProgramArguments;
        EnableWarningCheck.IsChecked = settings.ShowWarning;
        PromptModeCombo.SelectedIndex = settings.PromptMode == PromptMode.FullScreen ? 0 : 1;
        PromptScaleSlider.Value = Math.Round(settings.PromptScale * 100 / 10) * 10;
        ShowCountdownCheck.IsChecked = settings.ShowCountdown;
        StartOnLaunchCheck.IsChecked = settings.StartTimerOnLaunch;
        ShowWidgetOnStartupCheck.IsChecked = settings.ShowWidgetOnStartup;
        WidgetTopmostCheck.IsChecked = settings.WidgetAlwaysOnTop;
        WidgetOpacitySlider.Value = Math.Round(settings.WidgetOpacity * 100 / 5) * 5;
        UpdatePowerActionOptions();
        UpdateWarningControls();
    }

    private void PrimaryTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Engine.GetSnapshot().Phase == TimerPhase.Idle) _app.StartTimer();
        else _app.RestartTimer();
    }

    private void QuickPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button && int.TryParse(button.Tag?.ToString(), out var minutes))
            _app.StartPresetTimer(minutes);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _app.CancelTimer();

    private void WidgetToggleButton_Click(object sender, RoutedEventArgs e) => _app.ToggleWidget();

    private void PowerActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdatePowerActionOptions();

    private void UpdatePowerActionOptions()
    {
        if (CloseAppSettingsPanel is null || RunProgramSettingsPanel is null) return;
        var action = (PowerAction)PowerActionCombo.SelectedIndex;
        var shouldShowCloseApp = action == PowerAction.CloseApp;
        var wasShowingCloseApp = CloseAppSettingsPanel.Visibility == Visibility.Visible;
        CloseAppSettingsPanel.Visibility = action == PowerAction.CloseApp ? Visibility.Visible : Visibility.Collapsed;
        RunProgramSettingsPanel.Visibility = action == PowerAction.RunProgram ? Visibility.Visible : Visibility.Collapsed;
        if (shouldShowCloseApp && !wasShowingCloseApp) RefreshRunningApps();
    }

    private void RefreshRunningAppsButton_Click(object sender, RoutedEventArgs e) => RefreshRunningApps();

    private void RefreshRunningApps(string? preferredProcessName = null)
    {
        if (CloseAppProcessCombo is null) return;
        var processNameToKeep = preferredProcessName ?? GetCloseAppProcessName();
        var choices = new List<RunningAppChoice>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                    var title = process.MainWindowTitle.Trim();
                    if (title.Length == 0) continue;
                    choices.Add(new RunningAppChoice(title, process.ProcessName));
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException or ArgumentException)
                {
                    // Processes can exit or deny inspection while the list is being collected.
                }
            }
        }

        var sortedChoices = choices
            .DistinctBy(choice => choice.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(choice => choice.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        CloseAppProcessCombo.ItemsSource = sortedChoices;

        var selected = sortedChoices.FirstOrDefault(choice =>
            string.Equals(choice.ProcessName, processNameToKeep, StringComparison.OrdinalIgnoreCase)
            || string.Equals(choice.ProcessName + ".exe", processNameToKeep, StringComparison.OrdinalIgnoreCase));
        CloseAppProcessCombo.SelectedItem = selected;
        CloseAppProcessCombo.Text = selected?.DisplayName ?? processNameToKeep;
    }

    private string GetCloseAppProcessName()
    {
        if (CloseAppProcessCombo.SelectedItem is RunningAppChoice selected
            && string.Equals(CloseAppProcessCombo.Text, selected.DisplayName, StringComparison.OrdinalIgnoreCase))
            return selected.ProcessName;
        return CloseAppProcessCombo.Text.Trim();
    }

    private sealed record RunningAppChoice(string Title, string ProcessName)
    {
        public string DisplayName => $"{Title}  ·  {ProcessName}.exe";
    }

    private void BrowseProgramButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Choose the program to run when the timer ends",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) CustomProgramPathBox.Text = dialog.FileName;
    }

    private void WarningEnabledCheck_Changed(object sender, RoutedEventArgs e) => UpdateWarningControls();

    private void UpdateWarningControls()
    {
        if (WarningSecondsBox is null || EnableWarningCheck is null) return;
        var enabled = EnableWarningCheck.IsChecked == true;
        WarningSecondsBox.IsEnabled = enabled;
        SnoozeMinutesBox.IsEnabled = enabled;
        ShowCountdownCheck.IsEnabled = enabled;
        PromptModeCombo.IsEnabled = enabled;
        PromptScaleSlider.IsEnabled = enabled;
    }

    private void SettingsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            DashboardPanel.Visibility = Visibility.Visible;
            FadeIn(DashboardPanel);
            SettingsToggleButton.Content = "⚙  Settings";
            return;
        }
        OpenSettings();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleWindowSize();

    private void HideButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleWindowSize();
            e.Handled = true;
            return;
        }
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void ToggleWindowSize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static void FadeIn(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void PromptScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleLabel is not null) ScaleLabel.Text = $"{(int)e.NewValue}%";
    }

    private void WidgetOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WidgetOpacityLabel is not null) WidgetOpacityLabel.Text = $"{(int)e.NewValue}%";
        _app.PreviewWidgetOpacity(e.NewValue / 100.0);
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TimerHoursBox.Text, out var timerHours) || timerHours is < 0 or > 24
            || !int.TryParse(TimerMinutesPartBox.Text, out var timerMinutesPart) || timerMinutesPart is < 0 or > 59
            || timerHours * 60 + timerMinutesPart is < 1 or > 1440)
        {
            SetError("Timer length must be from 1 minute to 24 hours. Enter hours and minutes separately.");
            SettingsTabs.SelectedIndex = 0;
            TimerHoursBox.Focus();
            return;
        }
        var showWarning = EnableWarningCheck.IsChecked == true;
        var warningSeconds = _app.Settings.WarningSeconds;
        if (showWarning && (!int.TryParse(WarningSecondsBox.Text, out warningSeconds) || warningSeconds is < 5 or > 600))
        {
            SetError("Warning duration must be from 5 to 600 seconds.");
            SettingsTabs.SelectedIndex = 1;
            WarningSecondsBox.Focus();
            return;
        }
        var snoozeMinutes = _app.Settings.SnoozeMinutes;
        if (showWarning && (!int.TryParse(SnoozeMinutesBox.Text, out snoozeMinutes) || snoozeMinutes is < 1 or > 180))
        {
            SetError("Snooze interval must be from 1 to 180 minutes.");
            SettingsTabs.SelectedIndex = 1;
            SnoozeMinutesBox.Focus();
            return;
        }

        if (PowerActionCombo.SelectedIndex is < 0 or > (int)PowerAction.RunProgram)
        {
            SetError("Choose what should happen when the timer ends.");
            SettingsTabs.SelectedIndex = 0;
            PowerActionCombo.Focus();
            return;
        }
        var selectedAction = (PowerAction)PowerActionCombo.SelectedIndex;
        var closeAppProcessName = GetCloseAppProcessName();
        var customProgramPath = CustomProgramPathBox.Text.Trim();
        if (selectedAction == PowerAction.CloseApp && string.IsNullOrWhiteSpace(closeAppProcessName))
        {
            SetError("Enter the app's process name, such as chrome or notepad.");
            SettingsTabs.SelectedIndex = 0;
            CloseAppProcessCombo.Focus();
            return;
        }
        if (selectedAction == PowerAction.RunProgram
            && (!File.Exists(customProgramPath) || !string.Equals(Path.GetExtension(customProgramPath), ".exe", StringComparison.OrdinalIgnoreCase)))
        {
            SetError("Choose an existing .exe program file. You can enter command-line arguments below it.");
            SettingsTabs.SelectedIndex = 0;
            CustomProgramPathBox.Focus();
            return;
        }

        var updated = _app.Settings.Clone();
        updated.InitialTimerMinutes = timerHours * 60 + timerMinutesPart;
        updated.ShowWarning = showWarning;
        updated.WarningSeconds = warningSeconds;
        updated.SnoozeMinutes = snoozeMinutes;
        updated.PowerAction = selectedAction;
        updated.CloseAppProcessName = closeAppProcessName;
        updated.CustomProgramPath = customProgramPath;
        updated.CustomProgramArguments = CustomProgramArgumentsBox.Text;
        updated.PromptMode = PromptModeCombo.SelectedIndex == 1 ? PromptMode.Floating : PromptMode.FullScreen;
        updated.PromptScale = PromptScaleSlider.Value / 100.0;
        updated.ShowCountdown = ShowCountdownCheck.IsChecked == true;
        updated.StartTimerOnLaunch = StartOnLaunchCheck.IsChecked == true;
        updated.ShowWidgetOnStartup = ShowWidgetOnStartupCheck.IsChecked == true;
        updated.WidgetAlwaysOnTop = WidgetTopmostCheck.IsChecked == true;
        updated.WidgetOpacity = WidgetOpacitySlider.Value / 100.0;
        _app.SaveSettings(updated);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _app.IsExiting) return;
        e.Cancel = true;
        Hide();
    }

    private static string FormatRemaining(TimeSpan value)
    {
        var hours = (int)value.TotalHours;
        return hours > 0 ? $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    private static string FormatShortDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}h {value.Minutes:00}m";
        return $"{Math.Max(1, (int)Math.Ceiling(value.TotalMinutes))} min";
    }
}
