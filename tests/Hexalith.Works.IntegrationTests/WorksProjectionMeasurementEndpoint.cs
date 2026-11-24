using Dapr.Client;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Uses the local sidecar from the disposable measurement launcher.</summary>
internal static class WorksProjectionMeasurementEndpoint
{
    internal static DaprClient CreateClient()
    {
        string? endpoint = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_GRPC");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? address)
            || address.Scheme != Uri.UriSchemeHttp || address.Host != "127.0.0.1")
        {
            throw new InvalidOperationException("The projection measurement requires a disposable loopback Dapr gRPC endpoint.");
        }

        return new DaprClientBuilder().UseGrpcEndpoint(address.ToString()).Build();
    }
}
