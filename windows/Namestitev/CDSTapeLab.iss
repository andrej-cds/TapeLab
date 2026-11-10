; CDS TapeLab - namestitveni program (Inno Setup 6.3 ali novejsi, brezplacen: https://jrsoftware.org/isdl.php)
#define AppName "CDS TapeLab"
#ifndef AppVersion
#define AppVersion "0.6.0"
#endif
#define AppExe "CDSTapeLab.exe"

[Setup]
AppId={{6EB22735-A5E9-4B49-8DD1-63F06BC45412}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Cassette Deck Service
AppPublisherURL=https://www.cassettedeck-service.com
AppSupportURL=https://www.cassettedeck-service.com
DefaultDirName={autopf}\CDS TapeLab
DefaultGroupName=CDS TapeLab
DisableProgramGroupPage=yes
OutputDir=..\Izdaja
OutputBaseFilename=CDSTapeLab-Setup-{#AppVersion}
SetupIconFile=..\CDSMeasure\Assets\CDSTapeLab.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany=Cassette Deck Service

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
#if FileExists(CompilerPath + "Languages\Slovenian.isl")
Name: "slovenian"; MessagesFile: "compiler:Languages\Slovenian.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\Izdaja\CDS TapeLab\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Navodila\CDS_TapeLab_User_Manual.pdf"; DestDir: "{app}\Docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\{#AppName} Manual"; Filename: "{app}\Docs\CDS_TapeLab_User_Manual.pdf"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
