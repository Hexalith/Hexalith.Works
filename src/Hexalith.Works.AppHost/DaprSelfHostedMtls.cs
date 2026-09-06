using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using CommunityToolkit.Aspire.Hosting.Dapr;

namespace Hexalith.Works.AppHost;

/// <summary>
/// Composes the local Dapr Sentry control plane and supplies its external trust material to every sidecar.
/// </summary>
internal static class DaprSelfHostedMtls
{
    private const string CertificateDirectoryConfigurationKey = "Dapr:Mtls:CertificateDirectory";
    private const string ContainerCredentialsDirectory = "/var/run/dapr/credentials";
    private const string ContainerSentryConfigurationPath = "/var/run/dapr/config/sentry.yaml";
    private const int SentryPort = 50001;

    /// <summary>
    /// Adds the local Sentry container and returns its external credential directory.
    /// </summary>
    internal static (IResourceBuilder<ContainerResource> Sentry, string CertificateDirectory) AddSentry(
        IDistributedApplicationBuilder builder,
        string sentryConfigurationPath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sentryConfigurationPath);

        string certificateDirectory = ResolveCertificateDirectory(builder);
        Directory.CreateDirectory(certificateDirectory);

        IResourceBuilder<ContainerResource> sentry = builder
            .AddContainer("dapr-sentry", "daprio/sentry", "1.18.3")
            .WithEntrypoint("/sentry")
            .WithArgs(
                $"--issuer-credentials={ContainerCredentialsDirectory}",
                "--trust-domain=public",
                $"--config={ContainerSentryConfigurationPath}",
                "--listen-address=0.0.0.0",
                "--healthz-listen-address=0.0.0.0")
            .WithBindMount(certificateDirectory, ContainerCredentialsDirectory)
            .WithBindMount(sentryConfigurationPath, ContainerSentryConfigurationPath, isReadOnly: true)
            // Dapr's self-hosted configuration resolves Sentry through the host loopback address. Bind the
            // fixed host port directly so daprd does not depend on a DCP proxy while obtaining its identity.
            .WithEndpoint(port: SentryPort, targetPort: SentryPort, name: "grpc", isProxied: false)
            .WithHttpEndpoint(targetPort: 8080, name: "health")
            .WithHttpHealthCheck("/healthz", endpointName: "health")
            .WithLifetime(ContainerLifetime.Session)
            .ExcludeFromManifest();

        string? containerUser = ResolveContainerUser();
        if (!string.IsNullOrWhiteSpace(containerUser))
        {
            sentry = sentry.WithContainerRuntimeArgs("--user", containerUser);
        }

        return (sentry, certificateDirectory);
    }

    /// <summary>
    /// Makes a project and its generated Dapr CLI wait for Sentry, then injects the generated trust bundle.
    /// </summary>
    internal static void ConfigureSidecar(
        IResourceBuilder<ProjectResource> project,
        IDaprSidecarResource sidecar,
        IResourceBuilder<ContainerResource> sentry,
        string certificateDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(sentry);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateDirectory);

        _ = project.WaitFor(sentry);
        sidecar.Annotations.Add(new EnvironmentCallbackAnnotation(async context =>
        {
            context.EnvironmentVariables.TryAdd(
                "DAPR_TRUST_ANCHORS",
                await ReadCredentialAsync(certificateDirectory, "ca.crt", context.CancellationToken).ConfigureAwait(false));
            context.EnvironmentVariables.TryAdd(
                "DAPR_CERT_CHAIN",
                await ReadCredentialAsync(certificateDirectory, "issuer.crt", context.CancellationToken).ConfigureAwait(false));
            context.EnvironmentVariables.TryAdd(
                "DAPR_CERT_KEY",
                await ReadCredentialAsync(certificateDirectory, "issuer.key", context.CancellationToken).ConfigureAwait(false));
            context.EnvironmentVariables.TryAdd("NAMESPACE", "default");
        }));
    }

    private static async Task<string> ReadCredentialAsync(
        string certificateDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(certificateDirectory, fileName);
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Dapr Sentry credential '{path}' was unavailable after Sentry became healthy. "
                + "Keep the certificate directory outside the repository and ensure the Sentry container user can write it.",
                ex);
        }
    }

    private static string ResolveCertificateDirectory(IDistributedApplicationBuilder builder)
    {
        string configured = builder.Configuration[CertificateDirectoryConfigurationKey]
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dapr",
                "certs",
                "hexalith-works");
        string resolved = Path.GetFullPath(configured);
        string repositoryRoot = LocateRepositoryRoot(builder.AppHostDirectory);
        string relative = Path.GetRelativePath(repositoryRoot, resolved);
        if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(relative, "..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration '{CertificateDirectoryConfigurationKey}' must resolve outside the repository; got '{resolved}'.");
        }

        return resolved;
    }

    private static string LocateRepositoryRoot(string appHostDirectory)
    {
        DirectoryInfo? directory = new(appHostDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hexalith.Works.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate Hexalith.Works.slnx from AppHost directory '{appHostDirectory}'.");
    }

    private static string? ResolveContainerUser()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/self/status"))
        {
            return null;
        }

        Dictionary<string, string> status = File.ReadLines("/proc/self/status")
            .Select(static line => line.Split(':', 2))
            .Where(static parts => parts.Length == 2)
            .ToDictionary(static parts => parts[0], static parts => parts[1], StringComparer.Ordinal);
        string? userId = FirstValue(status, "Uid");
        string? groupId = FirstValue(status, "Gid");
        return userId is null || groupId is null ? null : $"{userId}:{groupId}";
    }

    private static string? FirstValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out string? value)
            ? value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            : null;
}
