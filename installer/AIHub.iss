; Build through Build-Installer.ps1. No PowerShell runs on the user's machine.
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifdef InstallerTest
  #define HubAppId "AIHub.InstallerTest"
  #define HubAppName "AI Hub Installer Test"
  #define LegacyKey "AIHub.InstallerTest.Legacy"
#else
  #define HubAppId "AIHub"
  #define HubAppName "AI Hub"
  #define LegacyKey "AIHub"
#endif

[Setup]
AppId={#HubAppId}
AppName={#HubAppName}
AppVersion={#AppVersion}
AppVerName={#HubAppName} v{#AppVersion}
AppPublisher=wernerong
AppPublisherURL=https://github.com/wernerong/ai-hub
AppSupportURL=https://github.com/wernerong/ai-hub/issues
AppUpdatesURL=https://github.com/wernerong/ai-hub/releases/latest
DefaultDirName={localappdata}\Programs\{#HubAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Local\{#HubAppId}.Desktop
CloseApplications=no
RestartApplications=no
OutputDir={#BuildOutput}
OutputBaseFilename={#SetupName}
SetupIconFile=..\Assets\AIHub.ico
UninstallDisplayIcon={app}\AIHub.exe
UninstallDisplayName={#HubAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#HubAppName} Setup

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#AppSource}\AIHub.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\validation.txt"; DestDir: "{app}"; Flags: ignoreversion
; Retain the flat package contract used by installed auto-updaters.
Source: "{#AppSource}\Install-AIHub.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\Uninstall-AIHub.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#HubAppName}"; Filename: "{app}\AIHub.exe"; WorkingDir: "{app}"; Comment: "AI apps and usage in one workspace"
Name: "{autodesktop}\{#HubAppName}"; Filename: "{app}\AIHub.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\AIHub.exe"; Description: "Open {#HubAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  LegacyUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#LegacyKey}';
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure CurStepChanged(CurStep: TSetupStep);
var
  Location: String;
begin
  if CurStep = ssPostInstall then
  begin
    // Remove the old script-based registration only after files are installed,
    // and only when it belongs to this exact installation directory.
    if RegQueryStringValue(HKCU, LegacyUninstallKey, 'InstallLocation', Location) and
       (CompareText(AddBackslash(Location), AddBackslash(ExpandConstant('{app}'))) = 0) then
      RegDeleteKeyIncludingSubkeys(HKCU, LegacyUninstallKey);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Preferences and provider apps are deliberately outside the uninstall log.
    if RegQueryStringValue(HKCU, StartupKey, '{#HubAppId}', Command) and
       (CompareText(Command, '"' + ExpandConstant('{app}\AIHub.exe') + '"') = 0) then
      RegDeleteValue(HKCU, StartupKey, '{#HubAppId}');
  end;
end;
