namespace SleepTimer.Core;

public enum PowerAction
{
    Sleep,
    ShutDown,
    Restart,
    Lock,
    CloseApp,
    RunProgram
}

public sealed record PowerActionRequest(
    PowerAction Action,
    string? CloseAppProcessName = null,
    string? CustomProgramPath = null,
    string? CustomProgramArguments = null);

public enum PromptMode
{
    FullScreen,
    Floating
}

public sealed class AppSettings
{
    public int InitialTimerMinutes { get; set; } = 120;
    public bool StartTimerOnLaunch { get; set; } = true;
    public PowerAction PowerAction { get; set; } = PowerAction.Sleep;
    public string CloseAppProcessName { get; set; } = string.Empty;
    public string CustomProgramPath { get; set; } = string.Empty;
    public string CustomProgramArguments { get; set; } = string.Empty;
    public bool ShowWarning { get; set; } = true;
    public int WarningSeconds { get; set; } = 60;
    public int SnoozeMinutes { get; set; } = 15;
    public bool ShowCountdown { get; set; } = true;
    public PromptMode PromptMode { get; set; } = PromptMode.FullScreen;
    public double PromptScale { get; set; } = 1.0;
    public double FloatingWidth { get; set; } = 520;
    public double FloatingHeight { get; set; } = 560;
    public double? FloatingLeft { get; set; }
    public double? FloatingTop { get; set; }
    public bool ShowWidgetOnStartup { get; set; }
    public bool WidgetAlwaysOnTop { get; set; } = true;
    public double WidgetOpacity { get; set; } = 0.96;
    public double WidgetWidth { get; set; } = 164;
    public double WidgetHeight { get; set; } = 60;
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        InitialTimerMinutes = Math.Clamp(InitialTimerMinutes, 1, 24 * 60);
        WarningSeconds = Math.Clamp(WarningSeconds, 5, 600);
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 1, 180);
        PromptScale = double.IsFinite(PromptScale) ? Math.Clamp(PromptScale, 0.7, 1.4) : 1.0;
        FloatingWidth = double.IsFinite(FloatingWidth) ? Math.Clamp(FloatingWidth, 360, 1000) : 520;
        FloatingHeight = double.IsFinite(FloatingHeight) ? Math.Clamp(FloatingHeight, 360, 1000) : 560;
        WidgetOpacity = double.IsFinite(WidgetOpacity) ? Math.Clamp(WidgetOpacity, 0.0, 1.0) : 0.96;
        WidgetWidth = double.IsFinite(WidgetWidth) ? Math.Clamp(WidgetWidth, 150, 180) : 164;
        WidgetHeight = double.IsFinite(WidgetHeight) ? Math.Clamp(WidgetHeight, 56, 68) : 60;
        CloseAppProcessName = (CloseAppProcessName ?? string.Empty).Trim();
        CustomProgramPath = (CustomProgramPath ?? string.Empty).Trim();
        CustomProgramArguments ??= string.Empty;
        if (FloatingLeft is not double left || !double.IsFinite(left)) FloatingLeft = null;
        if (FloatingTop is not double top || !double.IsFinite(top)) FloatingTop = null;
        if (WidgetLeft is not double widgetLeft || !double.IsFinite(widgetLeft)) WidgetLeft = null;
        if (WidgetTop is not double widgetTop || !double.IsFinite(widgetTop)) WidgetTop = null;
        if (!Enum.IsDefined(PowerAction)) PowerAction = PowerAction.Sleep;
        if (!Enum.IsDefined(PromptMode)) PromptMode = PromptMode.FullScreen;
    }
}

