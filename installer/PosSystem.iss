; Inno Setup script for LP_Pos
; Requires Inno Setup 6+

#define MyAppName "LP_Pos"
#define MyAppPublisher "LP_Pos"
#define MyAppURL ""
#define MyAppExeName "LP_Pos.exe"

; Optional icon (create this file to enable): ..\PosSystem.Main\Assets\app.ico
#define IconFile "..\\PosSystem.Main\\Assets\\app.ico"

; You should run `dotnet publish` before compiling this installer.
; Default expected publish folder (self-contained x64 publish):
#define PublishDir "..\\PosSystem.Main\\bin\\Release\\net10.0-windows\\win-x64\\publish"

[Setup]
AppId={{7B3D76D0-6A2D-4C80-AF8A-7B0B8B2F4F8A}
AppName={#MyAppName}
AppVersion=1.0.0
AppPublisher={#MyAppPublisher}
DefaultDirName={sd}\\LP_Pos
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=LP_Pos-Setup
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; allow customer to pick a different install path
DisableDirPage=no

#ifexist "{#IconFile}"
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\\{#MyAppExeName}
#endif

[Languages]
; Some Inno Setup installations may not include Vietnamese.isl.
; Use it when available, otherwise fall back to the default (English).
#ifexist "compiler:Languages\\Vietnamese.isl"
Name: "vietnamese"; MessagesFile: "compiler:Languages\\Vietnamese.isl"
#else
Name: "english"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài Desktop"; GroupDescription: "Tùy chọn:"; Flags: unchecked

[Files]
; Publish output
Source: "{#PublishDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Ensure required runtime layout exists in install folder
Name: "{app}\\data"
Name: "{app}\\data\\image"

[Icons]
Name: "{autoprograms}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#MyAppExeName}"; Description: "Mở {#MyAppName}"; Flags: nowait postinstall skipifsilent
