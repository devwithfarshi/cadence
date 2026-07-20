# syntax=docker/dockerfile:1

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Manifests first, so `restore` stays cached until dependencies actually change. Copying the whole
# source before restoring would invalidate that layer on every edit and make each build re-download
# the package graph.
COPY Directory.Build.props Directory.Packages.props Cadence.slnx ./
COPY .config/ .config/
COPY src/Cadence.Domain/*.csproj         src/Cadence.Domain/
COPY src/Cadence.Application/*.csproj    src/Cadence.Application/
COPY src/Cadence.Infrastructure/*.csproj src/Cadence.Infrastructure/
COPY src/Cadence.Api/*.csproj            src/Cadence.Api/
RUN dotnet restore src/Cadence.Api/Cadence.Api.csproj

COPY src/ src/
RUN dotnet publish src/Cadence.Api/Cadence.Api.csproj \
      --configuration Release \
      --no-restore \
      --output /app

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# wget is not in the aspnet image; the health check needs something to call the endpoint with.
RUN apt-get update \
 && apt-get install --yes --no-install-recommends wget \
 && rm -rf /var/lib/apt/lists/*

# Non-root: a container escape should not land on uid 0.
#
# The runtime image already ships an unprivileged `app` user (uid 1654, exposed as $APP_UID), so no
# account is created here — the base image has no `adduser`/`useradd`, and installing the passwd
# package just to duplicate a user that already exists is cost for nothing. The blueprint's §19.1
# snippet predates that and does not build.
COPY --from=build --chown=$APP_UID:$APP_UID /app .

USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# Liveness, not readiness: this decides whether to restart the container, and a database blip must
# not cause that.
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
  CMD wget --quiet --output-document=- http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Cadence.Api.dll"]
