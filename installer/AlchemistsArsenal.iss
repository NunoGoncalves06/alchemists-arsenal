; Inno Setup script for Alchemist's Arsenal (Windows installer).
; Build the game first:  Unity ▸ File ▸ Build Settings ▸ Windows/x64 ▸ Build  → build\
; Then compile this script with Inno Setup (https://jrsoftware.org/isinfo.php):
;   ISCC.exe installer\AlchemistsArsenal.iss
; Output: installer\Output\AlchemistsArsenal-Setup.exe

#define AppName        "Alchemist's Arsenal"
#define AppVersion     "0.1.0"
#define AppPublisher   "Alchemist's Arsenal Team"
#define AppExeName     "AlchemistsArsenal.exe"
#define BuildDir       "..\build"

[Setup]
AppId={{4B2C9E10-7A3D-4C55-9B1E-A5C0D1F2E3B4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=AlchemistsArsenal-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Recursively package the whole Unity build output.
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}";    Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";        Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
