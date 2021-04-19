# Reloaded II Repository :baguette_bread:

A lightweight [NuGet](https://docs.microsoft.com/en-us/nuget/what-is-nuget) server used for Hosting Reloaded II mods based on [BaGet](https://github.com/loic-sharma/BaGet).

![Image](https://i.imgur.com/7n43mp1.png)

# Modifications
- Themed in the style of Reloaded II Mod Loader.
- A basic "authentication-like" system. Each uploaded package is associated with a user provided API key. Only that API key (or a master key specified in config file) can be used to update/delete that package.
- Minor changes such as a new homepage, instructions specifically for including dependencies in Reloaded-II and storage usage.

This repository is a bit of a hackjob: I have learned the bare minimum of ASP.NET and React from scratch to be able to make changes to the site.

Contributions welcome.

# Development Instructions

## Running

1. Install [.NET Core SDK (2.2)](https://www.microsoft.com/net/download)
2. Download and extract [BaGet's latest release](https://github.com/Sewer56/BaGet-ReloadedII/releases)
3. Start the service with `dotnet BaGet.dll`
4. Browse `http://localhost:5000/` in your browser

Stay tuned, more features are planned!

## Building

1. Install [.NET Core SDK (2.2)](https://www.microsoft.com/net/download) and [Node.js](https://nodejs.org/)
2. Run `git clone https://github.com/Sewer56/BaGet-ReloadedII`
3. Navigate to `.\BaGet\src\BaGet.UI`
4. Install the frontend's dependencies with `npm install`
5. Navigate to `..\BaGet` (where `BaGet.csproj` resides)
6. Start the service with `dotnet run`
7. Open the URL `http://localhost:5000/v3/index.json` in your browser

If you would like to release a version ready for production, run the `dotnet publish` command.

# VPS Maintenance Instructions

This is documentation for personal use.<br/>
Based on Ubuntu Server 19.04.

## Installing .NET Core SDK

```
sudo snap install dotnet-sdk --channel=2.2/stable --classic
snap alias dotnet-sdk.dotnet dotnet
```

## Launching Server at Startup

### First create the bootup script.

`> nano /opt/start-baget-on-boot.sh`

```sh
#!/bin/sh

# Can be set in config too; see https://docs.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-3.1#endpoint-configuration
export ASPNETCORE_URLS=http://*:5000

DATE=`date '+%Y-%m-%d %H:%M:%S'`
echo "BaGet Service Started at ${DATE}" | systemd-cat -p info

cd /opt/reloaded-ii/
dotnet /opt/reloaded-ii/BaGet.dll
```

### Then create a systemd service.

`> sudo nano /lib/systemd/system/reloaded-ii.service`

```ini
[Unit]
Description=Reloaded II BaGet Server.

[Service]
Type=simple
ExecStart=/bin/sh /opt/start-baget-on-boot.sh

[Install]
WantedBy=multi-user.target
```

### Then enable and start the service.

```
sudo systemctl enable reloaded-ii.service
sudo systemctl start reloaded-ii.service
```

## Maintenance

### Updating BaGet-Reloaded

First stop the active server process:
```
sudo systemctl stop reloaded-ii.service
```

Then, using SFTP (or other method), overwrite published version in `/opt/reloaded-ii`.

Lastly, restart the service.

```
sudo systemctl start reloaded-ii.service
```

### Freeing Up Disk
To free up disk space, might be a good idea (on Ubuntu Server) to clean up old Snaps after updates.

```
chmod +x remove-old-snaps
sudo ./remove-old-snaps
```

```sh
#!/bin/sh
# Removes old revisions of snaps
# CLOSE ALL SNAPS BEFORE RUNNING THIS
set -eu
LANG=en_US.UTF-8

snap list --all | awk '/disabled/{print $1, $3}' |
while read snapname revision; do
    snap remove "$snapname" --revision="$revision"
done
```
