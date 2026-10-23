using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Separates authenticated certificate-refusal alerts from inconclusive TLS transport failures.</summary>
public sealed class WorksTlsCertificateRefusalTests
{
    private const string Identity = "spiffe://public/ns/default/works";

    /// <summary>Closing or resetting after authentication cannot establish why a peer refused HTTP/2 setup.</summary>
    [Theory]
    [InlineData(SslProtocols.Tls12, false)]
    [InlineData(SslProtocols.Tls12, true)]
    [InlineData(SslProtocols.Tls13, false)]
    [InlineData(SslProtocols.Tls13, true)]
    public async Task AuthenticatedCloseOrResetDoesNotProveCertificateRefusal(SslProtocols protocol, bool reset)
    {
        using X509Certificate2 root = Root("works-close-root");
        using X509Certificate2 server = Leaf(root, server: true);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            Task serving = ServeAsync();
            Task<WorksPlatformTrustProbeResult> probing = WorksPlatformTrustChecks.ProbeAsync(
                ((IPEndPoint)listener.LocalEndpoint).Port, root, root, Identity, null, null, deadline.Token);
            await Task.WhenAll(serving, probing).ConfigureAwait(true);
            WorksPlatformTrustProbeResult result = await probing.ConfigureAwait(true);
            result.ServerIdentity.ShouldBe(Identity);
            result.ClientRejectedServer.ShouldBeFalse(result.Failure);
            result.ServerAccepted.ShouldBeFalse();
            result.ServerRefusedClientCertificate.ShouldBeFalse(result.Failure);
            result.CertificateRefusalAlertReason.ShouldBeNull(result.Failure);
        }
        finally { listener.Stop(); }

        async Task ServeAsync()
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
            using var tls = new SslStream(connection.GetStream());
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = server,
                ApplicationProtocols = [SslApplicationProtocol.Http2],
                EnabledSslProtocols = protocol,
                AllowTlsResume = false,
            }, deadline.Token).ConfigureAwait(false);
            // Reading the entire preface proves that the client authenticated this server before the fault.
            await tls.ReadExactlyAsync(new byte[33], deadline.Token).ConfigureAwait(false);
            if (reset)
            {
                connection.Client.LingerState = new LingerOption(true, 0);
                connection.Close();
            }
            else
            {
                await tls.ShutdownAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Real OpenSSL alerts prove refusal only when the probe also authenticated the server.</summary>
    /// <remarks>The native control requires the Linux OpenSSL provider and the OpenSSL CLI on PATH.</remarks>
    [Theory]
    [InlineData(SslProtocols.Tls12, false)]
    [InlineData(SslProtocols.Tls12, true)]
    [InlineData(SslProtocols.Tls13, false)]
    [InlineData(SslProtocols.Tls13, true)]
    public async Task RealCertificateRefusalRequiresAuthenticatedServer(SslProtocols protocol, bool foreignClient)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Native certificate-refusal controls require the Linux OpenSSL provider.");
        using X509Certificate2 root = Root("works-alert-root");
        using X509Certificate2 server = Leaf(root, server: true);
        using X509Certificate2 foreignRoot = Root("works-foreign-root");
        using X509Certificate2 foreign = Leaf(foreignRoot, server: false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        string directory = Directory.CreateTempSubdirectory("works-tls-alert-").FullName;
        try
        {
            string rootPath = Path.Combine(directory, "root.pem");
            string certificatePath = Path.Combine(directory, "server.pem");
            string keyPath = Path.Combine(directory, "server.key");
            await File.WriteAllTextAsync(rootPath, root.ExportCertificatePem(), deadline.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(certificatePath, server.ExportCertificatePem(), deadline.Token).ConfigureAwait(true);
            using (RSA key = server.GetRSAPrivateKey()!)
                await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem(), deadline.Token).ConfigureAwait(true);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("openssl") { RedirectStandardOutput = true, RedirectStandardError = true },
            };
            // OpenSSL binds its own ephemeral loopback port, then publishes the bound endpoint before accepting.
            foreach (string argument in new[] { "s_server", "-accept", "127.0.0.1:0", "-cert", certificatePath,
                "-key", keyPath, "-CAfile", rootPath, "-Verify", "1", "-verify_return_error", "-naccept", "1",
                "-alpn", "h2", "-www", protocol == SslProtocols.Tls13 ? "-tls1_3" : "-tls1_2" })
                process.StartInfo.ArgumentList.Add(argument);
            try
            {
                process.Start();
            }
            catch (Win32Exception exception)
            {
                throw new InvalidOperationException(
                    "The native TLS certificate-refusal controls require the OpenSSL CLI ('openssl' on PATH). Install OpenSSL before running this test lane.", exception);
            }
            Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                int port = await BoundPortAsync(process, deadline.Token).ConfigureAwait(true);
                Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
                WorksPlatformTrustProbeResult result = await WorksPlatformTrustChecks.ProbeAsync(port, root, root, Identity,
                    foreignClient ? foreign : null, foreignClient ? foreignRoot : null, deadline.Token).ConfigureAwait(true);
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(true);
                _ = await output.ConfigureAwait(true);
                string diagnostic = await error.ConfigureAwait(true);
                process.ExitCode.ShouldBe(0, diagnostic);
                result.ServerAccepted.ShouldBeFalse();
                // TLS 1.2 refusal precedes .NET's server-certificate validation callback. Even an explicit
                // alert then remains inconclusive; TLS 1.3 authenticates the server before its refusal arrives.
                result.ClientRejectedServer.ShouldBe(protocol == SslProtocols.Tls12, result.Failure);
                result.ServerIdentity.ShouldBe(protocol == SslProtocols.Tls13 ? Identity : null, result.Failure);
                result.ServerRefusedClientCertificate.ShouldBe(protocol == SslProtocols.Tls13, result.Failure);
                int? expectedReason = foreignClient ? 1048 : protocol == SslProtocols.Tls13 ? 1116 : null;
                result.CertificateRefusalAlertReason.ShouldBe(expectedReason, result.Failure);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(stopDeadline.Token).ConfigureAwait(true);
                }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Both OpenSSL layouts recognize only certificate-specific peer alerts.</summary>
    [Theory]
    [InlineData(0x0A000418, true)]
    [InlineData(0x0A00045C, true)]
    [InlineData(0x14094418, true)]
    [InlineData(0x1409445C, true)]
    [InlineData(0x0A000410, false)]
    [InlineData(0x0A000126, false)]
    [InlineData(0x0A000086, false)]
    [InlineData(0x14094410, false)]
    [InlineData(0x03000418, false)]
    public void StructuredAlertCodesExcludeGenericTlsAndLocalValidationFailures(int code, bool expected)
    {
        var failure = new IOException("diagnostic only", new CryptographicException(code));
        WorksPlatformTrustChecks.HasCertificateRefusalAlert(failure).ShouldBe(expected && OperatingSystem.IsLinux());
    }

    /// <summary>Certificate-looking error text never upgrades an unauthenticated error to refusal evidence.</summary>
    [Fact]
    public void DiagnosticTextCannotProveCertificateRefusal()
    {
        WorksPlatformTrustChecks.HasCertificateRefusalAlert(new AuthenticationException("tlsv1 alert unknown ca")).ShouldBeFalse();
        WorksPlatformTrustChecks.HasCertificateRefusalAlert(new IOException("tlsv13 alert certificate required",
            new SocketException((int)SocketError.ConnectionReset))).ShouldBeFalse();
    }

    private static async Task<int> BoundPortAsync(Process process, CancellationToken cancellationToken)
    {
        const string prefix = "ACCEPT 127.0.0.1:";
        while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(line.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                && port is > 0 and <= 65535) return port;
        }

        throw new InvalidOperationException("The owned OpenSSL server did not report its bound loopback endpoint.");
    }

    private static X509Certificate2 Root(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static X509Certificate2 Leaf(X509Certificate2 root, bool server)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=works-refusal-probe", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        { new Oid(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri(Identity));
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 certificate = request.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
        return certificate.CopyWithPrivateKey(key);
    }
}
