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

public sealed class WeeklyScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsEnabled { get; set; }
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    public TimeOnly StartTime { get; set; } = new(22, 0);
    public bool SkipCountdown { get; set; }
    public PowerActionRequest ActionRequest { get; set; } = new(PowerAction.Sleep);

    public WeeklyScheduleEntry Clone() => new()
    {
        Id = Id,
        IsEnabled = IsEnabled,
        DaysOfWeek = [.. DaysOfWeek],
        StartTime = StartTime,
        SkipCountdown = SkipCountdown,
        ActionRequest = ActionRequest with { }
    };

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        DaysOfWeek ??= [];
        DaysOfWeek = DaysOfWeek
            .Where(Enum.IsDefined)
            .Distinct()
            .OrderBy(day => (int)day)
            .ToList();
        ActionRequest ??= new PowerActionRequest(PowerAction.Sleep);
        if (!Enum.IsDefined(ActionRequest.Action)) ActionRequest = new PowerActionRequest(PowerAction.Sleep);
        ActionRequest = ActionRequest with
        {
            CloseAppProcessName = (ActionRequest.CloseAppProcessName ?? string.Empty).Trim(),
            CustomProgramPath = (ActionRequest.CustomProgramPath ?? string.Empty).Trim(),
            CustomProgramArguments = ActionRequest.CustomProgramArguments ?? string.Empty
        };
    }
}

public enum PromptMode
{
    FullScreen,
    Floating
}

public static class AudioSelectionIds
{
    public const string CustomFile = "custom";

    public const string MoonlitAmbient = "moonlit-ambient";
    public const string SoftPiano = "soft-piano";
    public const string RainyNight = "rainy-night";
    public const string NightForest = "night-forest";
    public const string OceanWaves = "ocean-waves";

    public const string SoftChime = "soft-chime";
    public const string WarmBell = "warm-bell";
    public const string NightBird = "night-bird";
    public const string MoonSparkle = "moon-sparkle";
    public const string Stardust = "stardust";
    public const string DreamPortal = "dream-portal";
}

public sealed class AppSettings
{
    public int InitialTimerMinutes { get; set; } = 120;
    public bool StartTimerOnLaunch { get; set; } = true;
    public bool StartAppOnWindowsStartup { get; set; }
    public PowerAction PowerAction { get; set; } = PowerAction.Sleep;
    public string CloseAppProcessName { get; set; } = string.Empty;
    public string CustomProgramPath { get; set; } = string.Empty;
    public string CustomProgramArguments { get; set; } = string.Empty;
    public bool ShowWarning { get; set; } = true;
    public int WarningSeconds { get; set; } = 60;
    public int SnoozeMinutes { get; set; } = 15;
    public bool ShowCountdown { get; set; } = true;
    public PromptMode PromptMode { get; set; } = PromptMode.FullScreen;
    public double PromptScale { get; set; } = 0.8;
    public int SettingsSchemaVersion { get; set; }
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
    public bool SleepMusicEnabled { get; set; }
    public string SleepMusicSelectionId { get; set; } = AudioSelectionIds.MoonlitAmbient;
    public string SleepMusicFilePath { get; set; } = string.Empty;
    public int SleepMusicVolume { get; set; } = 35;
    public bool EndSoundEnabled { get; set; }
    public string EndSoundSelectionId { get; set; } = AudioSelectionIds.SoftChime;
    public string EndSoundFilePath { get; set; } = string.Empty;
    public int EndSoundVolume { get; set; } = 65;
    public List<WeeklyScheduleEntry> WeeklySchedules { get; set; } = [];

    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.WeeklySchedules = WeeklySchedules.Select(schedule => schedule.Clone()).ToList();
        return clone;
    }

    public void Normalize()
    {
        InitialTimerMinutes = Math.Clamp(InitialTimerMinutes, 1, 24 * 60);
        WarningSeconds = Math.Clamp(WarningSeconds, 5, 600);
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 1, 180);
        PromptScale = double.IsFinite(PromptScale) ? Math.Clamp(PromptScale, 0.3, 1.4) : 0.8;
        FloatingWidth = double.IsFinite(FloatingWidth) ? Math.Clamp(FloatingWidth, 360, 1000) : 520;
        FloatingHeight = double.IsFinite(FloatingHeight) ? Math.Clamp(FloatingHeight, 360, 1000) : 560;
        WidgetOpacity = double.IsFinite(WidgetOpacity) ? Math.Clamp(WidgetOpacity, 0.0, 1.0) : 0.96;
        WidgetWidth = double.IsFinite(WidgetWidth) ? Math.Clamp(WidgetWidth, 150, 180) : 164;
        WidgetHeight = double.IsFinite(WidgetHeight) ? Math.Clamp(WidgetHeight, 56, 68) : 60;
        SleepMusicSelectionId = string.IsNullOrWhiteSpace(SleepMusicSelectionId)
            ? AudioSelectionIds.MoonlitAmbient
            : SleepMusicSelectionId.Trim();
        if (string.Equals(SleepMusicSelectionId, "soft-binaural", StringComparison.OrdinalIgnoreCase))
            SleepMusicSelectionId = AudioSelectionIds.MoonlitAmbient;
        SleepMusicFilePath = (SleepMusicFilePath ?? string.Empty).Trim();
        SleepMusicVolume = Math.Clamp(SleepMusicVolume, 0, 100);
        EndSoundSelectionId = string.IsNullOrWhiteSpace(EndSoundSelectionId)
            ? AudioSelectionIds.SoftChime
            : EndSoundSelectionId.Trim();
        EndSoundFilePath = (EndSoundFilePath ?? string.Empty).Trim();
        EndSoundVolume = Math.Clamp(EndSoundVolume, 0, 100);
        WeeklySchedules ??= [];
        var scheduleIds = new HashSet<Guid>();
        WeeklySchedules = WeeklySchedules
            .Where(schedule => schedule is not null)
            .Select(schedule =>
            {
                schedule.Normalize();
                return schedule;
            })
            .Where(schedule => scheduleIds.Add(schedule.Id))
            .ToList();
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

