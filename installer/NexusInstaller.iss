; NEXUS System Optimizer — Inno Setup 6 Installer Script
; Produces: NEXUS-Setup-v{#AppVersion}-win-x64.exe

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\release\publish-win-x64"
#endif

[Setup]
AppId={{7A669999-2C41-44AB-8E6C-A83A38576B55}
AppName=NEXUS System Optimizer
AppVersion={#AppVersion}
AppPublisher=DigitCode Studio
AppPublisherURL=https://github.com/digitcodestudio/nexus
AppSupportURL=https://github.com/digitcodestudio/nexus/issues
AppUpdatesURL=https://github.com/digitcodestudio/nexus/releases
DefaultDirName={localappdata}\Programs\NEXUS System Optimizer
DefaultGroupName=NEXUS System Optimizer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release
OutputBaseFilename=NEXUS-Setup-v{#AppVersion}-win-x64
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\Nexus.UI.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "indonesian"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat ikon pintasan di Desktop"; GroupDescription: "Pintasan Tambahan:"
Name: "autostart"; Description: "Jalankan NEXUS otomatis di System Tray saat Windows menyala"; GroupDescription: "Startup Windows:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NEXUS System Optimizer"; Filename: "{app}\Nexus.UI.exe"; IconFilename: "{app}\Assets\icon.ico"
Name: "{group}\Uninstall NEXUS System Optimizer"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NEXUS System Optimizer"; Filename: "{app}\Nexus.UI.exe"; IconFilename: "{app}\Assets\icon.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NEXUS_System_Optimizer"; ValueData: """{app}\Nexus.UI.exe"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\Nexus.UI.exe"; Description: "Jalankan NEXUS System Optimizer sekarang"; Flags: nowait postinstall skipifsilent

