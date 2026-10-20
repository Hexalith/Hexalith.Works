using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Hexalith.Works.Projections;
using Hexalith.Works.Reminders;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Narrows EventStore readiness to the command-path dependency needed by the Works live lanes.
/// </summary>
internal static class WorksAppHostTestReadiness
{
    private static readonly Version MinimumDaprRuntimeVersion = new(1, 18, 3);
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

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

        DateTime deadline = DateTime.UtcNow.AddSeconds(120);
        string lastDiagnostic = "No readiness response was received.";

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestCts.CancelAfter(TimeSpan.FromSeconds(20));
                using HttpResponseMessage response = await client
                    .GetAsync("/ready", requestCts.Token)
                    .ConfigureAwait(false);
                string body = await response.Content
                    .ReadAsStringAsync(requestCts.Token)
                    .ConfigureAwait(false);
                lastDiagnostic = $"Status={(int)response.StatusCode} ({response.StatusCode}); Body={Bound(body)}";

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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastDiagnostic = "The EventStore /ready request exceeded 20 seconds.";
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "[startup/readiness] EventStore did not report healthy Dapr actor placement within 120 seconds. "
            + lastDiagnostic);
    }

    /// <summary>
    /// Waits for one AppHost resource and retains its state when startup cannot make it healthy.
    /// </summary>
    /// <param name="app">The running distributed application.</param>
    /// <param name="resourceName">The resource to wait for.</param>
    /// <param name="cancellationToken">The readiness token, usually the bounded startup budget.</param>
    /// <param name="callerCancellationToken">
    /// The caller's own (test-level) token. Only a cancellation of <em>this</em> token is a genuine abort that
    /// rethrows unchanged; the startup budget expiring is a readiness failure and must carry the resource
    /// snapshot that explains it, which is the whole reason this wrapper exists.
    /// </param>
    public static async Task<ResourceEvent> WaitForResourceHealthyAsync(
        DistributedApplication app,
        string resourceName,
        CancellationToken cancellationToken,
        CancellationToken callerCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        try
        {
            return await app.ResourceNotifications
                .WaitForResourceHealthyAsync(resourceName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            string diagnostic = app.ResourceNotifications.TryGetCurrentState(resourceName, out ResourceEvent? current)
                ? $"State={current.Snapshot.State?.Text ?? "unknown"}; "
                    + $"Health={current.Snapshot.HealthStatus?.ToString() ?? "unknown"}; "
                    + $"ExitCode={current.Snapshot.ExitCode?.ToString() ?? "none"}."
                : "No resource snapshot was published.";
            string budget = ex is OperationCanceledException && cancellationToken.IsCancellationRequested
                ? "the bounded startup budget expired before it became healthy"
                : "it did not become healthy";

            throw new InvalidOperationException(
                $"[startup/readiness] Resource '{resourceName}': {budget}. {diagnostic}",
                ex);
        }
    }

    /// <summary>
    /// Opens the Works Dapr HTTP endpoint exported by the running resource snapshot.
    /// </summary>
    public static HttpClient CreateWorksDaprClient(ResourceEvent worksResource)
        => CreateDaprClient(worksResource, "Works");

    /// <summary>
    /// Opens the Dapr HTTP endpoint exported by a running resource snapshot.
    /// </summary>
    public static HttpClient CreateDaprClient(ResourceEvent resource, string resourceDisplayName)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceDisplayName);

        string? endpoint = resource.Snapshot.EnvironmentVariables
            .FirstOrDefault(static variable => string.Equals(
                variable.Name,
                "DAPR_HTTP_ENDPOINT",
                StringComparison.Ordinal))
            ?.Value;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? address))
        {
            throw new InvalidOperationException(
                $"[startup/readiness] The healthy {resourceDisplayName} resource did not expose a valid DAPR_HTTP_ENDPOINT.");
        }

        return new HttpClient
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>
    /// Formats bounded state for the resources that can keep an AppHost start incomplete.
    /// </summary>
    public static string DescribeResourceStates(DistributedApplication app, IEnumerable<string> resourceNames)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(resourceNames);

        return string.Join(
            ' ',
            resourceNames.Select(resourceName =>
            {
                if (!app.ResourceNotifications.TryGetCurrentState(resourceName, out ResourceEvent? current))
                {
                    return $"{resourceName}[snapshot=missing]";
                }

                return $"{resourceName}[state={current.Snapshot.State?.Text ?? "unknown"},"
                    + $"health={current.Snapshot.HealthStatus?.ToString() ?? "unknown"},"
                    + $"exit={current.Snapshot.ExitCode?.ToString() ?? "none"}]";
            }));
    }

    /// <summary>
    /// Waits until the Works sidecar reports a compatible runtime, scheduler/placement connectivity, and the
    /// SDK reminder actor host as ready.
    /// </summary>
    public static async Task WaitForWorksActorRuntimeAsync(
        HttpClient sidecarClient,
        CancellationToken cancellationToken,
        string actorTypeName = "WorksSdkReminderActor")
    {
        ArgumentNullException.ThrowIfNull(sidecarClient);

        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        string lastDiagnostic = "No Dapr metadata response was received.";

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await sidecarClient
                    .GetAsync("/v1.0/metadata", cancellationToken)
                    .ConfigureAwait(false);
                string body = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                lastDiagnostic = $"Status={(int)response.StatusCode} ({response.StatusCode}); Body={Bound(body)}";

                if (response.IsSuccessStatusCode)
                {
                    using JsonDocument document = JsonDocument.Parse(body);
                    JsonElement root = document.RootElement;
                    string? runtimeVersionText = root.TryGetProperty("runtimeVersion", out JsonElement runtimeVersionElement)
                        ? runtimeVersionElement.GetString()
                        : null;

                    if (!Version.TryParse(runtimeVersionText, out Version? runtimeVersion))
                    {
                        // An unreported or unparseable runtime version cannot be checked against the minimum,
                        // so it must fail closed here rather than silently spinning to the readiness timeout
                        // with a message that blames placement or the actor host.
                        throw new InvalidOperationException(
                            "[startup/readiness] Works Dapr sidecar reported no parseable runtimeVersion "
                            + $"(runtimeVersion={runtimeVersionText ?? "<missing>"}); runtime "
                            + $"{MinimumDaprRuntimeVersion} or newer is required. {lastDiagnostic}");
                    }

                    if (runtimeVersion < MinimumDaprRuntimeVersion)
                    {
                        throw new InvalidOperationException(
                            $"[startup/readiness] Works Dapr runtime {runtimeVersion} is incompatible; "
                            + $"runtime {MinimumDaprRuntimeVersion} or newer is required. {lastDiagnostic}");
                    }

                    bool actorAdvertised = root.TryGetProperty("actors", out JsonElement actors)
                        && actors.ValueKind == JsonValueKind.Array
                        && actors.EnumerateArray().Any(actor => actor.TryGetProperty("type", out JsonElement type)
                            && string.Equals(type.GetString(), actorTypeName, StringComparison.Ordinal));
                    bool actorRuntimeReady = root.TryGetProperty("actorRuntime", out JsonElement actorRuntime)
                        && actorRuntime.TryGetProperty("runtimeStatus", out JsonElement runtimeStatus)
                        && string.Equals(runtimeStatus.GetString(), "RUNNING", StringComparison.Ordinal)
                        && actorRuntime.TryGetProperty("hostReady", out JsonElement hostReady)
                        && hostReady.ValueKind == JsonValueKind.True
                        && actorRuntime.TryGetProperty("placement", out JsonElement placement)
                        && IsPlacementConnected(placement.GetString());
                    bool schedulerConnected = root.TryGetProperty("scheduler", out JsonElement scheduler)
                        && scheduler.TryGetProperty("connected_addresses", out JsonElement addresses)
                        && addresses.ValueKind == JsonValueKind.Array
                        && addresses.GetArrayLength() > 0;

                    if (runtimeVersion >= MinimumDaprRuntimeVersion
                        && actorAdvertised
                        && actorRuntimeReady
                        && schedulerConnected)
                    {
                        return;
                    }
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastDiagnostic = "The Dapr metadata request exceeded the HTTP client timeout.";
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "[startup/readiness] Works Dapr sidecar did not advertise a compatible, scheduler/placement-connected "
            + $"{actorTypeName} host within 60 seconds. {lastDiagnostic}");
    }



    /// <summary>
    /// Returns whether the sidecar's placement status token is exactly "connected".
    /// </summary>
    /// <remarks>A substring test also matches "disconnected", which is the opposite of readiness.</remarks>
    private static bool IsPlacementConnected(string? placementStatus)
        => placementStatus is not null
            && placementStatus
                .Split([':', ' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(static token => string.Equals(token, "connected", StringComparison.OrdinalIgnoreCase));

    private static string Bound(string value)
    {
        const int MaximumDiagnosticLength = 2_000;
        return value.Length <= MaximumDiagnosticLength ? value : value[..MaximumDiagnosticLength] + "…";
    }
}
