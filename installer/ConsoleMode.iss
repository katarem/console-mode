; Instalador de Console Mode (Inno Setup 6)
;
; Compilar:
;   iscc /DAppVersion=1.1.0 /DSourceExe=..\publish\ConsoleMode.exe installer\ConsoleMode.iss
;
; El resultado queda en installer\Output\ConsoleMode-Setup-<versión>.exe

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\ConsoleMode.exe"
#endif

#define AppName "Console Mode"
#define AppExe "ConsoleMode.exe"
#define AppPublisher "katarem"
#define AppUrl "https://github.com/katarem/console-mode"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define FirewallRule "Console Mode"

[Setup]
; Mismo AppId en todas las versiones para que las actualizaciones se instalen encima.
AppId={{6F1E3C52-9B7A-4C1D-8E5F-2A9D4B7C3E10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Se necesita administrador para instalar en Archivos de programa y crear la regla del firewall.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=ConsoleMode-Setup-{#AppVersion}
SetupIconFile=..\pc\ConsoleMode\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Las entradas de HKCU (inicio con Windows) son para el usuario que instala, que es lo que queremos.
UsedUserAreasWarning=no
LicenseFile=..\LICENSE
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Instalador de {#AppName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked
Name: "startup"; Description: "Iniciar Console Mode con Windows (en segundo plano)"; GroupDescription: "Opciones:"
Name: "firewall"; Description: "Permitir Console Mode en el firewall de Windows (redes privadas)"; GroupDescription: "Opciones:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Mismo valor que usa la opción «Iniciar con Windows» de la app, así que se puede cambiar luego desde Ajustes.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "ConsoleMode"; ValueData: """{app}\{#AppExe}"" --background"; Tasks: startup

[Run]
; Regla del firewall para el puerto TCP de control y el descubrimiento UDP (se borra antes para no duplicarla).
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FirewallRule}"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes profile=private"; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "CloseApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden waituntilterminated; RunOnceId: "KillApp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"

[Code]
procedure CloseRunningApp();
var
  ResultCode: Integer;
  ExePath: String;
begin
  // Primero se le pide que se cierre por las buenas; si sigue abierta (p.ej. una versión sin --exit), se fuerza.
  ExePath := ExpandConstant('{app}\{#AppExe}');
  if FileExists(ExePath) then
    Exec(ExePath, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApp();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // El inicio automático también se puede activar desde la app: lo quitamos siempre.
    RegDeleteValue(HKCU, '{#RunKey}', 'ConsoleMode');
    // La configuración (%APPDATA%\ConsoleMode) se conserva por si se vuelve a instalar.
  end;
end;
