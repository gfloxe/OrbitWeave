; Installateur d'OrbitWeave (Inno Setup 6). Construit par .github/workflows/release.yml :
;   ISCC /DAppVersion=1.2.3 installer\OrbitWeave.iss   (l'appli publiée doit être dans publish\)
; Installation pour l'utilisateur seul, sans droits administrateur : la mise à jour depuis l'appli se fait sans fenêtre Windows.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8342D9AD-79F9-4C00-B21B-FB0581E303B4}
AppName=OrbitWeave
AppVersion={#AppVersion}
AppVerName=OrbitWeave {#AppVersion}
AppPublisher=gfloxe
AppPublisherURL=https://github.com/gfloxe/OrbitWeave
AppUpdatesURL=https://github.com/gfloxe/OrbitWeave/releases
DefaultDirName={localappdata}\Programs\OrbitWeave
DisableProgramGroupPage=yes
; Toujours le même dossier, à lui seul : la mise à jour peut y faire le ménage sans risque.
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=OrbitWeave-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\OrbitWeave.exe
UninstallDisplayName=OrbitWeave
; La roue est fermée par l'appli elle-même (ou par l'utilisateur) : voir InitializeSetup.
CloseApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "startup"; Description: "Lancer OrbitWeave au démarrage de Windows"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[InstallDelete]
; Une mise à jour repart d'un dossier propre : aucun fichier d'une ancienne version ne reste.
Type: filesandordirs; Name: "{app}\*.dll"
Type: filesandordirs; Name: "{app}\*.json"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\OrbitWeave"; Filename: "{app}\OrbitWeave.exe"
Name: "{autodesktop}\OrbitWeave"; Filename: "{app}\OrbitWeave.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OrbitWeave"; \
  ValueData: """{app}\OrbitWeave.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; Aussi en mode silencieux (mise à jour depuis l'appli) : la roue revient toute seule.
Filename: "{app}\OrbitWeave.exe"; Description: "Lancer OrbitWeave"; Flags: nowait postinstall

[Code]
const
  WheelMutex = 'Local\OrbitWeave.DesktopWidget';

// La roue qui vient de lancer la mise à jour se ferme : on lui laisse jusqu'à 15 secondes.
function WheelClosed(): Boolean;
var
  i: Integer;
begin
  for i := 1 to 60 do
  begin
    if not CheckForMutexes(WheelMutex) then
    begin
      Result := True;
      exit;
    end;
    Sleep(250);
  end;
  Result := not CheckForMutexes(WheelMutex);
end;

function InitializeSetup(): Boolean;
begin
  Result := WheelClosed();
  if not Result then
    SuppressibleMsgBox('OrbitWeave est ouvert. Quitte-le (clic droit sur le rond du milieu, « Quitter OrbitWeave »), puis relance l''installation.',
      mbInformation, MB_OK, IDOK);
end;

function InitializeUninstall(): Boolean;
begin
  Result := not CheckForMutexes(WheelMutex);
  if not Result then
    MsgBox('OrbitWeave est ouvert. Quitte-le (clic droit sur le rond du milieu, « Quitter OrbitWeave »), puis relance la désinstallation.',
      mbInformation, MB_OK);
end;
