using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SleepTimer.Core;

namespace SleepTimer.Desktop;

/// <summary>WPF media players for the timer loop, end cue, and user-initiated previews.</summary>
internal sealed class AudioPlaybackBackend : IAudioPlaybackBackend
{
    private static readonly TimeSpan MaximumCueDuration = TimeSpan.FromSeconds(5);
    private readonly MediaPlayer _musicPlayer = new();
    private readonly MediaPlayer _cuePlayer = new();
    private readonly MediaPlayer _previewPlayer = new();
    private DispatcherTimer? _cueTimeout;
    private TaskCompletionSource? _cueCompletion;
    private bool _musicLoop;
    private bool _musicPausedForPreview;
    private bool _previewMusicLoop;
    private bool _previewRunning;
    private bool _cueRunning;
    private bool _disposed;

    public event Action<string>? PlaybackFailed;
    public event Action? PreviewEnded;

    public AudioPlaybackBackend()
    {
        _musicPlayer.MediaOpened += (_, _) =>
        {
            if (_musicLoop) _musicPlayer.Play();
        };
        _musicPlayer.MediaEnded += (_, _) =>
        {
            if (!_musicLoop) return;
            _musicPlayer.Position = TimeSpan.Zero;
            _musicPlayer.Play();
        };
        _musicPlayer.MediaFailed += (_, _) =>
        {
            _musicLoop = false;
            ReportPlaybackFailure();
        };

        _cuePlayer.MediaOpened += (_, _) =>
        {
            if (_cueRunning) _cuePlayer.Play();
        };
        _cuePlayer.MediaEnded += (_, _) => CompleteCue(stopPlayer: true);
        _cuePlayer.MediaFailed += (_, _) =>
        {
            ReportPlaybackFailure();
            CompleteCue(stopPlayer: true);
        };

        _previewPlayer.MediaOpened += (_, _) =>
        {
            if (_previewRunning) _previewPlayer.Play();
        };
        _previewPlayer.MediaEnded += (_, _) =>
        {
            if (_previewMusicLoop)
            {
                _previewPlayer.Position = TimeSpan.Zero;
                _previewPlayer.Play();
                return;
            }
            FinishPreview();
        };
        _previewPlayer.MediaFailed += (_, _) =>
        {
            ReportPlaybackFailure();
            FinishPreview();
        };
    }

    public void PlayMusicLoop(string source, int volume)
    {
        ThrowIfDisposed();
        StopMusic();
        if (!TryGetUri(source, out var uri)) return;
        _musicPlayer.Volume = ToVolume(volume);
        _musicLoop = true;
        Open(_musicPlayer, uri);
    }

    public void SetMusicVolume(int volume)
    {
        ThrowIfDisposed();
        _musicPlayer.Volume = ToVolume(volume);
    }

    public void StopMusic()
    {
        if (_disposed) return;
        _musicLoop = false;
        _musicPausedForPreview = false;
        _musicPlayer.Stop();
        _musicPlayer.Close();
    }

    public void PauseMusic()
    {
        ThrowIfDisposed();
        if (!_musicLoop || _musicPlayer.Source is null) return;
        _musicPausedForPreview = true;
        _musicPlayer.Pause();
    }

    public void ResumeMusic()
    {
        ThrowIfDisposed();
        if (!_musicLoop || !_musicPausedForPreview) return;
        _musicPausedForPreview = false;
        _musicPlayer.Play();
    }

    public void PlayCue(string source, int volume)
    {
        ThrowIfDisposed();
        StopPreview();
        _ = StartCue(source, volume, MaximumCueDuration);
    }

    public Task PlayCueToCompletionAsync(string source, int volume, TimeSpan maximumWait)
    {
        ThrowIfDisposed();
        StopPreview();
        return StartCue(source, volume, maximumWait);
    }

    public void StopCue()
    {
        if (_disposed) return;
        CompleteCue(stopPlayer: true);
    }

    public void PreviewMusic(string source, int volume)
    {
        ThrowIfDisposed();
        StopPreview();
        if (!TryGetUri(source, out var uri))
        {
            PreviewEnded?.Invoke();
            return;
        }
        PauseMusic();
        _previewMusicLoop = true;
        _previewRunning = true;
        _previewPlayer.Volume = ToVolume(volume);
        Open(_previewPlayer, uri);
    }

    public void PreviewCue(string source, int volume)
    {
        ThrowIfDisposed();
        StopPreview();
        if (!TryGetUri(source, out var uri))
        {
            PreviewEnded?.Invoke();
            return;
        }
        _previewMusicLoop = false;
        _previewRunning = true;
        _previewPlayer.Volume = ToVolume(volume);
        Open(_previewPlayer, uri);
    }

    public void SetPreviewVolume(int volume)
    {
        ThrowIfDisposed();
        _previewPlayer.Volume = ToVolume(volume);
    }

    public void StopPreview()
    {
        if (!_previewRunning && !_musicPausedForPreview) return;
        FinishPreview();
    }

    private Task StartCue(string source, int volume, TimeSpan maximumWait)
    {
        StopCue();
        if (!TryGetUri(source, out var uri)) return Task.CompletedTask;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _cueCompletion = completion;
        _cueRunning = true;
        _cuePlayer.Volume = ToVolume(volume);
        _cueTimeout = new DispatcherTimer
        {
            Interval = maximumWait <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : maximumWait
        };
        _cueTimeout.Tick += CueTimeout_Tick;
        _cueTimeout.Start();
        Open(_cuePlayer, uri);
        return completion.Task;
    }

    private void CueTimeout_Tick(object? sender, EventArgs e) => CompleteCue(stopPlayer: true);

    private void CompleteCue(bool stopPlayer)
    {
        _cueTimeout?.Stop();
        if (_cueTimeout is not null) _cueTimeout.Tick -= CueTimeout_Tick;
        _cueTimeout = null;
        _cueRunning = false;
        if (stopPlayer) _cuePlayer.Stop();
        _cuePlayer.Close();
        var completion = _cueCompletion;
        _cueCompletion = null;
        completion?.TrySetResult();
    }

    private void FinishPreview()
    {
        _previewRunning = false;
        _previewMusicLoop = false;
        _previewPlayer.Stop();
        _previewPlayer.Close();
        if (_musicPausedForPreview) ResumeMusic();
        PreviewEnded?.Invoke();
    }

    private bool TryGetUri(string source, out Uri uri)
    {
        try
        {
            var fullPath = Path.GetFullPath(source);
            if (!File.Exists(fullPath))
            {
                uri = null!;
                ReportPlaybackFailure();
                return false;
            }
            uri = new Uri(fullPath, UriKind.Absolute);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            uri = null!;
            ReportPlaybackFailure();
            return false;
        }
    }

    private void Open(MediaPlayer player, Uri uri)
    {
        try { player.Open(uri); }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            ReportPlaybackFailure();
            if (ReferenceEquals(player, _musicPlayer)) _musicLoop = false;
            if (ReferenceEquals(player, _cuePlayer)) CompleteCue(stopPlayer: true);
            if (ReferenceEquals(player, _previewPlayer)) FinishPreview();
        }
    }

    private void ReportPlaybackFailure() => PlaybackFailed?.Invoke(
        "The selected audio could not be played. Check that the file exists and uses a supported format; the timer will continue.");

    private static double ToVolume(int volume) => Math.Clamp(volume, 0, 100) / 100.0;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        StopPreview();
        StopMusic();
        StopCue();
        _musicPlayer.Close();
        _cuePlayer.Close();
        _previewPlayer.Close();
        _disposed = true;
    }
}
