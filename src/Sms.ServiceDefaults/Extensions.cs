using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Common Aspire services: service discovery, resilience, health checks and OpenTelemetry.
/// Telemetry is exported twice: to the Aspire dashboard (OTEL_EXPORTER_OTLP_ENDPOINT, set by the AppHost)
/// and to the SigNoz collector (SIGNOZ_OTLP_ENDPOINT, set by the AppHost).
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>Name of the ActivitySource / Meter used for the application's own spans and metrics.</summary>
    public const string ActivitySourceName = "Sms";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(ActivitySourceName);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource(ActivitySourceName)
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                        // Exclude health check requests from tracing
                        o.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
                    })
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Aspire dashboard: the default (unnamed) OTLP exporter reads OTEL_EXPORTER_OTLP_* from the environment.
        var useAspire = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        // SigNoz: a second, named exporter with an explicit endpoint.
        var useSignoz = Uri.TryCreate(builder.Configuration["SIGNOZ_OTLP_ENDPOINT"], UriKind.Absolute, out var signozUri);

        void ConfigureSignoz(OtlpExporterOptions o)
        {
            o.Endpoint = signozUri!;
            o.Protocol = OtlpExportProtocol.Grpc;
            o.Headers = string.Empty; // do not forward the Aspire dashboard API key
        }

        builder.Services.AddOpenTelemetry()
            .WithTracing(t =>
            {
                if (useAspire) t.AddOtlpExporter();
                if (useSignoz) t.AddOtlpExporter("signoz", ConfigureSignoz);
            })
            .WithMetrics(m =>
            {
                if (useAspire) m.AddOtlpExporter();
                if (useSignoz) m.AddOtlpExporter("signoz", ConfigureSignoz);
            })
            .WithLogging(l =>
            {
                if (useAspire) l.AddOtlpExporter();
                if (useSignoz) l.AddOtlpExporter("signoz", ConfigureSignoz);
            });

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health endpoints are only mapped in Development; see https://aka.ms/aspire/healthchecks
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }
        return app;
    }
}
