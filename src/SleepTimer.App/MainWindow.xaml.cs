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
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace SleepTimer.Desktop;

public partial class MainWindow : Window
{
    private readonly App _app = (App)System.Windows.Application.Current;
    private bool _allowClose;
    private bool _updatingCloseAppSelection;
    private bool _expandedForManualEntry;
    private bool _updatingAudioControls;
    private string _sleepMusicFilePath = string.Empty;
    private string _endSoundFilePath = string.Empty;

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
        UpdateQuickTimerAppearance(active);
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
            HeroTitleText.Text = "Your timer is almost up.";
            DetailLabel.Text = "Snooze to add more time, or cancel the timer to stop the selected action.";
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

    private void UpdateQuickTimerAppearance(bool timerActive)
    {
        QuickTimersHint.Text = timerActive ? "adds time to this timer" : "starts a fresh timer";
        QuickTimersHint.Foreground = (WpfBrush)FindResource(timerActive ? "Mint" : "BrightText");
        QuickTimersHint.FontWeight = timerActive ? FontWeights.Bold : FontWeights.SemiBold;

        QuickTimerButtonsPanel.Tag = timerActive;
    }

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
        StartAppOnWindowsStartupCheck.IsChecked = settings.StartAppOnWindowsStartup;
        ShowWidgetOnStartupCheck.IsChecked = settings.ShowWidgetOnStartup;
        WidgetTopmostCheck.IsChecked = settings.WidgetAlwaysOnTop;
        WidgetOpacitySlider.Value = Math.Round(settings.WidgetOpacity * 100 / 5) * 5;
        _updatingAudioControls = true;
        try
        {
            SleepMusicEnabledCheck.IsChecked = settings.SleepMusicEnabled;
            EndSoundEnabledCheck.IsChecked = settings.EndSoundEnabled;
            _sleepMusicFilePath = settings.SleepMusicFilePath;
            _endSoundFilePath = settings.EndSoundFilePath;
            SelectAudioItem(SleepMusicTrackCombo, settings.SleepMusicSelectionId);
            SelectAudioItem(EndSoundTrackCombo, settings.EndSoundSelectionId);
            SleepMusicVolumeSlider.Value = settings.SleepMusicVolume;
            EndSoundVolumeSlider.Value = settings.EndSoundVolume;
            UpdateAudioFileLabels();
        }
        finally { _updatingAudioControls = false; }
        UpdatePowerActionOptions();
        UpdateWarningControls();
        RefreshAudioPreviewState();
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
        _updatingCloseAppSelection = true;
        try
        {
            CloseAppProcessCombo.Items.Clear();
            var placeholder = new ComboBoxItem
            {
                Content = sortedChoices.Count == 0 ? "No open app windows found" : "Choose an open app…",
                IsEnabled = false
            };
            CloseAppProcessCombo.Items.Add(placeholder);

            foreach (var choice in sortedChoices)
            {
                CloseAppProcessCombo.Items.Add(new ComboBoxItem
                {
                    Content = choice.DisplayName,
                    Tag = choice.ProcessName
                });
            }

            var selectedItem = CloseAppProcessCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is string processName
                    && string.Equals(NormalizeProcessName(processName), NormalizeProcessName(processNameToKeep), StringComparison.OrdinalIgnoreCase));
            CloseAppProcessCombo.SelectedItem = selectedItem ?? placeholder;
            CloseAppProcessNameBox.Text = selectedItem?.Tag as string ?? processNameToKeep;
            ManualProcessNameToggle.IsChecked = selectedItem is null && !string.IsNullOrWhiteSpace(processNameToKeep);
            OpenAppsHint.Text = sortedChoices.Count == 0
                ? "No selectable app windows were found. Refresh the list, or enter the app’s process name below."
                : $"{sortedChoices.Count} open app{(sortedChoices.Count == 1 ? "" : "s")} found. Choose one, or enter a process name below.";
        }
        finally
        {
            _updatingCloseAppSelection = false;
        }
        UpdateManualProcessNameVisibility();
    }

    private void CloseAppProcessCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCloseAppSelection || CloseAppProcessCombo.SelectedItem is not ComboBoxItem selected || selected.Tag is not string) return;
        _updatingCloseAppSelection = true;
        try
        {
            CloseAppProcessNameBox.Text = (string)selected.Tag;
            ManualProcessNameToggle.IsChecked = false;
        }
        finally { _updatingCloseAppSelection = false; }
        UpdateManualProcessNameVisibility();
    }

    private void ManualProcessNameToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateManualProcessNameVisibility();
        if (_updatingCloseAppSelection) return;
        if (ManualProcessNameToggle.IsChecked == true) ExpandWindowForManualEntry();
        _updatingCloseAppSelection = true;
        try
        {
            if (ManualProcessNameToggle.IsChecked == true)
            {
                CloseAppProcessCombo.SelectedIndex = 0;
                CloseAppProcessNameBox.Clear();
            }
            else if (CloseAppProcessCombo.SelectedItem is ComboBoxItem { Tag: string processName })
                CloseAppProcessNameBox.Text = processName;
            else
                CloseAppProcessNameBox.Clear();
        }
        finally { _updatingCloseAppSelection = false; }
        if (ManualProcessNameToggle.IsChecked == true) CloseAppProcessNameBox.Focus();
    }

    private void ExpandWindowForManualEntry()
    {
        if (_expandedForManualEntry || WindowState != WindowState.Normal) return;
        var workArea = SystemParameters.WorkArea;
        var oldHeight = Height;
        var newHeight = Math.Min(workArea.Height, oldHeight + 56);
        if (newHeight <= oldHeight + 1) return;

        var centerY = Top + oldHeight / 2;
        Height = newHeight;
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - newHeight);
        Top = Math.Clamp(centerY - newHeight / 2, workArea.Top, maxTop);
        _expandedForManualEntry = true;
    }

    private void UpdateManualProcessNameVisibility()
    {
        if (CloseAppProcessNameBox is null || ManualProcessNameToggle is null) return;
        CloseAppProcessNameBox.Visibility = ManualProcessNameToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetCloseAppProcessName() => CloseAppProcessNameBox.Text.Trim();

    private static string NormalizeProcessName(string? value)
        => Path.GetFileNameWithoutExtension(value?.Trim()) ?? string.Empty;

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

    private void PreviewWarningButton_Click(object sender, RoutedEventArgs e)
    {
        var action = PowerActionCombo.SelectedIndex is >= 0 and <= (int)PowerAction.RunProgram
            ? (PowerAction)PowerActionCombo.SelectedIndex
            : _app.Settings.PowerAction;
        var warningSeconds = int.TryParse(WarningSecondsBox.Text, out var parsedWarningSeconds)
            ? Math.Clamp(parsedWarningSeconds, 5, 600)
            : _app.Settings.WarningSeconds;
        var snoozeMinutes = int.TryParse(SnoozeMinutesBox.Text, out var parsedSnoozeMinutes)
            ? Math.Clamp(parsedSnoozeMinutes, 1, 180)
            : _app.Settings.SnoozeMinutes;
        var settings = _app.Settings.Clone();
        settings.PromptMode = PromptModeCombo.SelectedIndex == 1 ? PromptMode.Floating : PromptMode.FullScreen;
        settings.PromptScale = PromptScaleSlider.Value / 100.0;
        settings.ShowCountdown = ShowCountdownCheck.IsChecked == true;

        var actionRequest = new PowerActionRequest(
            action,
            GetCloseAppProcessName(),
            CustomProgramPathBox.Text.Trim(),
            CustomProgramArgumentsBox.Text);
        var duration = TimeSpan.FromSeconds(warningSeconds);
        var snapshot = new TimerSnapshot(TimerPhase.Warning, duration, action, duration)
        {
            ActionRequest = actionRequest,
            SnoozeDuration = TimeSpan.FromMinutes(snoozeMinutes),
            WarningEnabled = true
        };
        var preview = new PromptWindow(_app, settings, snapshot, isPreview: true)
        {
            Owner = this
        };
        preview.ShowDialog();
    }

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

    public void RefreshAudioPreviewState()
    {
        if (SleepMusicPreviewButton is not null)
            SleepMusicPreviewButton.Content = _app.IsMusicPreviewing ? "■  Stop preview" : "▶  Preview music";
        if (EndSoundPreviewButton is not null)
            EndSoundPreviewButton.Content = _app.IsEndSoundPreviewing ? "■  Stop preview" : "▶  Preview cue";
    }

    private void SleepMusicTrackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAudioControls) return;
        _app.StopAudioPreview();
        UpdateAudioFileLabels();
    }

    private void EndSoundTrackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAudioControls) return;
        _app.StopAudioPreview();
        UpdateAudioFileLabels();
    }

    private void BrowseSleepMusicButton_Click(object sender, RoutedEventArgs e) => BrowseAudioFile(isMusic: true);

    private void BrowseEndSoundButton_Click(object sender, RoutedEventArgs e) => BrowseAudioFile(isMusic: false);

    private void BrowseAudioFile(bool isMusic)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = isMusic ? "Choose sleep music" : "Choose a timer-end sound",
            Filter = "Supported audio (*.mp3;*.wav;*.wma;*.m4a)|*.mp3;*.wav;*.wma;*.m4a",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        if (!IsSupportedAudioFile(dialog.FileName))
        {
            SetError("Choose an MP3, WAV, WMA, or M4A audio file.");
            return;
        }

        _app.StopAudioPreview();
        _updatingAudioControls = true;
        try
        {
            if (isMusic)
            {
                _sleepMusicFilePath = dialog.FileName;
                SelectAudioItem(SleepMusicTrackCombo, AudioSelectionIds.CustomFile);
            }
            else
            {
                _endSoundFilePath = dialog.FileName;
                SelectAudioItem(EndSoundTrackCombo, AudioSelectionIds.CustomFile);
            }
            UpdateAudioFileLabels();
        }
        finally { _updatingAudioControls = false; }
    }

    private static bool IsSupportedAudioFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".wav" or ".wma" or ".m4a";

    private static void SelectAudioItem(WpfComboBox combo, string selectionId)
    {
        var selected = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selectionId, StringComparison.OrdinalIgnoreCase));
        combo.SelectedItem = selected ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private static string GetAudioSelectionId(WpfComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

    private void UpdateAudioFileLabels()
    {
        if (SleepMusicFileLabel is null || EndSoundFileLabel is null) return;
        UpdateAudioFileLabel(SleepMusicFileLabel, SleepMusicTrackCombo, _sleepMusicFilePath, "Built-in track");
        UpdateAudioFileLabel(EndSoundFileLabel, EndSoundTrackCombo, _endSoundFilePath, "Built-in cue");
    }

    private static void UpdateAudioFileLabel(TextBlock label, WpfComboBox combo, string path, string builtInText)
    {
        var isCustom = string.Equals(GetAudioSelectionId(combo), AudioSelectionIds.CustomFile, StringComparison.OrdinalIgnoreCase);
        var text = !isCustom ? builtInText
            : string.IsNullOrWhiteSpace(path) ? "Choose an audio file"
            : Path.GetFileName(path);
        label.Text = text;
        label.ToolTip = isCustom && !string.IsNullOrWhiteSpace(path) ? path : text;
    }

    private void AudioVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ReferenceEquals(sender, SleepMusicVolumeSlider) && SleepMusicVolumeLabel is not null)
            SleepMusicVolumeLabel.Text = $"{(int)e.NewValue}%";
        if (ReferenceEquals(sender, EndSoundVolumeSlider) && EndSoundVolumeLabel is not null)
            EndSoundVolumeLabel.Text = $"{(int)e.NewValue}%";
        if (_updatingAudioControls) return;
        if (ReferenceEquals(sender, SleepMusicVolumeSlider) && _app.IsMusicPreviewing)
            _app.SetAudioPreviewVolume((int)e.NewValue);
        if (ReferenceEquals(sender, EndSoundVolumeSlider) && _app.IsEndSoundPreviewing)
            _app.SetAudioPreviewVolume((int)e.NewValue);
    }

    private void SleepMusicPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_app.IsMusicPreviewing)
        {
            _app.StopAudioPreview();
            RefreshAudioPreviewState();
            return;
        }
        var selectionId = GetAudioSelectionId(SleepMusicTrackCombo);
        if (selectionId == AudioSelectionIds.CustomFile && string.IsNullOrWhiteSpace(_sleepMusicFilePath))
        {
            SetError("Browse for a music file before previewing your audio.");
            SettingsTabs.SelectedIndex = 3;
            return;
        }
        _app.PreviewMusic(selectionId, _sleepMusicFilePath, (int)SleepMusicVolumeSlider.Value);
        RefreshAudioPreviewState();
    }

    private void EndSoundPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_app.IsEndSoundPreviewing)
        {
            _app.StopAudioPreview();
            RefreshAudioPreviewState();
            return;
        }
        var selectionId = GetAudioSelectionId(EndSoundTrackCombo);
        if (selectionId == AudioSelectionIds.CustomFile && string.IsNullOrWhiteSpace(_endSoundFilePath))
        {
            SetError("Browse for an end-sound file before previewing your audio.");
            SettingsTabs.SelectedIndex = 3;
            return;
        }
        _app.PreviewEndSound(selectionId, _endSoundFilePath, (int)EndSoundVolumeSlider.Value);
        RefreshAudioPreviewState();
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
            SetError("Choose an open app or enter its process name manually.");
            SettingsTabs.SelectedIndex = 0;
            if (CloseAppProcessCombo.SelectedItem is not ComboBoxItem { Tag: string })
            {
                ManualProcessNameToggle.IsChecked = true;
                CloseAppProcessNameBox.Focus();
            }
            else CloseAppProcessCombo.Focus();
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

        var sleepMusicSelectionId = GetAudioSelectionId(SleepMusicTrackCombo);
        if (SleepMusicEnabledCheck.IsChecked == true
            && sleepMusicSelectionId == AudioSelectionIds.CustomFile && string.IsNullOrWhiteSpace(_sleepMusicFilePath))
        {
            SetError("Browse for a music file or choose one of the built-in tracks.");
            SettingsTabs.SelectedIndex = 3;
            BrowseSleepMusicButton.Focus();
            return;
        }
        var endSoundSelectionId = GetAudioSelectionId(EndSoundTrackCombo);
        if (EndSoundEnabledCheck.IsChecked == true
            && endSoundSelectionId == AudioSelectionIds.CustomFile && string.IsNullOrWhiteSpace(_endSoundFilePath))
        {
            SetError("Browse for an end-sound file or choose one of the built-in cues.");
            SettingsTabs.SelectedIndex = 3;
            BrowseEndSoundButton.Focus();
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
        updated.StartAppOnWindowsStartup = StartAppOnWindowsStartupCheck.IsChecked == true;
        updated.ShowWidgetOnStartup = ShowWidgetOnStartupCheck.IsChecked == true;
        updated.WidgetAlwaysOnTop = WidgetTopmostCheck.IsChecked == true;
        updated.WidgetOpacity = WidgetOpacitySlider.Value / 100.0;
        updated.SleepMusicEnabled = SleepMusicEnabledCheck.IsChecked == true;
        updated.SleepMusicSelectionId = sleepMusicSelectionId;
        updated.SleepMusicFilePath = _sleepMusicFilePath;
        updated.SleepMusicVolume = (int)Math.Round(SleepMusicVolumeSlider.Value);
        updated.EndSoundEnabled = EndSoundEnabledCheck.IsChecked == true;
        updated.EndSoundSelectionId = endSoundSelectionId;
        updated.EndSoundFilePath = _endSoundFilePath;
        updated.EndSoundVolume = (int)Math.Round(EndSoundVolumeSlider.Value);
        _app.SaveSettings(updated);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _app.IsExiting) return;
        _app.StopAudioPreview();
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
