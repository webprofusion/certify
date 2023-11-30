FOR /F "tokens=* USEBACKQ" %%F IN (`step path`) DO (
  SET STEPPATH=%%F
)

pwsh -Command "$psw = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'.tochararray() | Get-Random -Count 40 | Join-String;"^
 "echo $psw;"^
 "Out-File -FilePath "$Env:STEPPATH\password" -InputObject $psw;"^
 "Out-File -FilePath "$Env:STEPPATH\provisioner_password" -InputObject $psw;"^
 "Remove-Variable psw"

step ca init --deployment-type standalone --name Smallstep --dns localhost --provisioner admin ^
--password-file %STEPPATH%\password --provisioner-password-file %STEPPATH%\provisioner_password ^
--address :9000 --remote-management --admin-subject step

sdelete64 -accepteula -nobanner -q %STEPPATH%\provisioner_password

move "%STEPPATH%\password" "%STEPPATH%\secrets\password"

rmdir /s /q %STEPPATH%\db

step ca provisioner add acme --type ACME

step-ca --password-file %STEPPATH%\secrets\password %STEPPATH%\config\ca.json
