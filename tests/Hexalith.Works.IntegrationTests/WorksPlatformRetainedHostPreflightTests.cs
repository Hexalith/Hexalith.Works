using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Hexalith.EventStore.Client.Processes;

using Aspire.Hosting.Testing;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Starts the exact archived topology before the expensive complete-fleet rollback proof.</summary>
public sealed class WorksPlatformRetainedHostPreflightTests
{
    /// <summary>The historical topology must resolve all paths and reach real SDK readiness with run-owned providers.</summary>
    [Fact(Explicit = true)]
    public async Task ArchivedTopologyReachesHealthyReadiness()
    {
        if (!WorksAppHostSelection.IsPlatform)
            throw new InvalidOperationException("Retained topology preflight requires the canonical Platform verifier selection.");
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var cleanup = isolation.ConfigureAwait(false);
        using RSA rsa = RSA.Create(2048);
        string key = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, _gateway, _sidecar, token) =>
        {
            using HttpClient works = app.CreateHttpClient("works");
            JsonElement readiness = await works.GetFromJsonAsync<JsonElement>("/ready", token).ConfigureAwait(false);
            readiness.GetProperty("status").GetString().ShouldBe("Healthy");
        }, builder => WorksSdkRecoveryPipelineSmokeTests.Configure(builder, key, digestKey,
            isolation.ComponentPath, isolation.RedisEndpoint, [new ProcessSource("tenant-retained-preflight", "work", "item-retained-preflight")]), "WorksSdkReminderActor",
            schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory,
            useRetainedHost: true).ConfigureAwait(true);
    }
}
