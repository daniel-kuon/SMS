var builder = DistributedApplication.CreateBuilder(args);

// Observability: SigNoz (ClickHouse + ZooKeeper + SigNoz + OTel collector). See SigNoz.cs.
var signoz = builder.AddSigNoz();

// Mail catcher for confirmation / reset / invitation mails during development.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.29")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "http") // fixed port: e2e tests read confirmation mails via http://localhost:8025/api/v1
    .WithEndpoint(targetPort: 1025, scheme: "smtp", name: "smtp")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Mailpit inbox");

// PostgreSQL with PostGIS + pgRouting (spec 08/10).
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgrouting/pgrouting")
    .WithImageTag("17-3.5-4.0.1")
    .WithDataVolume("sms-postgres-data");
var db = postgres.AddDatabase("sms");

var api = builder.AddProject<Projects.Sms_Api>("api")
    .WithReference(db).WaitFor(db)
    .WithEnvironment("SMTP_ENDPOINT", mailpit.GetEndpoint("smtp"))
    .WithEnvironment("SIGNOZ_OTLP_ENDPOINT", signoz.OtlpGrpc)
    .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "deployment.environment=development")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Vector tile server for the OpenSeaMap sea-mark overlay (reads the Seamarks table filled by the API).
var tiles = builder.AddProject<Projects.Sms_Tiles>("tiles")
    .WithReference(db).WaitFor(api)
    .WithEnvironment("SIGNOZ_OTLP_ENDPOINT", signoz.OtlpGrpc)
    .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "deployment.environment=development")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var web = builder.AddViteApp("web", "../sms-web")
    .WithReference(api).WaitFor(api)
    .WithReference(tiles)
    .WithEnvironment("OTLP_HTTP", signoz.OtlpHttp)
    .WithExternalHttpEndpoints()
    .WithEndpoint("http", e => e.Port = 5173) // fixed port for bookmarks and the Playwright e2e tests
    .WithUrlForEndpoint("http", u => u.DisplayText = "Sailing portal");

builder.Build().Run();
