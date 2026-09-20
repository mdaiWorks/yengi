#define MyAppName "Yengi"
#define MyAppVersion "1.0"
#define MyAppPublisher "mdaiWorks"
#define MyAppURL "https://github.com/mdaiWorks/yengi"
#define MyAppExeName "Yengi.exe"

[Setup]
AppId={{B12BB525-1436-4688-96CB-77FCEDF70CB5}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=Yengi_Setup_v1.0
SetupIconFile=logo.ico
SolidCompression=yes
WizardStyle=modern
; Program Files klasörüne kurulum için yönetici iznini zorunlu kılar
PrivilegesRequired=admin
; Çalışan Yengi sürecini otomatik kapatır (sessiz güncelleme için gerekli)
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
; Masaüstü kısayolu artık varsayılan olarak tikli (işaretli) gelecek
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "BasucuIDE\bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "BasucuIDE\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Normal kurulum sonrası başlat (kullanıcı isterse)
Filename: "{app}\{#MyAppExeName}"; Parameters: "/lang={code:GetAppLangParam}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function GetAppLangParam(Param: String): String;
begin
  if ActiveLanguage = 'english' then
    Result := 'en'
  else if ActiveLanguage = 'turkish' then
    Result := 'tr'
  else
    Result := 'en';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDataDir: String;
  SettingsFile: String;
  LangCode: String;
  FileContent: String;
begin
  if CurStep = ssPostInstall then
  begin
    AppDataDir := ExpandConstant('{userappdata}\Yengi');
    SettingsFile := AppDataDir + '\settings.json';
    LangCode := GetAppLangParam('');

    if not DirExists(AppDataDir) then
      CreateDir(AppDataDir);

    if not FileExists(SettingsFile) then
    begin
      FileContent := '{"Language": "' + LangCode + '"}';
      SaveStringToFile(SettingsFile, FileContent, False);
    end;
  end;
end;