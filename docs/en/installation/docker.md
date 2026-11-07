# Docker

The repository contains a multi-stage `Dockerfile`: it builds the server from source with the .NET 10 SDK
and runs it on the ASP.NET Core 10 runtime image as the non-root user `app`.

## Build the image

```bash
docker build -t luceneserver-net .
```

Or with the NUKE build, which tags the image with the version (and `latest`, plus the
`ghcr.io/jugstalt/luceneserver-net` tags):

```bash
./build.cmd Docker
```

## Run

```bash
docker run -d --name luceneserver -p 8080:8080 -v luceneserver-data:/data luceneserver-net
```

The server listens on port **8080** inside the container.

## Volumes

| Path in the container | Setting | Content |
|---|---|---|
| `/data/indices` | `LuceneServer:RootPath` | the Lucene indices |
| `/data/archive` | `LuceneServer:ArchivePath` | the archive (restore after a restart) |

Mount a volume (or a host directory) on `/data`. A host directory must be writable for the user
`app` (uid `1654`).

!!! danger "One writing instance per index"
    A Lucene index may only be written by **one** process. Lucene protects an index with a
    `write.lock` file; a second container on the same volume fails with lock errors or, on network
    file systems with unreliable locks, can corrupt the index. Do **not** scale the server by starting
    several replicas on one shared volume. Run one instance per volume.

## Configuration

The image needs no config file: every setting is read from environment variables
(`:` becomes `__`, see [Configuration](../configuration/index.md)).

```bash
docker run -d -p 8080:8080 -v luceneserver-data:/data \
  -e Authorization__Type=basic \
  -e Authorization__Clients__0__Id=admin \
  -e Authorization__Clients__0__SecretHash='<hash>' \
  -e Authorization__Clients__0__Roles__0=admin \
  luceneserver-net
```

Create the hash with `docker run --rm luceneserver-net --hash-secret <secret>`.

Defaults set by the image:

| Variable | Value |
|---|---|
| `ASPNETCORE_HTTP_PORTS` | `8080` |
| `LUCENESERVER__ROOTPATH` | `/data/indices` |
| `LUCENESERVER__ARCHIVEPATH` | `/data/archive` |

## Health check

The image has a Docker `HEALTHCHECK` on [`/health`](operations.md#health-endpoints); `docker ps` shows the
container as `healthy` once the index directories are writable.

## GitHub Container Registry

`./build.cmd DockerPush` pushes `ghcr.io/jugstalt/luceneserver-net:{version}` and `:latest`.
Log in first with a token that has the `write:packages` scope:

```bash
docker login ghcr.io -u <user>
```
