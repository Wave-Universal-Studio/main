; Script Inno Setup do Wave Studio (Windows x64).
; Instala em modo usuário (%LOCALAPPDATA%\Programs\Wave Studio) sem exigir privilégios administrativos.
; Mantém perfis e configurações do usuário preservados na desinstalação padrão.

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0-alpha"
#endif

#ifndef MyPublishDir
  #define MyPublishDir "..\publish\win-x64"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "..\artifacts\installer"
#endif

#ifndef MyOutputBaseFilename
  #define MyOutputBaseFilename "WaveStudio-Setup-win-x64"
#endif

#define MyAppName "Wave Studio"
#define MyAppPublisher "Wave Universal Studio"
#define MyAppURL "https://github.com/Wave-Universal-Studio/main"
#define MyAppExeName "WaveStudio.exe"

[Setup]
AppId={{B4E7F1C2-8D3A-4E9B-9A2F-5C6D7E8F9012}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
SetupIconFile=..\assets\branding\icons\WaveStudio.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent