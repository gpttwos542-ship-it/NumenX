; NumenX installer. Build the app first (see build.ps1 / GitHub Actions) so that ..\publish exists.
#define AppName "NumenX"
#define AppVersion "0.1.0"

[Setup]
AppId={{6C1D3E9A-2B47-4F8A-9D05-3A7E5B21C4F8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NumenX
DefaultDirName={autopf}\NumenX
DefaultGroupName=NumenX
OutputDir=Output
OutputBaseFilename=NumenX-Setup
SetupIconFile=..\src\NumenX\Assets\numenx.ico
UninstallDisplayIcon={app}\NumenX.exe
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\NumenX"; Filename: "{app}\NumenX.exe"
Name: "{autodesktop}\NumenX"; Filename: "{app}\NumenX.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\NumenX.exe"; Description: "Launch NumenX"; Flags: nowait postinstall skipifsilent
