[Setup]
AppName=Yengi
AppVersion=0.1
DefaultDirName={pf64}\Yengi
DefaultGroupName=Yengi
OutputDir=..\..\dist
OutputBaseFilename=Yengi_installer
Compression=lzma2
SolidCompression=yes

[Files]
; Include all files from publish folder (relative to this script)
Source: "..\..\BasucuIDE\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Yengi"; Filename: "{app}\Yengi.exe"

[Run]
Filename: "{app}\Yengi.exe"; Description: "Run Yengi"; Flags: nowait postinstall shellexec

; Note: To build this installer, install Inno Setup and run:
; iscc mdaiAgent.iss
; The {#src} define should be replaced with the absolute path to the publish folder before running the compiler.
