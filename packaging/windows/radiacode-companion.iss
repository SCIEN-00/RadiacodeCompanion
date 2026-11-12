; Inno Setup script for the unpackaged Windows build (GitHub releases, winget).
; Build: ISCC.exe /DVersion=0.1.0 /DArch=x64 /DSourceDir=out\win-x64 /DOutputDir=dist radiacode-companion.iss
; Arch is x64 or arm64. Installs per user by default (no admin prompt); the user can choose all users.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\out\win-" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#if Arch == "arm64"
  #define ArchAllowed "arm64"
#else
  #define ArchAllowed "x64compatible"
#endif

[Setup]
AppId={{87395999-8BD5-4DBD-A9A6-3619E8227426}
AppName=Radiacode Companion
AppVersion={#Version}
AppVerName=Radiacode Companion {#Version}
AppPublisher=SCIENindustries
AppPublisherURL=https://github.com/SCIEN-00/RadiacodeCompanion
AppSupportURL=https://github.com/SCIEN-00/RadiacodeCompanion/issues
AppUpdatesURL=https://github.com/SCIEN-00/RadiacodeCompanion/releases
DefaultDirName={autopf}\Radiacode Companion
DefaultGroupName=Radiacode Companion
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode={#ArchAllowed}
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=radiacode-companion_{#Version}_win-{#Arch}_setup
SetupIconFile={#SourceDir}\icon.ico
UninstallDisplayIcon={app}\Radiacode.App.exe
UninstallDisplayName=Radiacode Companion
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Radiacode Companion"; Filename: "{app}\Radiacode.App.exe"
Name: "{autodesktop}\Radiacode Companion"; Filename: "{app}\Radiacode.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Radiacode.App.exe"; Description: "{cm:LaunchProgram,Radiacode Companion}"; Flags: nowait postinstall skipifsilent