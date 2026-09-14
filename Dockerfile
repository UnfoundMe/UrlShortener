# syntax=docker/dockerfile:1

# --- build: restores + compiles, and doubles as the image docker-compose's "migrate" service
# runs `dotnet ef database update` from (Task 12, docs/url-shortener-tasks.md) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY UrlShortener.Api/UrlShortener.Api.csproj UrlShortener.Api/
RUN dotnet restore UrlShortener.Api/UrlShortener.Api.csproj

COPY UrlShortener.Api/ UrlShortener.Api/

RUN dotnet tool install --global dotnet-ef --version 10.0.0
ENV PATH="$PATH:/root/.dotnet/tools"

# --- publish: runtime-ready output only, no SDK/build tooling ---
FROM build AS publish
RUN dotnet publish UrlShortener.Api/UrlShortener.Api.csproj -c Release -o /app/publish --no-restore

# --- final: small ASP.NET Core runtime image, what actually ships/runs the API ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "UrlShortener.Api.dll"]
