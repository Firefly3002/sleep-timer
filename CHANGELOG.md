# Changelog

## 1.4.0 — 2026-10-09

### Added

- Weekly schedules with multiple entries per day, selected weekday repetition, and local start times.
- Per-schedule power actions, action details, and an option to skip directly to the warning.
- A larger weekly calendar with day and date headers and quick add buttons for each day.
- Selectable hour, minute, and AM/PM controls for schedule start times.

### Behavior

- Scheduled entries use the saved countdown duration and the existing warning and snooze settings.
- Schedules run while Sleep Timer is open or minimized to the tray. Missed times and occurrences that conflict with an active timer are skipped.
- Schedule settings are saved locally; older settings load with an empty schedule list.
