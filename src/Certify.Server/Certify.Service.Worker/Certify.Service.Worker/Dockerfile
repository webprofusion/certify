#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

# grant write to store settings path before switching to app user
RUN mkdir /usr/share/certify && chown -R app:app /usr/share/certify 

USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Certify.Server/Certify.Service.Worker/Certify.Service.Worker/Certify.Service.Worker.csproj", "Certify.Server/Certify.Service.Worker/Certify.Service.Worker/"]
RUN dotnet restore "./Certify.Server/Certify.Service.Worker/Certify.Service.Worker/./Certify.Service.Worker.csproj"
COPY . .
WORKDIR "/src/Certify.Server/Certify.Service.Worker/Certify.Service.Worker"
RUN dotnet build "./Certify.Service.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Certify.Service.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Certify.Service.Worker.dll"]
