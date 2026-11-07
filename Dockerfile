# LuceneServer.NET container image (multi-stage)
#
#   docker build -t luceneserver-net .
#   docker run -p 8080:8080 -v luceneserver-data:/data luceneserver-net
#
# Configuration: environment variables, e.g. LuceneServer__RootPath, Authorization__Type, Authorization__Clients__0__Id
# Data: /data/indices (LuceneServer:RootPath) and /data/archive (LuceneServer:ArchivePath) - mount a volume on /data.
# Only ONE container may write the indices of a volume (Lucene write.lock), see docs: installation/docker.md

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore first (cached as long as the project files do not change)
COPY src/LuceneServerNET/LuceneServerNET.csproj src/LuceneServerNET/
COPY src/LuceneServerNET.Core/LuceneServerNET.Core.csproj src/LuceneServerNET.Core/
COPY src/LuceneServerNET.Engine/LuceneServerNET.Engine.csproj src/LuceneServerNET.Engine/
COPY src/LuceneServerNET.Parse/LuceneServerNET.Parse.csproj src/LuceneServerNET.Parse/
COPY src/aspire/LuceneServerNET.ServiceDefaults/LuceneServerNET.ServiceDefaults.csproj src/aspire/LuceneServerNET.ServiceDefaults/
RUN dotnet restore src/LuceneServerNET/LuceneServerNET.csproj

COPY LICENSE ./
COPY src/ src/
# no NuGet packages of the libraries in the image
RUN dotnet publish src/LuceneServerNET/LuceneServerNET.csproj -c Release -o /app --no-restore -p:GeneratePackageOnBuild=false \
    && rm -rf /app/_config/luceneserver.json /app/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# upper case: the first-start setup (Setup.cs) looks for LUCENESERVER__ROOTPATH (case-sensitive on Linux)
ENV ASPNETCORE_HTTP_PORTS=8080 \
    LUCENESERVER__ROOTPATH=/data/indices \
    LUCENESERVER__ARCHIVEPATH=/data/archive

# /data is owned by the non-root user "app" of the base image (uid 1654)
RUN mkdir -p /data/indices /data/archive && chown -R app:app /data
VOLUME ["/data"]

COPY --from=build /app .
COPY LICENSE ./

USER app
EXPOSE 8080

# /health: storage writable; the aspnet image has no curl => probe with bash /dev/tcp
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -n 1 <&3 | grep -q " 200 "' || exit 1

ENTRYPOINT ["dotnet", "LuceneServerNET.dll"]
