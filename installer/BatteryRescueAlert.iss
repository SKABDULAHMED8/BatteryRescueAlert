; Battery Rescue Alert - Inno Setup 6 script (per-user install, no administrator rights).
; Build with:  scripts\build-installer.ps1   (publishes the self-contained exe first)

#ifndef MyAppVersion
  #define MyAppVersion "1.0.5"
#endif
#define MyAppName "Battery Rescue Alert"
#define MyAppExe "BatteryRescueAlert.exe"

[Setup]
; Keep this GUID constant forever: it identifies the app for upgrades and uninstall.
AppId={{B7E2A5D4-3C1F-4E8A-9B26-5F0C7A1D8E43}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Battery Rescue Alert contributors
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Installs for the current user only, so no UAC prompt. The user may still choose "all users" in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=BatteryRescueAlert-Setup-{#MyAppVersion}
SetupIconFile=..\assets\icons\BatteryRescueAlert.ico
UninstallDisplayIcon={app}\{#MyAppExe}
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Asks the user to close the running app instead of failing on a locked file.
AppMutex=Local\BatteryRescueAlert.SingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#MyAppName} with &Windows (recommended)"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\self-contained\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"

[Registry]
; Same value the app writes itself when you tick "Start with Windows" in Settings (current user only).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BatteryRescueAlert"; \
  ValueData: """{app}\{#MyAppExe}"" --background"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Settings are kept by default so a reinstall does not lose them; the user can opt to remove them.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Also remove your Battery Rescue Alert settings?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\BatteryRescueAlert'), True, True, True);
  end;
end;
