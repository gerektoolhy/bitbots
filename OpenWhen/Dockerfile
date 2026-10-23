FROM node:22-alpine AS web
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
ENV OPENWHEN_OUTDIR=/web/wwwroot
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/OpenWhen.Api/OpenWhen.Api.csproj src/OpenWhen.Api/
RUN dotnet restore src/OpenWhen.Api/OpenWhen.Api.csproj
COPY src/OpenWhen.Api/ src/OpenWhen.Api/
COPY --from=web /web/wwwroot src/OpenWhen.Api/wwwroot
RUN dotnet publish src/OpenWhen.Api/OpenWhen.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenWhen.Api.dll"]
