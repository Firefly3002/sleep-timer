using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SleepTimer.Core;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SleepTimer.Desktop;

public partial class ScheduleEditorWindow : Window
{
    private readonly AppSettings _settings;
    private readonly WeeklyScheduleEntry _entry;
    private bool _updatingDays;
    private bool _updatingAppSelection;

    public WeeklyScheduleEntry? EditedEntry { get; private set; }
    public bool DeleteRequested { get; private set; }

    public ScheduleEditorWindow(AppSettings settings, WeeklyScheduleEntry? entry = null, DayOfWeek? defaultDay = null)
    {
        InitializeComponent();
        _settings = settings;
        _entry = entry?.Clone() ?? new WeeklyScheduleEntry
        {
            IsEnabled = false,
            DaysOfWeek = defaultDay is { } day ? [day] : [.. WeeklyScheduleService.Weekdays],
            StartTime = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(5)),
            ActionRequest = new PowerActionRequest(
                settings.PowerAction,
                settings.CloseAppProcessName,
                settings.CustomProgramPath,
                settings.CustomProgramArguments)
        };

        var editing = entry is not null;
        EditorTitleText.Text = editing ? "Edit schedule" : "Add schedule";
        DeleteButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        EnabledCheck.IsChecked = _entry.IsEnabled;
        StartHourCombo.ItemsSource = Enumerable.Range(1, 12).Select(hour => hour.ToString(CultureInfo.CurrentCulture)).ToList();
        StartMinuteCombo.ItemsSource = Enumerable.Range(0, 60).Select(minute => minute.ToString("00", CultureInfo.InvariantCulture)).ToList();
        var dateTimeFormat = CultureInfo.CurrentCulture.DateTimeFormat;
        StartPeriodCombo.ItemsSource = new[]
        {
            string.IsNullOrWhiteSpace(dateTimeFormat.AMDesignator) ? "AM" : dateTimeFormat.AMDesignator,
            string.IsNullOrWhiteSpace(dateTimeFormat.PMDesignator) ? "PM" : dateTimeFormat.PMDesignator
        };
        var hour12 = _entry.StartTime.Hour % 12;
        StartHourCombo.SelectedItem = (hour12 == 0 ? 12 : hour12).ToString(CultureInfo.CurrentCulture);
        StartMinuteCombo.SelectedItem = _entry.StartTime.Minute.ToString("00", CultureInfo.InvariantCulture);
        StartPeriodCombo.SelectedIndex = _entry.StartTime.Hour >= 12 ? 1 : 0;
        ActionCombo.SelectedIndex = (int)_entry.ActionRequest.Action;
        ProcessNameBox.Text = _entry.ActionRequest.CloseAppProcessName ?? string.Empty;
        ProgramPathBox.Text = _entry.ActionRequest.CustomProgramPath ?? string.Empty;
        ProgramArgumentsBox.Text = _entry.ActionRequest.CustomProgramArguments ?? string.Empty;
        SkipCountdownCheck.IsChecked = _entry.SkipCountdown;
        SetSelectedDays(_entry.DaysOfWeek);
        RefreshOpenApps(_entry.ActionRequest.CloseAppProcessName);
        UpdateActionPanels();
    }

    private void SetSelectedDays(IEnumerable<DayOfWeek> days)
    {
        _updatingDays = true;
        try
        {
            foreach (var (day, checkBox) in GetDayChecks()) checkBox.IsChecked = days.Contains(day);
            EveryDayCheck.IsChecked = GetDayChecks().All(item => item.CheckBox.IsChecked == true);
        }
        finally { _updatingDays = false; }
    }

    private IEnumerable<(DayOfWeek Day, WpfCheckBox CheckBox)> GetDayChecks()
    {
        yield return (DayOfWeek.Monday, MondayCheck);
        yield return (DayOfWeek.Tuesday, TuesdayCheck);
        yield return (DayOfWeek.Wednesday, WednesdayCheck);
        yield return (DayOfWeek.Thursday, ThursdayCheck);
        yield return (DayOfWeek.Friday, FridayCheck);
        yield return (DayOfWeek.Saturday, SaturdayCheck);
        yield return (DayOfWeek.Sunday, SundayCheck);
    }

    private void EveryDayCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingDays || !IsLoaded) return;
        _updatingDays = true;
        try
        {
            foreach (var (_, checkBox) in GetDayChecks()) checkBox.IsChecked = EveryDayCheck.IsChecked == true;
        }
        finally { _updatingDays = false; }
    }

    private void DayCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingDays || !IsLoaded) return;
        _updatingDays = true;
        try { EveryDayCheck.IsChecked = GetDayChecks().All(item => item.CheckBox.IsChecked == true); }
        finally { _updatingDays = false; }
    }

    private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionPanels();

    private void UpdateActionPanels()
    {
        if (CloseAppPanel is null || RunProgramPanel is null || ActionCombo.SelectedIndex < 0) return;
        CloseAppPanel.Visibility = ActionCombo.SelectedIndex == (int)PowerAction.CloseApp ? Visibility.Visible : Visibility.Collapsed;
        RunProgramPanel.Visibility = ActionCombo.SelectedIndex == (int)PowerAction.RunProgram ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshAppsButton_Click(object sender, RoutedEventArgs e) => RefreshOpenApps(ProcessNameBox.Text);

    private void RefreshOpenApps(string? preferredProcessName)
    {
        if (OpenAppCombo is null) return;
        var choices = new List<(string Title, string ProcessName)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                    var title = process.MainWindowTitle.Trim();
                    if (title.Length > 0) choices.Add((title, process.ProcessName));
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException or ArgumentException)
                {
                    // Processes can exit or deny inspection while the list is being collected.
                }
            }
        }

        var sorted = choices.DistinctBy(choice => choice.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(choice => choice.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        _updatingAppSelection = true;
        try
        {
            OpenAppCombo.Items.Clear();
            OpenAppCombo.Items.Add(new ComboBoxItem
            {
                Content = sorted.Count == 0 ? "No open app windows found" : "Choose an open app…",
                IsEnabled = false
            });
            foreach (var (title, processName) in sorted)
                OpenAppCombo.Items.Add(new ComboBoxItem { Content = $"{title}  ·  {processName}.exe", Tag = processName });

            var selected = OpenAppCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                item.Tag is string processName && string.Equals(
                    Path.GetFileNameWithoutExtension(processName),
                    Path.GetFileNameWithoutExtension(preferredProcessName?.Trim()),
                    StringComparison.OrdinalIgnoreCase));
            OpenAppCombo.SelectedItem = selected ?? OpenAppCombo.Items[0];
        }
        finally { _updatingAppSelection = false; }
    }

    private void OpenAppCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAppSelection || OpenAppCombo.SelectedItem is not ComboBoxItem { Tag: string processName }) return;
        ProcessNameBox.Text = processName;
    }

    private void BrowseProgramButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Choose the program to run for this schedule",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) ProgramPathBox.Text = dialog.FileName;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartHourCombo.SelectedItem is not string selectedHour
            || StartMinuteCombo.SelectedItem is not string selectedMinute
            || StartPeriodCombo.SelectedIndex < 0
            || !int.TryParse(selectedHour, NumberStyles.None, CultureInfo.CurrentCulture, out var hour12)
            || !int.TryParse(selectedMinute, NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            || hour12 is < 1 or > 12
            || minute is < 0 or > 59)
        {
            ShowValidation("Choose a valid local start time.");
            StartHourCombo.Focus();
            return;
        }
        var hour = hour12 % 12 + (StartPeriodCombo.SelectedIndex == 1 ? 12 : 0);
        var startTime = new TimeOnly(hour, minute);

        var days = GetDayChecks().Where(item => item.CheckBox.IsChecked == true).Select(item => item.Day).ToList();
        if (days.Count == 0)
        {
            ShowValidation("Choose at least one day for this schedule.");
            return;
        }

        if (ActionCombo.SelectedIndex < 0 || ActionCombo.SelectedIndex > (int)PowerAction.RunProgram)
        {
            ShowValidation("Choose what should happen when this schedule ends.");
            return;
        }

        var action = (PowerAction)ActionCombo.SelectedIndex;
        var processName = Path.GetFileNameWithoutExtension(ProcessNameBox.Text.Trim());
        var programPath = ProgramPathBox.Text.Trim();
        if (action == PowerAction.CloseApp && string.IsNullOrWhiteSpace(processName))
        {
            ShowValidation("Choose an open app or enter its process name.");
            ProcessNameBox.Focus();
            return;
        }
        if (action == PowerAction.RunProgram
            && (!File.Exists(programPath) || !string.Equals(Path.GetExtension(programPath), ".exe", StringComparison.OrdinalIgnoreCase)))
        {
            ShowValidation("Choose an existing .exe program file.");
            ProgramPathBox.Focus();
            return;
        }

        EditedEntry = _entry.Clone();
        EditedEntry.IsEnabled = EnabledCheck.IsChecked == true;
        EditedEntry.DaysOfWeek = days;
        EditedEntry.StartTime = startTime;
        EditedEntry.SkipCountdown = SkipCountdownCheck.IsChecked == true;
        EditedEntry.ActionRequest = new PowerActionRequest(
            action,
            action == PowerAction.CloseApp ? processName : string.Empty,
            action == PowerAction.RunProgram ? programPath : string.Empty,
            action == PowerAction.RunProgram ? ProgramArgumentsBox.Text : string.Empty);
        DialogResult = true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowValidation(string message)
        => WpfMessageBox.Show(this, message, "Check this schedule", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = false;
        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
}
