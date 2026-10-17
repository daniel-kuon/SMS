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

// Optional self-hosted base map: Martin serves media/tiles/*.pmtiles (built with tools/build-basemap.sh); the API
// switches the map style to it when the file exists.
var basemap = builder.AddContainer("basemap", "ghcr.io/maplibre/martin", "latest") // ghcr publishes no version tags
    .WithArgs("/data")
    .WithBindMount("../../media/tiles", "/data")
    .WithHttpEndpoint(port: 5161, targetPort: 3000, name: "http")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Martin tiles");

var api = builder.AddProject<Projects.Sms_Api>("api")
    .WithReference(db).WaitFor(db)
    .WithEnvironment("SMTP_ENDPOINT", mailpit.GetEndpoint("smtp"))
    .WithEnvironment("Map__BaseTilesUrl", "http://localhost:5161/basemap")
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
