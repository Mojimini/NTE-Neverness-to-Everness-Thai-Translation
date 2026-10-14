#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef RuntimeDir
  #define RuntimeDir "..\runtime"
#endif
#ifndef OutputDir
  #define OutputDir "out"
#endif
#ifndef TestBuild
  #define TestBuild 0
#endif

#if TestBuild == "1"
  #define AppIdValue "{{0E45F955-1DE9-4A70-A75F-6852543247D2}"
  #define AppNameValue "NTE Thai Translator TEST"
  #define GroupNameValue "NTE Thai Translator TEST"
#else
  #define AppIdValue "{{A13D5F8A-8F4B-4A1D-A1B5-F5EEA3D74AF1}"
  #define AppNameValue "NTE Thai Translator"
  #define GroupNameValue "NTE Thai Translator"
#endif

[Setup]
AppId={#AppIdValue}
AppName={#AppNameValue}
AppVersion={#AppVersion}
AppVerName={#AppNameValue} {#AppVersion}
AppPublisher=Mojimini
DefaultDirName={localappdata}\Programs\NTEThai
DefaultGroupName={#GroupNameValue}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=lowest
MinVersion=10.0.18362
OutputDir={#OutputDir}
#if TestBuild == "1"
OutputBaseFilename=Thai-Translator-TEST-Setup-{#AppVersion}
#else
OutputBaseFilename=Thai-Translator-Setup-{#AppVersion}
#endif
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayName={#AppNameValue}
Uninstallable=yes
CloseApplications= no
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppNameValue}
VersionInfoCompany=Mojimini
; Keep runtime files explicit: the installer must never ingest QA, sources, or user data.

[Languages]
Name: "thai"; MessagesFile: "compiler:Languages\Thai.isl"

[Tasks]
Name: "desktopicon"; Description: "สร้างทางลัดบนเดสก์ท็อป"; GroupDescription: "ทางลัดเพิ่มเติม:"; Flags: unchecked

[Dirs]
Name: "{app}\Settings"; Flags: uninsneveruninstall
Name: "{app}\Settings\Downloads"; Flags: uninsneveruninstall
Name: "{app}\Fonts\Imported"; Flags: uninsneveruninstall
Name: "{app}\Fonts\Profiles"; Flags: uninsneveruninstall
Name: "{app}\Fonts\Requests"; Flags: uninsneveruninstall
Name: "{app}\Profiles"; Flags: uninsneveruninstall
Name: "{app}\Requests"; Flags: uninsneveruninstall

[Files]
Source: "{#RuntimeDir}\เปิดคำแปลรุ่นหลัก-v28c.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\AntdUI.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\ThaiUpdate.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\release-config.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\content-catalog.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\วิธีใช้.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\Fonts\Default\current.ttf"; DestDir: "{app}\Fonts\Default"; Flags: ignoreversion
Source: "{#RuntimeDir}\Fonts\Default\english.ttf"; DestDir: "{app}\Fonts\Default"; Flags: ignoreversion
Source: "{#RuntimeDir}\Fonts\Default\default_profile.json"; DestDir: "{app}\Fonts\Default"; Flags: ignoreversion
Source: "{#RuntimeDir}\Fonts\Default\default_manifest.json"; DestDir: "{app}\Fonts\Default"; Flags: ignoreversion
Source: "{#RuntimeDir}\Fonts\Default\default_manifest.json"; DestDir: "{app}\แพ็ก"; DestName: "manifest.json"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#RuntimeDir}\Fonts\Bundled\bundled_fonts.json"; DestDir: "{app}\Fonts\Bundled"; Flags: ignoreversion
Source: "{#RuntimeDir}\Fonts\Bundled\Monocraft\font.ttf"; DestDir: "{app}\Fonts\Bundled\Monocraft"; Flags: ignoreversion
Source: "{#RuntimeDir}\Fonts\Bundled\Monocraft\preview.ttf"; DestDir: "{app}\Fonts\Bundled\Monocraft"; Flags: ignoreversion
Source: "{#RuntimeDir}\Licenses\*"; DestDir: "{app}\Licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NTE Thai Translator"; Filename: "{app}\เปิดคำแปลรุ่นหลัก-v28c.exe"; Check: not IsTestBuild
Name: "{group}\NTE Thai Translator TEST"; Filename: "{app}\เปิดคำแปลรุ่นหลัก-v28c.exe"; Check: IsTestBuild
Name: "{autodesktop}\NTE Thai Translator"; Filename: "{app}\เปิดคำแปลรุ่นหลัก-v28c.exe"; Tasks: desktopicon; Check: not IsTestBuild
Name: "{autodesktop}\NTE Thai Translator TEST"; Filename: "{app}\เปิดคำแปลรุ่นหลัก-v28c.exe"; Tasks: desktopicon; Check: IsTestBuild

[Run]
Filename: "{app}\เปิดคำแปลรุ่นหลัก-v28c.exe"; Description: "เปิด NTE Thai Translator"; Flags: nowait postinstall; Check: ShouldLaunchNormally
Filename: "{app}\เปิดคำแปลรุ่นหลัก-v28c.exe"; Flags: nowait; Check: ShouldLaunchAfterUpdate

[Code]
const
  TestBuildFlag = {#TestBuild};

var
  AppMutexHandle: THandle;
  AppMutexOwned: Boolean;
  OperationLockHandle: THandle;

function IsTestBuild(): Boolean;
begin
  Result := TestBuildFlag = 1;
end;

function CreateFileW(lpFileName: String; dwDesiredAccess: LongWord;
  dwShareMode: LongWord; lpSecurityAttributes: Integer;
  dwCreationDisposition: LongWord; dwFlagsAndAttributes: LongWord;
  hTemplateFile: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';

function CloseHandle(hObject: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function CreateMutexW(lpMutexAttributes: Integer; bInitialOwner: Boolean;
  lpName: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';

function WaitForSingleObject(hHandle: THandle; dwMilliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';

function ReleaseMutex(hMutex: THandle): Boolean;
  external 'ReleaseMutex@kernel32.dll stdcall';

function GetFileAttributesW(lpFileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

procedure Sleep(dwMilliseconds: LongWord);
  external 'Sleep@kernel32.dll stdcall';

function PathHasReparseComponent(Path: String): Boolean;
var
  CurrentPath, ParentPath: String;
  Attributes: LongWord;
begin
  CurrentPath := ExpandFileName(Path);
  while CurrentPath <> '' do
  begin
    Attributes := GetFileAttributesW(CurrentPath);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then
    begin
      Result := True;
      exit;
    end;
    ParentPath := ExtractFileDir(CurrentPath);
    if ParentPath = CurrentPath then break;
    CurrentPath := ParentPath;
  end;
  Result := False;
end;

function DirectoryTreeHasReparsePoint(Path: String): Boolean;
var
  FindRec: TFindRec;
  ChildPath: String;
  Attributes: LongWord;
begin
  Result := PathHasReparseComponent(Path);
  if Result or not DirExists(Path) then exit;
  if not FindFirst(AddBackslash(Path) + '*', FindRec) then
  begin
    Result := True;
    exit;
  end;
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        ChildPath := AddBackslash(Path) + FindRec.Name;
        Attributes := GetFileAttributesW(ChildPath);
        if (Attributes = $FFFFFFFF) or ((Attributes and $400) <> 0) then
        begin
          Result := True;
          exit;
        end;
        if ((Attributes and $10) <> 0) and DirectoryTreeHasReparsePoint(ChildPath) then
        begin
          Result := True;
          exit;
        end;
      end;
    until not FindNext(FindRec);
    Result := False;
  finally
    FindClose(FindRec);
  end;
end;

function UnsafeInstallTarget(AppPath: String): Boolean;
begin
  Result := PathHasReparseComponent(AppPath) or
    PathHasReparseComponent(AddBackslash(AppPath) + 'operation.lock') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'receipt.json') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'เปิดคำแปลรุ่นหลัก-v28c.exe') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'AntdUI.dll') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'ThaiUpdate.exe') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'release-config.json') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'content-catalog.json') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'วิธีใช้.md') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Default\current.ttf') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Default\english.ttf') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Default\default_profile.json') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Default\default_manifest.json') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Bundled\bundled_fonts.json') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Bundled\Monocraft\font.ttf') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Bundled\Monocraft\preview.ttf') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'แพ็ก\manifest.json') or
    DirectoryTreeHasReparsePoint(AddBackslash(AppPath) + 'Licenses') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Settings\Downloads') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Imported') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Profiles') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Fonts\Requests') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Profiles') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'Requests') or
    PathHasReparseComponent(AddBackslash(AppPath) + 'ตำแหน่งเกม.txt');
end;

function IsDriveRoot(AppPath: String): Boolean;
var
  FullPath, DriveRoot: String;
begin
  FullPath := ExpandFileName(AppPath);
  DriveRoot := AddBackslash(ExtractFileDrive(FullPath));
  Result := CompareText(FullPath, DriveRoot) = 0;
end;

function AcquireAppMutex(AppPath: String): Boolean;
var
  FullPath, Key, MutexName: String;
  WaitResult: LongWord;
begin
  FullPath := ExpandFileName(AppPath);
  while (Length(FullPath) > 3) and
    ((FullPath[Length(FullPath)] = '\') or (FullPath[Length(FullPath)] = '/')) do
    Delete(FullPath, Length(FullPath), 1);
  Key := Uppercase(GetSHA256OfString(Utf8Encode(Lowercase(FullPath))));
  MutexName := 'Local\NTEThaiUi2_' + Copy(Key, 1, 16);
  AppMutexHandle := CreateMutexW(0, False, MutexName);
  if AppMutexHandle = 0 then
  begin
    Result := False;
    exit;
  end;
  WaitResult := WaitForSingleObject(AppMutexHandle, 0);
  AppMutexOwned := (WaitResult = 0) or (WaitResult = $80);
  Result := AppMutexOwned;
  if not AppMutexOwned then
  begin
    CloseHandle(AppMutexHandle);
    AppMutexHandle := 0;
  end;
end;

function AcquireOperationFileLock(AppPath: String): Boolean;
var
  LockPath: String;
begin
  LockPath := AddBackslash(AppPath) + 'operation.lock';
  OperationLockHandle := CreateFileW(LockPath, $C0000000, 0, 0, 4, $200080, 0);
  Result := OperationLockHandle <> THandle(-1);
  if not Result then OperationLockHandle := 0;
end;

procedure ReleaseInstallGuards();
begin
  if OperationLockHandle <> 0 then
  begin
    CloseHandle(OperationLockHandle);
    OperationLockHandle := 0;
  end;
  if AppMutexOwned and (AppMutexHandle <> 0) then
    ReleaseMutex(AppMutexHandle);
  AppMutexOwned := False;
  if AppMutexHandle <> 0 then
  begin
    CloseHandle(AppMutexHandle);
    AppMutexHandle := 0;
  end;
end;

function IsDotNet48Installed(): Boolean;
var
  ReleaseValue: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', ReleaseValue)
    and (ReleaseValue >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := IsDotNet48Installed();
  if not Result then
    MsgBox('ต้องติดตั้ง .NET Framework 4.8 ก่อน จึงจะใช้โปรแกรมนี้ได้', mbError, MB_OK);
end;

function IsUpdateInstall(): Boolean;
begin
  Result := CompareText(ExpandConstant('{param:NTEUPDATE|0}'), '1') = 0;
end;

function ShouldLaunchNormally(): Boolean;
begin
  Result := (not IsTestBuild()) and (not IsUpdateInstall());
end;

function ShouldLaunchAfterUpdate(): Boolean;
begin
  Result := (not IsTestBuild()) and IsUpdateInstall();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  AppPath, TestMarker: String;
begin
  Result := '';
  AppPath := WizardDirValue;
  if IsDriveRoot(AppPath) then
  begin
    Result := 'กรุณาเลือกโฟลเดอร์ย่อยสำหรับติดตั้ง ไม่สามารถติดตั้งที่รากไดรฟ์ได้';
    exit;
  end;
  if UnsafeInstallTarget(AppPath) then
  begin
    Result := 'พบตำแหน่งเชื่อมโยงในโฟลเดอร์ติดตั้ง กรุณาเลือกโฟลเดอร์อื่น';
    exit;
  end;
  if IsUpdateInstall() and (not FileExists(AddBackslash(AppPath) + 'เปิดคำแปลรุ่นหลัก-v28c.exe')) then
  begin
    Result := 'ไม่พบโปรแกรมเดิมในโฟลเดอร์นี้ จึงติดตั้งการอัปเดตไม่ได้';
    exit;
  end;
  if not AcquireAppMutex(AppPath) then
  begin
    Result := 'โปรแกรมอีกหน้าต่างกำลังทำรายการอยู่ กรุณารอให้เสร็จก่อนติดตั้งหรืออัปเดต';
    exit;
  end;
  if not ForceDirectories(AppPath) then
  begin
    ReleaseInstallGuards();
    Result := 'สร้างโฟลเดอร์ติดตั้งไม่ได้ กรุณาเลือกตำแหน่งอื่น';
    exit;
  end;
  if not AcquireOperationFileLock(AppPath) then
  begin
    ReleaseInstallGuards();
    Result := 'ไฟล์ operation.lock ถูกใช้งานอยู่ กรุณารอให้เสร็จก่อนติดตั้งหรืออัปเดต';
    exit;
  end;
  TestMarker := ExpandConstant('{param:NTE_TEST_GATE_MARKER|}');
  if IsTestBuild() and (TestMarker <> '') then
  begin
    if not SaveStringToFile(TestMarker, 'app-gate-held', False) then
    begin
      ReleaseInstallGuards();
      Result := 'Unable to write private test fixture marker';
      exit;
    end;
    Sleep(1500);
  end;
end;

procedure DeinitializeSetup();
begin
  ReleaseInstallGuards();
end;

function InitializeUninstall(): Boolean;
var
  ReceiptPath, AppPath, TestMarker: String;
begin
  AppPath := ExpandConstant('{app}');
  ReceiptPath := ExpandConstant('{app}\receipt.json');
  Result := not FileExists(ReceiptPath);
  if not Result then
  begin
    if not UninstallSilent() then
      MsgBox('ยังคืนค่าไฟล์เกมไม่เสร็จ จึงถอนการติดตั้งไม่ได้ กรุณาคืนค่าไฟล์เกมก่อน', mbError, MB_OK);
    exit;
  end;
  if IsDriveRoot(AppPath) or UnsafeInstallTarget(AppPath) then
  begin
    Result := False;
    if not UninstallSilent() then
      MsgBox('โฟลเดอร์ติดตั้งมีตำแหน่งเชื่อมโยงหรือไม่ปลอดภัย จึงถอนการติดตั้งไม่ได้', mbError, MB_OK);
    exit;
  end;
  TestMarker := ExpandConstant('{param:NTE_TEST_UNINSTALL_RECEIPT_GAP_MARKER|}');
  if IsTestBuild() and (TestMarker <> '') then
  begin
    if not SaveStringToFile(TestMarker, 'receipt-probe-passed-before-guards', False) then
    begin
      Result := False;
      exit;
    end;
    Sleep(1500);
  end;
  if not AcquireAppMutex(AppPath) then
  begin
    Result := False;
    if not UninstallSilent() then
      MsgBox('โปรแกรมกำลังทำรายการอยู่ กรุณารอให้เสร็จก่อนถอนการติดตั้ง', mbError, MB_OK);
    exit;
  end;
  if not AcquireOperationFileLock(AppPath) then
  begin
    Result := False;
    ReleaseInstallGuards();
    if not UninstallSilent() then
      MsgBox('โปรแกรมกำลังทำรายการอยู่ กรุณารอให้เสร็จก่อนถอนการติดตั้ง', mbError, MB_OK);
    exit;
  end;
  if FileExists(ReceiptPath) then
  begin
    Result := False;
    ReleaseInstallGuards();
    if not UninstallSilent() then
      MsgBox('ยังคืนค่าไฟล์เกมไม่เสร็จ จึงถอนการติดตั้งไม่ได้ กรุณาคืนค่าไฟล์เกมก่อน', mbError, MB_OK);
    exit;
  end;
  TestMarker := ExpandConstant('{param:NTE_TEST_UNINSTALL_GATE_MARKER|}');
  if IsTestBuild() and (TestMarker <> '') then
  begin
    if not SaveStringToFile(TestMarker, 'uninstall-gate-held', False) then
    begin
      Result := False;
      ReleaseInstallGuards();
      exit;
    end;
    Sleep(1500);
  end;
end;

procedure DeinitializeUninstall();
var
  LockPath, AppPath: String;
  Attributes: LongWord;
begin
  AppPath := ExpandConstant('{app}');
  ReleaseInstallGuards();
  LockPath := AddBackslash(AppPath) + 'operation.lock';
  Attributes := GetFileAttributesW(LockPath);
  if (Attributes <> $FFFFFFFF) and ((Attributes and $400) = 0) then
    DeleteFile(LockPath);
  RemoveDir(AppPath);
end;
