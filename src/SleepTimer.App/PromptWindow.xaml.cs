using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using SleepTimer.Core;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfSize = System.Windows.Size;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace SleepTimer.Desktop;

public partial class PromptWindow : Window
{
    private const double FloatingTitleBarHeight = 42;
    private const double MinimumFloatingContentWidth = 560;
    private const double MinimumFloatingContentHeight = 620;
    private const double DetailedPromptScaleThreshold = 0.7;
    private readonly App _app;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;
    private readonly TimeSpan _warningDuration;
    private readonly bool _fullScreenPrompt;
    private readonly bool _compactFloatingPrompt;
    private readonly bool _isPreview;
    private bool _allowClose;

    public PromptWindow(App app, AppSettings settings, TimerSnapshot warningSnapshot, bool isPreview = false)
    {
        InitializeComponent();
        _app = app;
        _settings = settings.Clone();
        _isPreview = isPreview;
        _warningDuration = warningSnapshot.PhaseDuration;
        _fullScreenPrompt = settings.PromptMode == PromptMode.FullScreen;
        _compactFloatingPrompt = !_fullScreenPrompt;
        if (_isPreview)
        {
            Title = "Sleep Timer · Warning Preview";
            PromptTitleText.Text = "Sleep Timer · Warning Preview";
            PromptTitleCloseButton.ToolTip = "Close preview";
            FooterNotice.Text = "PREVIEW ONLY · no timer or action will run.";
            FooterNotice.Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("MoonGold");
        }
        var snoozeMinutes = Math.Max(1, (int)Math.Round(warningSnapshot.SnoozeDuration.TotalMinutes));
        SnoozeButton.Content = $"Snooze {snoozeMinutes} minute{(snoozeMinutes == 1 ? "" : "s")}";
        ActionText.Text = ActionPresentation.WarningMessage(warningSnapshot.ActionRequest);
        CountdownLabel.Text = ActionPresentation.CountdownLabel(warningSnapshot.Action);
        var showPromptDetails = settings.PromptScale >= DetailedPromptScaleThreshold;
        PromptHeadline.Visibility = showPromptDetails ? Visibility.Visible : Visibility.Collapsed;
        MoonBadge.Visibility = showPromptDetails ? Visibility.Visible : Visibility.Collapsed;
        ActionText.Visibility = showPromptDetails ? Visibility.Visible : Visibility.Collapsed;
        CountdownLabel.Visibility = showPromptDetails ? Visibility.Visible : Visibility.Collapsed;
        WarningProgress.Visibility = showPromptDetails ? Visibility.Visible : Visibility.Collapsed;
        FooterNotice.Visibility = showPromptDetails ? Visibility.Visible : Visibility.Collapsed;
        PromptCard.Padding = showPromptDetails
            ? new Thickness(32, 30, 32, 30)
            : new Thickness(18, 14, 18, 14);
        SnoozeButton.Margin = new Thickness(0, showPromptDetails ? 20 : 8, 0, 0);
        CancelButton.Margin = new Thickness(0, showPromptDetails ? 9 : 7, 0, 0);
        CountdownText.FontWeight = showPromptDetails ? FontWeights.Light : FontWeights.SemiBold;
        CountdownText.FontSize = Math.Max(40, 24 / settings.PromptScale);
        SnoozeButton.FontSize = Math.Max(17, 11 / settings.PromptScale);
        SnoozeButton.MinHeight = Math.Max(52, 36 / settings.PromptScale);
        CancelButton.FontSize = Math.Max(13, 10 / settings.PromptScale);
        CancelButton.MinHeight = Math.Max(44, 34 / settings.PromptScale);
        if (_isPreview) CancelButton.Content = "Close preview";
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
            PromptTitleBar.Visibility = _compactFloatingPrompt ? Visibility.Collapsed : Visibility.Visible;
            PromptTitleText.Visibility = _compactFloatingPrompt || settings.PromptScale < 0.6
                ? Visibility.Collapsed
                : Visibility.Visible;
            PromptCard.Margin = new Thickness(0, _compactFloatingPrompt ? 0 : FloatingTitleBarHeight, 0, 0);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = _compactFloatingPrompt ? 0 : FloatingTitleBarHeight,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(20),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
            var workArea = SystemParameters.WorkArea;
            if (_compactFloatingPrompt)
            {
                Width = Math.Min(MinimumFloatingContentWidth * settings.PromptScale, workArea.Width);
                Height = Math.Min(400 * settings.PromptScale, workArea.Height);
            }
            else
            {
                var baseWidth = Math.Max(settings.FloatingWidth, MinimumFloatingContentWidth);
                var baseHeight = Math.Max(settings.FloatingHeight, MinimumFloatingContentHeight);
                Width = Math.Min(baseWidth * settings.PromptScale, workArea.Width);
                Height = Math.Min(baseHeight * settings.PromptScale + FloatingTitleBarHeight, workArea.Height);
            }
            Topmost = true;
        }

        var scale = _fullScreenPrompt
            ? Transform.Identity
            : new ScaleTransform(settings.PromptScale, settings.PromptScale);
        PromptCard.LayoutTransform = scale;
        if (!_fullScreenPrompt)
        {
            if (_compactFloatingPrompt)
            {
                var workArea = SystemParameters.WorkArea;
                PromptCard.Measure(new WpfSize(Math.Max(1, workArea.Width - 12), Math.Max(1, workArea.Height - 12)));
                var desiredSize = PromptCard.DesiredSize;
                Width = Math.Min(Math.Max(desiredSize.Width + 12, 120), workArea.Width);
                Height = Math.Min(Math.Max(desiredSize.Height + 12, 96), workArea.Height);
            }
            SetFloatingPosition(settings);
        }
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refreshTimer.Tick += (_, _) =>
        {
            if (!_isPreview) Refresh(_app.Engine.GetSnapshot());
        };
        if (_isPreview)
        {
            Refresh(warningSnapshot);
        }
        else
        {
            _refreshTimer.Start();
            Refresh(_app.Engine.GetSnapshot());
        }
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

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPreview) Close();
        else _app.Engine.Snooze();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPreview) Close();
        else _app.CancelTimer();
    }

    private void PromptBackdrop_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_fullScreenPrompt || e.ChangedButton != MouseButton.Left
            || IsInteractiveSource(e.OriginalSource as DependencyObject)) return;

        e.Handled = true;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfButtonBase or WpfTextBoxBase or WpfComboBox or System.Windows.Controls.Slider) return true;
            source = source switch
            {
                System.Windows.Media.Visual visual => VisualTreeHelper.GetParent(visual),
                System.Windows.Media.Media3D.Visual3D visual3D => VisualTreeHelper.GetParent(visual3D),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }
        return false;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (_isPreview) Close();
        else _app.CancelTimer();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isPreview)
        {
            _refreshTimer.Stop();
            return;
        }

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
        if (!_compactFloatingPrompt)
        {
            updated.FloatingWidth = Width / _settings.PromptScale;
            updated.FloatingHeight = Math.Max(360, (Height - FloatingTitleBarHeight) / _settings.PromptScale);
        }
        _app.SavePromptPosition(updated);
    }
}

