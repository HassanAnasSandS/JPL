#define AppName "Cric360"
#ifndef AppVersion
  #define AppVersion "1.0.19"
#endif
#define AppPublisher "Cric360"
#define AppExeName "JPL.exe"
#define AppId "com.jpl.cricketscorer"
#define PublishDir "..\releases\Windows\Cric360"

[Setup]
AppId={{D6C9176A-DA66-4115-BDB5-BC6735D4A734}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Cric360
DefaultGroupName=Cric360
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\releases\Windows
OutputBaseFilename=Cric360-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Cric360"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Cric360"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Cric360"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
; MAUI's unpackaged app stores its databases, session, preferences and caches under its app ID.
Type: filesandordirs; Name: "{localappdata}\{#AppId}"
; Clean the product-name folder used by older unpackaged builds, if present.
Type: filesandordirs; Name: "{localappdata}\Cric360"
