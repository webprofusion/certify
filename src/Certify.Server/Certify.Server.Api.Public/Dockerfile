#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

# grant write to store settings path before switching to app user
RUN mkdir /usr/share/certify && chown -R app:app /usr/share/certify 

USER app
WORKDIR /app
EXPOSE 32768
EXPOSE 44360

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Certify.Server/Certify.Server.Api.Public/Certify.Server.Api.Public.csproj", "Certify.Server/Certify.Server.Api.Public/"]
COPY ["Certify.Client/Certify.Client.csproj", "Certify.Client/"]
COPY ["Certify.Locales/Certify.Locales.csproj", "Certify.Locales/"]
COPY ["Certify.Models/Certify.Models.csproj", "Certify.Models/"]
COPY ["Certify.Shared/Certify.Shared.Core.csproj", "Certify.Shared/"]
RUN dotnet restore "./Certify.Server/Certify.Server.Api.Public/./Certify.Server.Api.Public.csproj"
COPY . .
WORKDIR "/src/Certify.Server/Certify.Server.Api.Public"
RUN dotnet build "./Certify.Server.Api.Public.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Certify.Server.Api.Public.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Certify.Server.Api.Public.dll"]
