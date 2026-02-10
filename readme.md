# BaGetter-Reloaded :baguette_bread:  <!-- omit in toc -->

A lightweight [NuGet](https://docs.microsoft.com/en-us/nuget/what-is-nuget) server used for hosting Reloaded II mods based on [BaGetter](https://github.com/bagetter/BaGetter).

![Image](https://i.imgur.com/7n43mp1.png)

## About This Fork

This is a fork of BaGetter, specifically intended to be used with Reloaded-II mods.

- [About This Fork](#about-this-fork)
  - [Changes](#changes)
  - [Setup Instructions](#setup-instructions)
    - [Install .NET 9 SDK (Ubuntu 24.04)](#install-net-9-sdk-ubuntu-2404)
    - [Create the bootup script.](#create-the-bootup-script)
    - [Create a systemd service.](#create-a-systemd-service)
    - [Enable and start systemd service.](#enable-and-start-systemd-service)
  - [Deploying Updates (Framework-dependent)](#deploying-updates-framework-dependent)
- [Maintenance](#maintenance)
  - [Updating BaGetter-Reloaded (Quick)](#updating-bagetter-reloaded-quick)
- [Getting Started](#getting-started)
  - [Features](#features)
  - [Develop](#develop)

### Changes

- Themed.  
- Supports HTTPS.  
- Supports Response Compression.  
- Upstream with .NET 9 runtime.  
- Removed IIS-only startup option binding for non-IIS deployments (Kestrel/systemd).  
- Returns readme & changelog in search.  
- Custom auth. Associates each package with key at first upload.  
- Search by tag & title.  

### Setup Instructions
Based on Ubuntu Server 24.04.  

#### Install .NET 9 SDK (Ubuntu 24.04)

Install .NET 9 from Ubuntu's backports feed.

```bash
sudo add-apt-repository ppa:dotnet/backports
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0
dotnet --info
```

If Snap `dotnet` exists on the host, use an explicit runtime path in service scripts (`/usr/bin/dotnet`) to avoid conflicts.

Using Snap for production runtime is *not recommended*.
In past cases, unattended Snap updates broke startup, and Snap-based runtime
usage also showed higher memory overhead.

#### Create the bootup script.
Script to start the application.

`> sudo nano /opt/systemd/start-baget-on-boot.sh`

```sh
#!/bin/sh

DATE=$(date '+%Y-%m-%d %H:%M:%S')
echo "BaGet Service Started at ${DATE}" | systemd-cat -p info

cd /opt/baget-reloaded/
exec systemd-cat -t baget-reloaded /usr/bin/dotnet /opt/baget-reloaded/BaGetter.dll
```

#### Create a systemd service.

This runs the server at startup.

`> sudo nano /usr/lib/systemd/system/baget-reloaded.service`

```ini
[Unit]
Description=Reloaded II BaGet Server.

[Service]
Type=simple
ExecStart=/bin/sh /opt/systemd/start-baget-on-boot.sh
Restart=always

[Install]
WantedBy=multi-user.target
```

#### Enable and start systemd service.

```bash
sudo systemctl daemon-reload
sudo systemctl enable baget-reloaded.service
sudo systemctl restart baget-reloaded.service
sudo systemctl status baget-reloaded.service
```

### Deploying Updates (Framework-dependent)

The current production flow uses a framework-dependent publish and deploys only app files while preserving config/data on server.

```bash
# 1) Publish locally
dotnet publish src/BaGetter/BaGetter.csproj -c Release -o /tmp/baget-fdd-clean \
  -p:UseAppHost=false -p:CopyLocalLockFileAssemblies=false

# 2) Sync publish output to server (keep appsettings/db/packages)
rsync -av --delete \
  --exclude 'appsettings*.json' \
  --exclude 'baget.db*' \
  --exclude 'packages/' \
  -e "ssh -i <REDACTED_SSH_KEY>" \
  /tmp/baget-fdd-clean/ root@<REDACTED_IP>:/opt/baget-reloaded/

# 3) Optionally deploy a specific appsettings.json
scp -i <REDACTED_SSH_KEY> /path/to/appsettings.json \
  root@<REDACTED_IP>:/opt/baget-reloaded/appsettings.json

# 4) Restart and verify
ssh -i <REDACTED_SSH_KEY> root@<REDACTED_IP> \
  "systemctl daemon-reload && \
   systemctl restart baget-reloaded.service && \
   systemctl is-active baget-reloaded.service && \
   journalctl -u baget-reloaded.service -n 50 --no-pager"
```

If your SSH key is already configured in `~/.ssh/config` or agent, remove the `-i <REDACTED_SSH_KEY>` parts.

## Maintenance

### Updating BaGetter-Reloaded (Quick)

Stop, replace files, then restart:

```bash
sudo systemctl stop baget-reloaded.service
# copy published files to /opt/baget-reloaded
sudo systemctl start baget-reloaded.service
```

Use [Certificate Sources on MSDN](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-5.0#certificate-sources) as reference.

## Getting Started

1. Install [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
2. Download and extract [BaGetter's latest release](https://github.com/bagetter/BaGetter/releases)
3. Start the service with `dotnet BaGetter.dll`
4. Browse `http://localhost:5000/` in your browser

For more information, please refer to [our documentation](https://www.bagetter.com/).

### Features

* Cross-platform
* [Dockerized](https://www.bagetter.com/docs/Installation/docker)
* [Cloud ready](https://www.bagetter.com/docs/Installation/azure)
* [Supports read-through caching](https://www.bagetter.com/docs/configuration#enable-read-through-caching)
* Can index the entirety of nuget.org. See [this documentation](https://www.bagetter.com/docs/Import/nugetorg)

### Develop

1. Install [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and [Node.js](https://nodejs.org/)
2. Run `git clone https://github.com/bagetter/BaGetter.git`
3. Navigate to `./BaGetter/src/BaGetter`
4. Start the service with `dotnet run`
5. Open the URL `http://localhost:5000/v3/index.json` in your browser
