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
    ("settings round-trip and preserve widget, launch, layout, power, and audio options", SettingsRoundTrip),
    ("audio follows timer start, warning, snooze, and action transitions", AudioTimerLifecycle),
    ("end cue plays before an immediate no-warning power action", AudioCuePrecedesImmediateAction),
    ("audio playback failures are reported without stopping the timer", AudioFailureIsNonFatal),
    ("saved audio changes apply live and cancellation stops playback", AudioSettingsApplyLiveAndCancel),
    ("music preview pauses and resumes the timer soundtrack", AudioPreviewRestoresMusic)
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
        Equal(false, defaults.SleepMusicEnabled);
        Equal(false, defaults.EndSoundEnabled);
        Equal(AudioSelectionIds.MoonlitAmbient, defaults.SleepMusicSelectionId);
        Equal(AudioSelectionIds.SoftChime, defaults.EndSoundSelectionId);
        Equal(35, defaults.SleepMusicVolume);
        Equal(65, defaults.EndSoundVolume);

        File.WriteAllText(store.FilePath, "{\"InitialTimerMinutes\":75}");
        var oldSettings = store.Load();
        Equal(75, oldSettings.InitialTimerMinutes);
        Equal(false, oldSettings.SleepMusicEnabled);
        Equal(false, oldSettings.EndSoundEnabled);
        Equal(AudioSelectionIds.MoonlitAmbient, oldSettings.SleepMusicSelectionId);

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
            WidgetTop = 95,
            SleepMusicEnabled = true,
            SleepMusicSelectionId = AudioSelectionIds.CustomFile,
            SleepMusicFilePath = "C:\\Audio\\night-song.mp3",
            SleepMusicVolume = 28,
            EndSoundEnabled = true,
            EndSoundSelectionId = AudioSelectionIds.WarmBell,
            EndSoundFilePath = "C:\\Audio\\warm-bell.wav",
            EndSoundVolume = 72
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
        Equal(settings.SleepMusicEnabled, loaded.SleepMusicEnabled);
        Equal(settings.SleepMusicSelectionId, loaded.SleepMusicSelectionId);
        Equal(settings.SleepMusicFilePath, loaded.SleepMusicFilePath);
        Equal(settings.SleepMusicVolume, loaded.SleepMusicVolume);
        Equal(settings.EndSoundEnabled, loaded.EndSoundEnabled);
        Equal(settings.EndSoundSelectionId, loaded.EndSoundSelectionId);
        Equal(settings.EndSoundFilePath, loaded.EndSoundFilePath);
        Equal(settings.EndSoundVolume, loaded.EndSoundVolume);

        settings.SleepMusicVolume = -1;
        settings.EndSoundVolume = 150;
        settings.Normalize();
        Equal(0, settings.SleepMusicVolume);
        Equal(100, settings.EndSoundVolume);
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static void AudioTimerLifecycle()
{
    var backend = new FakeAudioPlaybackBackend();
    using var audio = new TimerAudioCoordinator(backend);
    var settings = new AppSettings { SleepMusicEnabled = true, EndSoundEnabled = true };
    audio.StartTimer(settings, "ambient.mp3", "chime.wav");
    Equal(1, backend.MusicStarts);
    Equal(true, backend.MusicLoopRequested);

    audio.ObserveSnapshot(new TimerSnapshot(TimerPhase.Running, TimeSpan.FromMinutes(1), PowerAction.Sleep, TimeSpan.FromMinutes(1)));
    audio.ObserveSnapshot(new TimerSnapshot(TimerPhase.Warning, TimeSpan.FromSeconds(60), PowerAction.Sleep, TimeSpan.FromSeconds(60)));
    Equal(1, backend.CueStarts);
    audio.ObserveSnapshot(new TimerSnapshot(TimerPhase.Running, TimeSpan.FromMinutes(15), PowerAction.Sleep, TimeSpan.FromMinutes(15)));
    audio.ObserveSnapshot(new TimerSnapshot(TimerPhase.Warning, TimeSpan.FromSeconds(60), PowerAction.Sleep, TimeSpan.FromSeconds(60)));
    Equal(2, backend.CueStarts);

    audio.PrepareForPowerActionAsync(warningWasEnabled: true).GetAwaiter().GetResult();
    Equal(2, backend.CueStarts);
    Equal(1, backend.MusicStops);
    Equal(false, backend.CueIsPlaying);
}

static void AudioCuePrecedesImmediateAction()
{
    var backend = new FakeAudioPlaybackBackend();
    using var audio = new TimerAudioCoordinator(backend);
    audio.StartTimer(new AppSettings { SleepMusicEnabled = true, EndSoundEnabled = true }, "ambient.mp3", "chime.wav");
    audio.PrepareForPowerActionAsync(warningWasEnabled: false).GetAwaiter().GetResult();
    var cueStarted = backend.Events.IndexOf("cue-wait-start");
    var cueEnded = backend.Events.IndexOf("cue-wait-end");
    var musicStopped = backend.Events.IndexOf("music-stop");
    if (cueStarted < 0 || cueEnded <= cueStarted || musicStopped <= cueEnded)
        throw new InvalidOperationException("The immediate action path did not wait for the end cue before stopping the music.");
}

static void AudioSettingsApplyLiveAndCancel()
{
    var backend = new FakeAudioPlaybackBackend();
    using var audio = new TimerAudioCoordinator(backend);
    var settings = new AppSettings();
    audio.StartTimer(settings, "ambient.mp3", "chime.wav");
    Equal(0, backend.MusicStarts);

    settings.SleepMusicEnabled = true;
    settings.SleepMusicVolume = 48;
    audio.ApplySettings(settings, "ambient.mp3", "chime.wav",
        new TimerSnapshot(TimerPhase.Running, TimeSpan.FromMinutes(2), PowerAction.Sleep, TimeSpan.FromMinutes(2)));
    Equal(1, backend.MusicStarts);
    Equal(48, backend.MusicVolume);

    settings.SleepMusicSelectionId = AudioSelectionIds.SoftPiano;
    audio.ApplySettings(settings, "piano.mp3", "chime.wav",
        new TimerSnapshot(TimerPhase.Running, TimeSpan.FromMinutes(1), PowerAction.Sleep, TimeSpan.FromMinutes(2)));
    Equal(2, backend.MusicStarts);
    Equal("piano.mp3", backend.MusicSource);

    audio.CancelTimer();
    Equal(2, backend.MusicStops);
}

static void AudioFailureIsNonFatal()
{
    var backend = new FakeAudioPlaybackBackend();
    using var audio = new TimerAudioCoordinator(backend);
    var failures = 0;
    audio.PlaybackFailed += _ => failures++;
    audio.StartTimer(new AppSettings { SleepMusicEnabled = true, EndSoundEnabled = true }, "missing.mp3", "chime.wav");
    Equal(1, failures);
    audio.ObserveSnapshot(new TimerSnapshot(TimerPhase.Warning, TimeSpan.FromSeconds(60), PowerAction.Sleep, TimeSpan.FromSeconds(60)));
    Equal(1, backend.CueStarts);
}

static void AudioPreviewRestoresMusic()
{
    var backend = new FakeAudioPlaybackBackend();
    using var audio = new TimerAudioCoordinator(backend);
    audio.StartTimer(new AppSettings { SleepMusicEnabled = true }, "ambient.mp3", "chime.wav");
    audio.PreviewMusic("piano.mp3", 35);
    Equal(true, audio.IsPreviewingMusic);
    Equal(1, backend.MusicPauses);
    audio.StopPreview();
    Equal(false, audio.IsPreviewingMusic);
    Equal(1, backend.MusicResumes);
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

sealed class FakeAudioPlaybackBackend : IAudioPlaybackBackend
{
    public event Action<string>? PlaybackFailed;
    public event Action? PreviewEnded;
    public List<string> Events { get; } = [];
    public int MusicStarts { get; private set; }
    public int MusicStops { get; private set; }
    public int MusicPauses { get; private set; }
    public int MusicResumes { get; private set; }
    public int CueStarts { get; private set; }
    public int MusicVolume { get; private set; }
    public bool MusicLoopRequested { get; private set; }
    public bool CueIsPlaying { get; private set; }
    public string? MusicSource { get; private set; }
    private bool _musicPaused;

    public void PlayMusicLoop(string source, int volume)
    {
        if (source == "missing.mp3")
        {
            PlaybackFailed?.Invoke("Audio unavailable.");
            return;
        }
        MusicStarts++;
        MusicSource = source;
        MusicVolume = volume;
        MusicLoopRequested = true;
        Events.Add("music-start");
    }

    public void SetMusicVolume(int volume) => MusicVolume = volume;
    public void StopMusic() { MusicStops++; Events.Add("music-stop"); }
    public void PauseMusic() { MusicPauses++; _musicPaused = true; }
    public void ResumeMusic() { if (!_musicPaused) return; MusicResumes++; _musicPaused = false; }
    public void PlayCue(string source, int volume) { CueStarts++; CueIsPlaying = true; Events.Add("cue"); }

    public Task PlayCueToCompletionAsync(string source, int volume, TimeSpan maximumWait)
    {
        CueStarts++;
        CueIsPlaying = true;
        Events.Add("cue-wait-start");
        Events.Add("cue-wait-end");
        CueIsPlaying = false;
        return Task.CompletedTask;
    }

    public void StopCue() { CueIsPlaying = false; Events.Add("cue-stop"); }
    public void PreviewMusic(string source, int volume) { PauseMusic(); Events.Add("preview-music"); }
    public void PreviewCue(string source, int volume) => Events.Add("preview-cue");
    public void SetPreviewVolume(int volume) { }
    public void StopPreview() { Events.Add("preview-stop"); ResumeMusic(); PreviewEnded?.Invoke(); }
    public void Dispose() { }
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

