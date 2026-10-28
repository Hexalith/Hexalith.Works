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
    private readonly Dictionary<string, WorksSdkProofRedisHashSnapshot> _hashSnapshots = new(StringComparer.Ordinal);
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

    /// <summary>Captures every owned Redis key, offline Scheduler file, component and retained credential byte.</summary>
    /// <remarks>A stopped Platform host has already removed its run-scoped issuer child, so its fleet retains no credentials.</remarks>
    internal async Task<IReadOnlyDictionary<string, string>> CaptureFleetAsync(CancellationToken cancellationToken)
    {
        await RequireStoppedFleetAsync(cancellationToken).ConfigureAwait(false);
        using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint + ",allowAdmin=true").ConfigureAwait(false);
        IDatabase database = redis.GetDatabase();
        _snapshot = new(StringComparer.Ordinal);
        _hashSnapshots.Clear();
        foreach (RedisKey key in redis.GetServer(RedisEndpoint).Keys())
        {
            var captured = await CaptureKeyAsync(database, key).ConfigureAwait(false);
            if (captured is null) continue;
            var entry = captured.Value;
            if (entry.Hash is not null) _hashSnapshots.Add(key.ToString(), entry.Hash);
            _snapshot.Add(key.ToString(), (entry.Dump, entry.ExpiresAt, entry.Fingerprint));
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

    /// <summary>Acquires one enumerated key, refusing an inconsistent expiry boundary before hash observation.</summary>
    /// <param name="database">The owned Redis database.</param>
    /// <param name="key">The previously enumerated key.</param>
    /// <returns>Captured bytes and logical evidence, or null if the atomic hash observation proves expiry.</returns>
    internal static async Task<(byte[] Dump, DateTimeOffset? ExpiresAt, string Fingerprint, WorksSdkProofRedisHashSnapshot? Hash)?> CaptureKeyAsync(
        IDatabase database, RedisKey key)
    {
        ArgumentNullException.ThrowIfNull(database);
        byte[] dump = await database.KeyDumpAsync(key).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The stopped synthetic fleet changed during capture.");
        TimeSpan? remaining = await database.KeyTimeToLiveAsync(key).ConfigureAwait(false);
        string fingerprint;
        WorksSdkProofRedisHashSnapshot? hash = null;
        if (await database.KeyTypeAsync(key).ConfigureAwait(false) == RedisType.Hash)
        {
            hash = await ReadHashSnapshotAsync(database, key).ConfigureAwait(false);
            if (hash.Type == "none") return null; // Every field expired during capture.
            if (hash.Type != "hash") throw new InvalidOperationException("The stopped synthetic hash changed type during capture.");
            fingerprint = HashFingerprint(hash.Fields);
        }
        else fingerprint = await RedisValueFingerprintAsync(database, key).ConfigureAwait(false);
        return (dump, remaining is null ? null : DateTimeOffset.UtcNow + remaining, fingerprint, hash);
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
            string expected = fingerprint;
            string actual;
            if (_hashSnapshots.TryGetValue(key, out WorksSdkProofRedisHashSnapshot? captured))
            {
                WorksSdkProofRedisHashSnapshot observed = await ReadHashSnapshotAsync(database, key).ConfigureAwait(false);
                var live = captured.LiveFieldsAt(observed.ObservedAt);
                if (live.Length == 0 && observed.Type == "none") continue;
                if (observed.Type != "hash") throw new InvalidOperationException("A restored synthetic hash changed type or disappeared.");
                expected = HashFingerprint(live);
                actual = HashFingerprint(observed.Fields);
            }
            else actual = await RedisValueFingerprintAsync(database, key).ConfigureAwait(false);
            if (expiresAt <= DateTimeOffset.UtcNow) continue;
            if (actual != expected)
                throw new InvalidOperationException("A restored synthetic value or type differs from its backup; key SHA256="
                    + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))));
            TimeSpan? remaining = await database.KeyTimeToLiveAsync(key).ConfigureAwait(false);
            if (expiresAt is null ? remaining is not null : remaining is null
                || Math.Abs(((DateTimeOffset.UtcNow + remaining!.Value) - expiresAt.Value).TotalMilliseconds) > 500)
                throw new InvalidOperationException("A restored synthetic expiry differs from its backup.");
        }
        HashSet<string> actualKeys = redis.GetServer(RedisEndpoint).Keys().Select(key => key.ToString()).ToHashSet(StringComparer.Ordinal);
        HashSet<string> expectedKeys = _snapshot.Where(pair => pair.Value.ExpiresAt is null || pair.Value.ExpiresAt >= DateTimeOffset.UtcNow)
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        foreach ((string key, WorksSdkProofRedisHashSnapshot captured) in _hashSnapshots)
        {
            WorksSdkProofRedisHashSnapshot observed = await ReadHashSnapshotAsync(database, key).ConfigureAwait(false);
            if (captured.LiveFieldsAt(observed.ObservedAt).Length != 0) continue;
            expectedKeys.Remove(key);
            // Redis can retain an empty physical hash until active field-expiry cleanup. Re-observe
            // its logical contents; never suppress a resurrected field, wrong type or unknown key.
            if (observed.Type == "none" || (observed.Type == "hash" && observed.Fields.Length == 0)) actualKeys.Remove(key);
        }
        // The hash observations await Redis. Use one later clock observation for both whole-key
        // inventories so a TTL crossing those round trips cannot leave a stale expected key.
        DateTimeOffset inventoryObservedAt = DateTimeOffset.UtcNow;
        expectedKeys.RemoveWhere(key => _snapshot[key].ExpiresAt <= inventoryObservedAt);
        foreach (string key in actualKeys.ToArray())
            if (_snapshot.TryGetValue(key, out var entry) && entry.ExpiresAt <= inventoryObservedAt
                && !await database.KeyExistsAsync(key).ConfigureAwait(false)) actualKeys.Remove(key);
        if (!actualKeys.SetEquals(expectedKeys) || await database.KeyExistsAsync("story416:post-snapshot-sentinel").ConfigureAwait(false))
            throw new InvalidOperationException("The full synthetic Redis restore did not reproduce its key inventory. Missing key SHA256: "
                + string.Join(",", expectedKeys.Except(actualKeys).Select(key => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))))
                + "; unexpected key SHA256: "
                + string.Join(",", actualKeys.Except(expectedKeys).Select(key => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))))));
    }

    /// <summary>Hashes all exact value bytes, canonicalizing only unordered Redis hashes and sets.</summary>
    internal static async Task<string> RedisValueFingerprintAsync(IDatabase database, RedisKey key)
    {
        RedisType type = await database.KeyTypeAsync(key).ConfigureAwait(false);
        if (type == RedisType.Hash)
        {
            WorksSdkProofRedisHashSnapshot hash = await ReadHashSnapshotAsync(database, key).ConfigureAwait(false);
            if (hash.Type != "hash") throw new InvalidOperationException("A synthetic hash expired or changed type during observation.");
            return HashFingerprint(hash.Fields);
        }
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(type.ToString());
            if (type == RedisType.Set)
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

    private static string HashFingerprint(IEnumerable<(HashEntry Entry, long ExpiresAt)> fields)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(RedisType.Hash.ToString());
            foreach (var field in fields.OrderBy(field => Convert.ToBase64String((byte[])field.Entry.Name!), StringComparer.Ordinal))
            {
                WriteValue(writer, field.Entry.Name);
                WriteValue(writer, field.Entry.Value);
                writer.Write(field.ExpiresAt);
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private static async Task<WorksSdkProofRedisHashSnapshot> ReadHashSnapshotAsync(IDatabase database, RedisKey key)
    {
        // Redis freezes expiry time for a script. The values, deadlines and server clock therefore
        // describe one observation even when a field expires at this boundary.
        const string script = """
            local t = redis.call('TIME')
            local kind = redis.call('TYPE', KEYS[1]).ok
            local result = {kind, t[1] * 1000 + math.floor(t[2] / 1000)}
            if kind == 'hash' then
                local fields = redis.call('HGETALL', KEYS[1])
                for i = 1, #fields, 2 do
                    local expiry = redis.call('HPEXPIRETIME', KEYS[1], 'FIELDS', 1, fields[i])[1]
                    -- Redis 7.4 listpack expiry lookup reports missing at equality although
                    -- HGETALL retains that field. Atomic visibility pins its deadline to now.
                    if expiry == -2 then expiry = result[2] end
                    table.insert(result, fields[i])
                    table.insert(result, fields[i + 1])
                    table.insert(result, expiry)
                end
            end
            return result
            """;
        RedisResult[] result = (RedisResult[]?)await database.ExecuteAsync("EVAL", script, 1, key).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Redis did not report an atomic hash snapshot.");
        if (result.Length < 2 || (result.Length - 2) % 3 != 0)
            throw new InvalidOperationException("Redis reported an invalid hash snapshot.");
        var fields = new (HashEntry Entry, long ExpiresAt)[(result.Length - 2) / 3];
        for (int index = 0; index < fields.Length; index++)
        {
            int offset = 2 + index * 3;
            fields[index] = (new HashEntry((byte[])result[offset]!, (byte[])result[offset + 1]!), (long)result[offset + 2]);
            if (fields[index].ExpiresAt < -1) throw new InvalidOperationException("A synthetic hash field expired during atomic observation.");
        }
        return new WorksSdkProofRedisHashSnapshot((string)result[0]!, (long)result[1], fields);
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
