using System.Formats.Asn1;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Probes a real owned sidecar's mutual-TLS endpoint. The probe accepts the server only under the configured root
/// and exact SPIFFE identity, so every client-certificate refusal it records is the server's own decision.
/// </summary>
internal static partial class WorksPlatformTrustChecks
{
    private const string TrustDomain = "public";
    private const string Namespace = "default";
    private const string ProbeAppId = "story416-trust-probe";

    // HTTP/2 client connection preface followed by an empty SETTINGS frame. No gRPC request is ever sent.
    private static readonly byte[] _http2Preface = [.. "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8, 0, 0, 0, 4, 0, 0, 0, 0, 0];

    /// <summary>Requires server refusal of absent and foreign-CA client certificates and probe refusal of a wrong identity.</summary>
    /// <param name="app">The running owned topology.</param>
    /// <param name="certificateDirectory">The owned Sentry trust anchors and issuer credentials.</param>
    /// <param name="sidecarResource">The Dapr CLI resource that owns the probed sidecar.</param>
    /// <param name="appId">The sidecar's Dapr application identity.</param>
    /// <param name="mismatchedAppId">Another application identity the probe must refuse to accept for this endpoint.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>Evidence of the four handshakes.</returns>
    internal static async Task<object> VerifyAsync(DistributedApplication app, string certificateDirectory, string sidecarResource,
        string appId, string mismatchedAppId, CancellationToken cancellationToken)
    {
        int port = await InternalGrpcPortAsync(app, sidecarResource, cancellationToken).ConfigureAwait(false);
        using X509Certificate2 root = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(certificateDirectory, "ca.crt")));
        using X509Certificate2 issuer = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(certificateDirectory, "issuer.crt")));
        string expected = SpiffeId(appId);
        string mismatched = SpiffeId(mismatchedAppId);
        using X509Certificate2 trustedClient = await IssuedLeafAsync(certificateDirectory, SpiffeId(ProbeAppId), cancellationToken).ConfigureAwait(false);
        using X509Certificate2 foreignRoot = ForeignRoot();
        using X509Certificate2 foreignClient = Leaf(foreignRoot, SpiffeId(ProbeAppId));

        // Positive control: a client chained to the configured trust anchors completes HTTP/2 setup.
        WorksPlatformTrustProbeResult accepted = await ProbeAsync(port, root, issuer, expected, trustedClient, issuer, cancellationToken).ConfigureAwait(false);
        accepted.ServerIdentity.ShouldBe(expected);
        accepted.ClientRejectedServer.ShouldBeFalse(accepted.Failure);
        accepted.ServerAccepted.ShouldBeTrue("The owned sidecar must accept a client chained to its configured root. " + accepted.Failure);

        // The probe accepted the server in both refusals, so the refusal is the sidecar's own decision.
        WorksPlatformTrustProbeResult none = await ProbeAsync(port, root, issuer, expected, null, null, cancellationToken).ConfigureAwait(false);
        none.ServerIdentity.ShouldBe(expected, none.Failure);
        none.ClientRejectedServer.ShouldBeFalse(none.Failure);
        none.ServerAccepted.ShouldBeFalse("The mTLS sidecar must refuse a client that presents no certificate.");
        WorksPlatformTrustProbeResult foreign = await ProbeAsync(port, root, issuer, expected, foreignClient, foreignRoot, cancellationToken).ConfigureAwait(false);
        foreign.ServerIdentity.ShouldBe(expected, foreign.Failure);
        foreign.ClientRejectedServer.ShouldBeFalse(foreign.Failure);
        foreign.ServerAccepted.ShouldBeFalse("The mTLS sidecar must refuse a client certificate from a foreign CA.");

        // The same configured root with another expected identity must refuse the genuine server.
        WorksPlatformTrustProbeResult mismatch = await ProbeAsync(port, root, issuer, mismatched, trustedClient, issuer, cancellationToken).ConfigureAwait(false);
        mismatch.ServerIdentity.ShouldBe(expected);
        mismatch.ClientRejectedServer.ShouldBeTrue("The configured-root probe must reject a server identity mismatch.");
        mismatch.ServerAccepted.ShouldBeFalse();

        // Each flag is derived from its observed handshake, so the evidence restates no assertion as a literal.
        return new
        {
            endpoint = sidecarResource + ":internal-grpc", port, serverIdentity = accepted.ServerIdentity,
            configuredRootAndIdentityAccepted = accepted.ServerIdentity == expected && !accepted.ClientRejectedServer,
            trustedClientAcceptedByServer = accepted.ServerAccepted,
            noClientCertificateRefusedByServer = !none.ServerAccepted && !none.ClientRejectedServer,
            foreignCaClientRefusedByServer = !foreign.ServerAccepted && !foreign.ClientRejectedServer,
            nameMismatchRejectedByProbe = mismatch.ClientRejectedServer && mismatch.ServerIdentity == expected,
            mismatchedIdentity = mismatched,
            refusals = new { noClientCertificate = none.Failure, foreignCaClient = foreign.Failure, nameMismatch = mismatch.Failure },
        };
    }

    private static string SpiffeId(string appId) => $"spiffe://{TrustDomain}/ns/{Namespace}/{appId}";

    private static async Task<int> InternalGrpcPortAsync(DistributedApplication app, string resource, CancellationToken cancellationToken)
    {
        app.ResourceNotifications.TryGetCurrentState(resource, out ResourceEvent? current).ShouldBeTrue("The probed sidecar resource is absent.");
        ResourceLoggerService logs = app.Services.GetRequiredService<ResourceLoggerService>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await foreach (IReadOnlyList<LogLine> batch in logs.GetAllAsync(current!.ResourceId).WithCancellation(budget.Token).ConfigureAwait(false))
            {
                foreach (LogLine line in batch)
                {
                    if (InternalGrpcLine().Match(line.Content) is { Success: true } match)
                        return int.Parse(match.Groups["port"].Value, CultureInfo.InvariantCulture);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Fall through to the explicit failure below.
        }

        throw new InvalidOperationException("The owned sidecar '" + resource + "' did not report its internal mTLS gRPC endpoint.");
    }

    /// <summary>Probes one owned TLS endpoint; a close before server authentication never qualifies as server-side refusal.</summary>
    /// <param name="port">The owned loopback port.</param>
    /// <param name="root">The configured trust root.</param>
    /// <param name="issuer">The configured issuing certificate.</param>
    /// <param name="expectedIdentity">The exact server SPIFFE identity.</param>
    /// <param name="clientCertificate">The optional client certificate.</param>
    /// <param name="clientIssuer">The optional client issuing certificate.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The observed server authentication and acceptance.</returns>
    internal static async Task<WorksPlatformTrustProbeResult> ProbeAsync(int port, X509Certificate2 root, X509Certificate2 issuer, string expectedIdentity,
        X509Certificate2? clientCertificate, X509Certificate2? clientIssuer, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(false);
        string? serverIdentity = null;
        bool clientRejected = true;
        using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, errors) =>
        {
            // Dapr workload certificates carry SPIFFE identities, not this loopback host name. The probe therefore
            // binds the exact SPIFFE URI under the configured root instead of DNS host-name matching.
            bool trusted = false;
            if (certificate is not null && (errors & SslPolicyErrors.RemoteCertificateNotAvailable) == 0)
            {
                using X509Certificate2 presented = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                string[] identities = [.. SpiffeIds(presented)];
                serverIdentity = identities.Length == 1 ? identities[0] : null;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.ExtraStore.Add(issuer);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                trusted = chain.Build(presented) && string.Equals(serverIdentity, expectedIdentity, StringComparison.Ordinal);
            }

            clientRejected = !trusted;
            return trusted;
        });
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            ApplicationProtocols = [SslApplicationProtocol.Http2],
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };
        if (clientCertificate is not null)
        {
            options.ClientCertificateContext = SslStreamCertificateContext.Create(clientCertificate,
                clientIssuer is null ? null : new X509Certificate2Collection(clientIssuer), offline: true);
        }

        try
        {
            await tls.AuthenticateAsClientAsync(options, deadline.Token).ConfigureAwait(false);

            // A TLS 1.3 client finishes before the server verifies its certificate, so a completed client
            // handshake is not acceptance. Only the server's HTTP/2 SETTINGS frame proves the server accepted it.
            await tls.WriteAsync(_http2Preface, deadline.Token).ConfigureAwait(false);
            await tls.FlushAsync(deadline.Token).ConfigureAwait(false);
            byte[] header = new byte[9];
            int read = 0;
            while (read < header.Length)
            {
                int count = await tls.ReadAsync(header.AsMemory(read), deadline.Token).ConfigureAwait(false);
                if (count == 0) break;
                read += count;
            }

            bool settings = read == header.Length && header[3] == 0x4;
            return new WorksPlatformTrustProbeResult(serverIdentity, clientRejected, settings,
                settings ? null : "The server closed the connection without an HTTP/2 SETTINGS frame.");
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException or SocketException)
        {
            // The innermost error carries the TLS alert the server sent, such as a missing or unknown certificate.
            Exception cause = exception.GetBaseException();
            return new WorksPlatformTrustProbeResult(serverIdentity, clientRejected, false, exception.GetType().Name + ": " + exception.Message
                + (ReferenceEquals(cause, exception) ? string.Empty : " | " + cause.GetType().Name + ": " + cause.Message));
        }
    }

    /// <summary>Has the owned Sentry issuer sign a probe SVID with the host OpenSSL CLI.</summary>
    /// <remarks>Sentry issues Ed25519 credentials, which .NET cannot use to sign certificates; OpenSSL can.</remarks>
    private static async Task<X509Certificate2> IssuedLeafAsync(string certificateDirectory, string spiffeId, CancellationToken cancellationToken)
    {
        string work = Directory.CreateTempSubdirectory("works-sdk416-trust-").FullName;
        try
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string keyPem = key.ExportPkcs8PrivateKeyPem();
            string keyPath = Path.Combine(work, "probe.key");
            await File.WriteAllTextAsync(keyPath, keyPem, cancellationToken).ConfigureAwait(false);
            string extensions = Path.Combine(work, "probe.ext");
            await File.WriteAllTextAsync(extensions, string.Join('\n',
                "basicConstraints=critical,CA:FALSE", "keyUsage=critical,digitalSignature",
                "extendedKeyUsage=clientAuth,serverAuth", "subjectAltName=critical,URI:" + spiffeId, string.Empty), cancellationToken).ConfigureAwait(false);
            string request = Path.Combine(work, "probe.csr");
            string certificate = Path.Combine(work, "probe.crt");
            await OpenSslAsync(["req", "-new", "-key", keyPath, "-subj", "/CN=" + ProbeAppId, "-out", request], cancellationToken).ConfigureAwait(false);
            await OpenSslAsync(["x509", "-req", "-in", request, "-CA", Path.Combine(certificateDirectory, "issuer.crt"),
                "-CAkey", Path.Combine(certificateDirectory, "issuer.key"), "-set_serial", "0x" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(15)),
                "-days", "1", "-extfile", extensions, "-out", certificate], cancellationToken).ConfigureAwait(false);
            return X509Certificate2.CreateFromPem(await File.ReadAllTextAsync(certificate, cancellationToken).ConfigureAwait(false), keyPem);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private static async Task OpenSslAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("openssl") { RedirectStandardOutput = true, RedirectStandardError = true },
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        _ = await output.ConfigureAwait(false);
        string diagnostic = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("The owned issuer could not sign the probe certificate: " + diagnostic);
    }

    private static X509Certificate2 ForeignRoot()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=story416-foreign-root", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
    }

    private static X509Certificate2 Leaf(X509Certificate2 issuer, string spiffeId)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + ProbeAppId, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.2"), new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri(spiffeId));
        request.CertificateExtensions.Add(names.Build(critical: true));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset notBefore = now.AddMinutes(-1) < issuer.NotBefore ? issuer.NotBefore : now.AddMinutes(-1);
        DateTimeOffset notAfter = now.AddMinutes(10) > issuer.NotAfter ? issuer.NotAfter : now.AddMinutes(10);
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using X509Certificate2 signed = request.Create(issuer, notBefore, notAfter, serial);
        return signed.CopyWithPrivateKey(key);
    }

    private static IEnumerable<string> SpiffeIds(X509Certificate2 certificate)
    {
        X509Extension? extension = certificate.Extensions["2.5.29.17"];
        if (extension is null) return [];
        var uriTag = new Asn1Tag(TagClass.ContextSpecific, 6);
        List<string> identities = [];
        AsnReader names = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
        while (names.HasData)
        {
            if (names.PeekTag().HasSameClassAndValue(uriTag))
            {
                string value = names.ReadCharacterString(UniversalTagNumber.IA5String, uriTag);
                if (value.StartsWith("spiffe://", StringComparison.Ordinal)) identities.Add(value);
            }
            else
            {
                _ = names.ReadEncodedValue();
            }
        }

        return identities;
    }

    [GeneratedRegex(@"Internal gRPC server is running on [^\s""]*:(?<port>\d{1,5})")]
    private static partial Regex InternalGrpcLine();
}
