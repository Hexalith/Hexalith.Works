using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>The live fixture extends, never replaces, the hosting values the selected AppHost composed.</summary>
public sealed class WorksPlatformHostingCompositionTests
{
    private const string PlatformHook = "/platform/Hexalith.Platform.Works.Runtime.dll";
    private const string ProofHook = "/consumer/Hexalith.Works.IntegrationTests.dll";

    /// <summary>Two runs with one configured parent resolve only their own mounted issuer child.</summary>
    [Fact]
    public void TrustCredentialsUseEachComposedSentryMount()
    {
        string parent = Path.Combine(Path.GetTempPath(), "works-trust-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (int index = 0; index < 2; index++)
            {
                string child = Path.Combine(parent, "dapr-credentials-" + index);
                Directory.CreateDirectory(child);
                File.WriteAllText(Path.Combine(child, "issuer.key"), "run-" + index);
                var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
                builder.Configuration["Dapr:Mtls:CertificateDirectory"] = parent;
                builder.AddContainer("dapr-sentry", "not-started")
                    .WithBindMount(child, "/var/run/dapr/credentials")
                    .WithBindMount(parent, "/var/run/dapr/config", isReadOnly: true);
                string credentials = WorksPlatformTrustChecks.CredentialDirectory(builder.Resources);
                credentials.ShouldBe(child);
                File.ReadAllText(Path.Combine(credentials, "issuer.key")).ShouldBe("run-" + index);
            }
            File.Exists(Path.Combine(parent, "issuer.key")).ShouldBeFalse();
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    /// <summary>Platform's startup hook and hosting startup stay first; the proof is appended with each list separator.</summary>
    [Fact]
    public void ProofIsAppendedAfterPlatformComposedHostingValues()
    {
        var environment = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["DOTNET_STARTUP_HOOKS"] = PlatformHook,
            ["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "Hexalith.Platform.Works.Runtime",
        };
        WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(environment, "DOTNET_STARTUP_HOOKS", ProofHook, ':');
        WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(environment, "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "Hexalith.Works.IntegrationTests", ';');
        environment["DOTNET_STARTUP_HOOKS"].ShouldBe(PlatformHook + ":" + ProofHook);
        environment["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"].ShouldBe("Hexalith.Platform.Works.Runtime;Hexalith.Works.IntegrationTests");

        // A second evaluation of the same callback cannot register the proof twice.
        WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(environment, "DOTNET_STARTUP_HOOKS", ProofHook, ':');
        environment["DOTNET_STARTUP_HOOKS"].ShouldBe(PlatformHook + ":" + ProofHook);
    }

    /// <summary>A host that composed nothing receives only the proof; blank composed values are treated as absent.</summary>
    [Fact]
    public void AbsentOrBlankComposedValueReceivesOnlyTheProof()
    {
        var environment = new Dictionary<string, object>(StringComparer.Ordinal) { ["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = string.Empty };
        WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(environment, "DOTNET_STARTUP_HOOKS", ProofHook, ':');
        WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(environment, "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "Hexalith.Works.IntegrationTests", ';');
        environment["DOTNET_STARTUP_HOOKS"].ShouldBe(ProofHook);
        environment["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"].ShouldBe("Hexalith.Works.IntegrationTests");
    }

    /// <summary>A non-string or malformed composed value is refused rather than silently replaced.</summary>
    [Fact]
    public void UnextendableComposedValueIsRefused()
    {
        var reference = new Dictionary<string, object>(StringComparer.Ordinal) { ["DOTNET_STARTUP_HOOKS"] = new object() };
        Should.Throw<InvalidOperationException>(() =>
            WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(reference, "DOTNET_STARTUP_HOOKS", ProofHook, ':'));
        var malformed = new Dictionary<string, object>(StringComparer.Ordinal) { ["DOTNET_STARTUP_HOOKS"] = PlatformHook + "::" };
        Should.Throw<InvalidOperationException>(() =>
            WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(malformed, "DOTNET_STARTUP_HOOKS", ProofHook, ':'));
        malformed["DOTNET_STARTUP_HOOKS"].ShouldBe(PlatformHook + "::");
    }

    /// <summary>The HA replica mirrors exactly Platform's composed runtime, SDK-mode, caller and JWT values.</summary>
    [Fact]
    public void ReplicaMirrorsPlatformComposedWorksValues()
    {
        Dictionary<string, object> composed = Composed();
        composed["APP_API_TOKEN"] = new object();
        composed["OTEL_SERVICE_NAME"] = "works";
        IReadOnlyList<(string Name, string Value)> values = WorksPlatformHaPipelineSmokeTests.PlatformComposedValues(composed);
        values.Select(value => value.Name).ShouldBe([
            "DOTNET_ENVIRONMENT", "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "Authentication__JwtBearer__Authority", "Authentication__JwtBearer__SigningKey",
            "Authentication__Workload__AllowedCallers__0", "Authentication__Workload__AllowedCallers__1", "Authentication__Workload__Audience",
            "Authentication__WorkloadIssuer__Workload", "DOTNET_STARTUP_HOOKS", "Dapr__DeclarativeSubscriptions", "Works__Projection__UseSdkDelivery",
            "Works__Query__GatewayWorkloadId", "Works__Recovery__UseSdkRuntime", "WORKS_PLATFORM_RUNTIME"], ignoreOrder: true);
        values.ShouldContain(("DOTNET_STARTUP_HOOKS", PlatformHook));
        values.ShouldContain(("Authentication__Workload__AllowedCallers__1", "eventstore-admin"));
        values.ShouldContain(("Authentication__JwtBearer__Authority", string.Empty));
    }

    /// <summary>A replica cannot start when Platform omitted, blanked or replaced a required value with a non-string.</summary>
    [Theory]
    [InlineData("DOTNET_ENVIRONMENT")]
    [InlineData("WORKS_PLATFORM_RUNTIME")]
    [InlineData("Dapr__DeclarativeSubscriptions")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES")]
    [InlineData("Authentication__Workload__AllowedCallers__0")]
    [InlineData("Works__Recovery__UseSdkRuntime")]
    public void MissingPlatformComposedValueIsRefused(string name)
    {
        Dictionary<string, object> composed = Composed();
        composed.Remove(name);
        Should.Throw<InvalidOperationException>(() => WorksPlatformHaPipelineSmokeTests.PlatformComposedValues(composed));
        composed[name] = string.Empty;
        Should.Throw<InvalidOperationException>(() => WorksPlatformHaPipelineSmokeTests.PlatformComposedValues(composed));
        composed[name] = new object();
        Should.Throw<InvalidOperationException>(() => WorksPlatformHaPipelineSmokeTests.PlatformComposedValues(composed));
    }

    /// <summary>A reset before any server certificate is received cannot count as a server-side mTLS refusal.</summary>
    [Fact]
    public async Task CloseBeforeServerAuthenticationCannotQualifyAsCertificateRefusal()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=unreached-test-server", key,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            Task<WorksPlatformTrustProbeResult> probing = WorksPlatformTrustChecks.ProbeAsync(port, root, root,
                "spiffe://public/ns/default/works", null, null, TestContext.Current.CancellationToken);
            using System.Net.Sockets.TcpClient connection = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            connection.Client.LingerState = new System.Net.Sockets.LingerOption(true, 0);
            connection.Close();
            WorksPlatformTrustProbeResult result = await probing.ConfigureAwait(true);
            result.ServerIdentity.ShouldBeNull();
            result.ClientRejectedServer.ShouldBeTrue();
            result.ServerAccepted.ShouldBeFalse();
        }
        finally { listener.Stop(); }
    }

    private static Dictionary<string, object> Composed() => new(StringComparer.Ordinal)
    {
        ["DOTNET_ENVIRONMENT"] = "Development",
        ["WORKS_PLATFORM_RUNTIME"] = "1",
        ["DOTNET_STARTUP_HOOKS"] = PlatformHook,
        ["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "Hexalith.Platform.Works.Runtime",
        ["Dapr__DeclarativeSubscriptions"] = "true",
        ["Works__Projection__UseSdkDelivery"] = "true",
        ["Works__Recovery__UseSdkRuntime"] = "true",
        ["Works__Query__GatewayWorkloadId"] = "eventstore",
        ["Authentication__Workload__Audience"] = "works",
        ["Authentication__WorkloadIssuer__Workload"] = "works",
        ["Authentication__Workload__AllowedCallers__0"] = "eventstore",
        ["Authentication__Workload__AllowedCallers__1"] = "eventstore-admin",
        ["Authentication__JwtBearer__Authority"] = string.Empty,
        ["Authentication__JwtBearer__SigningKey"] = "synthetic-signing-key",
    };
}
