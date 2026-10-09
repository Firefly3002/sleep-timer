#define AppName "Bedtime Timer"
#define AppVersion "1.4.1"
#define AppPublisher "Sparkfly"
#define AppExecutable "SleepTimer.exe"

[Setup]
AppId={{752F4310-493D-4A60-9E11-331779B840D7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Sleep Timer
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\dist
OutputBaseFilename=BedtimeTimerSetup
SetupIconFile=..\src\SleepTimer.App\Assets\sleep-timer.ico
UninstallDisplayIcon={app}\Assets\sleep-timer.ico
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\dist\win-x64\SleepTimer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\win-x64\Assets\Audio\*"; DestDir: "{app}\Assets\Audio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\SleepTimer.App\Assets\sleep-timer.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\Bedtime Timer"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\sleep-timer.ico"
Name: "{group}\Uninstall Bedtime Timer"; Filename: "{uninstallexe}"; IconFilename: "{uninstallexe}"
Name: "{autodesktop}\Bedtime Timer"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\sleep-timer.ico"; Tasks: desktopicon
