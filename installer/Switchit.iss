#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\dist\Switchit"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist\installer"
#endif

[Setup]
AppId={{C9AE8261-1F60-4999-932E-E8628EECDF61}
AppName=Switchit!
AppVersion={#AppVersion}
AppPublisher=dyn0saur
AppPublisherURL=https://github.com/dyn0saur/Switchit
AppSupportURL=https://github.com/dyn0saur/Switchit/issues
AppUpdatesURL=https://github.com/dyn0saur/Switchit/releases
DefaultDirName={localappdata}\Programs\Switchit
DefaultGroupName=Switchit!
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\Switchit.exe
SetupIconFile=..\assets\Switchit.ico
OutputDir={#OutputDir}
OutputBaseFilename=Switchit-Setup-{#AppVersion}-win-x64
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousTasks=yes
DisableWelcomePage=no
VersionInfoProductName=Switchit!
VersionInfoDescription=Switchit! Setup
VersionInfoVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Switchit!"; Filename: "{app}\Switchit.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Switchit!"; Filename: "{app}\Switchit.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Switchit.exe"; Description: "{cm:LaunchProgram,Switchit!}"; Flags: nowait postinstall skipifsilent
