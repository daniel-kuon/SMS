using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Sms.Api;
using Sms.Api.Endpoints;
using Sms.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
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
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddSingleton<HtmlSanitizerService>();
builder.Services.AddSingleton<MediaStorage>();
builder.Services.AddSingleton<MailSender>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<Access>();
builder.Services.AddSingleton<FeedTokens>();
builder.Services.AddHttpClient("overpass");
builder.Services.AddHttpClient<EcbRates>();
builder.Services.AddHostedService<EcbRateJob>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    o.SerializerOptions.Converters.Add(new NetTopologySuite.IO.Converters.GeoJsonConverterFactory());
});

var app = builder.Build();

app.UseExceptionHandler();
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
api.MapAuth();
api.MapProfiles();
api.MapShips();
api.MapHarbours();
api.MapTrips();
api.MapLogbook();
api.MapPosts();
api.MapMedia();
api.MapCosts();
api.MapTasks();
api.MapPages();
api.MapCalendar();

app.MapDefaultEndpoints();

// Apply migrations + seed on start (development convenience; use a migration job in production).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    await db.Database.MigrateAsync();
    await Seed.RunAsync(scope.ServiceProvider);
}

app.Run();

public partial class Program;
