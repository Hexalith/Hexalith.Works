using System.Text;

using Shouldly;

using StackExchange.Redis;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Actual isolated Redis restore preserves complete values, expiry and captured authority.</summary>
public sealed class WorksPlatformFleetRestoreTests
{
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
        await fixture.VerifyRestoredConfigurationAsync(ct).ConfigureAwait(true);
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
