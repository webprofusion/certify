#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/aspnet:5.0-buster-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/core/sdk:5.0-buster AS build
WORKDIR /src
COPY ["Certify.Server.Core/Certify.Server.Core.csproj", "Certify.Server.Core/"]
RUN dotnet restore "Certify.Server.Core/Certify.Server.Core.csproj"
COPY . .
WORKDIR "/src/Certify.Server.Core"
RUN dotnet build "Certify.Server.Core.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Certify.Server.Core.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Certify.Server.Core.dll"]