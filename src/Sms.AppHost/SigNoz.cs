using Aspire.Hosting.ApplicationModel;

/// <summary>
/// SigNoz single-node stack as Aspire resources. Mirrors the last upstream docker-compose
/// (signoz v0.116.1, deploy/docker) before SigNoz moved to the Foundry installer:
/// ZooKeeper + ClickHouse (+ one-off init and schema migrator) + SigNoz + SigNoz OTel collector.
/// Config files live in infra/signoz. Data lives in named Docker volumes and survives restarts.
/// </summary>
public static class SigNozResources
{
    private const string ClickHouseImage = "clickhouse/clickhouse-server";
    private const string ClickHouseTag = "25.5.6";
    private const string SigNozTag = "v0.116.1";
    private const string CollectorTag = "v0.144.2";
    private const string UserScriptsVolume = "sms-signoz-clickhouse-user-scripts";
    private const string ClickHouseDsn = "tcp://clickhouse:9000";

    public sealed record SigNozStack(
        IResourceBuilder<ContainerResource> Ui,
        IResourceBuilder<ContainerResource> Collector)
    {
        /// <summary>OTLP/gRPC endpoint of the collector (for .NET services).</summary>
        public EndpointReference OtlpGrpc => Collector.GetEndpoint("otlp-grpc");
        /// <summary>OTLP/HTTP endpoint of the collector (for the browser).</summary>
        public EndpointReference OtlpHttp => Collector.GetEndpoint("otlp-http");
    }

    public static SigNozStack AddSigNoz(this IDistributedApplicationBuilder builder)
    {
        var cfg = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "infra", "signoz"));

        // Downloads the histogramQuantile UDF binary used by SigNoz metrics queries into a shared volume.
        var init = builder.AddContainer("signoz-init-clickhouse", ClickHouseImage, ClickHouseTag)
            .WithEntrypoint("bash")
            .WithArgs("-c", """
                set -e
                target=/var/lib/clickhouse/user_scripts/histogramQuantile
                if [ -f "$target" ]; then echo "histogramQuantile already present"; exit 0; fi
                version="v0.0.1"
                node_os=$(uname -s | tr '[:upper:]' '[:lower:]')
                node_arch=$(uname -m | sed s/aarch64/arm64/ | sed s/x86_64/amd64/)
                echo "Fetching histogram-binary for ${node_os}/${node_arch}"
                cd /tmp
                wget -O histogram-quantile.tar.gz "https://github.com/SigNoz/signoz/releases/download/histogram-quantile%2F${version}/histogram-quantile_${node_os}_${node_arch}.tar.gz"
                tar -xvzf histogram-quantile.tar.gz
                mv histogram-quantile "$target"
                """)
            .WithVolume(UserScriptsVolume, "/var/lib/clickhouse/user_scripts/");

        var zookeeper = builder.AddContainer("zookeeper-1", "signoz/zookeeper", "3.7.1")
            .WithContainerRuntimeArgs("--user", "root")
            .WithEnvironment("ZOO_SERVER_ID", "1")
            .WithEnvironment("ALLOW_ANONYMOUS_LOGIN", "yes")
            .WithEnvironment("ZOO_AUTOPURGE_INTERVAL", "1")
            .WithEnvironment("ZOO_ENABLE_PROMETHEUS_METRICS", "yes")
            .WithEnvironment("ZOO_PROMETHEUS_METRICS_PORT_NUMBER", "9141")
            .WithVolume("sms-signoz-zookeeper-1", "/bitnami/zookeeper");

        var clickhouse = builder.AddContainer("clickhouse", ClickHouseImage, ClickHouseTag)
            .WithContainerRuntimeArgs("--ulimit", "nofile=262144:262144", "--ulimit", "nproc=65535")
            .WithEnvironment("CLICKHOUSE_SKIP_USER_SETUP", "1")
            .WithBindMount(Path.Combine(cfg, "clickhouse", "config.xml"), "/etc/clickhouse-server/config.xml", isReadOnly: true)
            .WithBindMount(Path.Combine(cfg, "clickhouse", "users.xml"), "/etc/clickhouse-server/users.xml", isReadOnly: true)
            .WithBindMount(Path.Combine(cfg, "clickhouse", "custom-function.xml"), "/etc/clickhouse-server/custom-function.xml", isReadOnly: true)
            .WithBindMount(Path.Combine(cfg, "clickhouse", "cluster.xml"), "/etc/clickhouse-server/config.d/cluster.xml", isReadOnly: true)
            .WithVolume(UserScriptsVolume, "/var/lib/clickhouse/user_scripts/")
            .WithVolume("sms-signoz-clickhouse", "/var/lib/clickhouse/")
            .WithHttpEndpoint(targetPort: 8123, name: "http")
            .WithHttpHealthCheck("/ping", endpointName: "http")
            .WaitFor(zookeeper)
            .WaitForCompletion(init);

        var migrator = builder.AddContainer("signoz-schema-migrator", "signoz/signoz-otel-collector", CollectorTag)
            .WithEntrypoint("/bin/sh")
            .WithArgs("-c", "/signoz-otel-collector migrate bootstrap && /signoz-otel-collector migrate sync up && /signoz-otel-collector migrate async up")
            .WithCollectorClickHouseEnvironment()
            .WaitFor(clickhouse);

        var signoz = builder.AddContainer("signoz", "signoz/signoz", SigNozTag)
            .WithEnvironment("SIGNOZ_ALERTMANAGER_PROVIDER", "signoz")
            .WithEnvironment("SIGNOZ_TELEMETRYSTORE_CLICKHOUSE_DSN", ClickHouseDsn)
            .WithEnvironment("SIGNOZ_SQLSTORE_SQLITE_PATH", "/var/lib/signoz/signoz.db")
            .WithEnvironment("SIGNOZ_TOKENIZER_JWT_SECRET", "local-dev-secret-change-in-production")
            .WithVolume("sms-signoz-sqlite", "/var/lib/signoz/")
            .WithHttpEndpoint(port: 3302, targetPort: 8080, name: "http")
            .WithHttpHealthCheck("/api/v1/health")
            .WithUrlForEndpoint("http", u => u.DisplayText = "SigNoz UI")
            .WaitFor(clickhouse)
            .WaitForCompletion(migrator);

        var collector = builder.AddContainer("signoz-otel-collector", "signoz/signoz-otel-collector", CollectorTag)
            .WithEntrypoint("/bin/sh")
            .WithArgs("-c", "/signoz-otel-collector migrate sync check && /signoz-otel-collector --config=/etc/otel-collector-config.yaml --manager-config=/etc/manager-config.yaml --copy-path=/var/tmp/collector-config.yaml")
            .WithBindMount(Path.Combine(cfg, "otel-collector-config.yaml"), "/etc/otel-collector-config.yaml", isReadOnly: true)
            .WithBindMount(Path.Combine(cfg, "otel-collector-opamp-config.yaml"), "/etc/manager-config.yaml", isReadOnly: true)
            .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "host.name=signoz-host,os.type=linux")
            .WithEnvironment("LOW_CARDINAL_EXCEPTION_GROUPING", "false")
            .WithCollectorClickHouseEnvironment()
            .WithEndpoint(targetPort: 4317, scheme: "http", name: "otlp-grpc")
            .WithHttpEndpoint(targetPort: 4318, name: "otlp-http")
            .WaitFor(signoz)
            .WaitForCompletion(migrator);

        return new SigNozStack(signoz, collector);
    }

    private static IResourceBuilder<ContainerResource> WithCollectorClickHouseEnvironment(this IResourceBuilder<ContainerResource> b) => b
        .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_CLICKHOUSE_DSN", ClickHouseDsn)
        .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_CLICKHOUSE_CLUSTER", "cluster")
        .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_CLICKHOUSE_REPLICATION", "true")
        .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_TIMEOUT", "10m");
}
