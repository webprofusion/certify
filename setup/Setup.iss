; -- Setup.iss --
; Inno Setup Configuration for Certify Installer

[Setup]
AppName=Certify
AppVersion=2.0.3
VersionInfoVersion=2.0.3
AppPublisher=Webprofusion Pty Ltd
AppPublisherURL=https://webprofusion.com
AppUpdatesURL=https://certifythweb.com/
DefaultDirName={pf}\Certify
DefaultGroupName=Certify
UninstallDisplayIcon={app}\Certify.UI.exe
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=CertifySetup
SetupIconFile=icon.ico

[InstallDelete]
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.exe"

[Files]
Source: "..\src\Certify.UI\bin\Release\*"; DestDir: "{app}"; Excludes: "*.pdb,*.*xml, *.vshost.*"

[Icons]
Name: "{group}\Certify"; Filename: "{app}\Certify.UI.exe"
Name: "{commondesktop}\Certify"; Filename: "{app}\Certify.UI.exe"

