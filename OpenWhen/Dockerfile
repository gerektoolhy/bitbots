FROM node:22-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/OpenWhen.Api/OpenWhen.Api.csproj src/OpenWhen.Api/
RUN dotnet restore src/OpenWhen.Api/OpenWhen.Api.csproj
COPY src/OpenWhen.Api/ src/OpenWhen.Api/
COPY --from=web /src/web/dist/ src/OpenWhen.Api/wwwroot/
RUN dotnet publish src/OpenWhen.Api/OpenWhen.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app .
EXPOSE 8080
CMD ["sh", "-c", "dotnet OpenWhen.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]
