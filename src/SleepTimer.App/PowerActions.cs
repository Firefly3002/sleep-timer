using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SleepTimer.Core;

namespace SleepTimer.Desktop;

internal static class PowerActions
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;

    public static string Execute(PowerActionRequest request)
    {
        switch (request.Action)
        {
            case PowerAction.Sleep:
                EnterSleep();
                return "Sleep was requested.";
            case PowerAction.ShutDown:
                ShutDown(restart: false);
                return "Shut down was requested.";
            case PowerAction.Restart:
                ShutDown(restart: true);
                return "Restart was requested.";
            case PowerAction.Lock:
                LockWorkStation();
                return "Windows was locked.";
            case PowerAction.CloseApp:
                return CloseApplication(request.CloseAppProcessName);
            case PowerAction.RunProgram:
                return RunProgram(request.CustomProgramPath, request.CustomProgramArguments);
            default:
                throw new InvalidOperationException("The selected power action is not supported.");
        }
    }

    private static void EnterSleep()
    {
        EnableShutdownPrivilege();
        if (!SetSuspendState(false, false, false))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enter sleep. Check your power settings or system policy, then try again.");
    }

    private static void ShutDown(bool restart)
    {
        var shutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = shutdownPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { restart ? "/r" : "/s", "/t", "0" }
        }) ?? throw new InvalidOperationException("Windows did not start the shutdown request.");

        if (!process.WaitForExit(5000)) return;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Windows rejected the {(restart ? "restart" : "shutdown")} request (exit code {process.ExitCode}).");
    }

    private static void LockWorkStation()
    {
        if (!RequestLockWorkStation())
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not lock this workstation.");
    }

    private static string CloseApplication(string? configuredProcessName)
    {
        var processName = Path.GetFileNameWithoutExtension(configuredProcessName?.Trim());
        if (string.IsNullOrWhiteSpace(processName))
            throw new InvalidOperationException("Enter the app's process name in Timer settings.");

        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
            throw new InvalidOperationException($"No running app named '{processName}' was found.");

        var closeRequested = false;
        try
        {
            foreach (var process in processes)
            {
                if (process.MainWindowHandle == IntPtr.Zero || !process.CloseMainWindow()) continue;
                closeRequested = true;
                if (!process.WaitForExit(5000))
                    throw new InvalidOperationException($"'{processName}' did not close within five seconds. It was not force-closed, so you can save your work and close it yourself.");
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        if (!closeRequested)
            throw new InvalidOperationException($"'{processName}' has no closable app window. It was not force-closed.");
        return $"Asked '{processName}' to close gracefully.";
    }

    private static string RunProgram(string? configuredPath, string? arguments)
    {
        var programPath = configuredPath?.Trim();
        if (string.IsNullOrWhiteSpace(programPath) || !File.Exists(programPath))
            throw new InvalidOperationException("Choose an existing program file in Timer settings.");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = programPath,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = Path.GetDirectoryName(programPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Windows did not start the selected program.");

        return $"Started {Path.GetFileName(programPath)}.";
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not prepare Windows sleep permission.");

        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not locate the Windows sleep permission.");

            var privileges = new TokenPrivileges
            {
                Count = 1,
                Privilege = new LuidAndAttributes { Luid = luid, Attributes = SePrivilegeEnabled }
            };
            Marshal.SetLastPInvokeError(0);
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enable Windows sleep permission.");
            if (Marshal.GetLastPInvokeError() == ErrorNotAllAssigned)
                throw new InvalidOperationException("Windows does not grant this account permission to request sleep.");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public LuidAndAttributes Privilege;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "LockWorkStation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RequestLockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}

