using SleepTimer.Core;
using System.IO;

var checks = new (string Name, Action Run)[]
{
    ("timer enters warning after the configured interval", TimerEntersWarning),
    ("timer can run an action immediately without a warning", TimerCompletesWithoutWarning),
    ("snooze schedules another timer and repeats the warning", SnoozeRepeats),
    ("cancel during the timer prevents a power action", CancelDuringTimer),
    ("cancel during warning prevents a power action", CancelDuringWarning),
    ("restart replaces the current timer", RestartReplacesTimer),
    ("settings round-trip and preserve widget, launch, layout, and power options", SettingsRoundTrip)
};

var failed = 0;
foreach (var (name, run) in checks)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}: {exception.Message}");
    }
}

Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed.");
return failed == 0 ? 0 : 1;

static void TimerEntersWarning()
{
    using var fixture = new Fixture();
    fixture.Engine.Start(TimeSpan.FromMinutes(120), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(15), PowerAction.Sleep);
    Equal(TimerPhase.Running, fixture.Engine.GetSnapshot().Phase);
    fixture.Advance(TimeSpan.FromMinutes(120));
    Equal(TimerPhase.Warning, fixture.Engine.GetSnapshot().Phase);
    Equal(TimeSpan.FromSeconds(60), fixture.Engine.GetSnapshot().Remaining);
}

static void TimerCompletesWithoutWarning()
{
    using var fixture = new Fixture();
    var request = new PowerActionRequest(PowerAction.RunProgram, CustomProgramPath: "C:\\Tools\\night.exe", CustomProgramArguments: "--quiet");
    PowerActionRequest? requested = null;
    fixture.Engine.PowerActionRequested += action => requested = action;
    fixture.Engine.Start(TimeSpan.FromMinutes(2), TimeSpan.Zero, TimeSpan.FromMinutes(15), request);
    Equal(false, fixture.Engine.GetSnapshot().WarningEnabled);
    fixture.Advance(TimeSpan.FromMinutes(2));
    Equal(TimerPhase.Idle, fixture.Engine.GetSnapshot().Phase);
    Equal(request, requested);
    Equal(request, fixture.Engine.GetSnapshot().ActionRequest);
}

static void SnoozeRepeats()
{
    using var fixture = new Fixture();
    PowerActionRequest? requested = null;
    fixture.Engine.PowerActionRequested += action => requested = action;
    fixture.Engine.Start(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(15), PowerAction.ShutDown);
    fixture.Advance(TimeSpan.FromMinutes(2));
    fixture.Engine.Snooze();
    Equal(TimerPhase.Running, fixture.Engine.GetSnapshot().Phase);
    Equal(TimeSpan.FromMinutes(15), fixture.Engine.GetSnapshot().Remaining);
    fixture.Advance(TimeSpan.FromMinutes(15));
    Equal(TimerPhase.Warning, fixture.Engine.GetSnapshot().Phase);
    fixture.Advance(TimeSpan.FromSeconds(10));
    Equal(PowerAction.ShutDown, requested?.Action);
    Equal(TimerPhase.Idle, fixture.Engine.GetSnapshot().Phase);
}

static void CancelDuringTimer()
{
    using var fixture = new Fixture();
    var requested = 0;
    fixture.Engine.PowerActionRequested += _ => requested++;
    fixture.Engine.Start(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), PowerAction.Sleep);
    fixture.Engine.Cancel();
    fixture.Advance(TimeSpan.FromMinutes(2));
    Equal(TimerPhase.Idle, fixture.Engine.GetSnapshot().Phase);
    Equal(0, requested);
}

static void CancelDuringWarning()
{
    using var fixture = new Fixture();
    var requested = 0;
    fixture.Engine.PowerActionRequested += _ => requested++;
    fixture.Engine.Start(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), PowerAction.Sleep);
    fixture.Advance(TimeSpan.FromMinutes(1));
    fixture.Engine.Cancel();
    fixture.Advance(TimeSpan.FromSeconds(10));
    Equal(TimerPhase.Idle, fixture.Engine.GetSnapshot().Phase);
    Equal(0, requested);
}

static void RestartReplacesTimer()
{
    using var fixture = new Fixture();
    fixture.Engine.Start(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), PowerAction.Sleep);
    fixture.Clock.Advance(TimeSpan.FromMinutes(4));
    fixture.Engine.Start(TimeSpan.FromMinutes(90), TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(7), PowerAction.ShutDown);
    Equal(TimeSpan.FromMinutes(90), fixture.Engine.GetSnapshot().Remaining);
    fixture.Advance(TimeSpan.FromMinutes(89));
    Equal(TimerPhase.Running, fixture.Engine.GetSnapshot().Phase);
    fixture.Advance(TimeSpan.FromMinutes(1));
    Equal(TimerPhase.Warning, fixture.Engine.GetSnapshot().Phase);
    Equal(PowerAction.ShutDown, fixture.Engine.GetSnapshot().Action);
}

static void SettingsRoundTrip()
{
    var directory = Path.Combine(Path.GetTempPath(), "SleepTimer-Test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new AppSettings());
        var defaults = store.Load();
        if (defaults.FloatingLeft is not null || defaults.FloatingTop is not null)
            throw new InvalidOperationException("Centered default position was not preserved as unset.");
        Equal(true, defaults.StartTimerOnLaunch);
        Equal(false, defaults.ShowWidgetOnStartup);
        Equal(true, defaults.WidgetAlwaysOnTop);
        Equal(0.96, defaults.WidgetOpacity);

        var settings = new AppSettings
        {
            InitialTimerMinutes = 95,
            ShowWarning = false,
            WarningSeconds = 45,
            SnoozeMinutes = 12,
            PowerAction = PowerAction.RunProgram,
            CloseAppProcessName = "chrome.exe",
            CustomProgramPath = "C:\\Tools\\night.exe",
            CustomProgramArguments = "--quiet /s",
            PromptMode = PromptMode.Floating,
            PromptScale = 1.2,
            ShowCountdown = false,
            StartTimerOnLaunch = false,
            FloatingWidth = 610,
            FloatingHeight = 590,
            FloatingLeft = 40,
            FloatingTop = 75,
            ShowWidgetOnStartup = true,
            WidgetAlwaysOnTop = false,
            WidgetOpacity = 0.0,
            WidgetWidth = 172,
            WidgetHeight = 64,
            WidgetLeft = 820,
            WidgetTop = 95
        };
        store.Save(settings);
        var loaded = store.Load();
        Equal(settings.InitialTimerMinutes, loaded.InitialTimerMinutes);
        Equal(settings.ShowWarning, loaded.ShowWarning);
        Equal(settings.WarningSeconds, loaded.WarningSeconds);
        Equal(settings.SnoozeMinutes, loaded.SnoozeMinutes);
        Equal(settings.PowerAction, loaded.PowerAction);
        Equal(settings.CloseAppProcessName, loaded.CloseAppProcessName);
        Equal(settings.CustomProgramPath, loaded.CustomProgramPath);
        Equal(settings.CustomProgramArguments, loaded.CustomProgramArguments);
        Equal(settings.PromptMode, loaded.PromptMode);
        Equal(settings.PromptScale, loaded.PromptScale);
        Equal(settings.ShowCountdown, loaded.ShowCountdown);
        Equal(settings.StartTimerOnLaunch, loaded.StartTimerOnLaunch);
        Equal(settings.FloatingWidth, loaded.FloatingWidth);
        Equal(settings.FloatingHeight, loaded.FloatingHeight);
        Equal(settings.FloatingLeft, loaded.FloatingLeft);
        Equal(settings.FloatingTop, loaded.FloatingTop);
        Equal(settings.ShowWidgetOnStartup, loaded.ShowWidgetOnStartup);
        Equal(settings.WidgetAlwaysOnTop, loaded.WidgetAlwaysOnTop);
        Equal(settings.WidgetOpacity, loaded.WidgetOpacity);
        Equal(settings.WidgetWidth, loaded.WidgetWidth);
        Equal(settings.WidgetHeight, loaded.WidgetHeight);
        Equal(settings.WidgetLeft, loaded.WidgetLeft);
        Equal(settings.WidgetTop, loaded.WidgetTop);
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

sealed class Fixture : IDisposable
{
    public FakeClock Clock { get; } = new();
    public ManualScheduler Scheduler { get; }
    public TimerEngine Engine { get; }
    public Fixture()
    {
        Scheduler = new ManualScheduler(Clock);
        Engine = new TimerEngine(Clock, Scheduler);
    }
    public void Advance(TimeSpan elapsed) => Scheduler.Advance(elapsed);
    public void Dispose() => Engine.Dispose();
}

sealed class FakeClock : IMonotonicClock
{
    private long _ticks;
    public long GetTimestamp() => _ticks;
    public TimeSpan GetElapsedTime(long start, long end) => TimeSpan.FromTicks(end - start);
    public void Advance(TimeSpan amount) => _ticks += amount.Ticks;
    public void AdvanceTo(long timestamp) => _ticks = timestamp;
}

sealed class ManualScheduler : IOneShotScheduler
{
    private readonly List<Job> _jobs = [];
    private readonly FakeClock _clock;
    public ManualScheduler(FakeClock clock) => _clock = clock;
    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var job = new Job(_clock.GetTimestamp() + delay.Ticks, callback);
        _jobs.Add(job);
        return job;
    }

    public void Advance(TimeSpan elapsed)
    {
        var target = _clock.GetTimestamp() + elapsed.Ticks;
        while (true)
        {
            var next = _jobs
                .Where(candidate => !candidate.Disposed && candidate.DueAt <= target)
                .OrderBy(candidate => candidate.DueAt)
                .FirstOrDefault();
            if (next is null) break;
            _clock.AdvanceTo(next.DueAt);
            next.Disposed = true;
            next.Callback();
        }
        _clock.AdvanceTo(target);
    }

    private sealed class Job(long dueAt, Action callback) : IDisposable
    {
        public long DueAt { get; } = dueAt;
        public Action Callback { get; } = callback;
        public bool Disposed { get; set; }
        public void Dispose() => Disposed = true;
    }
}

