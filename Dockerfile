# syntax=docker/dockerfile:1

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so this layer is cached until the csproj changes.
COPY PlasticSurgery/PlasticSurgery.csproj PlasticSurgery/
RUN dotnet restore PlasticSurgery/PlasticSurgery.csproj

COPY PlasticSurgery/ PlasticSurgery/
RUN dotnet publish PlasticSurgery/PlasticSurgery.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

# Render injects PORT and terminates TLS in front of the container, so listen on plain HTTP there.
# Falls back to 8080 for local `docker run`.
EXPOSE 8080
CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet PlasticSurgery.dll"]
