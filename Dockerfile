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

RUN dotnet restore src/Api/StrmManager.Api/StrmManager.Api.csproj

COPY src/ src/

RUN dotnet publish src/Api/StrmManager.Api/StrmManager.Api.csproj \
    --no-restore \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# ffprobe/ffmpeg are not installed yet - the FfprobeMediaValidator (MediaProcessing
# module) has not landed. Add `apt-get install -y ffmpeg` here once it does.

RUN mkdir -p /app/data /stream && chown -R app:app /app/data /stream
USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__Database="Data Source=/app/data/strm-manager.db"
EXPOSE 8080

VOLUME ["/app/data", "/stream"]

ENTRYPOINT ["dotnet", "StrmManager.Api.dll"]
