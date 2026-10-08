namespace SleepTimer.Core;

/// <summary>Platform audio operations used by the timer audio lifecycle.</summary>
public interface IAudioPlaybackBackend : IDisposable
{
    event Action<string>? PlaybackFailed;
    event Action? PreviewEnded;

    void PlayMusicLoop(string source, int volume);
    void SetMusicVolume(int volume);
    void StopMusic();
    void PauseMusic();
    void ResumeMusic();
    void PlayCue(string source, int volume);
    Task PlayCueToCompletionAsync(string source, int volume, TimeSpan maximumWait);
    void StopCue();
    void PreviewMusic(string source, int volume);
    void PreviewCue(string source, int volume);
    void SetPreviewVolume(int volume);
    void StopPreview();
}

/// <summary>Coordinates audio with timer transitions while leaving platform playback injectable.</summary>
public sealed class TimerAudioCoordinator : IDisposable
{
    private readonly IAudioPlaybackBackend _backend;
    private AppSettings _settings = new();
    private string? _musicSource;
    private string? _cueSource;
    private TimerPhase _lastPhase = TimerPhase.Idle;
    private bool _timerActive;
    private bool _disposed;
    private AudioPreviewKind _previewKind;

    public event Action<string>? PlaybackFailed;
    public event Action? PreviewStateChanged;

    public bool IsPreviewingMusic => _previewKind == AudioPreviewKind.Music;
    public bool IsPreviewingCue => _previewKind == AudioPreviewKind.Cue;

    public TimerAudioCoordinator(IAudioPlaybackBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _backend.PlaybackFailed += ForwardPlaybackFailure;
        _backend.PreviewEnded += OnBackendPreviewEnded;
    }

    public void StartTimer(AppSettings settings, string? musicSource, string? cueSource)
    {
        ThrowIfDisposed();
        _settings = settings.Clone();
        _musicSource = musicSource;
        _cueSource = cueSource;
        _timerActive = true;
        _lastPhase = TimerPhase.Running;
        StopPreview();
        StartOrStopMusic(restart: true);
    }

    public void ObserveSnapshot(TimerSnapshot snapshot)
    {
        ThrowIfDisposed();
        var warningOpened = _timerActive && _lastPhase == TimerPhase.Running && snapshot.Phase == TimerPhase.Warning;
        _lastPhase = snapshot.Phase;
        if (warningOpened && _settings.EndSoundEnabled && _cueSource is not null)
            TryPlayback(() => _backend.PlayCue(_cueSource, _settings.EndSoundVolume));
    }

    public void ApplySettings(AppSettings settings, string? musicSource, string? cueSource, TimerSnapshot currentSnapshot)
    {
        ThrowIfDisposed();
        var oldSettings = _settings;
        var oldMusicSource = _musicSource;
        var oldCueSource = _cueSource;
        var musicSelectionChanged = oldMusicSource != musicSource
            || oldSettings.SleepMusicEnabled != settings.SleepMusicEnabled;
        var cueSelectionChanged = oldCueSource != cueSource
            || oldSettings.EndSoundEnabled != settings.EndSoundEnabled;
        if ((IsPreviewingMusic && musicSelectionChanged) || (IsPreviewingCue && cueSelectionChanged))
            StopPreview();
        else if (IsPreviewingMusic && oldSettings.SleepMusicVolume != settings.SleepMusicVolume)
            TryPlayback(() => _backend.SetPreviewVolume(settings.SleepMusicVolume));
        else if (IsPreviewingCue && oldSettings.EndSoundVolume != settings.EndSoundVolume)
            TryPlayback(() => _backend.SetPreviewVolume(settings.EndSoundVolume));

        _settings = settings.Clone();
        _musicSource = musicSource;
        _cueSource = cueSource;

        if (_timerActive && currentSnapshot.Phase != TimerPhase.Idle)
        {
            var restartMusic = oldMusicSource != _musicSource
                || oldSettings.SleepMusicEnabled != _settings.SleepMusicEnabled;
            StartOrStopMusic(restartMusic);
            if (!_settings.EndSoundEnabled) TryPlayback(_backend.StopCue);
        }
    }

    public void PreviewMusic(string source, int volume)
    {
        ThrowIfDisposed();
        StopPreview();
        _previewKind = AudioPreviewKind.Music;
        PreviewStateChanged?.Invoke();
        if (!TryPlayback(() => _backend.PreviewMusic(source, Math.Clamp(volume, 0, 100)))
            && _previewKind != AudioPreviewKind.None) OnBackendPreviewEnded();
    }

    public void PreviewCue(string source, int volume)
    {
        ThrowIfDisposed();
        StopPreview();
        _previewKind = AudioPreviewKind.Cue;
        PreviewStateChanged?.Invoke();
        if (!TryPlayback(() => _backend.PreviewCue(source, Math.Clamp(volume, 0, 100)))
            && _previewKind != AudioPreviewKind.None) OnBackendPreviewEnded();
    }

    public void SetPreviewVolume(int volume)
    {
        ThrowIfDisposed();
        TryPlayback(() => _backend.SetPreviewVolume(Math.Clamp(volume, 0, 100)));
    }

    public void StopPreview()
    {
        if (_previewKind == AudioPreviewKind.None) return;
        _previewKind = AudioPreviewKind.None;
        TryPlayback(_backend.StopPreview);
        PreviewStateChanged?.Invoke();
    }

    public void CancelTimer()
    {
        if (_disposed) return;
        _timerActive = false;
        _lastPhase = TimerPhase.Idle;
        StopAllAudio();
    }

    /// <summary>Waits for a no-warning end cue before the action, then releases all playback.</summary>
    public async Task PrepareForPowerActionAsync(bool warningWasEnabled)
    {
        ThrowIfDisposed();
        StopPreview();
        if (!warningWasEnabled && _settings.EndSoundEnabled && _cueSource is not null)
        {
            try
            {
                await _backend.PlayCueToCompletionAsync(_cueSource, _settings.EndSoundVolume, TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                ForwardPlaybackFailure(exception.Message);
            }
        }

        _timerActive = false;
        _lastPhase = TimerPhase.Idle;
        StopAllAudio();
    }

    private void StartOrStopMusic(bool restart)
    {
        if (!_timerActive || !_settings.SleepMusicEnabled || _musicSource is null)
        {
            TryPlayback(_backend.StopMusic);
            return;
        }

        if (restart) TryPlayback(() => _backend.PlayMusicLoop(_musicSource, _settings.SleepMusicVolume));
        else TryPlayback(() => _backend.SetMusicVolume(_settings.SleepMusicVolume));
    }

    private void StopAllAudio()
    {
        StopPreview();
        TryPlayback(_backend.StopMusic);
        TryPlayback(_backend.StopCue);
    }

    private bool TryPlayback(Action operation)
    {
        try
        {
            operation();
            return true;
        }
        catch (Exception exception)
        {
            ForwardPlaybackFailure($"Audio could not be played. The timer will continue. {exception.Message}");
            return false;
        }
    }

    private void ForwardPlaybackFailure(string message) => PlaybackFailed?.Invoke(message);

    private void OnBackendPreviewEnded()
    {
        if (_previewKind == AudioPreviewKind.None) return;
        _previewKind = AudioPreviewKind.None;
        PreviewStateChanged?.Invoke();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        StopAllAudio();
        _backend.PlaybackFailed -= ForwardPlaybackFailure;
        _backend.PreviewEnded -= OnBackendPreviewEnded;
        TryPlayback(_backend.Dispose);
        _disposed = true;
    }

    private enum AudioPreviewKind
    {
        None,
        Music,
        Cue
    }
}
