using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using SleepTimer.Core;

namespace SleepTimer.Desktop;

public partial class PromptWindow : Window
{
    private const double FloatingTitleBarHeight = 42;
    private const double MinimumFloatingContentWidth = 560;
    private const double MinimumFloatingContentHeight = 620;

    private readonly App _app;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;
    private readonly TimeSpan _warningDuration;
    private readonly bool _fullScreenPrompt;
    private bool _allowClose;

    public PromptWindow(App app, AppSettings settings, TimerSnapshot warningSnapshot)
    {
        InitializeComponent();
        _app = app;
        _settings = settings.Clone();
        _warningDuration = warningSnapshot.PhaseDuration;
        _fullScreenPrompt = settings.PromptMode == PromptMode.FullScreen;
        var snoozeMinutes = Math.Max(1, (int)Math.Round(warningSnapshot.SnoozeDuration.TotalMinutes));
        SnoozeButton.Content = $"Snooze {snoozeMinutes} minute{(snoozeMinutes == 1 ? "" : "s")}";
        ActionText.Text = ActionPresentation.WarningMessage(warningSnapshot.ActionRequest);
        CountdownLabel.Text = ActionPresentation.CountdownLabel(warningSnapshot.Action);
        if (!settings.ShowCountdown)
        {
            CountdownLabel.Visibility = Visibility.Collapsed;
            CountdownText.Visibility = Visibility.Collapsed;
            WarningProgress.Visibility = Visibility.Collapsed;
        }

        if (_fullScreenPrompt)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            PromptTitleBar.Visibility = Visibility.Collapsed;
            PromptCard.Margin = new Thickness(0);
            WindowChrome.SetWindowChrome(this, null);
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            SourceInitialized += (_, _) => ApplyPrimaryScreenBounds();
            Loaded += (_, _) => FitPromptCardToScreen();
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            AllowsTransparency = false;
            Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("NightBackground");
            PromptBackdrop.Background = Background;
            PromptTitleBar.Visibility = Visibility.Visible;
            PromptTitleText.Visibility = settings.PromptScale < 0.6 ? Visibility.Collapsed : Visibility.Visible;
            PromptCard.Margin = new Thickness(0, FloatingTitleBarHeight, 0, 0);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = FloatingTitleBarHeight,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(20),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
            var workArea = SystemParameters.WorkArea;
            var baseWidth = Math.Max(settings.FloatingWidth, MinimumFloatingContentWidth);
            var baseHeight = Math.Max(settings.FloatingHeight, MinimumFloatingContentHeight);
            Width = Math.Min(baseWidth * settings.PromptScale, workArea.Width);
            Height = Math.Min(baseHeight * settings.PromptScale + FloatingTitleBarHeight, workArea.Height);
            Topmost = true;
            SetFloatingPosition(settings);
        }

        var scale = _fullScreenPrompt
            ? Transform.Identity
            : new ScaleTransform(settings.PromptScale, settings.PromptScale);
        PromptCard.LayoutTransform = scale;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refreshTimer.Tick += (_, _) => Refresh(_app.Engine.GetSnapshot());
        _refreshTimer.Start();
        Refresh(_app.Engine.GetSnapshot());
    }

    private void ApplyPrimaryScreenBounds()
    {
        var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
        if (primaryScreen is null) return;

        var bounds = primaryScreen.Bounds;
        var deviceToDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? System.Windows.Media.Matrix.Identity;
        var topLeft = deviceToDip.Transform(new System.Windows.Point(bounds.Left, bounds.Top));
        var size = deviceToDip.Transform(new System.Windows.Vector(bounds.Width, bounds.Height));

        WindowState = WindowState.Normal;
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = size.X;
        Height = size.Y;
    }

    private void FitPromptCardToScreen()
    {
        if (!_fullScreenPrompt) return;

        var available = new System.Windows.Size(Math.Max(1, ActualWidth - 36), Math.Max(1, ActualHeight - 36));
        PromptCard.LayoutTransform = Transform.Identity;
        PromptCard.Measure(available);
        var desired = PromptCard.DesiredSize;
        if (desired.Width <= 0 || desired.Height <= 0) return;

        var fitScale = Math.Min(available.Width / desired.Width, available.Height / desired.Height);
        var scale = Math.Min(_settings.PromptScale, fitScale);
        PromptCard.LayoutTransform = new ScaleTransform(scale, scale);
    }

    public void Refresh(TimerSnapshot snapshot)
    {
        if (snapshot.Phase != TimerPhase.Warning) return;
        if (_settings.ShowCountdown)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling(snapshot.Remaining.TotalSeconds));
            CountdownText.Text = $"{seconds / 60:00}:{seconds % 60:00}";
            WarningProgress.Value = _warningDuration.TotalMilliseconds <= 0
                ? 100
                : Math.Clamp(100 * (1 - snapshot.Remaining.TotalMilliseconds / _warningDuration.TotalMilliseconds), 0, 100);
        }
    }

    public void CloseFromApp()
    {
        _allowClose = true;
        _refreshTimer.Stop();
        Close();
    }

    private void SetFloatingPosition(AppSettings settings)
    {
        var workArea = SystemParameters.WorkArea;
        Left = settings.FloatingLeft is double left && double.IsFinite(left)
            ? Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width))
            : workArea.Left + (workArea.Width - Width) / 2;
        Top = settings.FloatingTop is double top && double.IsFinite(top)
            ? Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height))
            : workArea.Top + (workArea.Height - Height) / 2;
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e) => _app.Engine.Snooze();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => _app.CancelTimer();

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _app.CancelTimer();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            _refreshTimer.Stop();
            SaveFloatingBounds();
            Dispatcher.BeginInvoke(_app.CancelTimer);
            return;
        }

        _refreshTimer.Stop();
        SaveFloatingBounds();
    }

    private void SaveFloatingBounds()
    {
        if (_settings.PromptMode != PromptMode.Floating || !double.IsFinite(Left) || !double.IsFinite(Top)) return;
        var updated = _app.Settings.Clone();
        updated.FloatingLeft = Left;
        updated.FloatingTop = Top;
        updated.FloatingWidth = Width / _settings.PromptScale;
        updated.FloatingHeight = Math.Max(360, (Height - FloatingTitleBarHeight) / _settings.PromptScale);
        _app.SavePromptPosition(updated);
    }
}

