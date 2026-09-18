# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY .editorconfig .
COPY StrmManager.slnx .
COPY src/Api/StrmManager.Api/StrmManager.Api.csproj src/Api/StrmManager.Api/
COPY src/Common/StrmManager.Common.Domain/StrmManager.Common.Domain.csproj src/Common/StrmManager.Common.Domain/
COPY src/Common/StrmManager.Common.Application/StrmManager.Common.Application.csproj src/Common/StrmManager.Common.Application/
COPY src/Common/StrmManager.Common.Presentation/StrmManager.Common.Presentation.csproj src/Common/StrmManager.Common.Presentation/
COPY src/Modules/Catalog/StrmManager.Modules.Catalog.Domain/StrmManager.Modules.Catalog.Domain.csproj src/Modules/Catalog/StrmManager.Modules.Catalog.Domain/
COPY src/Modules/Catalog/StrmManager.Modules.Catalog.Application/StrmManager.Modules.Catalog.Application.csproj src/Modules/Catalog/StrmManager.Modules.Catalog.Application/
COPY src/Modules/Catalog/StrmManager.Modules.Catalog.Infrastructure/StrmManager.Modules.Catalog.Infrastructure.csproj src/Modules/Catalog/StrmManager.Modules.Catalog.Infrastructure/
COPY src/Modules/Catalog/StrmManager.Modules.Catalog.Presentation/StrmManager.Modules.Catalog.Presentation.csproj src/Modules/Catalog/StrmManager.Modules.Catalog.Presentation/
COPY src/Modules/MediaProcessing/StrmManager.Modules.MediaProcessing.Application/StrmManager.Modules.MediaProcessing.Application.csproj src/Modules/MediaProcessing/StrmManager.Modules.MediaProcessing.Application/
COPY src/Modules/MediaProcessing/StrmManager.Modules.MediaProcessing.Infrastructure/StrmManager.Modules.MediaProcessing.Infrastructure.csproj src/Modules/MediaProcessing/StrmManager.Modules.MediaProcessing.Infrastructure/
COPY src/Modules/Scheduling/StrmManager.Modules.Scheduling.Infrastructure/StrmManager.Modules.Scheduling.Infrastructure.csproj src/Modules/Scheduling/StrmManager.Modules.Scheduling.Infrastructure/

RUN dotnet restore src/Api/StrmManager.Api/StrmManager.Api.csproj

COPY src/ src/

RUN dotnet publish src/Api/StrmManager.Api/StrmManager.Api.csproj \
    --no-restore \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Debian's ffmpeg package bundles the ffprobe binary FfprobeMediaValidator
# (MediaProcessing module) shells out to for media validation - installed before
# switching to the non-root `app` user, and the apt lists are dropped afterward to
# keep the layer small.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /app/data /stream && chown -R app:app /app/data /stream
USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__Database="Data Source=/app/data/strm-manager.db"
EXPOSE 8080

VOLUME ["/app/data", "/stream"]

ENTRYPOINT ["dotnet", "StrmManager.Api.dll"]
