using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGetter;
using BaGetter.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGetter.Web.Tests;

public sealed class CustomPagesPreservationFacts : IDisposable
{
    private readonly string _tempPath;
    private readonly WebApplicationFactory<Startup> _factory;
    private readonly HttpClient _client;

    public CustomPagesPreservationFacts()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "BaGetterWebTests", Guid.NewGuid().ToString("N"));
        var sqlitePath = Path.Combine(_tempPath, "BaGetter.Web.Tests.db");

        Directory.CreateDirectory(_tempPath);

        _factory = new WebApplicationFactory<Startup>().WithWebHostBuilder(builder =>
        {
            builder
                .UseEnvironment("Production")
                .ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "Database:ConnectionString", $"Data Source={sqlitePath}" },
                        { "Storage:Path", Path.Combine(_tempPath, "Packages") },
                    });
                });
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IContext>().Database.Migrate();

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();

        try
        {
            Directory.Delete(_tempPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task AboutPage_RendersReloadedCustomMarkers()
    {
        using var response = await _client.GetAsync("/About");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Reloaded II Community NuGet Repository", html);
        Assert.Contains("This service is free and open in good faith", html);
    }

    [Fact]
    public async Task UploadPage_RendersStorageUsageAndProgressBar()
    {
        using var response = await _client.GetAsync("/Upload");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>Storage Usage</h1>", html);
        Assert.Contains("progress-bar progress-bar-danger", html);
        Assert.Matches("style=\"width: [0-9]+(\\.[0-9]+)?%\"", html);
        Assert.Matches("[0-9]+(\\.[0-9]+)? / [0-9]+(\\.[0-9]+)? GB", html);
    }

    [Fact]
    public async Task Layout_NavContainsReloadedLinks()
    {
        var html = await _client.GetStringAsync("/About");

        Assert.Contains("href=\"/about\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">About<", html);
        Assert.Contains(">Source Code<", html);
        Assert.Contains(">Reloaded Documentation<", html);
        Assert.Contains("https://github.com/Sewer56/BaGet-ReloadedII", html);
        Assert.Contains("https://reloaded-project.github.io/Reloaded-II/", html);
    }

    [Fact]
    public async Task IndexPage_UsesSimplifiedPrereleaseFilterUi()
    {
        var html = await _client.GetStringAsync("/");

        Assert.Contains("Include prerelease.", html);
        Assert.Contains("form-group checkbox", html);
        Assert.DoesNotContain("Package type:", html);
        Assert.DoesNotContain("Framework:", html);
    }

    [Fact]
    public async Task PackageNotFound_UsesReloadedHelpLinks()
    {
        using var response = await _client.GetAsync("/packages/PackageDoesNotExist");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("not found", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://github.com/Sewer56/BaGet-ReloadedII/blob/reloaded-ii/README.md", html);
        Assert.Contains("https://github.com/Sewer56/BaGet-ReloadedII/issues", html);
    }
}
