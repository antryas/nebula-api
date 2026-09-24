# ---- build ---------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /source

# Restore first so the package layer is cached until a project file changes.
# Restore for the target architecture only (drops native libs of other platforms).
# global.json is intentionally not copied: the image SDK may be a newer feature band.
COPY nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Nebula.Domain/Nebula.Domain.csproj src/Nebula.Domain/
COPY src/Nebula.Application/Nebula.Application.csproj src/Nebula.Application/
COPY src/Nebula.Infrastructure/Nebula.Infrastructure.csproj src/Nebula.Infrastructure/
COPY src/Nebula.Api/Nebula.Api.csproj src/Nebula.Api/
RUN dotnet restore src/Nebula.Api/Nebula.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/Nebula.Api/Nebula.Api.csproj -c Release -a $TARGETARCH -o /app --no-restore /p:UseAppHost=false

# ---- runtime -------------------------------------------------------------
# Chiseled: no shell, no package manager, non-root by default.
# The "-extra" flavour ships ICU + tzdata so culture-aware sorting matches the Angular mock (Intl.Collator).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Database__Path=/tmp/nebula/nebula.db

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nebula.Api.dll"]
