#define AppName "Sleep Timer"
#define AppVersion "1.4.0"
#define AppPublisher "Sleep Timer"
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
OutputBaseFilename=SleepTimerSetup
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
Name: "{group}\Sleep Timer"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\sleep-timer.ico"
Name: "{group}\Uninstall Sleep Timer"; Filename: "{uninstallexe}"; IconFilename: "{uninstallexe}"
Name: "{autodesktop}\Sleep Timer"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\sleep-timer.ico"; Tasks: desktopicon
