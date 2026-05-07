; setup.iss — Inno Setup script for DANTE CLI (Windows)
; Compiled with ISCC.exe in the GitHub Actions pipeline. Outputs DanteCLI-Setup-x64.exe.

#define MyAppName "DANTE CLI"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Dante Testa"
#define MyAppURL "https://github.com/dantetesta/dantecli_windows"
#define MyAppExeName "DanteCLI.exe"

[Setup]
AppId={{F8A8E5B2-DA17-4E11-9C71-DA17EC1100B1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\DanteCLI
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\installer-output
OutputBaseFilename=DanteCLI-Setup-x64
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=app.ico
WizardImageFile=
WizardSmallImageFile=
ShowLanguageDialog=auto

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1

[Files]
Source: "..\publish\x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: VCRedistNeedsInstall
Source: "redist\windowsappruntime.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "redist\windowsdesktop-runtime.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: DotNet8NeedsInstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Site oficial"; Filename: "{#MyAppURL}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; \
  StatusMsg: "Instalando Microsoft Visual C++ Runtime (1/3)..."; \
  Check: VCRedistNeedsInstall
Filename: "{tmp}\windowsdesktop-runtime.exe"; Parameters: "/install /quiet /norestart"; \
  StatusMsg: "Instalando .NET 8 Desktop Runtime (2/3)..."; \
  Check: DotNet8NeedsInstall
Filename: "{tmp}\windowsappruntime.exe"; Parameters: "--quiet"; \
  StatusMsg: "Instalando Windows App Runtime 1.7 (3/3)..."
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function VCRedistNeedsInstall: Boolean;
var
  Installed: Cardinal;
begin
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
    Result := (Installed <> 1)
  else
    Result := True;
end;

function DotNet8NeedsInstall: Boolean;
var
  Output: AnsiString;
  ResultCode: Integer;
  Found: Boolean;
begin
  Found := False;
  // Look for any installed Microsoft.WindowsDesktop.App 8.x via the "dotnet --list-runtimes" CLI.
  if Exec(ExpandConstant('{cmd}'), '/C dotnet --list-runtimes > "{tmp}\dotnet_runtimes.txt" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringFromFile(ExpandConstant('{tmp}\dotnet_runtimes.txt'), Output) then
    begin
      if Pos('Microsoft.WindowsDesktop.App 8.', String(Output)) > 0 then
        Found := True;
    end;
  end;
  Result := not Found;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\DanteCLI"
