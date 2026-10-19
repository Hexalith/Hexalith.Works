using System.Text;

using Shouldly;

using StackExchange.Redis;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Actual isolated Redis restore preserves complete values, expiry and captured authority.</summary>
public sealed class WorksPlatformFleetRestoreTests
{
    /// <summary>Initial component capture binds only Platform's synthetic Redis endpoint.</summary>
    [Fact]
    public void InitialComponentCapturePreservesTextAndBindsOnlyTheSelectedEndpoint()
    {
        string root = Path.Combine(Path.GetTempPath(), "works416-component-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "original.yaml");
            const string content = "# Platform-owned component\r\nredisHost: localhost:6379\r\nother: localhost:6378\r\n";
            File.WriteAllText(original, content);
            string captured = Path.Combine(root, "captured.yaml");
            WorksSdkRecoveryPipelineSmokeTests.CaptureComponentConfiguration(original, captured, "127.0.0.1:49321", isPlatform: true);
            File.ReadAllText(captured).ShouldBe(content.Replace("localhost:6379", "127.0.0.1:49321", StringComparison.Ordinal));
            string retained = Path.Combine(root, "retained.yaml");
            WorksSdkRecoveryPipelineSmokeTests.CaptureComponentConfiguration(original, retained, "127.0.0.1:49321", isPlatform: false);
            File.ReadAllText(retained).ShouldBe(content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A retained host cannot overwrite captured bytes from a different component original.</summary>
    [Fact]
    public void RetainedComponentStartupPreservesCapturedBytesEvenWhenOriginalChangesOrDisappears()
    {
        string root = Path.Combine(Path.GetTempPath(), "works416-component-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "original.yaml");
            string captured = Path.Combine(root, "captured.yaml");
            File.WriteAllText(original, "# Platform-owned component\r\nredisHost: localhost:6379\r\n");
            WorksSdkRecoveryPipelineSmokeTests.CaptureComponentConfiguration(original, captured, "127.0.0.1:49321", isPlatform: true);
            byte[] preserved = File.ReadAllBytes(captured);
            File.WriteAllText(original, "# Works-owned component\nredisHost: incompatible\n");
            WorksSdkRecoveryPipelineSmokeTests.CaptureComponentConfiguration(original, captured, "127.0.0.1:49322", isPlatform: true);
            File.ReadAllBytes(captured).ShouldBe(preserved);
            File.Delete(original);
            WorksSdkRecoveryPipelineSmokeTests.CaptureComponentConfiguration(original, captured, "127.0.0.1:49323", isPlatform: true);
            File.ReadAllBytes(captured).ShouldBe(preserved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Redis encoding changes cannot hide missing members, bytes, list order or key expiry.</summary>
    [Fact]
    public async Task CompleteFleetRoundTripPreservesEveryLogicalValueAndCapturedFile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorksSdkProofRedis fixture = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var ownedFixture = fixture.ConfigureAwait(true);
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(fixture.RedisEndpoint).ConfigureAwait(true);
        IDatabase database = connection.GetDatabase();
        byte[] binary = Enumerable.Range(0, 4096).Select(index => (byte)(index % 256)).ToArray();
        HashEntry[] hash = Enumerable.Range(0, 1024)
            .Select(index => new HashEntry("field-" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                Encoding.UTF8.GetBytes("synthetic-value-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-\u03bb\0")))
            .ToArray();
        RedisValue[] set = Enumerable.Range(0, 1024).Select(index => (RedisValue)("member-" + index)).ToArray();
        RedisValue[] list = ["first", binary, "first", "last"];
        SortedSetEntry[] sorted = [new("z", 1.5), new("a", 1.5), new(binary, -12.75)];
        await database.StringSetAsync("binary", binary, TimeSpan.FromMinutes(30)).ConfigureAwait(true);
        await database.HashSetAsync("large-hash", hash).ConfigureAwait(true);
        long fieldDeadline = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds();
        await database.ExecuteAsync("HPEXPIREAT", "large-hash", fieldDeadline, "FIELDS", 1, hash[1].Name).ConfigureAwait(true);
        await database.SetAddAsync("large-set", set).ConfigureAwait(true);
        await database.ListRightPushAsync("ordered-list", list).ConfigureAwait(true);
        await database.SortedSetAddAsync("scored-set", sorted).ConfigureAwait(true);
        await database.StringSetAsync("integer-string", "00123").ConfigureAwait(true);
        RedisValue streamId = await database.StreamAddAsync("stream", [new("payload", binary), new("kind", "synthetic")]).ConfigureAwait(true);
        await database.StreamCreateConsumerGroupAsync("stream", "group", "0-0").ConfigureAwait(true);
        (await database.StreamReadGroupAsync("stream", "group", "consumer", ">").ConfigureAwait(true)).Single().Id.ShouldBe(streamId);
        byte[] streamDump = (await database.KeyDumpAsync("stream").ConfigureAwait(true))!;

        string root = Path.GetDirectoryName(fixture.ComponentPath)!;
        string configuration = Path.Combine(root, "synthetic-provider.json");
        byte[] configurationBytes = Encoding.UTF8.GetBytes("{\"provider\":\"synthetic\",\"generation\":1}\n");
        await File.WriteAllBytesAsync(configuration, configurationBytes, ct).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(configuration, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await fixture.CaptureAuthorityAsync("synthetic-signing-key", "synthetic-digest-key", ct).ConfigureAwait(true);
        byte[] componentBytes = await File.ReadAllBytesAsync(fixture.ComponentPath, ct).ConfigureAwait(true);
        TimeSpan before = (await database.KeyTimeToLiveAsync("binary").ConfigureAwait(true))!.Value;
        IReadOnlyDictionary<string, string> captured = await fixture.CaptureFleetAsync(ct).ConfigureAwait(true);
        int keyCount = int.Parse(captured["redis-key-count"], System.Globalization.CultureInfo.InvariantCulture);

        await database.HashDeleteAsync("large-hash", hash[0].Name).ConfigureAwait(true);
        await database.SetRemoveAsync("large-set", set[0]).ConfigureAwait(true);
        await database.ListLeftPopAsync("ordered-list").ConfigureAwait(true);
        await database.SortedSetIncrementAsync("scored-set", "z", 7).ConfigureAwait(true);
        await database.StringSetAsync("binary", "changed").ConfigureAwait(true);
        await database.StringSetAsync("post-snapshot-key", "must disappear").ConfigureAwait(true);
        await database.StreamAcknowledgeAsync("stream", "group", streamId).ConfigureAwait(true);
        await File.WriteAllTextAsync(configuration, "changed", ct).ConfigureAwait(true);
        await File.WriteAllTextAsync(fixture.ComponentPath, "changed", ct).ConfigureAwait(true);
        await fixture.CaptureAuthorityAsync("changed-signing-key", "changed-digest-key", ct).ConfigureAwait(true);

        await fixture.RestoreFleetAsync(ct).ConfigureAwait(true);

        (await database.StringGetAsync("binary").ConfigureAwait(true)).ShouldBe((RedisValue)binary);
        (await database.StringGetAsync("integer-string").ConfigureAwait(true)).ShouldBe((RedisValue)"00123");
        HashEntry[] restoredHash = await database.HashGetAllAsync("large-hash").ConfigureAwait(true);
        restoredHash.Length.ShouldBe(hash.Length);
        foreach (HashEntry entry in hash)
            restoredHash.Single(value => value.Name == entry.Name).Value.ShouldBe(entry.Value);
        RedisResult deadlineResult = await database.ExecuteAsync("HPEXPIRETIME", "large-hash", "FIELDS", 1, hash[1].Name).ConfigureAwait(true);
        RedisResult[] deadlines = (RedisResult[]?)deadlineResult ?? throw new InvalidOperationException("Missing field deadlines.");
        ((long)deadlines.Single()).ShouldBe(fieldDeadline);
        (await database.SetMembersAsync("large-set").ConfigureAwait(true)).ToHashSet().SetEquals(set).ShouldBeTrue();
        (await database.ListRangeAsync("ordered-list").ConfigureAwait(true)).ShouldBe(list);
        SortedSetEntry[] restoredSorted = await database.SortedSetRangeByRankWithScoresAsync("scored-set").ConfigureAwait(true);
        restoredSorted.Length.ShouldBe(sorted.Length);
        foreach (SortedSetEntry entry in sorted)
            restoredSorted.Single(value => value.Element == entry.Element).Score.ShouldBe(entry.Score);
        TimeSpan after = (await database.KeyTimeToLiveAsync("binary").ConfigureAwait(true))!.Value;
        after.ShouldBeGreaterThan(TimeSpan.Zero);
        after.ShouldBeLessThanOrEqualTo(before);
        (before - after).ShouldBeLessThan(TimeSpan.FromSeconds(30));
        (await database.KeyTimeToLiveAsync("integer-string").ConfigureAwait(true)).ShouldBeNull();
        (await database.KeyDumpAsync("stream").ConfigureAwait(true)).ShouldBe(streamDump);
        (await database.StreamPendingAsync("stream", "group").ConfigureAwait(true)).PendingMessageCount.ShouldBe(1);
        (await database.KeyExistsAsync("post-snapshot-key").ConfigureAwait(true)).ShouldBeFalse();
        (await database.KeyExistsAsync("story416:post-snapshot-sentinel").ConfigureAwait(true)).ShouldBeFalse();
        connection.GetServer(fixture.RedisEndpoint).Keys().Count().ShouldBe(keyCount);
        (await File.ReadAllBytesAsync(configuration, ct).ConfigureAwait(true)).ShouldBe(configurationBytes);
        (await File.ReadAllBytesAsync(fixture.ComponentPath, ct).ConfigureAwait(true)).ShouldBe(componentBytes);
        if (!OperatingSystem.IsWindows()) File.GetUnixFileMode(configuration).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        (await fixture.ReadRestoredAuthorityAsync(ct).ConfigureAwait(true)).ShouldBe(("synthetic-signing-key", "synthetic-digest-key"));
        File.Exists(Path.Combine(root, "post-snapshot-sentinel")).ShouldBeFalse();
        IReadOnlyList<string> verified = await fixture.VerifyRestoredConfigurationAsync(ct).ConfigureAwait(true);
        verified.ShouldContain("synthetic-provider.json");
        verified.ShouldContain(Path.GetFileName(fixture.ComponentPath));
        // The independent re-observation of the restored stopped fleet reproduces every capture entry except the
        // capture-only DUMP inventory, which Redis may re-encode after RESTORE.
        IReadOnlyDictionary<string, string> observed = await fixture.ObserveFleetAsync(ct).ConfigureAwait(true);
        observed.ContainsKey(WorksSdkProofRedis.DumpInventoryKey).ShouldBeFalse();
        observed.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ShouldBe(captured.Where(pair => pair.Key != WorksSdkProofRedis.DumpInventoryKey).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        await database.StringSetAsync("integer-string", "00124").ConfigureAwait(true);
        (await fixture.ObserveFleetAsync(ct).ConfigureAwait(true))["redis-logical-value-inventory-sha256"]
            .ShouldNotBe(captured["redis-logical-value-inventory-sha256"], "A changed restored value must change the re-observation.");
    }

    /// <summary>The Scheduler inventory detects changed modes, owners, entry types and link targets; restore reproduces them.</summary>
    [Fact]
    public async Task SchedulerInventoryBindsEntryMetadataAndRestoreReproducesIt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorksSdkProofRedis fixture = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var ownedFixture = fixture.ConfigureAwait(true);
        _ = await fixture.ExecuteInSchedulerVolumeAsync(
            "mkdir -p /fleet/member/snap && printf wal > /fleet/member/wal && chmod 600 /fleet/member/wal"
            + " && printf db > /fleet/member/snap/db && chmod 640 /fleet/member/snap/db && chown 1000:1000 /fleet/member/snap/db"
            + " && chmod 700 /fleet/member && ln -s snap/db /fleet/member/current", ct).ConfigureAwait(true);
        _ = await fixture.ExecuteInSchedulerVolumeAsync("chmod 751 /fleet && chown 1000:1000 /fleet", ct).ConfigureAwait(true);
        string captured = await fixture.SchedulerInventoryAsync(ct).ConfigureAwait(true);
        // The volume root itself is an inventory entry, so its mode and owner are bound too.
        captured.ShouldContain("directory|751|1000:1000|.\n");
        captured.ShouldContain("regular file|600|0:0|./member/wal");
        captured.ShouldContain("regular file|640|1000:1000|./member/snap/db");
        captured.ShouldContain("symbolic link|777|0:0|'./member/current' -> 'snap/db'");
        _ = await fixture.CaptureFleetAsync(ct).ConfigureAwait(true);
        foreach (string mutation in new[]
        {
            "chmod 700 /fleet",
            "chown 0:0 /fleet",
            "chmod 644 /fleet/member/wal",
            "chown 0:0 /fleet/member/snap/db",
            "ln -sfn wal /fleet/member/current",
            "rm /fleet/member/current && printf db > /fleet/member/current",
            "printf changed > /fleet/member/wal && chmod 600 /fleet/member/wal",
        })
        {
            _ = await fixture.ExecuteInSchedulerVolumeAsync(mutation, ct).ConfigureAwait(true);
            (await fixture.SchedulerInventoryAsync(ct).ConfigureAwait(true)).ShouldNotBe(captured, mutation);
            await fixture.RestoreFleetAsync(ct).ConfigureAwait(true);
            (await fixture.SchedulerInventoryAsync(ct).ConfigureAwait(true)).ShouldBe(captured, mutation);
        }
    }

    /// <summary>Full restored-state validation refuses changed bytes, collections, types and expiry.</summary>
    [Theory]
    [InlineData("string")]
    [InlineData("hash-value")]
    [InlineData("hash-field")]
    [InlineData("set-member")]
    [InlineData("type")]
    [InlineData("ttl-removed")]
    [InlineData("ttl-extended")]
    [InlineData("ttl-added")]
    [InlineData("extra-key")]
    [InlineData("missing-key")]
    [InlineData("list-order")]
    [InlineData("stream-pending")]
    [InlineData("field-ttl-removed")]
    [InlineData("field-ttl-extended")]
    public async Task RestoredInventoryRejectsEveryChangedValueTypeAndExpiry(string mutation)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorksSdkProofRedis fixture = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var ownedFixture = fixture.ConfigureAwait(true);
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(fixture.RedisEndpoint).ConfigureAwait(true);
        IDatabase database = connection.GetDatabase();
        await database.StringSetAsync("bytes", new byte[] { 0, 128, 255 }).ConfigureAwait(true);
        await database.HashSetAsync("hash", [new("field", "value"), new("other", "kept")]).ConfigureAwait(true);
        await database.ExecuteAsync("HPEXPIRE", "hash", 1_800_000, "FIELDS", 1, "field").ConfigureAwait(true);
        await database.SetAddAsync("set", ["a", "b"]).ConfigureAwait(true);
        await database.StringSetAsync("expiring", "value", TimeSpan.FromMinutes(30)).ConfigureAwait(true);
        await database.ListRightPushAsync("list", ["a", "b"]).ConfigureAwait(true);
        RedisValue streamId = await database.StreamAddAsync("stream", "field", "value").ConfigureAwait(true);
        await database.StreamCreateConsumerGroupAsync("stream", "group", "0-0").ConfigureAwait(true);
        await database.StreamReadGroupAsync("stream", "group", "consumer", ">").ConfigureAwait(true);
        await fixture.CaptureFleetAsync(ct).ConfigureAwait(true);
        await fixture.RestoreFleetAsync(ct).ConfigureAwait(true);

        switch (mutation)
        {
            case "string": await database.StringSetAsync("bytes", new byte[] { 0, 129, 255 }).ConfigureAwait(true); break;
            case "hash-value": await database.HashSetAsync("hash", "field", "changed").ConfigureAwait(true); break;
            case "hash-field": await database.HashDeleteAsync("hash", "field").ConfigureAwait(true); break;
            case "set-member": await database.SetRemoveAsync("set", "a").ConfigureAwait(true); break;
            case "type":
                await database.KeyDeleteAsync("bytes").ConfigureAwait(true);
                await database.HashSetAsync("bytes", "field", "value").ConfigureAwait(true);
                break;
            case "ttl-removed": await database.KeyPersistAsync("expiring").ConfigureAwait(true); break;
            case "ttl-extended": await database.KeyExpireAsync("expiring", TimeSpan.FromHours(1)).ConfigureAwait(true); break;
            case "ttl-added": await database.KeyExpireAsync("bytes", TimeSpan.FromMinutes(1)).ConfigureAwait(true); break;
            case "extra-key": await database.StringSetAsync("extra", "unexpected").ConfigureAwait(true); break;
            case "missing-key": await database.KeyDeleteAsync("bytes").ConfigureAwait(true); break;
            case "list-order": await database.ListSetByIndexAsync("list", 0, "b").ConfigureAwait(true); break;
            case "stream-pending": await database.StreamAcknowledgeAsync("stream", "group", streamId).ConfigureAwait(true); break;
            case "field-ttl-removed": await database.ExecuteAsync("HPERSIST", "hash", "FIELDS", 1, "field").ConfigureAwait(true); break;
            case "field-ttl-extended": await database.ExecuteAsync("HPEXPIRE", "hash", 3_600_000, "FIELDS", 1, "field").ConfigureAwait(true); break;
        }

        await Should.ThrowAsync<InvalidOperationException>(() => fixture.VerifyRestoredRedisAsync(ct)).ConfigureAwait(true);
    }
}
