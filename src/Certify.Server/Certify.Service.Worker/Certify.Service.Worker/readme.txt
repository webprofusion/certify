Running Manually:

dotnet ./Certify.Service.Worker.dll

API will listen on http://localhost:500 and https://localhost:5001

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