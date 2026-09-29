FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
COPY global.json ./
COPY backend/src/ServiceOps.Api/ServiceOps.Api.csproj backend/src/ServiceOps.Api/packages.lock.json backend/src/ServiceOps.Api/
RUN dotnet restore backend/src/ServiceOps.Api/ServiceOps.Api.csproj --locked-mode
COPY backend/src/ServiceOps.Api/ backend/src/ServiceOps.Api/
RUN dotnet publish backend/src/ServiceOps.Api/ServiceOps.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 5080
ENTRYPOINT ["dotnet", "ServiceOps.Api.dll"]
