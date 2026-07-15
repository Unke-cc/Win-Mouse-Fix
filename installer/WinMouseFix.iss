#define MyAppName "Win Mouse Fix"
#define MyAppVersion "0.1.3"
#define MyAppDisplayVersion "0.1.3 Beta"
#define MyAppExeName "WinMouseFix.exe"

[Setup]
AppId={{5E15FC13-D79D-471D-B0E0-C347367A59C6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppDisplayVersion}
AppPublisher=Hu Wenkai
DefaultDirName={localappdata}\Programs\Win Mouse Fix
DisableDirPage=no
DefaultGroupName=Win Mouse Fix
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
OutputDir=..\dist
OutputBaseFilename=WinMouseFix-Setup-0.1.3-beta
SetupIconFile=..\assets\WinMouseFix.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
RestartApplications=no
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNet48Release = 528040;

function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(
    HKLM64,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release',
    Release) and (Release >= DotNet48Release);
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNet48Installed;
  if not Result then
    MsgBox('Win Mouse Fix requires .NET Framework 4.8.', mbError, MB_OK);
end;

procedure StopApplication(const FileName: String);
var
  ResultCode: Integer;
begin
  Exec(
    ExpandConstant('{sys}\taskkill.exe'),
    '/F /T /IM ' + FileName,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
end;

procedure RequestApplicationShutdown;
var
  ResultCode: Integer;
  ApplicationPath: String;
begin
  ApplicationPath := ExpandConstant('{app}\{#MyAppExeName}');
  if FileExists(ApplicationPath) then
  begin
    Exec(
      ApplicationPath,
      '--shutdown',
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode);
  end;
end;

procedure CloseRunningApplications;
begin
  RequestApplicationShutdown;
  Sleep(5000);
  StopApplication('{#MyAppExeName}');
  StopApplication('WinMouseFix.Engine.exe');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApplications;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseRunningApplications;
    RegDeleteValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'WinMouseFix');
  end;
end;
