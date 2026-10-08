using System.Diagnostics;

namespace SleepTimer.Core;

public enum TimerPhase
{
    Idle,
    Running,
    Warning
}

public sealed record TimerSnapshot(TimerPhase Phase, TimeSpan Remaining, PowerAction Action, TimeSpan PhaseDuration)
{
    public PowerActionRequest ActionRequest { get; init; } = new(PowerAction.Sleep);
    public TimeSpan SnoozeDuration { get; init; }
    public bool WarningEnabled { get; init; }
}

public interface IMonotonicClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long start, long end);
}

public sealed class SystemMonotonicClock : IMonotonicClock
{
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public TimeSpan GetElapsedTime(long start, long end) => Stopwatch.GetElapsedTime(start, end);
}

public interface IOneShotScheduler
{
    IDisposable Schedule(TimeSpan delay, Action callback);
}

public sealed class ThreadPoolOneShotScheduler : IOneShotScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);
        return timer;
    }
}

/// <summary>Runs the timer and warning stages without depending on the UI framework.</summary>
public sealed class TimerEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly IMonotonicClock _clock;
    private readonly IOneShotScheduler _scheduler;
    private IDisposable? _scheduled;
    private long _version;
    private long _phaseStartedAt;
    private TimeSpan _phaseDuration;
    private TimeSpan _warningDuration;
    private TimeSpan _snoozeDuration;
    private TimerPhase _phase;
    private PowerActionRequest _action = new(PowerAction.Sleep);
    private bool _disposed;

    public event Action<TimerSnapshot>? SnapshotChanged;
    public event Action<PowerActionRequest>? PowerActionRequested;

    public TimerEngine(IMonotonicClock clock, IOneShotScheduler scheduler)
    {
        _clock = clock;
        _scheduler = scheduler;
    }

    public TimerSnapshot GetSnapshot()
    {
        lock (_gate)
            return SnapshotUnsafe();
    }

    public void Start(TimeSpan duration, TimeSpan warningDuration, TimeSpan snoozeDuration, PowerAction action)
        => Start(duration, warningDuration, snoozeDuration, new PowerActionRequest(action));

    public void Start(TimeSpan duration, TimeSpan warningDuration, TimeSpan snoozeDuration, PowerActionRequest action)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (warningDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(warningDuration));
        if (snoozeDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(snoozeDuration));
        ArgumentNullException.ThrowIfNull(action);
        if (!Enum.IsDefined(action.Action)) throw new ArgumentOutOfRangeException(nameof(action));

        TimerSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            _warningDuration = warningDuration;
            _snoozeDuration = snoozeDuration;
            _action = action;
            BeginPhaseUnsafe(TimerPhase.Running, duration);
            snapshot = SnapshotUnsafe();
        }
        SnapshotChanged?.Invoke(snapshot);
    }

    public void Snooze()
    {
        TimerSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_phase != TimerPhase.Warning) return;
            BeginPhaseUnsafe(TimerPhase.Running, _snoozeDuration);
            snapshot = SnapshotUnsafe();
        }
        SnapshotChanged?.Invoke(snapshot);
    }

    /// <summary>Adds time to the active phase, or starts a new running interval from a warning.</summary>
    public bool AddTime(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        TimerSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_phase == TimerPhase.Idle) return false;

            if (_phase == TimerPhase.Warning)
            {
                // A quick timer chosen during the final check-in gives the user the selected
                // amount of normal countdown time, followed by the configured warning again.
                BeginPhaseUnsafe(TimerPhase.Running, duration);
            }
            else
            {
                // Keep the existing start time so the progress display remains continuous;
                // only move the scheduled end later by the requested amount.
                var remaining = SnapshotUnsafe().Remaining;
                var phase = _phase;
                _version++;
                var version = _version;
                _scheduled?.Dispose();
                _phaseDuration += duration;
                _scheduled = _scheduler.Schedule(remaining + duration, () => OnElapsed(version, phase));
            }

            snapshot = SnapshotUnsafe();
        }
        SnapshotChanged?.Invoke(snapshot);
        return true;
    }

    public void Cancel()
    {
        TimerSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            _version++;
            _scheduled?.Dispose();
            _scheduled = null;
            _phase = TimerPhase.Idle;
            _phaseDuration = TimeSpan.Zero;
            snapshot = SnapshotUnsafe();
        }
        SnapshotChanged?.Invoke(snapshot);
    }

    private void BeginPhaseUnsafe(TimerPhase phase, TimeSpan duration)
    {
        _version++;
        var version = _version;
        _scheduled?.Dispose();
        _phase = phase;
        _phaseDuration = duration;
        _phaseStartedAt = _clock.GetTimestamp();
        _scheduled = _scheduler.Schedule(duration, () => OnElapsed(version, phase));
    }

    private void OnElapsed(long version, TimerPhase expectedPhase)
    {
        TimerSnapshot? snapshot = null;
        PowerActionRequest? action = null;
        lock (_gate)
        {
            if (_disposed || version != _version || _phase != expectedPhase) return;
            _scheduled?.Dispose();
            _scheduled = null;

            if (expectedPhase == TimerPhase.Running)
            {
                if (_warningDuration == TimeSpan.Zero)
                {
                    _version++;
                    _phase = TimerPhase.Idle;
                    _phaseDuration = TimeSpan.Zero;
                    snapshot = SnapshotUnsafe();
                    action = _action;
                }
                else
                {
                    BeginPhaseUnsafe(TimerPhase.Warning, _warningDuration);
                    snapshot = SnapshotUnsafe();
                }
            }
            else if (expectedPhase == TimerPhase.Warning)
            {
                _version++;
                _phase = TimerPhase.Idle;
                _phaseDuration = TimeSpan.Zero;
                snapshot = SnapshotUnsafe();
                action = _action;
            }
        }

        if (snapshot is not null) SnapshotChanged?.Invoke(snapshot);
        if (action is not null) PowerActionRequested?.Invoke(action);
    }

    private TimerSnapshot SnapshotUnsafe()
    {
        var remaining = _phase == TimerPhase.Idle
            ? TimeSpan.Zero
            : _phaseDuration - _clock.GetElapsedTime(_phaseStartedAt, _clock.GetTimestamp());
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return new TimerSnapshot(_phase, remaining, _action.Action, _phaseDuration)
        {
            ActionRequest = _action,
            SnoozeDuration = _snoozeDuration,
            WarningEnabled = _warningDuration > TimeSpan.Zero
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _version++;
            _scheduled?.Dispose();
            _scheduled = null;
            _phase = TimerPhase.Idle;
            _phaseDuration = TimeSpan.Zero;
        }
    }
}

