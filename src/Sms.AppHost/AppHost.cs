var builder = DistributedApplication.CreateBuilder(args);

// Observability: SigNoz (ClickHouse + ZooKeeper + SigNoz + OTel collector). See SigNoz.cs.
var signoz = builder.AddSigNoz();

// Mail catcher for confirmation / reset / invitation mails during development.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.29")
    .WithHttpEndpoint(targetPort: 8025, name: "http")
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

var web = builder.AddViteApp("web", "../sms-web")
    .WithReference(api).WaitFor(api)
    .WithEnvironment("OTLP_HTTP", signoz.OtlpHttp)
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", u => u.DisplayText = "Sailing portal");

builder.Build().Run();
