# Sleep Timer for Windows

A small, local Windows 10/11 utility for starting a sleep or shutdown timer from a desktop shortcut.

## Use

- Double-click **Sleep Timer** on the desktop. It starts the saved timer immediately and shows the remaining time.
- The first run uses a two-hour timer, Sleep, a 60-second warning, a 15-minute snooze, and a full-screen warning.
- The home screen has one-click 30-minute, 1-hour, 2-hour, and 4-hour timers. These are one-time choices and ask before replacing an active timer.
- Open **Settings** to edit the saved duration and choose Sleep, Shut down, Restart, Lock, Close an app, or Run a custom program when time is up. Close an app offers a refreshed list of open apps with their window titles; enable manual entry to type a process name if the app isn’t listed. It sends a normal close request, waits up to 30 seconds for the app window to close, and never force-kills the app. A custom program uses a selected `.exe` and optional arguments, launched directly as your Windows account.
- In **Timer** settings, optionally open Sleep Timer when you sign in to Windows. The separate saved-timer checkbox decides whether opening the app also starts a countdown.
- In **Warning** settings, turn off **Show a warning before the selected action** to run the action as soon as the timer ends. When enabled, warning and snooze intervals remain configurable.
- Use **Preview warning** in Warning settings to see the current warning design and action text. Preview buttons, closing the preview, and Escape never affect a running timer or perform its action.
- In **Audio** settings, optionally play one of five built-in sleep tracks or choose a local MP3, WAV, WMA, or M4A file. Choose moonlit ambient, soft piano, gentle rain, night forest, or ocean waves. Music loops through the timer, warning, and snooze stages. Choose among six built-in end cues—Soft chime, Warm bell, Night bird, Moon sparkle, Stardust, or Dream portal—or browse for your own cue. A separately enabled cue plays when a warning opens (or at zero when warnings are off). Music and cue volume have independent controls and previews. Audio changes apply to an active timer when you save; a missing or unsupported local file is skipped with a notice and does not affect the timer action.
- Choose **Widget** in the custom title bar to show a tiny moon-backed countdown pill. Click anywhere on it to open Sleep Timer. Shift-click-drag to move it, resize from the corner, adjust opacity, and pin it above other windows. Settings can also show the widget when the app opens.
- During a timer, choose **Restart timer** to apply updated settings. Choose **Cancel timer** in the app, widget, warning, or tray menu to stop the pending power action.
- The custom title bar's close button hides the app to the notification area. Use the tray menu's **Exit** command to close the app; it asks before stopping an active timer.

Settings are stored at `%LOCALAPPDATA%\Sleep Timer\settings.json`. The timer runs only while the app is running.

The ambient, piano, and end cues are original procedurally synthesized audio. Regenerate only the end cues with `python tools\generate_sleep_audio.py --cues-only`, or regenerate all original audio with `python tools\generate_sleep_audio.py`. The rain, forest, and ocean loops are edited mixes based on the Pixabay recordings listed in `src\SleepTimer.App\Assets\Audio\AUDIO-CREDITS.md`. Regenerate the nature mixes with `python tools\mix_reference_audio.py` after placing the three source MP3s in the ignored `tools\audio-source` folder. The audio generator requires Python 3; full regeneration also requires FFmpeg.

## Build and validate

Build the solution with `dotnet build SleepTimer.sln -c Release`.

Run the simulated timer checks with `dotnet run --project tests\SleepTimer.SimulatedTests\SleepTimer.SimulatedTests.csproj`.

Publish the self-contained app and create/update the desktop shortcut with `pwsh -NoProfile -File tools\Publish.ps1`.

Send `dist\SleepTimerSetup.exe` to a friend; running it opens a Windows setup wizard. It installs for the current Windows user, creates desktop and Start menu shortcuts, and registers an uninstaller in Windows Settings. No .NET installation or administrator access is required. The installer does not start the timer after installation. Uninstalling keeps your saved settings so they are available if you reinstall later.

To rebuild the installer, install [Inno Setup 6 or 7](https://jrsoftware.org/isdl.php), then run `pwsh -NoProfile -File tools\Build-Installer.ps1`.

