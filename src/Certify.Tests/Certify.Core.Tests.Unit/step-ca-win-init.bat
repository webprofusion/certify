FOR /F "tokens=* USEBACKQ" %%F IN (`step path`) DO (
  SET STEPPATH=%%F
)

IF EXIST %STEPPATH%\config\ca.json (
    step-ca --password-file %STEPPATH%\secrets\password %STEPPATH%\config\ca.json
    EXIT 0
)

IF "%DOCKER_STEPCA_INIT_NAME%"=="" (
    echo "there is no ca.json config file; please run step ca init, or provide config parameters via DOCKER_STEPCA_INIT_ vars"
    EXIT 1
)

IF "%DOCKER_STEPCA_INIT_DNS_NAMES%"=="" (
    echo "there is no ca.json config file; please run step ca init, or provide config parameters via DOCKER_STEPCA_INIT_ vars"
    EXIT 1
)

IF "%DOCKER_STEPCA_INIT_PROVISIONER_NAME%"=="" SET DOCKER_STEPCA_INIT_PROVISIONER_NAME=admin
IF "%DOCKER_STEPCA_INIT_ADMIN_SUBJECT%"=="" SET DOCKER_STEPCA_INIT_ADMIN_SUBJECT=step
IF "%DOCKER_STEPCA_INIT_ADDRESS%"=="" SET DOCKER_STEPCA_INIT_ADDRESS=:9000

IF NOT "%DOCKER_STEPCA_INIT_PASSWORD%"=="" (
pwsh -Command "Out-File -FilePath "$Env:STEPPATH\password" -InputObject "$Env:DOCKER_STEPCA_INIT_PASSWORD";"^
 "Out-File -FilePath "$Env:STEPPATH\provisioner_password" -InputObject "$Env:DOCKER_STEPCA_INIT_PASSWORD";"
) ELSE IF NOT "%DOCKER_STEPCA_INIT_PASSWORD_FILE%"=="" (
pwsh -Command "Out-File -FilePath "$Env:STEPPATH\password" -InputObject "$Env:DOCKER_STEPCA_INIT_PASSWORD_FILE";"^
 "Out-File -FilePath "$Env:STEPPATH\provisioner_password" -InputObject "$Env:DOCKER_STEPCA_INIT_PASSWORD_FILE";"
) ELSE (
pwsh -Command "$psw = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'.tochararray() | Get-Random -Count 40 | Join-String;"^
 "Out-File -FilePath "$Env:STEPPATH\password" -InputObject $psw;"^
 "Out-File -FilePath "$Env:STEPPATH\provisioner_password" -InputObject $psw;"^
 "Remove-Variable psw"
)

setlocal

SET INIT_ARGS=--deployment-type standalone --name %DOCKER_STEPCA_INIT_NAME% --dns %DOCKER_STEPCA_INIT_DNS_NAMES% --provisioner %DOCKER_STEPCA_INIT_PROVISIONER_NAME% --password-file %STEPPATH%\password --provisioner-password-file %STEPPATH%\provisioner_password --address %DOCKER_STEPCA_INIT_ADDRESS%

IF "%DOCKER_STEPCA_INIT_SSH%"=="true" SET INIT_ARGS=%INIT_ARGS% -ssh
IF "%DOCKER_STEPCA_INIT_REMOTE_MANAGEMENT%"=="true" SET INIT_ARGS=%INIT_ARGS% --remote-management --admin-subject %DOCKER_STEPCA_INIT_ADMIN_SUBJECT%

step ca init %INIT_ARGS%
SET /p psw=<%STEPPATH%\provisioner_password
echo "👉 Your CA administrative password is: %psw%"
echo "🤫 This will only be displayed once."

endlocal

SET HEALTH_URL=https://%DOCKER_STEPCA_INIT_DNS_NAMES%%DOCKER_STEPCA_INIT_ADDRESS%/health

sdelete64 -accepteula -nobanner -q %STEPPATH%\provisioner_password

move "%STEPPATH%\password" "%STEPPATH%\secrets\password"

:: Current error with running this program in Windows Docker Container causes issue reading DB first time, so they must be deleted to be recreated
rmdir /s /q %STEPPATH%\db

:: Current error with running this program in Windows Docker Container causes ACME not to be set with --acme
IF "%DOCKER_STEPCA_INIT_ACME%"=="true" step ca provisioner add acme --type ACME

step-ca --password-file %STEPPATH%\secrets\password %STEPPATH%\config\ca.json
