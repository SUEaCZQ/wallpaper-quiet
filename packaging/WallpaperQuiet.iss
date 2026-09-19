#ifndef AppVersion
  #define AppVersion "1.3.2"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release"
#endif

[Setup]
AppId={{9417B21C-4BD7-45B2-AB2E-58C6436D652A}
AppName=留景
AppVersion={#AppVersion}
AppPublisher=SUEaCZQ
AppPublisherURL=https://github.com/SUEaCZQ/wallpaper-quiet
AppSupportURL=https://github.com/SUEaCZQ/wallpaper-quiet/issues
AppUpdatesURL=https://github.com/SUEaCZQ/wallpaper-quiet/releases
DefaultDirName={localappdata}\Programs\WallpaperQuiet
DefaultGroupName=留景
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#ReleaseDir}
OutputBaseFilename=WallpaperQuiet-{#AppVersion}-Setup-x64
SetupIconFile=..\Assets\App.ico
UninstallDisplayIcon={app}\WallpaperQuiet.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=留景 (WallpaperQuiet)
VersionInfoVersion={#AppVersion}
VersionInfoDescription=留景安装程序

[Tasks]
Name: "startup"; Description: "开机自启动：登录后收起到托盘，并自动开启静享"; GroupDescription: "启动方式："
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\留景"; Filename: "{app}\WallpaperQuiet.exe"
Name: "{group}\恢复桌面"; Filename: "{app}\WallpaperQuiet.exe"; Parameters: "--restore"
Name: "{autodesktop}\留景"; Filename: "{app}\WallpaperQuiet.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WallpaperQuiet.exe"; Description: "打开留景"; Flags: nowait postinstall skipifsilent

[Code]
// Never remove a login task belonging to a different installation.
function StartupPointsHere(): Boolean;
var
  Service, Tasks, Task: Variant;
  I: Integer;
begin
  Result := False;
  try
    Service := CreateOleObject('Schedule.Service');
    Service.Connect();
    Tasks := Service.GetFolder('\').GetTasks(0);
    for I := 1 to Tasks.Count do
    begin
      Task := Tasks.Item(I);
      if Pos('WallpaperQuiet-', String(Task.Name)) = 1 then
        if Task.Definition.Actions.Count = 1 then
          if CompareText(String(Task.Definition.Actions.Item(1).Path),
            ExpandConstant('{app}\WallpaperQuiet.exe')) = 0 then
          begin
            Result := True;
            Exit;
          end;
    end;
  except
    Log('Could not inspect the existing login task: ' + GetExceptionMessage);
  end;
end;

procedure ConfigureStartup(Enable: Boolean);
var
  Args: String;
  ResultCode: Integer;
begin
  Args := '--configure-startup';
  if not Enable then Args := Args + ' --disable';
  if not Exec(ExpandConstant('{app}\WallpaperQuiet.exe'), Args,
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('无法配置开机自启动。请打开留景，在设置中重试。');
  if ResultCode <> 0 then
    RaiseException('Windows 未能保存开机自启动设置。请打开留景，在设置中重试。');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if WizardIsTaskSelected('startup') then ConfigureStartup(True)
    else if StartupPointsHere() then ConfigureStartup(False);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if StartupPointsHere() then ConfigureStartup(False);
    Exec(ExpandConstant('{app}\WallpaperQuiet.exe'), '--quit',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Let the main process and recovery companion restore the desktop.
    Sleep(1000);
  end;
end;
