; Brisk installer (Inno Setup 6). Build with build.ps1 -Installer, or:
;   ISCC.exe /DVersion=0.3.0 installer\brisk.iss
; Installs per user (no admin rights) into %LOCALAPPDATA%\Programs\Brisk, adds Start menu and optional
; desktop shortcuts, registers Brisk as a browser Windows can make the default, and installs the
; .NET Desktop Runtime first if it's missing.

#ifndef Version
  #define Version "0.0.0"
#endif

[Setup]
AppId={{6F2C3E0A-7B1D-4C55-9E2A-B815C0F3A9D1}
AppName=Brisk
AppVersion={#Version}
AppVerName=Brisk {#Version}
AppPublisher=Abubaker
AppPublisherURL=https://github.com/Jumbox249/brisk
AppSupportURL=https://github.com/Jumbox249/brisk/issues
DefaultDirName={localappdata}\Programs\Brisk
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\out
OutputBaseFilename=Brisk-Setup-{#Version}
SetupIconFile=..\assets\brisk.ico
UninstallDisplayIcon={app}\Brisk.exe
UninstallDisplayName=Brisk
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\Brisk"; Filename: "{app}\Brisk.exe"
Name: "{userprograms}\Brisk (Private Window)"; Filename: "{app}\Brisk.exe"; Parameters: "--private"
Name: "{userdesktop}\Brisk"; Filename: "{app}\Brisk.exe"; Tasks: desktopicon

[Run]
; Make Brisk show up under Settings > Apps > Default apps.
Filename: "{app}\Brisk.exe"; Parameters: "--register-default-browser"; Flags: runhidden waituntilterminated
Filename: "{app}\Brisk.exe"; Description: "Open Brisk"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\Brisk.exe"; Parameters: "--unregister-default-browser"; Flags: runhidden waituntilterminated; RunOnceId: "Unregister"

[Code]
const
  RuntimeUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

{ Brisk needs the .NET 10 Desktop Runtime (it keeps the download small). }
function HasDesktopRuntime(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Getting ready', 'Downloading the Microsoft .NET Desktop Runtime that Brisk needs...', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    if HasDesktopRuntime() then Exit;
    DownloadPage.Clear;
    DownloadPage.Add(RuntimeUrl, 'windowsdesktop-runtime.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        SuppressibleMsgBox('Couldn''t download the .NET Desktop Runtime: ' + GetExceptionMessage + #13#10#13#10 +
          'Brisk will be installed, but you''ll need to install the .NET 10 Desktop Runtime from dotnet.microsoft.com before it can start.',
          mbError, MB_OK, IDOK);
        Exit;
      end;
    finally
      DownloadPage.Hide;
    end;
    { The runtime installs for all users, so Windows asks for permission here. }
    if not ShellExec('', ExpandConstant('{tmp}\windowsdesktop-runtime.exe'), '/install /passive /norestart', '',
                     SW_SHOW, ewWaitUntilTerminated, ResultCode) or not HasDesktopRuntime() then
      SuppressibleMsgBox('The .NET Desktop Runtime wasn''t installed. Brisk will be installed, but it needs the .NET 10 ' +
        'Desktop Runtime from dotnet.microsoft.com before it can start.', mbInformation, MB_OK, IDOK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Data := ExpandConstant('{localappdata}\Brisk');
  if not DirExists(Data) then Exit;
  if SuppressibleMsgBox('Also delete your Brisk data (history, bookmarks, saved passwords, cookies and settings)?',
                        mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    DelTree(Data, True, True, True);
end;
