# Installation

LuceneServer.NET is an ASP.NET Core application (.NET 10). There are no prebuilt binaries yet,
so the server is built from source.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build, the ASP.NET Core 10 runtime to run
- Windows or Linux
- A directory for the index files (see [`RootPath`](../configuration/index.md))

## Build

```bash
git clone https://github.com/jugstalt/LuceneServerNET.git
cd LuceneServerNET
dotnet publish src/LuceneServerNET -c Release -o ./publish
```

## First start

```bash
cd publish
dotnet LuceneServerNET.dll
```

On the first start the server creates `_config/luceneserver.json` from the template
`_config/proto/_luceneserver.json` and prints it to the console. Adjust this file for your
environment, see [Configuration](../configuration/index.md).

- **Windows:** the index directory defaults to a `data` folder a few levels above the application.
- **Linux:** the data directory is taken from the environment variable `LUCENESERVER_DATA_PATH`
  (default `/etc/luceneserver/data`). If `LUCENESERVER__ROOTPATH` is set, no config file is created
  and the settings are read from environment variables only.

## Ports

By default the usual ASP.NET Core settings apply (`ASPNETCORE_URLS`, `--urls`). For quick tests the
server also accepts:

```bash
dotnet LuceneServerNET.dll -expose-http 5000
dotnet LuceneServerNET.dll -expose-https 5001
```

!!! note
    `-expose-http` / `-expose-https` bind to `localhost` only. Use `ASPNETCORE_URLS`
    (e.g. `http://0.0.0.0:5000`) to listen on all interfaces.

Check that the server runs:

```bash
curl http://localhost:5000/info
```

## Hosting

### IIS (Windows)

The publish output contains a `web.config`. Install the
[ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0), create a website or
application pointing to the publish folder and use an application pool with *No Managed Code*.
The application pool identity needs write access to `RootPath` (and `ArchivePath`).

### systemd (Linux)

```ini
# /etc/systemd/system/luceneserver.service
[Unit]
Description=LuceneServer.NET
After=network.target

[Service]
WorkingDirectory=/opt/luceneserver
ExecStart=/usr/bin/dotnet /opt/luceneserver/LuceneServerNET.dll
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=LUCENESERVER_DATA_PATH=/var/lib/luceneserver
Restart=always
User=luceneserver

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now luceneserver
```

### Windows service

The server does not register itself as a Windows service. On Windows, hosting in **IIS** is
recommended.

### Docker

See [Docker](docker.md).

## Swagger

With `"useSwagger": true` (or in the `Development` environment) the Swagger UI is available at
`/swagger`.
