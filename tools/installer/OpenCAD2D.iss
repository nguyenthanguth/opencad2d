; The setup of OpenCAD2D (Inno Setup 6). Built by make-installer.ps1, which publishes the app first and passes the
; version; run by hand, ISCC reads the version from the publish folder's OpenCAD2D.exe below.
;
; Per user: no administrator is asked for, the app goes to %LocalAppData%\Programs\OpenCAD2D. The uninstaller removes
; what this setup installed and nothing else: files a user added to Lisps, Pythons, Plugins or Fonts beside the exe stay
; (their folders too, while they hold a file), and so does %AppData%\OpenCAD2D, the settings.

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString("..\..\artifacts\publish\app\OpenCAD2D.exe")
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\publish\app"
#endif

#define AppName "OpenCAD2D"
#define AppExe "OpenCAD2D.exe"

[Setup]
; One fixed id for ever, generated once: it is how a newer setup finds the install to upgrade, and how Windows lists the
; uninstaller. Never change it, never reuse it for another product.
AppId={{CFF9090B-3DE5-438A-AC7A-86ED890B73FB}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Nguyen Thang
AppPublisherURL=https://github.com/nguyenthanguth/opencad2d
AppSupportURL=https://github.com/nguyenthanguth/opencad2d/issues
AppUpdatesURL=https://github.com/nguyenthanguth/opencad2d/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} setup
VersionInfoCompany=Nguyen Thang
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; An upgrade closes a running OpenCAD2D first, through the Restart Manager, and offers to start it again.
CloseApplications=yes
RestartApplications=yes
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\src\OpenCAD2D.App\Icons\app.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\..\artifacts\installer
OutputBaseFilename=OpenCAD2D-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate"; Description: "Associate .dxf and .dwg files with {#AppName}"; GroupDescription: "File types:"

[Files]
; Everything dotnet publish wrote: the exe, the .NET runtime, the libraries, the support folders, Plugins\DrawingInfo, Plugins\DrawRandom and Plugins\HelloWorld.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The notices of the libraries and wheels the app carries (NuGet client, Newtonsoft.Json, pythonnet, clr_loader, cffi, pycparser), as their licences ask.
Source: "..\..\src\OpenCAD2D.Core\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Writes the ProgID and the Open with entries under HKEY_CURRENT_USER\Software\Classes; shows no window and exits at once.
Filename: "{app}\{#AppExe}"; Parameters: "--register"; Flags: runhidden; Tasks: associate; StatusMsg: "Associating .dxf and .dwg files..."

[UninstallRun]
; Takes the association out, but only where it names this install: an OpenCAD2D registered from another folder (a copy
; run from a USB stick, a build folder) keeps its own.
Filename: "{app}\{#AppExe}"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "Unregister"; Check: AssociationIsThisInstall

[Code]
// True when the Open command of OpenCAD2D.Drawing in the user's registry names the exe of this install.
function AssociationIsThisInstall: Boolean;
var
  Command: String;
begin
  Result :=
    RegQueryStringValue(HKCU, 'Software\Classes\OpenCAD2D.Drawing\shell\open\command', '', Command)
    and (Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Command)) > 0);
end;
