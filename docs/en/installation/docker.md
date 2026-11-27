# Docker

The image `ghcr.io/jugstalt/luceneserver-net` is based on the ASP.NET Core 11 Alpine runtime image and runs the server as the
non-root user `app`. The NUKE build publishes the server and copies it into the image
(`deploy/docker/server/Dockerfile`); releases push it to the GitHub Container Registry.

## Get or build the image

```bash
docker pull ghcr.io/jugstalt/luceneserver-net
```

Or build it locally (tags `{version}` and `latest`; `--container-cli wslc` or `podman` instead of Docker):

```bash
./build.cmd DockerBuild
```

## Run

```bash
docker run -d --name luceneserver -p 8080:8080 -v luceneserver-data:/data ghcr.io/jugstalt/luceneserver-net
```

The server listens on port **8080** inside the container.

## Volumes

| Path in the container | Setting | Content |
|---|---|---|
| `/data/indices` | `LuceneServer:RootPath` | the Lucene indices |
| `/data/archive` | `LuceneServer:ArchivePath` | optional, not set by default: the archive (`-e LuceneServer__ArchivePath=/data/archive`), see [Configuration](../configuration/index.md) |

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
  ghcr.io/jugstalt/luceneserver-net
```

Create the hash with `docker run --rm ghcr.io/jugstalt/luceneserver-net --hash-secret <secret>`.

Defaults set by the image:

| Variable | Value |
|---|---|
| `ASPNETCORE_HTTP_PORTS` | `8080` |
| `LUCENESERVER__ROOTPATH` | `/data/indices` |
| `LUCENESERVER__ARCHIVEPATH` | `/data/archive` |

## Docker Compose (server + admin UI)

`deploy/docker/docker-compose.yml` starts the server (port `8080`) with Basic authentication and the
[admin UI](../admin.md) (port `8081`), which is connected to the server as instance `local`.

1. Copy `deploy/docker/.env.example` to `deploy/docker/.env` (ignored by git) and fill in the secrets:

    ```bash
    docker run --rm ghcr.io/jugstalt/luceneserver-net --hash-secret 'client-secret'      # LUCENE_ADMIN_CLIENT_SECRET_HASH
    docker run --rm ghcr.io/jugstalt/luceneserver-net-admin --hash-password 'password'   # ADMIN_PASSWORD_HASH
    ```

    `LUCENE_ADMIN_CLIENT_SECRET` is the same client secret in plain text. Keep the hashes in single quotes:
    they contain `$`.

2. Start:

    ```bash
    docker compose -f deploy/docker/docker-compose.yml up -d
    ```

`LUCENE_TAG` selects the image version (default `latest`). Images built locally with
`--container-cli wslc` are not visible to Docker Compose.

## Health check

The image has a Docker `HEALTHCHECK` on [`/health`](operations.md#health-endpoints); `docker ps` shows the
container as `healthy` once the index directories are writable.

## GitHub Container Registry

Releases (git tag `v*`) push `ghcr.io/jugstalt/luceneserver-net:{version}`, `:{major.minor}` and `:latest` from the GitHub workflow
`release.yml`; pre-releases only get `:{version}`. To push from your machine, log in with a token that has the
`write:packages` scope and run `./build.cmd DockerPush`:

```bash
docker login ghcr.io -u <user>
```
