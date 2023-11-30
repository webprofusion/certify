FROM mcr.microsoft.com/dotnet/sdk:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
EXPOSE 9696
RUN wget https://dl.smallstep.com/gh-release/cli/docs-cli-install/v0.23.0/step-cli_0.23.0_amd64.deb && dpkg -i step-cli_0.23.0_amd64.deb

# define build and copy required source files
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ./certify/src ./certify/src
COPY ./certify-plugins/src ./certify-plugins/src
COPY ./certify-internal/src/Certify.Plugins ./certify-internal/src/Certify.Plugins
COPY ./libs/anvil ./libs/anvil

# build and publish (as Release mode) to /app/publish
FROM build AS publish
RUN dotnet publish ./certify/src/Certify.Tests/Certify.Core.Tests.Unit/Certify.Core.Tests.Unit.csproj -f net8.0 -c Debug -o /app/publish
RUN dotnet publish ./certify-internal/src/Certify.Plugins/Plugins.All/Plugins.All.csproj -f net8.0 -c Debug -o /app/publish/plugins
COPY ./libs/Posh-ACME/Posh-ACME /app/publish/Scripts/DNS/PoshACME

# copy build from /app/publish in sdk image to final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# run the service, alternatively we could runs tests etc
ENTRYPOINT ["dotnet", "test", "Certify.Core.Tests.Unit.dll", "-f", "net8.0"]
