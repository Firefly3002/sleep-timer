using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WpfBrush = System.Windows.Media.Brush;
using WpfRectangleGeometry = System.Windows.Media.RectangleGeometry;
using SleepTimer.Core;

namespace SleepTimer.Desktop;

public partial class DesktopWidgetWindow : Window
{
    private readonly App _app;
    private bool _allowClose;
    private double _progressFraction;
    private TimerPhase _lastPhase = TimerPhase.Idle;
    private TimeSpan _runningDuration;
    private TimeSpan _warningDuration;
    private bool _finishedAfterAction;
    private bool _cancellationPending;
    private bool _pendingClickOrDrag;
    private System.Windows.Point _mouseDownPosition;

    public DesktopWidgetWindow(App app, AppSettings settings)
    {
        InitializeComponent();
        _app = app;
        ApplySettings(settings);
        SetSavedBounds(settings);
        Refresh(_app.Engine.GetSnapshot());
    }

    public void ApplySettings(AppSettings settings)
    {
        Topmost = settings.WidgetAlwaysOnTop;
        SetOpacity(settings.WidgetOpacity);
    }

    public void SetOpacity(double opacity) => Opacity = Math.Clamp(opacity, 0, 1);

    public void ShowWidgetWindow()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Refresh(_app.Engine.GetSnapshot());
        Activate();
    }

    public void HideWidgetWindow()
    {
        SaveBounds();
        Hide();
    }

    public void Refresh(TimerSnapshot snapshot)
    {
        var action = snapshot.Phase == TimerPhase.Idle ? _app.Settings.PowerAction : snapshot.Action;

        if (snapshot.Phase == TimerPhase.Idle)
        {
            WidgetTimeText.Text = FormatRemaining(TimeSpan.FromMinutes(_app.Settings.InitialTimerMinutes));
            WidgetTimeText.ToolTip = $"Saved timer · {ActionPresentation.Title(action)}";
            WidgetTimeText.Foreground = (System.Windows.Media.Brush)FindResource("BrightText");
            if (!_cancellationPending && _lastPhase == TimerPhase.Warning)
                _finishedAfterAction = true;
            _progressFraction = _finishedAfterAction ? 1 : 0;
            _runningDuration = TimeSpan.Zero;
            _warningDuration = TimeSpan.Zero;
            _lastPhase = TimerPhase.Idle;
            _cancellationPending = false;
            UpdateProgressFill();
            return;
        }

        if (snapshot.Phase == TimerPhase.Running
            && (_lastPhase != TimerPhase.Running || snapshot.PhaseDuration != _runningDuration))
        {
            _runningDuration = snapshot.PhaseDuration;
            _warningDuration = TimeSpan.FromSeconds(_app.Settings.WarningSeconds);
            _finishedAfterAction = false;
        }

        if (snapshot.Phase == TimerPhase.Warning)
        {
            if (_lastPhase != TimerPhase.Warning)
                _warningDuration = snapshot.PhaseDuration;
            WidgetTimeText.Foreground = (System.Windows.Media.Brush)FindResource("MoonGold");
            WidgetTimeText.ToolTip = ActionPresentation.WidgetTooltip(action);
        }
        else
        {
            WidgetTimeText.Foreground = (System.Windows.Media.Brush)FindResource("BrightText");
            WidgetTimeText.ToolTip = ActionPresentation.WidgetTooltip(action);
        }

        WidgetTimeText.Text = FormatRemaining(snapshot.Remaining);
        var totalDuration = _runningDuration + _warningDuration;
        var elapsed = snapshot.Phase == TimerPhase.Warning
            ? _runningDuration + (_warningDuration - snapshot.Remaining)
            : _runningDuration - snapshot.Remaining;
        _progressFraction = totalDuration <= TimeSpan.Zero
            ? 0
            : Math.Clamp(elapsed.TotalMilliseconds / totalDuration.TotalMilliseconds, 0, 1);
        if (snapshot.Phase == TimerPhase.Warning && snapshot.Remaining <= TimeSpan.FromSeconds(1))
            _progressFraction = 1;
        _lastPhase = snapshot.Phase;
        UpdateProgressFill();
    }

    private void WidgetProgressHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateProgressFill();

    private void UpdateProgressFill()
    {
        if (WidgetProgressHost is null || WidgetProgressFill is null) return;
        var width = WidgetProgressHost.ActualWidth;
        var height = WidgetProgressHost.ActualHeight;
        var fraction = Math.Clamp(double.IsFinite(_progressFraction) ? _progressFraction : 0, 0, 1);
        WidgetProgressFill.Width = fraction >= 1
            ? width
            : Math.Clamp(Math.Ceiling(width * fraction), 0, width);
        var radius = Math.Min(29, Math.Min(width, height) / 2);
        WidgetProgressHost.Clip = new WpfRectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }

    public void CompleteProgress()
    {
        _finishedAfterAction = true;
        _progressFraction = 1;
        UpdateProgressFill();
    }

    public void PrepareForCancellation()
    {
        _cancellationPending = true;
        _finishedAfterAction = false;
    }

    public void CloseFromApp()
    {
        _allowClose = true;
        SaveBounds();
        Close();
    }

    private void SetSavedBounds(AppSettings settings)
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(settings.WidgetWidth, MinWidth, Math.Max(MinWidth, workArea.Width));
        Height = Math.Clamp(settings.WidgetHeight, MinHeight, Math.Max(MinHeight, workArea.Height));
        Left = settings.WidgetLeft is double left && double.IsFinite(left)
            ? Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width))
            : Math.Max(workArea.Left, workArea.Right - Width - 24);
        Top = settings.WidgetTop is double top && double.IsFinite(top)
            ? Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height))
            : Math.Max(workArea.Top, workArea.Bottom - Height - 48);
    }

    private void SaveBounds()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top)) return;
        var updated = _app.Settings.Clone();
        updated.WidgetLeft = Left;
        updated.WidgetTop = Top;
        updated.WidgetWidth = ActualWidth > 0 ? ActualWidth : Width;
        updated.WidgetHeight = ActualHeight > 0 ? ActualHeight : Height;
        _app.SaveWidgetLayout(updated);
    }

    private void Widget_ClickOrDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !WidgetClickSurface.IsMouseOver) return;

        _mouseDownPosition = e.GetPosition(this);
        _pendingClickOrDrag = true;
        WidgetClickSurface.CaptureMouse();
        e.Handled = true;
    }

    private void Widget_ClickOrDrag_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pendingClickOrDrag || e.LeftButton != MouseButtonState.Pressed) return;

        var currentPosition = e.GetPosition(this);
        if (Math.Abs(currentPosition.X - _mouseDownPosition.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _mouseDownPosition.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pendingClickOrDrag = false;
        WidgetClickSurface.ReleaseMouseCapture();
        try { DragMove(); }
        catch (InvalidOperationException) { }
        SaveBounds();
        e.Handled = true;
    }

    private void Widget_ClickOrDrag_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_pendingClickOrDrag) return;

        _pendingClickOrDrag = false;
        if (WidgetClickSurface.IsMouseCaptured) WidgetClickSurface.ReleaseMouseCapture();
        _app.ShowMainWindow();
        e.Handled = true;
    }

    private void Widget_ClickOrDrag_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pendingClickOrDrag = false;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveBounds();
        if (_allowClose || _app.IsExiting) return;
        e.Cancel = true;
        Hide();
        _app.NotifyWidgetHidden();
    }

    private static string FormatRemaining(TimeSpan value)
    {
        var hours = (int)value.TotalHours;
        return hours > 0 ? $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes:00}:{value.Seconds:00}";
    }
}
