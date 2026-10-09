namespace SleepTimer.Core;

public static class WeeklyScheduleValidation
{
    public static string? Validate(IEnumerable<WeeklyScheduleEntry> schedules)
    {
        var occupiedTimes = new HashSet<(DayOfWeek Day, TimeOnly Time)>();
        foreach (var schedule in schedules.Where(schedule => schedule.IsEnabled))
        {
            if (schedule.DaysOfWeek.Count == 0)
                return "Choose at least one day for every enabled schedule.";

            if (schedule.ActionRequest.Action == PowerAction.CloseApp
                && string.IsNullOrWhiteSpace(schedule.ActionRequest.CloseAppProcessName))
                return "Choose or enter an app for every enabled Close an app schedule.";

            if (schedule.ActionRequest.Action == PowerAction.RunProgram
                && (string.IsNullOrWhiteSpace(schedule.ActionRequest.CustomProgramPath)
                    || !string.Equals(Path.GetExtension(schedule.ActionRequest.CustomProgramPath), ".exe", StringComparison.OrdinalIgnoreCase)))
                return "Choose a program file for every enabled Run a custom program schedule.";

            foreach (var day in schedule.DaysOfWeek)
                if (!occupiedTimes.Add((day, schedule.StartTime)))
                    return "Two enabled schedules cannot use the same time on the same day.";
        }

        return null;
    }
}

/// <summary>Matches weekly local-time rules once per occurrence; missed minutes are never replayed.</summary>
public sealed class WeeklyScheduleService
{
    private static readonly DayOfWeek[] CalendarDays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    private List<WeeklyScheduleEntry> _schedules = [];
    private readonly Dictionary<Guid, DateOnly> _lastTriggeredDate = [];
    private DateTime? _lastEvaluatedMinute;

    public void UpdateSchedules(IEnumerable<WeeklyScheduleEntry> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        _schedules = schedules.Select(schedule => schedule.Clone()).ToList();
        _lastEvaluatedMinute = null;
        var activeIds = _schedules.Select(schedule => schedule.Id).ToHashSet();
        foreach (var staleId in _lastTriggeredDate.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            _lastTriggeredDate.Remove(staleId);
    }

    /// <summary>
    /// Returns the first enabled rule matching this local clock minute. Any matching rule is marked as
    /// processed even when a timer is active, so a skipped occurrence cannot start later in the same minute.
    /// </summary>
    public WeeklyScheduleEntry? Evaluate(DateTime localTime, bool timerActive)
    {
        var minute = new DateTime(localTime.Year, localTime.Month, localTime.Day, localTime.Hour, localTime.Minute, 0);
        if (_lastEvaluatedMinute == minute) return null;
        _lastEvaluatedMinute = minute;

        var date = DateOnly.FromDateTime(minute);
        var matches = _schedules
            .Where(schedule => schedule.IsEnabled
                && schedule.DaysOfWeek.Contains(minute.DayOfWeek)
                && schedule.StartTime.Hour == minute.Hour
                && schedule.StartTime.Minute == minute.Minute
                && (!_lastTriggeredDate.TryGetValue(schedule.Id, out var lastDate) || lastDate != date))
            .ToList();

        foreach (var schedule in matches) _lastTriggeredDate[schedule.Id] = date;
        if (timerActive || matches.Count == 0) return null;
        return matches[0].Clone();
    }

    public static IReadOnlyList<DayOfWeek> Weekdays => CalendarDays;
}
