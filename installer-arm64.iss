#define MyAppName "图吧工具箱winui3"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "罗澜嘎嘎"
#define MyAppExeName "TubaWinUi3.exe"
#define MyAppCopyright "Copyright (C) 2025 罗澜嘎嘎"

[Setup]
AppId={{DA3D64F4-winui3-Tuba-arm64-2025}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}_arm64
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/luolangaga/tubatool
AppSupportURL=https://github.com/luolangaga/tubatool
AppCopyright={#MyAppCopyright}
DefaultDirName={sd}\TubaWinUi3
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=SetupOutput
OutputBaseFilename=TubaWinUi3_Setup_{#MyAppVersion}_arm64
SetupIconFile=Assets\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} (ARM64)
PrivilegesRequired=admin
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
LanguageDetectionMethod=locale
ShowLanguageDialog=no
UpdateUninstallLogAppName=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousSetupType=yes
UsePreviousTasks=yes
DirExistsWarning=no
AppendDefaultDirName=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "publish_arm64_installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup: Boolean;
var
  PrevPath: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DA3D64F4-winui3-Tuba-arm64-2025}_is1',
    'InstallLocation', PrevPath) then
  begin
    if PrevPath <> '' then
      WizardDirValue := PrevPath;
  end
  else if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DA3D64F4-winui3-Tuba-arm64-2025}_is1',
    'Inno Setup: App Path', PrevPath) then
  begin
    if PrevPath <> '' then
      WizardDirValue := PrevPath;
  end;
end;

function IsVCRedistInstalled: Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\ARM64', 'Installed', Installed) and (Installed = 1);
  if not Result then
    Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and (Installed = 1);
end;

function IsWindowsVersionOk: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major > 10) or
            ((Version.Major = 10) and (Version.Build >= 17763));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorCode: Integer;
  Msg: String;
begin
  if not IsWindowsVersionOk then
  begin
    Msg := '本程序需要 Windows 10 1809 (Build 17763) 或更高版本。' + #13#10 +
           '您当前的系统版本过低，无法运行本程序。' + #13#10#13#10 +
           '请先更新 Windows 系统后再安装。';
    MsgBox(Msg, mbCriticalError, MB_OK);
    Result := '系统版本不满足要求，安装已取消。';
    Exit;
  end;

  if not IsVCRedistInstalled then
  begin
    Msg := '检测到你的系统缺少以下运行库：' + #13#10#13#10 +
           '• Microsoft Visual C++ 2015-2022 运行库 (ARM64)' + #13#10#13#10 +
           '是否立即下载并安装？（安装完成后请重新运行本安装包）';
    if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://aka.ms/vs/17/release/vc_redist.arm64.exe', '', '', SW_SHOW, ewNoWait, ErrorCode);
    end;
    Result := '请先安装缺少的运行库后再继续安装。';
    Exit;
  end;

  Result := '';
end;