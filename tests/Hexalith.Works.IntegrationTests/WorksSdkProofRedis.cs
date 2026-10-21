using System.Diagnostics;
using System.Security.Cryptography;

using StackExchange.Redis;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Owns an isolated disposable Redis keyspace and generated Dapr component for the live SDK proof.</summary>
internal sealed class WorksSdkProofRedis(string name, string directory, Func<string[], CancellationToken, Task<string>> dockerCommand) : IAsyncDisposable
{
    private readonly Func<string[], CancellationToken, Task<string>> _docker = dockerCommand;
    private bool _redisStarted;
    private bool _volumeCreated;
    private Dictionary<string, (byte[] Dump, DateTimeOffset? ExpiresAt, string Fingerprint)>? _snapshot;
    private Dictionary<string, byte[]>? _files;
    private Dictionary<string, UnixFileMode>? _fileModes;
    private string? _schedulerInventory;

    internal string ComponentPath => Path.Combine(directory, "statestore.yaml");

    internal string RedisEndpoint { get; private set; } = string.Empty;

    internal string? SchedulerVolumeName { get; private set; }

    internal string? CertificateDirectory => SchedulerVolumeName is null ? null : Path.Combine(directory, "certificates");

    internal static async Task<WorksSdkProofRedis> StartAsync(CancellationToken cancellationToken, bool activateSdkWriter = false,
        Func<string[], CancellationToken, Task<string>>? dockerCommand = null)
    {
        string name = "works-sdk415-" + Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), name);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var fixture = new WorksSdkProofRedis(name, directory, dockerCommand ?? DockerAsync);
        try
        {
            // Mark the unique labelled creation attempt before Docker starts: cancellation can arrive
            // after the daemon created its container but before the client acknowledges success.
            fixture._redisStarted = true;
            _ = await fixture._docker(["run", "--rm", "-d", "--name", name, "--label", "hexalith.works.sdk-fixture=" + name,
                "-p", "127.0.0.1::6379", "redis:7.4-alpine"], cancellationToken).ConfigureAwait(false);
            string binding = (await fixture._docker(["port", name, "6379/tcp"], cancellationToken).ConfigureAwait(false)).Trim();
            fixture.RedisEndpoint = binding;
            using (var readiness = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                readiness.CancelAfter(TimeSpan.FromSeconds(60));
                while (true)
                {
                    try
                    {
                        string pong = await fixture._docker(["exec", name, "redis-cli", "PING"], readiness.Token).ConfigureAwait(false);
                        if (pong.Trim() != "PONG") throw new InvalidOperationException("Synthetic Redis did not acknowledge PING.");
                        break;
                    }
                    catch (InvalidOperationException exception) when (exception.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(100, readiness.Token).ConfigureAwait(false);
                    }
                }

                // docker exec reaches Redis inside the container; the published host port can still lag behind it.
                // Every consumer connects through that host binding, so it must answer before the fixture is ready.
                // A substituted Docker command (ownership tests) publishes no real port and skips this probe.
                while (dockerCommand is null)
                {
                    try
                    {
                        using ConnectionMultiplexer probe = await ConnectionMultiplexer.ConnectAsync(binding + ",connectTimeout=2000")
                            .WaitAsync(readiness.Token).ConfigureAwait(false);
                        _ = await probe.GetDatabase().PingAsync().WaitAsync(readiness.Token).ConfigureAwait(false);
                        break;
                    }
                    catch (RedisConnectionException)
                    {
                        await Task.Delay(100, readiness.Token).ConfigureAwait(false);
                    }
                }
            }
            if (activateSdkWriter)
            {
                fixture.SchedulerVolumeName = "works-sdk416-" + name["works-sdk415-".Length..];
                fixture._volumeCreated = true;
                _ = await fixture._docker(["volume", "create", "--label", "hexalith.works.sdk-fixture=" + name,
                    fixture.SchedulerVolumeName], cancellationToken).ConfigureAwait(false);
                // Explicit synthetic admission, before the real readiness gate runs. This store belongs only
                // to this disposable proof; no existing or production fleet is admitted by this marker.
                string activation = System.Text.Json.JsonSerializer.Serialize(new
                {
                    schemaVersion = 1, writerProtocolVersion = 2,
                    cutoverCommit = "story416-disposable-synthetic", activatedAt = DateTimeOffset.UtcNow,
                });
                _ = await fixture._docker(["exec", name, "redis-cli", "HSET", "eventstore||projection-delivery-writer-protocol",
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
        catch (Exception startupFailure)
        {
            try { await fixture.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Synthetic fixture startup and owned-resource cleanup both failed.", startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    internal async Task CaptureAuthorityAsync(string signingKey, string digestKey, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "synthetic-authority.json");
        string channels = WorksPlatformAppChannelTokens.EnsureCaptured(ComponentPath);
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["signingKey"] = signingKey, ["digestKey"] = digestKey,
            ["developmentSigningKey"] = WorksAppHostSmokeHarness.DevSigningKey,
            ["appChannelTokensSha256"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(channels, cancellationToken).ConfigureAwait(false))),
        }), cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    internal async Task<(string SigningKey, string DigestKey)> ReadRestoredAuthorityAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "synthetic-authority.json");
        var authority = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false))!;
        if (authority["developmentSigningKey"] != WorksAppHostSmokeHarness.DevSigningKey)
            throw new InvalidOperationException("The captured development verifier key is incompatible.");
        string channels = WorksPlatformAppChannelTokens.EnsureCaptured(ComponentPath);
        if (authority["appChannelTokensSha256"] != Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(channels, cancellationToken).ConfigureAwait(false))))
            throw new InvalidOperationException("Restored synthetic channel tokens differ from their captured authority binding.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return (authority["signingKey"], authority["digestKey"]);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_redisStarted)
            {
                string owned = (await _docker(["ps", "--all", "--filter", "label=hexalith.works.sdk-fixture=" + name,
                    "--format", "{{.Names}}"], CancellationToken.None).ConfigureAwait(false)).Trim();
                if (owned.Length > 0)
                {
                    if (owned != name) throw new InvalidOperationException("Synthetic Redis cleanup ownership is ambiguous.");
                    _ = await _docker(["rm", "-f", name], CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try
            {
                if (_volumeCreated)
                {
                    string owned = (await _docker(["volume", "ls", "--filter", "label=hexalith.works.sdk-fixture=" + name,
                        "--format", "{{.Name}}"], CancellationToken.None).ConfigureAwait(false)).Trim();
                    if (owned.Length > 0)
                    {
                        if (owned != SchedulerVolumeName) throw new InvalidOperationException("Synthetic Scheduler cleanup ownership is ambiguous.");
                        _ = await _docker(["volume", "rm", SchedulerVolumeName!], CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
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
            _snapshot.Add(key.ToString(), (dump, remaining is null ? null : DateTimeOffset.UtcNow + remaining,
                await RedisValueFingerprintAsync(database, key).ConfigureAwait(false)));
        }
        _files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes, StringComparer.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            _fileModes = new(StringComparer.Ordinal);
            foreach (string relative in _files.Keys) _fileModes.Add(relative, File.GetUnixFileMode(Path.Combine(directory, relative)));
        }
        await ArchiveSchedulerAsync(restore: false, cancellationToken).ConfigureAwait(false);
        _schedulerInventory = await SchedulerInventoryAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> hashes = _files.ToDictionary(pair => pair.Key, pair => Convert.ToHexString(SHA256.HashData(pair.Value)), StringComparer.Ordinal);
        hashes["scheduler.tar"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, "scheduler.tar"), cancellationToken).ConfigureAwait(false)));
        hashes["scheduler-file-inventory-sha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_schedulerInventory)));
        hashes["redis-key-count"] = _snapshot.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string inventory = string.Join("\n", _snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pair.Key))) + ":" + Convert.ToHexString(SHA256.HashData(pair.Value.Dump))));
        hashes["redis-full-value-inventory-sha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(inventory)));
        string logicalInventory = string.Join("\n", _snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pair.Key))) + ":" + pair.Value.Fingerprint));
        hashes["redis-logical-value-inventory-sha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(logicalInventory)));
        return hashes;
    }

    /// <summary>The capture-only inventory of original DUMP bytes, which a re-observation cannot reproduce.</summary>
    internal const string DumpInventoryKey = "redis-full-value-inventory-sha256";

    /// <summary>Re-observes the stopped fleet as it now is, without consulting any captured snapshot value.</summary>
    /// <remarks>
    /// Redis may re-encode unordered hashes and sets after RESTORE, so a re-observation reports every other
    /// <see cref="CaptureFleetAsync"/> entry, including the canonical logical inventory, and omits <see cref="DumpInventoryKey"/>.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The re-observed file, Scheduler and Redis inventory hashes.</returns>
    internal async Task<IReadOnlyDictionary<string, string>> ObserveFleetAsync(CancellationToken cancellationToken)
    {
        await RequireStoppedFleetAsync(cancellationToken).ConfigureAwait(false);
        using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint + ",allowAdmin=true").ConfigureAwait(false);
        IDatabase database = redis.GetDatabase();
        var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (RedisKey key in redis.GetServer(RedisEndpoint).Keys())
            fingerprints.Add(key.ToString(), await RedisValueFingerprintAsync(database, key).ConfigureAwait(false));
        Dictionary<string, string> hashes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path))
            .Where(relative => relative != "scheduler.tar")
            .ToDictionary(relative => relative, relative => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, relative)))), StringComparer.Ordinal);
        hashes["scheduler.tar"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, "scheduler.tar"), cancellationToken).ConfigureAwait(false)));
        hashes["scheduler-file-inventory-sha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            await SchedulerInventoryAsync(cancellationToken).ConfigureAwait(false))));
        hashes["redis-key-count"] = fingerprints.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string logicalInventory = string.Join("\n", fingerprints
            .Select(pair => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pair.Key))) + ":" + pair.Value));
        hashes["redis-logical-value-inventory-sha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(logicalInventory)));
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
        foreach ((string key, (byte[] dump, DateTimeOffset? expiresAt, _)) in _snapshot)
        {
            TimeSpan? remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) continue;
            await database.KeyRestoreAsync(key, dump, remaining).ConfigureAwait(false);
        }
        await VerifyRestoredRedisAsync(cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, "post-snapshot-sentinel"), "must-be-removed", cancellationToken).ConfigureAwait(false);
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "scheduler.tar")) File.Delete(path);
        foreach ((string relative, byte[] bytes) in _files)
        {
            string path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            if (!(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)).AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("Captured configuration or credential restore differs from its backup.");
            if (!OperatingSystem.IsWindows() && _fileModes is not null)
                File.SetUnixFileMode(path, _fileModes[relative]);
        }
        await ArchiveSchedulerAsync(restore: true, cancellationToken).ConfigureAwait(false);
        if (await SchedulerInventoryAsync(cancellationToken).ConfigureAwait(false) != _schedulerInventory)
            throw new InvalidOperationException("Restored Scheduler file inventory differs from its stopped backup.");
    }

    /// <summary>Verifies complete keys, full value bytes and absolute expiry after restoring the owned store.</summary>
    internal async Task VerifyRestoredRedisAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_snapshot is null) throw new InvalidOperationException("No compatible Redis snapshot exists.");
        using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint + ",allowAdmin=true").ConfigureAwait(false);
        IDatabase database = redis.GetDatabase();
        foreach ((string key, (_, DateTimeOffset? expiresAt, string fingerprint)) in _snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expiresAt <= DateTimeOffset.UtcNow) continue;
            string actual = await RedisValueFingerprintAsync(database, key).ConfigureAwait(false);
            if (expiresAt <= DateTimeOffset.UtcNow) continue;
            if (actual != fingerprint)
                throw new InvalidOperationException("A restored synthetic value or type differs from its backup; key SHA256="
                    + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))));
            TimeSpan? remaining = await database.KeyTimeToLiveAsync(key).ConfigureAwait(false);
            if (expiresAt is null ? remaining is not null : remaining is null
                || Math.Abs(((DateTimeOffset.UtcNow + remaining!.Value) - expiresAt.Value).TotalMilliseconds) > 500)
                throw new InvalidOperationException("A restored synthetic expiry differs from its backup.");
        }
        HashSet<string> actualKeys = redis.GetServer(RedisEndpoint).Keys().Select(key => key.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (string key in actualKeys.ToArray())
            if (_snapshot.TryGetValue(key, out var entry) && entry.ExpiresAt <= DateTimeOffset.UtcNow
                && !await database.KeyExistsAsync(key).ConfigureAwait(false)) actualKeys.Remove(key);
        HashSet<string> expectedKeys = _snapshot.Where(pair => pair.Value.ExpiresAt is null || pair.Value.ExpiresAt > DateTimeOffset.UtcNow)
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        if (!actualKeys.SetEquals(expectedKeys) || await database.KeyExistsAsync("story416:post-snapshot-sentinel").ConfigureAwait(false))
            throw new InvalidOperationException("The full synthetic Redis restore did not reproduce its key inventory.");
    }

    /// <summary>Hashes all exact value bytes, canonicalizing only unordered Redis hashes and sets.</summary>
    internal static async Task<string> RedisValueFingerprintAsync(IDatabase database, RedisKey key)
    {
        RedisType type = await database.KeyTypeAsync(key).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(type.ToString());
            if (type == RedisType.Hash)
            {
                HashEntry[] entries = [.. (await database.HashGetAllAsync(key).ConfigureAwait(false))
                    .OrderBy(entry => Convert.ToBase64String((byte[])entry.Name!), StringComparer.Ordinal)];
                object[] arguments = new object[entries.Length + 3];
                arguments[0] = key;
                arguments[1] = "FIELDS";
                arguments[2] = entries.Length;
                for (int index = 0; index < entries.Length; index++) arguments[index + 3] = entries[index].Name;
                RedisResult[] deadlines = (RedisResult[]?)await database.ExecuteAsync("HPEXPIRETIME", arguments).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Redis did not report captured hash-field deadlines.");
                if (deadlines.Length != entries.Length) throw new InvalidOperationException("The captured hash changed during expiry inspection.");
                for (int index = 0; index < entries.Length; index++)
                {
                    WriteValue(writer, entries[index].Name);
                    WriteValue(writer, entries[index].Value);
                    writer.Write((long)deadlines[index]);
                }
            }
            else if (type == RedisType.Set)
            {
                RedisValue[] members = await database.SetMembersAsync(key).ConfigureAwait(false);
                foreach (RedisValue member in members.OrderBy(member => Convert.ToBase64String((byte[])member!), StringComparer.Ordinal))
                    WriteValue(writer, member);
            }
            else
            {
                // Keep the full RDB encoding for ordered values and streams, including consumer groups,
                // pending entries and other stream metadata that XRANGE alone would omit.
                byte[] dump = await database.KeyDumpAsync(key).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A restored or captured synthetic key is missing.");
                writer.Write(dump.Length);
                writer.Write(dump);
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private static void WriteValue(BinaryWriter writer, RedisValue value)
    {
        byte[] bytes = (byte[]?)value ?? throw new InvalidOperationException("A Redis value cannot be null.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    // Binds every entry's type, mode, owner and link target, including the volume root itself, plus the content hash of
    // every regular file. A restore that changes permissions, ownership, entry types or symbolic links changes the inventory.
    internal Task<string> SchedulerInventoryAsync(CancellationToken cancellationToken)
        => _docker(["run", "--rm", "--entrypoint", "sh", "-v", SchedulerVolumeName + ":/fleet:ro",
            "redis:7.4-alpine", "-c", SchedulerInventoryScript], cancellationToken);

    /// <summary>Runs a fixture-owned shell script against this fixture's own Scheduler volume.</summary>
    /// <param name="script">The script, run as root with the volume mounted at <c>/fleet</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The script output.</returns>
    internal Task<string> ExecuteInSchedulerVolumeAsync(string script, CancellationToken cancellationToken)
    {
        if (!_volumeCreated || SchedulerVolumeName is null) throw new InvalidOperationException("This fixture owns no Scheduler volume.");
        return _docker(["run", "--rm", "--entrypoint", "sh", "-v", SchedulerVolumeName + ":/fleet", "redis:7.4-alpine", "-c", script], cancellationToken);
    }

    /// <summary>The complete Scheduler volume inventory command run against the read-only volume mount.</summary>
    internal const string SchedulerInventoryScript =
        "cd /fleet && find . -exec stat -c '%F|%a|%u:%g|%N' {} + | sort && find . -type f -exec sha256sum {} + | sort";

    /// <summary>Re-reads every captured configuration and credential file of the running restored fleet.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The relative paths whose re-read bytes equal their capture.</returns>
    internal async Task<IReadOnlyList<string>> VerifyRestoredConfigurationAsync(CancellationToken cancellationToken)
    {
        if (_files is null) throw new InvalidOperationException("No fleet configuration snapshot exists.");
        var verified = new List<string>();
        foreach ((string relative, byte[] bytes) in _files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            byte[] actual = await File.ReadAllBytesAsync(Path.Combine(directory, relative), cancellationToken).ConfigureAwait(false);
            if (!actual.AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("The running restored fleet changed captured configuration or credential bytes: "
                    + relative + "; expected SHA256=" + Convert.ToHexString(SHA256.HashData(bytes))
                    + "; actual SHA256=" + Convert.ToHexString(SHA256.HashData(actual)));
            verified.Add(relative);
        }

        return verified;
    }

    private async Task RequireStoppedFleetAsync(CancellationToken cancellationToken)
    {
        if (!_volumeCreated || SchedulerVolumeName is null || !RedisEndpoint.StartsWith("127.0.0.1:", StringComparison.Ordinal)
            || RedisEndpoint.EndsWith(":6379", StringComparison.Ordinal))
            throw new InvalidOperationException("Fleet restore requires this fixture's isolated Redis and Scheduler volume.");
        string owners = await _docker(["ps", "--all", "--filter", "volume=" + SchedulerVolumeName, "--format", "{{.ID}}"], cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(owners)) throw new InvalidOperationException("Stop every synthetic Scheduler owner before fleet capture or restore.");
    }

    private async Task ArchiveSchedulerAsync(bool restore, CancellationToken cancellationToken)
    {
        string script = restore ? "find /fleet -mindepth 1 -delete && tar xf /backup/scheduler.tar -C /fleet"
            : "tar cf /backup/scheduler.tar -C /fleet .";
        _ = await _docker(["run", "--rm", "--entrypoint", "sh", "-v", SchedulerVolumeName + ":/fleet" + (restore ? "" : ":ro"),
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
