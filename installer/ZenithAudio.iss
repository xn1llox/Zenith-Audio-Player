#define MyAppName "Zenith Audio"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Felipe Espinoza"
#define MyAppExeName "ZenithAudio.exe"
#define SourceDir "..\src\ZenithAudio\bin\Release\net8.0-windows10.0.19041.0\win-x64"
#define DotNetDesktopRuntimeInstaller "windowsdesktop-runtime-8.0.25-win-x64.exe"

[Setup]
AppId={{A4C11848-F4F1-4745-9F0F-6FEF6846730C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Zenith Audio
DefaultGroupName=Zenith Audio
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=ZenithAudio_Setup_Inno_win-x64
SetupIconFile=..\src\ZenithAudio\Assets\Icono.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "startup-error.log"
Source: "redist\{#DotNetDesktopRuntimeInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsDotNetDesktopRuntime

[Icons]
Name: "{group}\Zenith Audio"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Desinstalar Zenith Audio"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Zenith Audio"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\{#DotNetDesktopRuntimeInstaller}"; Parameters: "/install /quiet /norestart"; StatusMsg: "Instalando .NET Desktop Runtime 8 x64..."; Check: NeedsDotNetDesktopRuntime; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir Zenith Audio"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsDotNetDesktopRuntime: Boolean;
var
  ValueNames: TArrayOfString;
  I: Integer;
begin
  Result := True;

  if RegGetValueNames(
    HKLM32,
    'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    ValueNames) then
  begin
    for I := 0 to GetArrayLength(ValueNames) - 1 do
    begin
      if Pos('8.', ValueNames[I]) = 1 then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;
end;
