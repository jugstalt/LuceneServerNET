# LuceneServer.NET Admin UI container image
#
# Built by the NUKE target DockerBuild (.\build.cmd DockerBuild [--container-cli wslc]): the context is
# artifacts/docker/admin, where app/ is the published admin UI (linux-musl-x64) and override/ holds the files of
# deploy/docker/admin/override. No SDK, no multi-stage: works the same with docker, wslc and podman.
#
#   docker run -p 8081:8080 -v luceneserver-admin-data:/data \
#     -e Admin__Authentication__Users__0__Name=admin \
#     -e Admin__Authentication__Users__0__PasswordHash='pbkdf2-sha256$...' \
#     ghcr.io/jugstalt/luceneserver-net-admin
#
# Create a password hash: docker run --rm ghcr.io/jugstalt/luceneserver-net-admin --hash-password <password>
# Data: /data (SQLite database admin.db and the data protection keys for the stored instance secrets) - mount a volume.

FROM mcr.microsoft.com/dotnet/aspnet:11.0-alpine

# ICU instead of the invariant globalization mode of the alpine image: the import jobs read
# SQL Server (Microsoft.Data.SqlClient) and PostgreSQL, which need culture data
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080 \
    ADMIN__DATAPATH=/data \
    DOTNET_EnableDiagnostics=0

RUN mkdir -p /data && chown -R $APP_UID /data
VOLUME ["/data"]

WORKDIR /app
COPY app/ .
# optional files (e.g. _config/*.json) that override the published app
COPY override/ .

USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "LuceneServerNET.Admin.dll"]
