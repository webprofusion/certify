# syntax=docker/dockerfile:1
# Debug-friendly Dockerfile for Certify.Server.Core.
# Build context must be the shared repo root: C:\Work\GIT\certify_dev

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:9696
EXPOSE 9696

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Debug
WORKDIR /src

# Copy all related repositories needed by project references.
COPY certify/ ./certify/
COPY certify-plugins/ ./certify-plugins/
COPY libs/ ./libs/

RUN dotnet restore "certify/src/Certify.Server/Certify.Server.Core/Certify.Server.Core/Certify.Server.Core.csproj"

RUN dotnet publish "certify/src/Certify.Server/Certify.Server.Core/Certify.Server.Core/Certify.Server.Core.csproj" \
    -c ${BUILD_CONFIGURATION} \
    -o /app/publish \
    /p:UseAppHost=false

# Keep legacy DNS scripts expected by Certify runtime.
RUN if [ -d "./libs/Posh-ACME/Posh-ACME" ]; then \
      mkdir -p /app/publish/Scripts/DNS/PoshACME && \
      cp -r ./libs/Posh-ACME/Posh-ACME/* /app/publish/Scripts/DNS/PoshACME/; \
    fi

FROM base AS final

#ENV CERTIFY_MANAGEMENT_HUB=https://local.dev.certificatemanager.app:44361
#ENV CERTIFY_MANAGEMENT_HUB_CLIENT_ID=managedinstance_sp_01
#ENV CERTIFY_MANAGEMENT_HUB_CLIENT_SECRET=65ae05da-213f-4de9-8e94-81d560609dfb
#ENV CERTIFY_MANAGEMENT_HUB_AUTOJOIN=true

WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /usr/share/certify \
    && chgrp -R 0 /usr/share/certify \
    && chmod -R g=u /usr/share/certify \
    && chmod 777 /usr/share/certify

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["dotnet", "Certify.Server.Core.dll"]
