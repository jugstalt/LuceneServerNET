# LuceneServer.NET container image
#
# Built by the NUKE target DockerBuild (.\build.cmd DockerBuild [--container-cli wslc]): the context is
# artifacts/docker/server, where app/ is the published server (linux-musl-x64) and override/ holds the files of
# deploy/docker/server/override. No SDK, no multi-stage: works the same with docker, wslc and podman.
#
#   docker run -p 8080:8080 -v luceneserver-data:/data ghcr.io/jugstalt/luceneserver-net
#
# Configuration: environment variables, e.g. LuceneServer__RootPath, Authorization__Type, Authorization__Clients__0__Id
# Data: /data/indices (LuceneServer:RootPath) - mount a volume on /data.
# Optional archive (one JSON file per document, slow): -e LuceneServer__ArchivePath=/data/archive
# Only ONE container may write the indices of a volume (Lucene write.lock), see docs: installation/docker.md

FROM mcr.microsoft.com/dotnet/aspnet:11.0-alpine

# upper case: the first-start setup (Setup.cs) looks for LUCENESERVER__ROOTPATH (case-sensitive on Linux)
ENV ASPNETCORE_HTTP_PORTS=8080 \
    LUCENESERVER__ROOTPATH=/data/indices \
    DOTNET_EnableDiagnostics=0

# /data is owned by the non-root user "app" of the base image (uid 1654)
RUN mkdir -p /data/indices && chown -R $APP_UID /data
VOLUME ["/data"]

WORKDIR /app
COPY app/ .
# optional files (e.g. _config/*.json) that override the published app
COPY override/ .

USER $APP_UID
EXPOSE 8080

# /health: storage writable (busybox wget of the alpine image)
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "LuceneServerNET.dll"]
