using System.Text.Json;

using Aspire.Hosting.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Narrows EventStore readiness to the command-path dependency needed by the Works live lanes.
/// </summary>
internal static class WorksAppHostTestReadiness
{
    /// <summary>Keeps expected AppHost/Dapr diagnostics from flooding the in-process runner.</summary>
    public static void ConfigureHarnessLogging(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.Services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Critical));
    }

    /// <summary>
    /// Waits until the EventStore Dapr actor host has joined placement.
    /// </summary>
    /// <remarks>
    /// EventStore's aggregate command path requires actor placement. Its aggregate <c>/ready</c> endpoint also
    /// includes the independently operated projection-writer cutover, so the endpoint can correctly remain 503
    /// after the command path is usable. Development responses expose the individual checks; these live tests
    /// wait for only the load-bearing <c>dapr-actor-placement</c> entry.
    /// </remarks>
    public static async Task WaitForEventStoreCommandRuntimeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        string lastDiagnostic = "No readiness response was received.";

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await client
                    .GetAsync("/ready", cancellationToken)
                    .ConfigureAwait(false);
                string body = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                lastDiagnostic = $"Status={(int)response.StatusCode} ({response.StatusCode}); Body={body}";

                using JsonDocument document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("results", out JsonElement results)
                    && results.TryGetProperty("dapr-actor-placement", out JsonElement placement)
                    && placement.TryGetProperty("status", out JsonElement status)
                    && string.Equals(status.GetString(), "Healthy", StringComparison.Ordinal))
                {
                    // The Works sidecar starts its own /alive probes before the Works process binds its port.
                    // Give the default five-second Dapr probe interval one complete pass so direct service
                    // invocation no longer reports the otherwise-transient "app unhealthy" response.
                    await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (HttpRequestException ex)
            {
                lastDiagnostic = $"{ex.GetType().Name}: {ex.Message}";
            }
            catch (JsonException ex)
            {
                lastDiagnostic = $"{ex.GetType().Name}: {ex.Message}";
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "EventStore did not report healthy Dapr actor placement within 60 seconds. " + lastDiagnostic);
    }
}
