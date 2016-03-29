; -- Example1.iss --
; Demonstrates copying 3 files and creating an icon.

; SEE THE DOCUMENTATION FOR DETAILS ON CREATING .ISS SCRIPT FILES!

[Setup]
AppName=Certify
AppVersion=0.9.85
AppPublisher=Webprofusion Ltd
AppPublisherURL=http://webprofusion.com
AppUpdatesURL=http://certify.webprofusion.com/
DefaultDirName={pf}\Certify
DefaultGroupName=Certify
UninstallDisplayIcon={app}\Certify.exe
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=CertifySetup
; OutputDir=userdocs:Inno Setup Examples Output

[InstallDelete]
Type: files; Name: "{app}\ManagedOpenSsl.dll"
Type: files; Name: "{app}\ManagedOpenSsl64.dll"
Type: files; Name: "{app}\ACME*.dll"

[Files]
Source: "..\src\bin\Release\*"; DestDir: "{app}"; Excludes: "*.pdb,*.*xml, *.vshost.*"
Source: "..\src\bin\Release\x86\*"; DestDir: "{app}\x86\"; Excludes: "*.pdb,*.*xml, *.vshost.*"
Source: "..\src\bin\Release\x64\*"; DestDir: "{app}\x64\"; Excludes: "*.pdb,*.*xml, *.vshost.*"

;Source: "Readme.txt"; DestDir: "{app}"; Flags: isreadme

[Icons]
Name: "{group}\Certify"; Filename: "{app}\Certify.exe"

