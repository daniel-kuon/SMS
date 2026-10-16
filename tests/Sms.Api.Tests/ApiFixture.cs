extern alias Tiles;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Domain;
using Sms.Infrastructure;
using Testcontainers.PostgreSql;

namespace Sms.Api.Tests;

/// <summary>Real API against a throw-away PostGIS/pgRouting container (spec 11.1 "Testcontainers DB").</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("pgrouting/pgrouting:17-3.5-4.0.1").WithDatabase("sms").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private WebApplicationFactory<Tiles::Program> _tiles = null!;
    public HttpClient Tiles => _tiles.CreateClient();
    private int _users;

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:sms", _db.GetConnectionString());
            b.UseSetting("Media:RootPath", Path.Combine(Path.GetTempPath(), "sms-tests-media"));
            b.UseSetting("Web:BaseUrl", "http://localhost");
            b.UseSetting("Logging:LogLevel:Default", "Warning");
            b.UseSetting("RateLimiting:AuthPerMinute", "100000");
            b.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Error");
        });
        _ = _factory.Server; // boots the app: migrations + seed
        _tiles = new WebApplicationFactory<Tiles::Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:sms", _db.GetConnectionString());
            b.UseSetting("Logging:LogLevel:Default", "Warning");
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _tiles.DisposeAsync();
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient Anonymous() => Client();

    /// <summary>Signed-in client for the seeded platform admin (Development seed).</summary>
    public async Task<(HttpClient client, Guid id)> AdminAsync()
    {
        var client = Client();
        (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@localhost.dev", password = "SailingPortal2026!" })).EnsureSuccessStatusCode();
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        return (client, me.GetProperty("id").GetGuid());
    }

    /// <summary>Creates a confirmed user with a profile and returns a signed-in client plus the user id.</summary>
    public async Task<(HttpClient client, Guid id)> UserAsync(string? name = null)
    {
        name ??= $"user{Interlocked.Increment(ref _users)}";
        var email = $"{name}@test.local";
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await users.CreateAsync(user, "Passw0rd123");
            if (!result.Succeeded) throw new InvalidOperationException(string.Join(",", result.Errors.Select(e => e.Description)));
            db.Profiles.Add(new UserProfile { UserId = user.Id, DisplayName = name });
            await db.SaveChangesAsync();
            id = user.Id;
        }
        var client = Client();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd123" });
        login.EnsureSuccessStatusCode();
        return (client, id);
    }

    private HttpClient Client()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        c.DefaultRequestHeaders.Add("Origin", "http://localhost");
        return c;
    }
}
