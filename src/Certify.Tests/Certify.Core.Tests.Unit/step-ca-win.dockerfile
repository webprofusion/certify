FROM mcr.microsoft.com/dotnet/sdk:8.0-nanoserver-ltsc2022 AS base
WORKDIR /app
EXPOSE 9000
RUN mkdir C:\temp 
RUN pwsh -Command "Invoke-WebRequest -Method 'GET' -uri 'https://dl.smallstep.com/gh-release/cli/docs-cli-install/v0.24.4/step_windows_0.24.4_amd64.zip' -Outfile 'C:\temp\step_windows_0.24.4_amd64.zip'" && tar -oxzf C:\temp\step_windows_0.24.4_amd64.zip -C "C:\Program Files"
RUN pwsh -Command "Invoke-WebRequest -Method 'GET' -uri 'https://dl.smallstep.com/gh-release/certificates/gh-release-header/v0.24.2/step-ca_windows_0.24.2_amd64.zip' -Outfile 'C:\temp\step-ca_windows_0.24.2_amd64.zip'" && tar -oxzf C:\temp\step-ca_windows_0.24.2_amd64.zip -C "C:\Program Files"
RUN mkdir "C:\Program Files\SDelete" && pwsh -Command "Invoke-WebRequest -Method 'GET' -uri 'https://download.sysinternals.com/files/SDelete.zip' -Outfile 'C:\temp\SDelete.zip'" && tar -oxzf C:\temp\SDelete.zip -C "C:\Program Files\SDelete"
RUN rmdir /s /q C:\temp
USER ContainerAdministrator
RUN setx /M PATH "%PATH%;C:\Program Files\step_0.24.4\bin;C:\Program Files\step-ca_0.24.2;C:\Program Files\SDelete"
USER ContainerUser

FROM mcr.microsoft.com/dotnet/sdk:8.0-windowsservercore-ltsc2022 AS netapi

FROM base AS final
COPY ./step-ca-win-init.bat .
COPY --from=netapi /Windows/System32/netapi32.dll /Windows/System32/netapi32.dll

HEALTHCHECK CMD curl -Method GET -f http://localhost:9000/health || exit 1

CMD step-ca-win-init.bat && cmd
