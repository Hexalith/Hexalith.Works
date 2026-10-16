using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Probes the real owned TLS control plane using configured and foreign roots.</summary>
internal static class WorksPlatformTrustChecks
{
    internal static async Task<object> VerifyAsync(string certificateDirectory, CancellationToken cancellationToken)
    {
        using X509Certificate2 configuredRoot = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(certificateDirectory, "ca.crt")));
        using RSA foreignKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=story416-foreign-root", foreignKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, critical: true));
        using X509Certificate2 foreignRoot = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        (await HandshakeAsync(configuredRoot, cancellationToken).ConfigureAwait(false)).ShouldBeTrue("The owned Sentry must present the configured trust chain.");
        (await HandshakeAsync(foreignRoot, cancellationToken).ConfigureAwait(false)).ShouldBeFalse("A foreign root must refuse the same real TLS endpoint before a request.");
        return new { configuredRootHandshake = true, foreignRootHandshakeDenied = true, endpoint = "owned-sentry:50001", domainRequests = 0 };
    }

    private static async Task<bool> HandshakeAsync(X509Certificate2 trustedRoot, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("localhost", 50001, deadline.Token).ConfigureAwait(false);
        using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, certificate, suppliedChain, _) =>
        {
            if (certificate is null) return false;
            using X509Certificate2 presented = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            if (suppliedChain is not null)
                foreach (X509ChainElement element in suppliedChain.ChainElements) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            return chain.Build(presented);
        });
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost", ApplicationProtocols = [SslApplicationProtocol.Http2],
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (AuthenticationException) { return false; }
    }
}
