using System.Diagnostics;
using System.Security.Cryptography;

using StackExchange.Redis;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Owns an isolated disposable Redis keyspace and generated Dapr component for the live SDK proof.</summary>
internal sealed class WorksSdkProofRedis(string name, string directory) : IAsyncDisposable
{
    private bool _redisStarted;
    private bool _volumeCreated;
    private Dictionary<string, (byte[] Dump, DateTimeOffset? ExpiresAt)>? _snapshot;
    private Dictionary<string, byte[]>? _files;

    internal string ComponentPath => Path.Combine(directory, "statestore.yaml");

    internal string RedisEndpoint { get; private set; } = string.Empty;

    internal string? SchedulerVolumeName { get; private set; }

    internal string? CertificateDirectory => SchedulerVolumeName is null ? null : Path.Combine(directory, "certificates");

    internal static async Task<WorksSdkProofRedis> StartAsync(CancellationToken cancellationToken, bool activateSdkWriter = false)
    {
        string name = "works-sdk415-" + Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(directory);
        var fixture = new WorksSdkProofRedis(name, directory);
        try
        {
            _ = await DockerAsync(["run", "--rm", "-d", "--name", name, "-p", "127.0.0.1::6379", "redis:7.4-alpine"], cancellationToken).ConfigureAwait(false);
            fixture._redisStarted = true;
            string binding = (await DockerAsync(["port", name, "6379/tcp"], cancellationToken).ConfigureAwait(false)).Trim();
            fixture.RedisEndpoint = binding;
            if (activateSdkWriter)
            {
                fixture.SchedulerVolumeName = "works-sdk416-" + name["works-sdk415-".Length..];
                _ = await DockerAsync(["volume", "create", fixture.SchedulerVolumeName], cancellationToken).ConfigureAwait(false);
                fixture._volumeCreated = true;
                // Explicit synthetic admission, before the real readiness gate runs. This store belongs only
                // to this disposable proof; no existing or production fleet is admitted by this marker.
                string activation = System.Text.Json.JsonSerializer.Serialize(new
                {
                    schemaVersion = 1, writerProtocolVersion = 2,
                    cutoverCommit = "story416-disposable-synthetic", activatedAt = DateTimeOffset.UtcNow,
                });
                _ = await DockerAsync(["exec", name, "redis-cli", "HSET", "eventstore||projection-delivery-writer-protocol",
                    "data", activation, "version", "1"], cancellationToken).ConfigureAwait(false);
            }
            string yaml = """
                apiVersion: dapr.io/v1alpha1
                kind: Component
                metadata:
                  name: statestore
                spec:
                  type: state.redis
                  version: v1
                  metadata:
                    - name: redisHost
                      value: "BINDING"
                    - name: redisPassword
                      value: ""
                    - name: actorStateStore
                      value: "true"
                scopes:
                  - eventstore
                  - works
                  - eventstore-admin
                  - eventstore-operations
                """;
            await File.WriteAllTextAsync(fixture.ComponentPath, yaml.Replace("BINDING", binding, StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_redisStarted) _ = await DockerAsync(["rm", "-f", name], CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (_volumeCreated) _ = await DockerAsync(["volume", "rm", SchedulerVolumeName!], CancellationToken.None).ConfigureAwait(false);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>Captures every owned Redis key, offline Scheduler file, component and credential byte.</summary>
    internal async Task<IReadOnlyDictionary<string, string>> CaptureFleetAsync(CancellationToken cancellationToken)
    {
        await RequireStoppedFleetAsync(cancellationToken).ConfigureAwait(false);
        using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint + ",allowAdmin=true").ConfigureAwait(false);
        IDatabase database = redis.GetDatabase();
        _snapshot = new(StringComparer.Ordinal);
        foreach (RedisKey key in redis.GetServer(RedisEndpoint).Keys())
        {
            byte[] dump = await database.KeyDumpAsync(key).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The stopped synthetic fleet changed during capture.");
            TimeSpan? remaining = await database.KeyTimeToLiveAsync(key).ConfigureAwait(false);
            _snapshot.Add(key.ToString(), (dump, remaining is null ? null : DateTimeOffset.UtcNow + remaining));
        }
        _files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes, StringComparer.Ordinal);
        await ArchiveSchedulerAsync(restore: false, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> hashes = _files.ToDictionary(pair => pair.Key, pair => Convert.ToHexString(SHA256.HashData(pair.Value)), StringComparer.Ordinal);
        hashes["scheduler.tar"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, "scheduler.tar"), cancellationToken).ConfigureAwait(false)));
        hashes["redis-key-count"] = _snapshot.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return hashes;
    }

    /// <summary>Restores the complete owned fleet and proves a post-snapshot sentinel is removed.</summary>
    internal async Task RestoreFleetAsync(CancellationToken cancellationToken)
    {
        await RequireStoppedFleetAsync(cancellationToken).ConfigureAwait(false);
        if (_snapshot is null || _files is null) throw new InvalidOperationException("No compatible fleet snapshot exists.");
        using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint + ",allowAdmin=true").ConfigureAwait(false);
        IDatabase database = redis.GetDatabase();
        const string sentinel = "story416:post-snapshot-sentinel";
        await database.StringSetAsync(sentinel, "must-be-removed").ConfigureAwait(false);
        await redis.GetServer(RedisEndpoint).FlushDatabaseAsync().ConfigureAwait(false);
        HashSet<string> expected = new(StringComparer.Ordinal);
        foreach ((string key, (byte[] dump, DateTimeOffset? expiresAt)) in _snapshot)
        {
            TimeSpan? remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) continue;
            await database.KeyRestoreAsync(key, dump, remaining).ConfigureAwait(false);
            expected.Add(key);
        }
        HashSet<string> actual = redis.GetServer(RedisEndpoint).Keys().Select(key => key.ToString()).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected) || await database.KeyExistsAsync(sentinel).ConfigureAwait(false))
            throw new InvalidOperationException("The full synthetic Redis restore did not reproduce its key inventory.");
        foreach (string key in expected)
        {
            byte[] dump = await database.KeyDumpAsync(key).ConfigureAwait(false)
                ?? throw new InvalidOperationException("A restored key is missing.");
            if (!dump.AsSpan().SequenceEqual(_snapshot[key].Dump))
                throw new InvalidOperationException("A restored synthetic value differs from its backup.");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "post-snapshot-sentinel"), "must-be-removed", cancellationToken).ConfigureAwait(false);
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "scheduler.tar")) File.Delete(path);
        foreach ((string relative, byte[] bytes) in _files)
        {
            string path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        }
        await ArchiveSchedulerAsync(restore: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireStoppedFleetAsync(CancellationToken cancellationToken)
    {
        if (!_volumeCreated || SchedulerVolumeName is null || !RedisEndpoint.StartsWith("127.0.0.1:", StringComparison.Ordinal)
            || RedisEndpoint.EndsWith(":6379", StringComparison.Ordinal))
            throw new InvalidOperationException("Fleet restore requires this fixture's isolated Redis and Scheduler volume.");
        string owners = await DockerAsync(["ps", "--all", "--filter", "volume=" + SchedulerVolumeName, "--format", "{{.ID}}"], cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(owners)) throw new InvalidOperationException("Stop every synthetic Scheduler owner before fleet capture or restore.");
    }

    private async Task ArchiveSchedulerAsync(bool restore, CancellationToken cancellationToken)
    {
        string script = restore ? "find /fleet -mindepth 1 -delete && tar xf /backup/scheduler.tar -C /fleet"
            : "tar cf /backup/scheduler.tar -C /fleet .";
        _ = await DockerAsync(["run", "--rm", "--entrypoint", "sh", "-v", SchedulerVolumeName + ":/fleet" + (restore ? "" : ":ro"),
            "-v", directory + ":/backup", "redis:7.4-alpine", "-c", script], cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> DockerAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch { process.Kill(true); throw; }
        string diagnostic = await error.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException("Synthetic Redis fixture Docker operation failed: " + diagnostic);
        return await output.ConfigureAwait(false);
    }
}
