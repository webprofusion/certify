FROM mcr.microsoft.com/dotnet/sdk:9.0-preview-windowsservercore-ltsc2022 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
EXPOSE 9696

# define build and copy required source files
FROM mcr.microsoft.com/dotnet/sdk:9.0-preview-windowsservercore-ltsc2022 AS build
WORKDIR /src
COPY ./certify/src ./certify/src
COPY ./certify-plugins/src ./certify-plugins/src
COPY ./certify-internal/src/Certify.Plugins ./certify-internal/src/Certify.Plugins
COPY ./libs/anvil ./libs/anvil
RUN dotnet build ./certify/src/Certify.Tests/Certify.Core.Tests.Unit/Certify.Core.Tests.Unit.csproj -f net462 -c Debug -o /app/build

# build and publish (as Debug mode) to /app/publish
FROM build AS publish
COPY --from=build /app/build/x64/SQLite.Interop.dll /app/publish/x64/
RUN dotnet publish ./certify/src/Certify.Tests/Certify.Core.Tests.Unit/Certify.Core.Tests.Unit.csproj -f net462 -c Debug -o /app/publish
RUN dotnet publish ./certify-internal/src/Certify.Plugins/Plugins.All/Plugins.All.csproj -f net462 -c Debug -o /app/publish/Plugins
COPY ./libs/Posh-ACME/Posh-ACME /app/publish/Scripts/DNS/PoshACME

# copy build from /app/publish in sdk image to final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# run the service, alternatively we could runs tests etc
ENTRYPOINT ["dotnet", "test", "Certify.Core.Tests.Unit.dll", "-f", "net462"]
