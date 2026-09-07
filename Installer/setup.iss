#define MyAppName "SynToolkit"
#ifndef MyAppVersion
#define MyAppVersion "1.7.0"
#endif
#define MyAppPublisher "Kwanteks"
#define MyAppURL "https://github.com/kwanteks/synergyos"
#define MyAppExeName "SynToolkit.exe"
#define AppChannel "Beta"

[Setup]
; AppId is the stable GUID used by Inno Setup for upgrade detection.
; NEVER change this GUID between versions — only change AppVersion.
AppId={{FE4CD776-C158-49D7-8B5F-F73D3D342E8C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
DefaultDirName={autopf}\Synergy\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\SynToolkit\Assets\Logo\SynToolkit.ico
WizardImageFile=Synergy\Assets\WizardImage.bmp
WizardSmallImageFile=Synergy\Assets\WizardSmallImage.bmp
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=SynToolkit-Setup-{#MyAppVersion}-{#AppChannel}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Restrict Restart Manager to SynToolkit itself. This prevents Setup from
; trying to close unrelated applications that merely loaded a published DLL.
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
; UsePreviousAppDir enables in-place upgrade: reuses the existing install path.
UsePreviousAppDir=yes
; UsePreviousGroup and UsePreviousTasks preserve user choices from prior install.
UsePreviousGroup=yes
UsePreviousTasks=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Dirs]
; User data directory: preserved across updates, upgrades, repairs, and even uninstalls.
; The uninsneveruninstall flag ensures user profiles are NEVER deleted.
Name: "{commonappdata}\Synergy\Profiles"; Permissions: users-modify; Flags: uninsneveruninstall

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "Synergy\*"; DestDir: "{commonappdata}\Synergy"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Remove installer-owned values without deleting saved tweak snapshots that may
; still be needed for a later verified revert after reinstall.
Root: HKLM; Subkey: "Software\SynToolkit"; ValueType: string; ValueName: "Channel"; ValueData: "{#AppChannel}"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\SynToolkit"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\SynToolkit"; ValueType: string; ValueName: "lang"; ValueData: "en_us"; Flags: createvalueifdoesntexist

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runascurrentuser

[InstallDelete]
; Retired files from earlier FE4 SynToolkit builds. These are exact
; installer-owned paths only; Profiles, snapshots, and unknown legacy folders
; are never removed.
Type: files; Name: "{app}\BcdSharp.dll"
Type: files; Name: "{commonappdata}\Synergy\PostInstall.cmd"
Type: files; Name: "{commonappdata}\Synergy\PostInstall\PrepareDirectories.cmd"
Type: files; Name: "{commonappdata}\Synergy\PostInstall\ValidatePayload.cmd"
Type: files; Name: "{commonappdata}\Synergy\PostInstall\WriteInstallState.cmd"
Type: dirifempty; Name: "{commonappdata}\Synergy\PostInstall"
Type: files; Name: "{commonappdata}\Synergy\Scripts\StaticIP\AutomaticallySetStaticIP.cmd"
Type: files; Name: "{commonappdata}\Synergy\Scripts\StaticIP\RevertStaticIP.cmd"
Type: dirifempty; Name: "{commonappdata}\Synergy\Scripts\StaticIP"
Type: files; Name: "{commonappdata}\Synergy\Scripts\Copilot\DisableMicrosoftCopilot.cmd"
Type: files; Name: "{commonappdata}\Synergy\Scripts\Copilot\EnableMicrosoftCopilot.cmd"
Type: dirifempty; Name: "{commonappdata}\Synergy\Scripts\Copilot"
Type: files; Name: "{commonappdata}\Synergy\Scripts\SuperFetch\DisableSuperFetch.cmd"
Type: files; Name: "{commonappdata}\Synergy\Scripts\SuperFetch\EnableSuperFetch.cmd"
Type: dirifempty; Name: "{commonappdata}\Synergy\Scripts\SuperFetch"
Type: files; Name: "{commonappdata}\Synergy\Scripts\Troubleshooting\Fix Errors 2502 and 2503.cmd"
Type: files; Name: "{commonappdata}\Synergy\Scripts\Troubleshooting\TroubleshootingNetwork\SynToolkitDefaults.cmd"
Type: files; Name: "{commonappdata}\Synergy\ConfigurationServices\FileSharing\disable.cmd"
Type: files; Name: "{commonappdata}\Synergy\ConfigurationServices\FileSharing\enable.cmd"
Type: dirifempty; Name: "{commonappdata}\Synergy\ConfigurationServices\FileSharing"

[UninstallDelete]
Type: files; Name: "{app}\assets\Tools\HWiNFO64.INI"
Type: dirifempty; Name: "{app}\assets\Tools"
Type: dirifempty; Name: "{app}\assets"
Type: files; Name: "{commonappdata}\Synergy\PostInstall.log"
Type: files; Name: "{commonappdata}\Synergy\InstallState.ini"
Type: files; Name: "{commonappdata}\Synergy\InstallState.ini.tmp"

[Code]
var
  InstalledVersion: String;
  InstallerVersion: String;
  UpgradeMode: Integer;  // 0=Fresh, 1=Upgrade, 2=Repair, 3=Downgrade

const
  MODE_FRESH = 0;
  MODE_UPGRADE = 1;
  MODE_REPAIR = 2;
  MODE_DOWNGRADE = 3;

function LegacySynToolkitInstalled(): Boolean;
var
  DisplayName: String;
  Publisher: String;
  DisplayVersion: String;
begin
  Result :=
    RegQueryStringValue(
      HKLM64,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{98A41650-0BAA-476E-A372-01B5FB0A76FA}_is1',
      'DisplayName',
      DisplayName) and
    RegQueryStringValue(
      HKLM64,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{98A41650-0BAA-476E-A372-01B5FB0A76FA}_is1',
      'Publisher',
      Publisher) and
    RegQueryStringValue(
      HKLM64,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{98A41650-0BAA-476E-A372-01B5FB0A76FA}_is1',
      'DisplayVersion',
      DisplayVersion) and
    (CompareText(Trim(DisplayName), 'Syntoolkit') = 0) and
    (CompareText(Trim(Publisher), 'Kwanteks') = 0) and
    (CompareText(Trim(DisplayVersion), '1.5.0') = 0);
end;

function GetInstalledVersion(): String;
var
  Version: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64, 'Software\SynToolkit', 'Version', Version) then
    Result := Trim(Version)
  else if RegQueryStringValue(
      HKLM64,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{FE4CD776-C158-49D7-8B5F-F73D3D342E8C}_is1',
      'DisplayVersion',
      Version) then
    Result := Trim(Version);
end;

function ParseVersionPart(const S: String; PartIndex: Integer): Integer;
var
  Parts: TArrayOfString;
  I, Start, PartCount: Integer;
begin
  Result := 0;
  if S = '' then exit;

  SetArrayLength(Parts, 4);
  for I := 0 to 3 do Parts[I] := '0';

  Start := 1;
  PartCount := 0;
  for I := 1 to Length(S) do
  begin
    if (S[I] = '.') or (I = Length(S)) then
    begin
      if I = Length(S) then
        Parts[PartCount] := Copy(S, Start, I - Start + 1)
      else
        Parts[PartCount] := Copy(S, Start, I - Start);
      PartCount := PartCount + 1;
      Start := I + 1;
      if PartCount >= 4 then break;
    end;
  end;

  if (PartIndex >= 0) and (PartIndex < 4) then
    Result := StrToIntDef(Parts[PartIndex], 0);
end;

function CompareVersions(const V1, V2: String): Integer;
var
  I, P1, P2: Integer;
begin
  Result := 0;
  for I := 0 to 3 do
  begin
    P1 := ParseVersionPart(V1, I);
    P2 := ParseVersionPart(V2, I);
    if P1 > P2 then
    begin
      Result := 1;
      exit;
    end
    else if P1 < P2 then
    begin
      Result := -1;
      exit;
    end;
  end;
end;

function DetermineUpgradeMode(): Integer;
var
  Comparison: Integer;
begin
  InstalledVersion := GetInstalledVersion();
  InstallerVersion := '{#MyAppVersion}';

  if InstalledVersion = '' then
  begin
    Result := MODE_FRESH;
    exit;
  end;

  Comparison := CompareVersions(InstallerVersion, InstalledVersion);
  if Comparison > 0 then
    Result := MODE_UPGRADE
  else if Comparison = 0 then
    Result := MODE_REPAIR
  else
    Result := MODE_DOWNGRADE;
end;

function InitializeSetup(): Boolean;
var
  Response: Integer;
begin
  Result := True;
  if not IsWin64 then
  begin
    MsgBox('{#MyAppName} requires a 64-bit version of Windows.', mbError, MB_OK);
    Result := False;
    exit;
  end;

  UpgradeMode := DetermineUpgradeMode();

  case UpgradeMode of
    MODE_UPGRADE:
      begin
        MsgBox(
          'Updating {#MyAppName}' + #13#10 + #13#10 +
          'Installed version: ' + InstalledVersion + #13#10 +
          'New version: ' + InstallerVersion + #13#10 + #13#10 +
          'Your settings and profiles will be preserved.',
          mbInformation, MB_OK);
      end;

    MODE_REPAIR:
      begin
        Response := MsgBox(
          '{#MyAppName} ' + InstallerVersion + ' is already installed.' + #13#10 + #13#10 +
          'Would you like to repair the installation?' + #13#10 +
          '(This will reinstall application files without removing your settings or profiles.)',
          mbConfirmation, MB_YESNO);
        if Response <> IDYES then
        begin
          Result := False;
          exit;
        end;
      end;

    MODE_DOWNGRADE:
      begin
        Response := MsgBox(
          'A newer version is already installed!' + #13#10 + #13#10 +
          'Installed version: ' + InstalledVersion + #13#10 +
          'This installer version: ' + InstallerVersion + #13#10 + #13#10 +
          'Downgrading is not recommended and may cause issues.' + #13#10 +
          'Are you sure you want to continue?',
          mbError, MB_YESNO);
        if Response <> IDYES then
        begin
          Result := False;
          exit;
        end;
      end;
  end;

  if LegacySynToolkitInstalled() then
  begin
    MsgBox(
      'An older Kwanteks Syntoolkit 1.5.0 registration was found.' + #13#10 + #13#10 +
      'This Setup will install SynToolkit side by side in its isolated Synergy folder. It will not delete, overwrite, or run the older product or its support files.' + #13#10 + #13#10 +
      'Original legacy profiles remain untouched. On first launch, SynToolkit will copy valid profile JSON into its isolated Synergy profile folder without applying it. Review the older product separately before uninstalling it, especially on a customized Windows or Playbook installation.',
      mbInformation,
      MB_OK);
  end;
end;

function WaitForSynToolkitToExit(Attempts: Integer): Boolean;
var
  Attempt: Integer;
begin
  for Attempt := 1 to Attempts do
  begin
    if (not CheckForMutexes('Global\SynToolkit-FE4CD776-C158-49D7-8B5F-F73D3D342E8C')) and
       (not CheckForMutexes('SynToolkit-FE4CD776-C158-49D7-8B5F-F73D3D342E8C')) then
    begin
      Result := True;
      exit;
    end;

    Sleep(100);
  end;

  Result := (not CheckForMutexes('Global\SynToolkit-FE4CD776-C158-49D7-8B5F-F73D3D342E8C')) and
            (not CheckForMutexes('SynToolkit-FE4CD776-C158-49D7-8B5F-F73D3D342E8C'));
end;

function ForceCloseInstalledSynToolkit(AppPath: String): Boolean;
var
  ResultCode: Integer;
  ScriptPath: String;
  CompletePath: String;
  ScriptBody: String;
  Attempt: Integer;
begin
  ScriptPath := ExpandConstant('{tmp}\SynToolkit-CloseExact.ps1');
  CompletePath := ExpandConstant('{tmp}\SynToolkit-CloseExact.done');
  DeleteFile(CompletePath);

  ScriptBody :=
    'param([Parameter(Mandatory=$true)][string]$TargetPath,[Parameter(Mandatory=$true)][string]$CompletePath)' + #13#10 +
    '$ErrorActionPreference = ''SilentlyContinue''' + #13#10 +
    '$target = [IO.Path]::GetFullPath($TargetPath)' + #13#10 +
    '$processes = @(Get-Process -Name ''SynToolkit'' -ErrorAction SilentlyContinue)' + #13#10 +
    'foreach ($process in $processes) {' + #13#10 +
    '  try {' + #13#10 +
    '    if ([IO.Path]::GetFullPath($process.Path) -ieq $target) {' + #13#10 +
    '      Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue' + #13#10 +
    '      Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue' + #13#10 +
    '    }' + #13#10 +
    '  } catch {}' + #13#10 +
    '}' + #13#10 +
    '[IO.File]::WriteAllText($CompletePath, ''done'')' + #13#10;

  if not SaveStringToFile(ScriptPath, ScriptBody, False) then
  begin
    Log('Unable to create the path-qualified SynToolkit shutdown helper.');
    Result := False;
    exit;
  end;

  Log('Forcing only the installed SynToolkit process to close.');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + AddQuotes(ScriptPath) +
    ' -TargetPath ' + AddQuotes(AppPath) + ' -CompletePath ' + AddQuotes(CompletePath),
    ExpandConstant('{tmp}'), SW_HIDE, ewNoWait, ResultCode) then
  begin
    Log('Unable to start the path-qualified SynToolkit shutdown helper.');
    Result := False;
    exit;
  end;

  for Attempt := 1 to 100 do
  begin
    if FileExists(CompletePath) then
      break;
    Sleep(100);
  end;

  Result := FileExists(CompletePath) and WaitForSynToolkitToExit(50);
  DeleteFile(CompletePath);
  DeleteFile(ScriptPath);
end;

function CloseRunningSynToolkit(): Boolean;
var
  ResultCode: Integer;
  AppPath: String;
begin
  if (not CheckForMutexes('Global\SynToolkit-FE4CD776-C158-49D7-8B5F-F73D3D342E8C')) and
     (not CheckForMutexes('SynToolkit-FE4CD776-C158-49D7-8B5F-F73D3D342E8C')) then
  begin
    Result := True;
    exit;
  end;

  AppPath := ExpandConstant('{app}\{#MyAppExeName}');
  if FileExists(AppPath) then
  begin
    Log('Requesting a graceful SynToolkit shutdown.');
    Exec(AppPath, '--shutdown-for-update', ExpandConstant('{app}'), SW_HIDE,
      ewNoWait, ResultCode);

    if WaitForSynToolkitToExit(100) then
    begin
      Result := True;
      exit;
    end;
  end;

  // Older builds may not understand --shutdown-for-update. The fallback is
  // path-qualified and never terminates similarly named apps or child tweaks.
  Result := ForceCloseInstalledSynToolkit(AppPath);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if CloseRunningSynToolkit() then
    Result := ''
  else
    Result := 'SynToolkit is still running. Close it in Task Manager, then click Retry.';
end;

procedure RequireInstalledFile(Path: String; var MissingFiles: String);
var
  InstalledSize: Int64;
begin
  if (not FileExists(Path)) or
     (not FileSize64(Path, InstalledSize)) or
     (InstalledSize <= 0) then
  begin
    if MissingFiles <> '' then
      MissingFiles := MissingFiles + #13#10;
    MissingFiles := MissingFiles + Path;
  end;
end;

procedure RunPostInstallPreparation();
var
  AppRoot: String;
  SynergyRoot: String;
  MissingFiles: String;
  StateText: String;
  LogText: String;
begin
  AppRoot := ExpandConstant('{app}');
  SynergyRoot := ExpandConstant('{commonappdata}\Synergy');
  MissingFiles := '';

  RequireInstalledFile(AppRoot + '\{#MyAppExeName}', MissingFiles);
  RequireInstalledFile(AppRoot + '\LICENSE.txt', MissingFiles);
  RequireInstalledFile(AppRoot + '\THIRD-PARTY-NOTICES.md', MissingFiles);
  RequireInstalledFile(AppRoot + '\assets\Fonts\OFL-Archivo.txt', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Assets\SynToolkit.ico', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Assets\Blank.ico', MissingFiles);

  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\ContextMenuTerminals\ContextMenuTerminals_0.reg', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\ContextMenuTerminals\ContextMenuTerminals_1.reg', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\ContextMenuTerminals\ContextMenuTerminals_2.reg', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\Mitigations\Mitigations_0.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\Mitigations\Mitigations_1.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\Mitigations\Mitigations_2.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\SafeMode\SafeMode_0.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\SafeMode\SafeMode_1.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\SafeMode\SafeMode_2.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\SafeMode\SafeMode_3.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\ShortcutIcon\ShortcutIcon_1.reg', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\ShortcutIcon\ShortcutIcon_2.reg', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\ConfigurationServices\ShortcutIcon\ShortcutIcon_3.reg', MissingFiles);

  RequireInstalledFile(SynergyRoot + '\Scripts\toggleDefender.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\vbsCurrentConfig.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\viewBootValues.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\NVidia\DisableNVIDIADisplayContainerLS.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\NVidia\EnableNVIDIADisplayContainerLS.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\NVidia\NVIDIADisplayContainerState.ps1', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\SecurityHealthTray\AddTray.reg', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\SecurityHealthTray\RemoveTray.reg', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\Troubleshooting\Repair Windows Installer.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\Troubleshooting\Repair Windows Components.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\Troubleshooting\Telemetry Components.cmd', MissingFiles);
  RequireInstalledFile(SynergyRoot + '\Scripts\Troubleshooting\TroubleshootingNetwork\WindowsDefaults.cmd', MissingFiles);

  if MissingFiles <> '' then
  begin
    Log('Installed support payload validation failed:' + #13#10 + MissingFiles);
    RaiseException(
      'SynToolkit support-file validation failed. The following installed files are missing:' + #13#10 + #13#10 + MissingFiles);
  end;

  StateText :=
    '[SynToolkit]' + #13#10 +
    'Product=SynToolkit' + #13#10 +
    'SupportRoot=Synergy' + #13#10 +
    'SchemaVersion=1' + #13#10 +
    'Version={#MyAppVersion}' + #13#10 +
    'ApplicationPath=' + AppRoot + #13#10 +
    'DataPath=' + SynergyRoot + #13#10 +
    'Completed=1' + #13#10;
  if not SaveStringToFile(SynergyRoot + '\InstallState.ini', StateText, False) then
    RaiseException('SynToolkit Setup could not write its installation state.');

  LogText :=
    'SynToolkit {#MyAppVersion} support payload validated by Setup.' + #13#10 +
    'No tweak, power plan, service, registry, Bluetooth, or Xbox action was executed.' + #13#10;
  if not SaveStringToFile(SynergyRoot + '\PostInstall.log', LogText, False) then
    RaiseException('SynToolkit Setup could not write its post-install validation log.');

  Log('Installed Synergy support payload validation completed without executing tweak scripts.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RunPostInstallPreparation();
end;

procedure CurPageChanged(CurPageID: Integer);
var
  PageTitle: String;
  PageDescription: String;
begin
  if CurPageID = wpWelcome then
  begin
    case UpgradeMode of
      MODE_UPGRADE:
        begin
          PageTitle := 'Update {#MyAppName}';
          PageDescription := 'Setup will update {#MyAppName} from ' + InstalledVersion + ' to ' + InstallerVersion + '.';
        end;
      MODE_REPAIR:
        begin
          PageTitle := 'Repair {#MyAppName}';
          PageDescription := 'Setup will repair {#MyAppName} ' + InstallerVersion + '.';
        end;
      MODE_DOWNGRADE:
        begin
          PageTitle := 'Downgrade {#MyAppName}';
          PageDescription := 'Setup will downgrade {#MyAppName} from ' + InstalledVersion + ' to ' + InstallerVersion + '.';
        end;
    else
      begin
        PageTitle := 'Install {#MyAppName}';
        PageDescription := 'Setup will install {#MyAppName} ' + InstallerVersion + ' on your computer.';
      end;
    end;
    WizardForm.WelcomeLabel1.Caption := PageTitle;
    WizardForm.WelcomeLabel2.Caption := PageDescription + #13#10 + #13#10 +
      'Click Next to continue, or Cancel to exit Setup.';
  end;

  if CurPageID = wpFinished then
  begin
    case UpgradeMode of
      MODE_UPGRADE:
        WizardForm.FinishedLabel.Caption :=
          '{#MyAppName} has been successfully updated to version ' + InstallerVersion + '.' + #13#10 + #13#10 +
          'Your settings and profiles have been preserved.';
      MODE_REPAIR:
        WizardForm.FinishedLabel.Caption :=
          '{#MyAppName} ' + InstallerVersion + ' has been successfully repaired.' + #13#10 + #13#10 +
          'Your settings and profiles have been preserved.';
      MODE_DOWNGRADE:
        WizardForm.FinishedLabel.Caption :=
          '{#MyAppName} has been downgraded to version ' + InstallerVersion + '.' + #13#10 + #13#10 +
          'Your settings and profiles have been preserved.';
    else
      WizardForm.FinishedLabel.Caption :=
        '{#MyAppName} ' + InstallerVersion + ' has been successfully installed.' + #13#10 + #13#10 +
        'Click Finish to exit Setup.';
    end;
  end;
end;

function InitializeUninstall(): Boolean;
var
  Response: Integer;
begin
  Result := CloseRunningSynToolkit();
  while not Result do
  begin
    Response := MsgBox(
      'SynToolkit is still running and its files cannot be removed safely.' + #13#10 + #13#10 +
      'Use Exit SynToolkit from the system-tray menu or close it in Task Manager, then click Retry.',
      mbError, MB_RETRYCANCEL);
    if Response <> IDRETRY then
      exit;
    Result := CloseRunningSynToolkit();
  end;
end;
