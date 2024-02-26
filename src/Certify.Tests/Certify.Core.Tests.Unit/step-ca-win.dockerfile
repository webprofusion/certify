FROM mcr.microsoft.com/dotnet/sdk:8.0-nanoserver-ltsc2022 AS base
WORKDIR /app
EXPOSE 9000
COPY ./step-ca-win-build.bat .
RUN step-ca-win-build.bat

USER ContainerAdministrator
RUN setx /M PATH "%PATH%;C:\Program Files\step_0.24.4\bin;C:\Program Files\step-ca_0.24.2;C:\Program Files\SDelete"
USER ContainerUser

FROM mcr.microsoft.com/dotnet/sdk:8.0-windowsservercore-ltsc2022 AS netapi

FROM base AS final

COPY ./step-ca-win-init.bat .
COPY --from=netapi /Windows/System32/netapi32.dll /Windows/System32/netapi32.dll

HEALTHCHECK CMD curl -Method GET -f %HEALTH_URL% || exit 1

CMD step-ca-win-init.bat && cmd
