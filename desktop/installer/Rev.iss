; The recipe for Rev's installer (Rev-Setup-<version>.exe), made with Inno Setup (https://jrsoftware.org).
;
; It installs Rev just for you (no administrator needed) into your own Programs folder, adds it to the
; Start menu (and, if you tick the box, the desktop), and can be removed from Windows Settings → Apps.
; Your decks and progress live elsewhere (%LOCALAPPDATA%\Rev), so installing a newer version or
; uninstalling never touches them.
;
; GitHub Actions builds it (see .github/workflows/build.yml). To build it yourself, publish Rev.exe into
; "publish" first (see README.md), then run:   ISCC.exe /DAppVersion=1.0.0 installer\Rev.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
; Never change this id: it's how Windows knows a new version replaces the old one.
AppId={{D381DDF2-C00F-46D9-B922-6B7BD5FFD227}
AppName=Rev
AppVersion={#AppVersion}
AppVerName=Rev {#AppVersion}
AppPublisher=NevamindHaze
AppPublisherURL=https://github.com/NevamindHaze/Rev
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Rev
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Rev-Setup-{#AppVersion}
SetupIconFile=..\src\Rev.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Rev.exe
UninstallDisplayName=Rev
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Close Rev if it's open, so a new version can replace it.
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Put a Rev shortcut on the desktop"; Flags: unchecked

[Files]
Source: "..\publish\Rev.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Rev"; Filename: "{app}\Rev.exe"
Name: "{autodesktop}\Rev"; Filename: "{app}\Rev.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Rev.exe"; Description: "Open Rev now"; Flags: nowait postinstall skipifsilent
