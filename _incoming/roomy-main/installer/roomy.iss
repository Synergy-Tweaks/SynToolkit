; Roomy installer (Inno Setup 6). Per-user install, no admin prompt.
#ifndef AppVersion
  #define AppVersion "0.1.5"
#endif

[Setup]
AppId={{6B0F3B2E-6C1A-4C7E-9E3B-52A1F0D7C6A4}
AppName=Roomy
AppVersion={#AppVersion}
AppPublisher=krezio
AppPublisherURL=https://roomy.krezio.xyz
AppSupportURL=https://github.com/krezio/roomy/issues
DefaultDirName={localappdata}\Programs\Roomy
DefaultGroupName=Roomy
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Roomy-Setup
SetupIconFile=..\src\Roomy\Assets\roomy.ico
UninstallDisplayIcon={app}\Roomy.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\dist\publish\Roomy.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Roomy"; Filename: "{app}\Roomy.exe"
Name: "{autodesktop}\Roomy"; Filename: "{app}\Roomy.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Roomy.exe"; Description: "Launch Roomy"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Roomy.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RoomyBackground"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Roomy"; Flags: dontcreatekey uninsdeletevalue
