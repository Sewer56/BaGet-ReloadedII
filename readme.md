# Reloaded II Repository :baguette_bread:

A lightweight [NuGet](https://docs.microsoft.com/en-us/nuget/what-is-nuget) server used for Hosting Reloaded II mods based on [BaGet](https://github.com/loic-sharma/BaGet).

![Image](https://i.imgur.com/7n43mp1.png)

## Modifications
- Themed in the style of Reloaded II Mod Loader.
- A basic "authentication-like" system. Each uploaded package is associated with a user provided API key. Only that API key can be used to update/delete that package.
- Minor changes such as a new homepage, instructions specifically for including dependencies in Reloaded-II and storage usage.

This repository is a bit of a hackjob: I have learned the bare minimum of ASP.NET and React from scratch to be able to make changes to the site.

Contributions welcome.

## Running

1. Install [.NET Core SDK](https://www.microsoft.com/net/download)
2. Download and extract [BaGet's latest release](https://github.com/loic-sharma/BaGet/releases)
3. Start the service with `dotnet BaGet.dll`
4. Browse `http://localhost:5000/` in your browser

Stay tuned, more features are planned!

## Building

1. Install [.NET Core SDK](https://www.microsoft.com/net/download) and [Node.js](https://nodejs.org/)
2. Run `git clone https://github.com/Sewer56/BaGet`
3. Navigate to `.\BaGet\src\BaGet.UI`
4. Install the frontend's dependencies with `npm install`
5. Navigate to `..\BaGet` (where `BaGet.csproj` resides)
6. Start the service with `dotnet run`
7. Open the URL `http://localhost:5000/v3/index.json` in your browser

If you would like to release a version ready for production, run the `dotnet publish` command.