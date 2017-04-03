; -- Setup.iss --
; Inno Setup Configuration for Certify Installer

[Setup]
AppName=Certify
AppVersion=2.0.0
AppPublisher=Webprofusion Ltd
AppPublisherURL=https://webprofusion.com
AppUpdatesURL=https://certify.webprofusion.com/
DefaultDirName={pf}\Certify
DefaultGroupName=Certify
UninstallDisplayIcon={app}\Certify.exe
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=CertifySetup
; OutputDir=userdocs:Inno Setup Examples Output

[InstallDelete]
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.exe"

[Files]
Source: "..\src\Certify.UI\bin\Release\*"; DestDir: "{app}"; Excludes: "*.pdb,*.*xml, *.vshost.*"
;Source: "..\src\Certify.UI\bin\Release\ACMESharp-Providers\*"; DestDir: "{app}\ACMESharp-Providers\"; Excludes: "*.pdb,*.*xml, *.vshost.*"
;Source: "..\src\Certify.WinForms\bin\Release\x86\*"; DestDir: "{app}\x86\"; Excludes: "*.pdb,*.*xml, *.vshost.*"
;Source: "..\src\Certify.WinForms\bin\Release\x64\*"; DestDir: "{app}\x64\"; Excludes: "*.pdb,*.*xml, *.vshost.*"

;Source: "Readme.txt"; DestDir: "{app}"; Flags: isreadme

[Icons]
Name: "{group}\Certify"; Filename: "{app}\Certify.UI.exe"

