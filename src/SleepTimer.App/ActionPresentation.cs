using System.IO;
using SleepTimer.Core;

namespace SleepTimer.Desktop;

internal static class ActionPresentation
{
    public static string Title(PowerAction action) => action switch
    {
        PowerAction.Sleep => "SLEEP",
        PowerAction.ShutDown => "SHUT DOWN",
        PowerAction.Restart => "RESTART",
        PowerAction.Lock => "LOCK",
        PowerAction.CloseApp => "CLOSE APP",
        PowerAction.RunProgram => "RUN PROGRAM",
        _ => "ACTION"
    };

    public static string TimerDescription(PowerAction action) => action switch
    {
        PowerAction.Sleep => "until your PC sleeps",
        PowerAction.ShutDown => "until your PC shuts down",
        PowerAction.Restart => "until your PC restarts",
        PowerAction.Lock => "until Windows locks",
        PowerAction.CloseApp => "until the selected app closes",
        PowerAction.RunProgram => "until the selected program starts",
        _ => "until the selected action runs"
    };

    public static string CountdownLabel(PowerAction action) => action switch
    {
        PowerAction.Sleep => "Sleep begins in",
        PowerAction.ShutDown => "Shut down begins in",
        PowerAction.Restart => "Restart begins in",
        PowerAction.Lock => "Lock begins in",
        PowerAction.CloseApp => "App close begins in",
        PowerAction.RunProgram => "Program starts in",
        _ => "Action begins in"
    };

    public static string WarningMessage(PowerActionRequest request) => request.Action switch
    {
        PowerAction.Sleep => "Your computer will go to sleep unless you choose what happens next.",
        PowerAction.ShutDown => "Your computer will shut down unless you cancel.",
        PowerAction.Restart => "Your computer will restart unless you cancel.",
        PowerAction.Lock => "Windows will lock unless you cancel.",
        PowerAction.CloseApp => $"Sleep Timer will ask {DisplayProcessName(request.CloseAppProcessName)} to close unless you cancel.",
        PowerAction.RunProgram => $"{DisplayProgramName(request.CustomProgramPath)} will start unless you cancel.",
        _ => "The selected action will run unless you cancel."
    };

    public static string WidgetTooltip(PowerAction action) => action switch
    {
        PowerAction.Sleep => "Time until the computer sleeps.",
        PowerAction.ShutDown => "Time until the computer shuts down.",
        PowerAction.Restart => "Time until the computer restarts.",
        PowerAction.Lock => "Time until Windows locks.",
        PowerAction.CloseApp => "Time until the selected app is asked to close.",
        PowerAction.RunProgram => "Time until the selected program starts.",
        _ => "Time until the selected action runs."
    };

    private static string DisplayProcessName(string? processName)
    {
        var value = Path.GetFileNameWithoutExtension(processName?.Trim());
        return string.IsNullOrWhiteSpace(value) ? "the selected app" : $"the {value} app";
    }

    private static string DisplayProgramName(string? programPath)
    {
        var value = Path.GetFileName(programPath?.Trim());
        return string.IsNullOrWhiteSpace(value) ? "The selected program" : value;
    }
}
