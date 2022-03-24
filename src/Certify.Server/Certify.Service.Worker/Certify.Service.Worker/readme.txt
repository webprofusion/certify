Running Manually:

dotnet ./Certify.Service.Worker.dll

API will listen on http://localhost:32768 and https://localhost:44360
HTTPS certificate setup is configured in Program.cs
Initial setup should use invalid pfx for https, with valid PFX to be acquired from own API. API status should flag https cert status for UI to report.

WSL debug
---------
- set DebugType to portable in build.props to enable debugging
- in debug service will run from host pc with a mnt
- database and log are in /usr/share/certify from Environment.SpecialFolder.CommonApplicationData


Linux Install
------------

apt-get certifytheweb

sudo mkdir /opt/certifytheweb

Systemd
-----------

[Unit]
Description=Certify The Web

[Service]
ExecStart=dotnet /opt/certifytheweb/certify.service
WorkingDirectory=/opt/certifytheweb/
User=certifytheweb
Restart=on-failure
SyslogIdentifier=certifytheweb
PrivateTmp=true

[Install]
WantedBy=multi-user.target