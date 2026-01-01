; Inno Setup Script for POS System
; This script creates a professional .exe installer for POS System

[Setup]
AppName=POS System
AppVersion=1.0.0
AppPublisher=Your Restaurant
AppPublisherURL=
DefaultDirName={pf}\POS System
DefaultGroupName=POS System
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=PosSystem_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
WizardStyle=modern
ShowLanguageDialog=auto
VersionInfoVersion=1.0.0.0
VersionInfoCopyright=2026

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 0,6.1

[Files]
; Copy all files from the publish directory
Source: "*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\POS System"; Filename: "{app}\PosSystem.Main.exe"; IconFileName: "{app}\PosSystem.Main.exe"
Name: "{group}\Uninstall POS System"; Filename: "{uninstallexe}"
Name: "{commondesktop}\POS System"; Filename: "{app}\PosSystem.Main.exe"; Tasks: desktopicon; IconFileName: "{app}\PosSystem.Main.exe"
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\POS System"; Filename: "{app}\PosSystem.Main.exe"; Tasks: quicklaunchicon; IconFileName: "{app}\PosSystem.Main.exe"

[Run]
Filename: "{app}\PosSystem.Main.exe"; Description: "{cm:LaunchProgram,POS System}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    MsgBox('POS System has been installed successfully!' + #13#13 + 
           'You can now launch the application from the Start Menu or Desktop shortcut.', 
           mbInformation, MB_OK);
  end;
end;
