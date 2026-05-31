; RGTools — Inno Setup script. Genera RGTools-Setup-<version>.exe (instala y actualiza).
; No editar a mano la version: build-release.ps1 la pasa con /DAppVersion=...

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "RGTools"
#define AppPublisher "rgdevment"
#define AppExeName "RGTools.App.exe"
#define AppId "{{8F2C6A41-7B3E-4D9A-9C1F-A1B2C3D4E5F6}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\RGTools
DefaultGroupName=RGTools
DisableProgramGroupPage=yes
DisableDirPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\releases
OutputBaseFilename=RGTools-Setup-{#AppVersion}
SetupIconFile=..\RGTools.App\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "autostart"; Description: "Iniciar RGTools automáticamente con Windows"; GroupDescription: "Arranque:"

[Files]
Source: "..\RGTools.App\bin\Release\net10.0-windows\win-x64\publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\RGTools"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Desinstalar RGTools"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Ejecutar RGTools ahora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/delete /tn ""RGToolsLauncher"" /f"; Flags: runhidden; RunOnceId: "DelRGToolsTask"
Filename: "taskkill.exe"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden; RunOnceId: "KillRGTools"

; Auto-inicio via tarea programada con privilegios elevados (la app es requireAdministrator).
; Se hace en [Code] para escapar correctamente las comillas del /tr (un [Run] de schtasks
; falla cuando la ruta del exe tiene espacios, p.ej. "Program Files").
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  ExePath, Params: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('autostart') then
  begin
    ExePath := ExpandConstant('{app}\{#AppExeName}');
    Params := '/create /tn "RGToolsLauncher" /tr "\"' + ExePath + '\"" /sc onlogon /rl highest /f';
    Exec('schtasks.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
