using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Sms.Api;
using Sms.Api.Endpoints;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;
using Sms.Infrastructure.Tracking;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false); // binding failures -> 400, also in Development
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// Database (PostgreSQL + PostGIS via Aspire; connection string "sms" is injected by the AppHost).
builder.AddNpgsqlDbContext<SmsDbContext>("sms", configureDbContextOptions: o => o.UseNpgsql(n => n.UseNetTopologySuite()));

// Identity with cookie auth for the same-origin SPA (spec 11.2): 401/403 instead of redirects.
builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireDigit = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<SmsDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, o =>
    {
        o.Cookie.Name = "sms.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

// Rate limiting for auth endpoints (spec 07 §7.4).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authPerMinute = builder.Configuration.GetValue("RateLimiting:AuthPerMinute", 20);
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("ingest", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddSingleton<HtmlSanitizerService>();
builder.Services.AddSingleton<MediaStorage>();
builder.Services.AddSingleton<MailSender>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<Access>();
builder.Services.AddSingleton<FeedTokens>();
// Overpass queries run for minutes: no standard resilience handler (its 10 s attempt timeout would abort them).
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental
builder.Services.AddHttpClient("overpass", c => c.DefaultRequestHeaders.UserAgent.ParseAdd("sailing-portal/0.1 (+https://github.com/daniel-kuon/SMS)")).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient("routing");
builder.Services.AddHttpClient("marinetraffic");
builder.Services.AddSingleton<AisKeys>();
builder.Services.AddSingleton<PbfImportJob>();
builder.Services.AddHttpClient("weather", c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IWeatherProvider, OpenMeteoProvider>();
builder.Services.AddSingleton<IWeatherOverlayProvider, XweatherOverlayProvider>();
builder.Services.AddSingleton<ITideProvider, OpenWatersTideProvider>();
builder.Services.AddSingleton<IDepthProvider, SeascapeDepthProvider>();
builder.Services.AddScoped<RoutingAudit>();
builder.Services.AddSingleton<WeatherRecorder>();
builder.Services.AddSingleton<IAisPositionProvider, AisStreamProvider>();
builder.Services.AddSingleton<IAisPositionProvider, MarineTrafficProvider>();
builder.Services.AddSingleton<AisPoller>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AisPoller>());
builder.Services.AddSingleton<RoutingGraphBuilder>();
builder.Services.AddScoped<SeaRouter>();
builder.Services.AddSingleton<TrackProcessor>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddHttpClient<EcbRates>();
builder.Services.AddTransient<IExchangeRates>(sp => sp.GetRequiredService<EcbRates>());
builder.Services.AddHostedService<EcbRateJob>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    o.SerializerOptions.Converters.Add(new NetTopologySuite.IO.Converters.GeoJsonConverterFactory());
});

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions { StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError });
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// CSRF guard for the cookie-authenticated JSON API: cross-site forms cannot set custom headers,
// so state-changing requests must carry X-Requested-With (the SPA's HTTP interceptor adds it).
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsPost(ctx.Request.Method) || HttpMethods.IsPut(ctx.Request.Method) || HttpMethods.IsDelete(ctx.Request.Method) || HttpMethods.IsPatch(ctx.Request.Method))
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/tracking")
            && ctx.User.Identity?.IsAuthenticated == true && ctx.Request.Headers["X-Requested-With"] != "XMLHttpRequest")
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { title = "Missing X-Requested-With header." });
            return;
        }
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/api/docs");
}

var api = app.MapGroup("/api");
api.MapGet("/ping", () => Results.Ok(new { ok = true, time = DateTimeOffset.UtcNow })).WithName("Ping");
// Public client configuration (map style/tiles); only non-secret values belong here.
api.MapGet("/config", (IConfiguration cfg, IWebHostEnvironment env) =>
{
    var selfHosted = MapStyleEndpoints.BasemapAvailable(cfg, env); // Planetiler tiles + Martin (tools/build-basemap.sh)
    return Results.Ok(new
    {
        mapStyleUrl = selfHosted ? "/api/map/style.json" : cfg["Map:StyleUrl"] ?? MapStyleEndpoints.DefaultStyleUrl,
        seamarkTilesUrl = cfg["Map:SeamarkTilesUrl"] ?? "/tiles/seamark/{z}/{x}/{y}.mvt",
        attribution = selfHosted ? "© OpenStreetMap-Mitwirkende · OpenSeaMap · OpenMapTiles" : cfg["Map:Attribution"] ?? "© OpenStreetMap-Mitwirkende · OpenSeaMap · OpenFreeMap",
        selfHostedBasemap = selfHosted,
    });
}).WithName("Config");
api.MapMapStyle();
api.MapAuth();
api.MapProfiles();
api.MapShips();
api.MapHarbours();
api.MapSeamarks(); api.MapPois();
api.MapCms();
api.MapAis(); api.MapWeather();
api.MapRouting();
api.MapTracking();
api.MapSocial();
api.MapTrips();
api.MapLogbook();
api.MapPosts();
api.MapMedia();
api.MapCosts();
api.MapTasks();
api.MapPages();
api.MapLinkTargets();
api.MapCalendar();

app.MapDefaultEndpoints();

// Apply migrations + seed on start (development convenience; use a migration job in production).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment()) await Seed.RunAsync(scope.ServiceProvider); // dev-only demo admin + sample harbours
}

app.Run();

public partial class Program;
