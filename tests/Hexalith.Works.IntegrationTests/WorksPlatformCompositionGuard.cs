using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Refuses an actual Platform start whose fixture configuration sets any environment key Platform composed for that
/// resource, even to an equal value, or removes or changes one, and records Platform's own composed values for the verifier.
/// </summary>
/// <remarks>
/// The fixture's callbacks are evaluated twice: on an empty environment, which reveals every key they write
/// unconditionally, and after Platform's values, which reveals a callback that removes or rewrites a key only where
/// Platform composed it. Only three deliberate fixture interactions with Platform-composed keys are permitted, each with a checked shape:
/// the proof hosting startup and startup hook <em>extend</em> Platform's composed lists (Platform's entries stay first);
/// the captured, restorable synthetic app-channel secret replaces Platform's generated per-run secret through the
/// same public SDK helper; and the telemetry lanes redirect the orchestrator's OTLP export to the proof's owned loopback
/// collector so recovery metrics can be observed. Every other overlap refuses the start before any resource is built.
/// </remarks>
internal static class WorksPlatformCompositionGuard
{
    /// <summary>The app-channel secret both hosts generate per run and the fixture captures for restore.</summary>
    internal const string AppChannelToken = "APP_API_TOKEN";

    /// <summary>OTLP export settings the telemetry lanes may redirect to the proof's owned loopback collector.</summary>
    internal static readonly string[] TelemetryRedirect = ["OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_PROTOCOL", "OTEL_METRIC_EXPORT_INTERVAL"];

    /// <summary>Composed list variables the proof may extend, with each list's separator.</summary>
    internal static readonly IReadOnlyDictionary<string, char> ExtendableLists = new Dictionary<string, char>(StringComparer.Ordinal)
    {
        ["DOTNET_STARTUP_HOOKS"] = Path.PathSeparator,
        ["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = ';',
    };

    /// <summary>Captures each project resource's environment callbacks at this point of composition.</summary>
    /// <param name="builder">The testing builder.</param>
    /// <returns>The callbacks per project resource.</returns>
    internal static Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> Capture(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Resources.OfType<ProjectResource>()
            .ToDictionary(resource => resource, resource => resource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToArray());
    }

    /// <summary>Evaluates Platform's and the fixture's callbacks separately and refuses an unpermitted overlap.</summary>
    /// <param name="builder">The testing builder.</param>
    /// <param name="platform">The callbacks captured before the fixture configured the builder.</param>
    /// <param name="configured">The callbacks captured immediately after the fixture configured the builder.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Per-resource evidence: Platform's composed keys, the fixture's keys, permitted overlaps and tracked values.</returns>
    internal static async Task<Dictionary<string, object>> VerifyAsync(IDistributedApplicationBuilder builder,
        IReadOnlyDictionary<ProjectResource, EnvironmentCallbackAnnotation[]> platform,
        IReadOnlyDictionary<ProjectResource, EnvironmentCallbackAnnotation[]> configured, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(configured);
        var evidence = new Dictionary<string, object>(StringComparer.Ordinal);
        var refusals = new List<string>();
        foreach ((ProjectResource resource, EnvironmentCallbackAnnotation[] composed) in platform.OrderBy(pair => pair.Key.Name, StringComparer.Ordinal))
        {
            EnvironmentCallbackAnnotation[] current = configured.TryGetValue(resource, out EnvironmentCallbackAnnotation[]? after) ? after : [];
            if (composed.Any(callback => !current.Contains(callback)))
                refusals.Add(resource.Name + ": the fixture removed a Platform-composed environment callback");
            EnvironmentCallbackAnnotation[] fixture = [.. current.Where(callback => !composed.Contains(callback))];
            Dictionary<string, object> platformValues = await EvaluateAsync(builder, resource, composed, new Dictionary<string, object>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> fixtureWrites = await EvaluateAsync(builder, resource, fixture, new Dictionary<string, object>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> combined = await EvaluateAsync(builder, resource, fixture,
                new Dictionary<string, object>(platformValues, StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            IReadOnlyList<(string Key, string Rule)> permitted = Overlaps(resource.Name, platformValues, fixtureWrites, combined, refusals);
            evidence[resource.Name] = new
            {
                platformComposedKeys = platformValues.Keys.Order(StringComparer.Ordinal).ToArray(),
                // Every key the fixture wrote, plus every Platform key it removed or rewrote only where Platform composed it.
                fixtureKeys = fixtureWrites.Keys.Union(AffectedKeys(platformValues, fixtureWrites, combined), StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray(),
                permittedOverlaps = permitted.Select(item => new { key = item.Key, rule = item.Rule }).ToArray(),
                composition = platformValues.Where(pair => WorksPlatformTrackedEnvironment.IsTracked(pair.Key))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value as string, StringComparer.Ordinal),
            };
        }

        if (refusals.Count > 0)
            throw new InvalidOperationException("[composition] The fixture set keys the actual Platform composed: " + string.Join("; ", refusals));
        return evidence;
    }

    /// <summary>
    /// Classifies every Platform-composed key the fixture affected, adding a refusal for each unpermitted one: keys the
    /// fixture writes, and keys whose combined value lacks or differs from Platform's value.
    /// </summary>
    /// <param name="resource">The resource name.</param>
    /// <param name="platform">Platform's composed values.</param>
    /// <param name="fixtureWrites">Every key the fixture's callbacks write, evaluated on an empty environment.</param>
    /// <param name="combined">The fixture's callbacks evaluated after Platform's.</param>
    /// <param name="refusals">Receives one refusal per unpermitted overlap.</param>
    /// <returns>The permitted overlaps and their rules.</returns>
    internal static IReadOnlyList<(string Key, string Rule)> Overlaps(string resource, IReadOnlyDictionary<string, object> platform,
        IReadOnlyDictionary<string, object> fixtureWrites, IReadOnlyDictionary<string, object> combined, ICollection<string> refusals)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(fixtureWrites);
        ArgumentNullException.ThrowIfNull(combined);
        ArgumentNullException.ThrowIfNull(refusals);
        var permitted = new List<(string Key, string Rule)>();
        foreach (string key in AffectedKeys(platform, fixtureWrites, combined))
        {
            if (ExtendableLists.TryGetValue(key, out char separator))
            {
                if (platform[key] is string { Length: > 0 } composed && combined.TryGetValue(key, out object? value)
                    && value is string extended && extended.StartsWith(composed + separator, StringComparison.Ordinal)
                    && extended.Length > composed.Length + 1)
                {
                    permitted.Add((key, "extends-composed-list"));
                    continue;
                }

                refusals.Add(resource + ": " + key + " does not extend Platform's composed list");
                continue;
            }

            if (key == AppChannelToken)
            {
                if (combined.TryGetValue(key, out object? token) && token is ParameterResource { Secret: true } parameter
                    && parameter.Name == "proof-" + resource + "-app-api-token" && platform[key] is ParameterResource { Secret: true })
                {
                    permitted.Add((key, "captured-restorable-synthetic-secret"));
                    continue;
                }

                refusals.Add(resource + ": " + key + " is not the captured synthetic app-channel secret");
                continue;
            }

            if (TelemetryRedirect.Contains(key, StringComparer.Ordinal))
            {
                if (combined.TryGetValue(key, out object? telemetry) && telemetry is string setting && IsOwnedTelemetry(key, setting, fixtureWrites, combined))
                {
                    permitted.Add((key, "redirects-otlp-export-to-owned-loopback-collector"));
                    continue;
                }

                refusals.Add(resource + ": " + key + " does not redirect OTLP export to an owned loopback collector");
                continue;
            }

            refusals.Add(resource + ": " + key + (fixtureWrites.ContainsKey(key) ? string.Empty
                : combined.ContainsKey(key) ? " was changed where Platform composed it" : " was removed"));
        }

        return permitted;
    }

    /// <summary>
    /// Returns, in ordinal order, every Platform-composed key the fixture affected: each key its callbacks write on an
    /// empty environment, and each key whose value after the fixture's callbacks is absent or differs from Platform's.
    /// </summary>
    /// <param name="platform">Platform's composed values.</param>
    /// <param name="fixtureWrites">Every key the fixture's callbacks write, evaluated on an empty environment.</param>
    /// <param name="combined">The fixture's callbacks evaluated after Platform's.</param>
    /// <returns>The affected Platform-composed keys.</returns>
    internal static IEnumerable<string> AffectedKeys(IReadOnlyDictionary<string, object> platform, IReadOnlyDictionary<string, object> fixtureWrites,
        IReadOnlyDictionary<string, object> combined)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(fixtureWrites);
        ArgumentNullException.ThrowIfNull(combined);

        // Combined evaluation starts from Platform's own value instances, so an untouched key keeps an equal value.
        return platform.Keys.Where(key => fixtureWrites.ContainsKey(key) || !combined.TryGetValue(key, out object? value) || !Equals(value, platform[key]))
            .Order(StringComparer.Ordinal);
    }

    // The fixture itself must redirect both the endpoint (an owned loopback HTTP collector) and the protocol together.
    private static bool IsOwnedTelemetry(string key, string value, IReadOnlyDictionary<string, object> fixtureWrites,
        IReadOnlyDictionary<string, object> combined)
    {
        bool owned = fixtureWrites.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out object? endpoint) && endpoint is string uri
            && Uri.TryCreate(uri, UriKind.Absolute, out Uri? collector) && collector.Scheme == Uri.UriSchemeHttp && collector.IsLoopback
            && fixtureWrites.TryGetValue("OTEL_EXPORTER_OTLP_PROTOCOL", out object? protocol) && protocol as string == "http/protobuf"
            && combined.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out object? effectiveEndpoint) && Equals(endpoint, effectiveEndpoint)
            && combined.TryGetValue("OTEL_EXPORTER_OTLP_PROTOCOL", out object? effectiveProtocol) && Equals(protocol, effectiveProtocol);
        return owned && (key != "OTEL_METRIC_EXPORT_INTERVAL"
            || int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int interval)
                && interval is > 0 and <= 60000);
    }

    private static async Task<Dictionary<string, object>> EvaluateAsync(IDistributedApplicationBuilder builder, IResource resource,
        IEnumerable<EnvironmentCallbackAnnotation> callbacks, Dictionary<string, object> environment, CancellationToken cancellationToken)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, environment, cancellationToken);
        foreach (EnvironmentCallbackAnnotation callback in callbacks)
            await callback.Callback(context).ConfigureAwait(false);
        return context.EnvironmentVariables;
    }
}
