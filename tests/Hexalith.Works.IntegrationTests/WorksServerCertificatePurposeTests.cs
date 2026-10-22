using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Tests the exact custom-root server-purpose policy used by the live TLS probe.</summary>
public sealed class WorksServerCertificatePurposeTests
{
    /// <summary>A trusted client-only certificate cannot authenticate a server; server and dual-purpose certificates can.</summary>
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ConfiguredRootTrustRequiresServerAuthenticationPurpose(bool serverAuth, bool clientAuth, bool expected)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=works-purpose-root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=works-purpose-leaf", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var purposes = new OidCollection();
        if (serverAuth) purposes.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        if (clientAuth) purposes.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes, true));
        using var leaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));

        // All controls are otherwise valid under the same configured root.
        using var genericChain = new X509Chain();
        genericChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        genericChain.ChainPolicy.CustomTrustStore.Add(root);
        genericChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        genericChain.Build(leaf).ShouldBeTrue();
        WorksPlatformTrustChecks.HasServerAuthenticationTrust(leaf, root, root).ShouldBe(expected);
    }

    /// <summary>The actual TLS probe applies server-purpose validation before accepting a matching SPIFFE identity.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveProbeRequiresServerPurposeDespiteTrustedMatchingIdentity(bool serverAuth)
    {
        const string identity = "spiffe://public/ns/default/works";
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=works-probe-root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        using var leafKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=works-probe-server", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri(identity));
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        { new Oid(serverAuth ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        using var publicLeaf = request.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
        using var leaf = publicLeaf.CopyWithPrivateKey(leafKey);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task serving = ServeAsync();
            Task<WorksPlatformTrustProbeResult> probing = WorksPlatformTrustChecks.ProbeAsync(port, root, root, identity, null, null, deadline.Token);
            await Task.WhenAll(serving, probing).ConfigureAwait(true);
            WorksPlatformTrustProbeResult result = await probing.ConfigureAwait(true);
            result.ServerIdentity.ShouldBe(identity);
            result.ClientRejectedServer.ShouldBe(!serverAuth);
            result.ServerAccepted.ShouldBe(serverAuth);
        }
        finally { listener.Stop(); }

        async Task ServeAsync()
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
            using var tls = new SslStream(connection.GetStream());
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = leaf,
                    ApplicationProtocols = [SslApplicationProtocol.Http2],
                    EnabledSslProtocols = SslProtocols.Tls12,
                }, deadline.Token).ConfigureAwait(false);
                await tls.ReadExactlyAsync(new byte[33], deadline.Token).ConfigureAwait(false);
                await tls.WriteAsync(new byte[] { 0, 0, 0, 4, 0, 0, 0, 0, 0 }, deadline.Token).ConfigureAwait(false);
                await tls.FlushAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!serverAuth && (exception is AuthenticationException or IOException))
            {
                // A rejected server receives the client's handshake alert instead of an HTTP/2 preface.
            }
        }
    }
}
