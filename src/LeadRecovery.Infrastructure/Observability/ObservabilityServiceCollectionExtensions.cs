using System.Diagnostics;
using System.Reflection;

using LeadRecovery.Infrastructure.Integrations.Twilio;
using LeadRecovery.Infrastructure.Messaging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LeadRecovery.Infrastructure.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddLeadRecoveryObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string environmentName,
        bool instrumentAspNetCore)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        Uri? otlpEndpoint = GetOtlpEndpoint(configuration);
        string serviceVersion = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        IOpenTelemetryBuilder telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>(
                        "deployment.environment.name",
                        environmentName),
                ]));

        telemetry.WithTracing(tracing =>
        {
            _ = tracing
                .AddSource(LeadRecoveryTelemetry.ActivitySourceName)
                .AddSource("Npgsql")
                .AddHttpClientInstrumentation();
            if (instrumentAspNetCore)
            {
                _ = tracing.AddAspNetCoreInstrumentation();
            }

            if (otlpEndpoint is not null)
            {
                _ = tracing.AddOtlpExporter(options => options.Endpoint = otlpEndpoint);
            }
        });

        telemetry.WithMetrics(metrics =>
        {
            _ = metrics
                .AddMeter(LeadRecoveryTelemetry.MeterName)
                .AddMeter(SmsMetrics.MeterName)
                .AddMeter(CallStatusMetrics.MeterName)
                .AddMeter("Npgsql")
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();
            if (instrumentAspNetCore)
            {
                _ = metrics.AddAspNetCoreInstrumentation();
            }

            if (otlpEndpoint is not null)
            {
                _ = metrics.AddOtlpExporter(options => options.Endpoint = otlpEndpoint);
            }
        });

        return services;
    }

    private static Uri? GetOtlpEndpoint(IConfiguration configuration)
    {
        string? configured = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp &&
                endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "OTEL_EXPORTER_OTLP_ENDPOINT must be an absolute HTTP or HTTPS URL.");
        }

        return endpoint;
    }
}
