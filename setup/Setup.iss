; -- Setup.iss --
; Inno Setup Configuration for Certify Installer

[Setup]
AppName=Certify
AppVersion=2.0.1
AppPublisher=Webprofusion Pty Ltd
AppPublisherURL=https://webprofusion.com
AppUpdatesURL=https://certify.webprofusion.com/
DefaultDirName={pf}\Certify
DefaultGroupName=Certify
UninstallDisplayIcon={app}\Certify.exe
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=CertifySetup

[InstallDelete]
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.exe"

[Files]
Source: "..\src\Certify.UI\bin\Release\*"; DestDir: "{app}"; Excludes: "*.pdb,*.*xml, *.vshost.*"

[Icons]
Name: "{group}\Certify"; Filename: "{app}\Certify.UI.exe"
Name: "{commondesktop}\Certify"; Filename: "{app}\Certify.UI.exe"

