using NSubstitute;

using Shouldly;

using StackExchange.Redis;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Real Redis expiry at deterministic command boundaries cannot produce inconsistent capture evidence.</summary>
public sealed class WorksRedisCaptureAcquisitionTests
{
    /// <summary>Expiry during acquisition either refuses capture or produces only proven empty logical hash evidence.</summary>
    /// <param name="boundary">The command immediately before which the last hash field expires.</param>
    [Theory]
    [InlineData("dump")]
    [InlineData("type")]
    [InlineData("hash")]
    public async Task LastFieldExpiryAtCaptureBoundaryRemainsFailClosed(string boundary)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorksSdkProofRedis fixture = await WorksSdkProofRedis.StartAsync(ct).ConfigureAwait(true);
        await using var cleanup = fixture.ConfigureAwait(true);
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(fixture.RedisEndpoint).ConfigureAwait(true);
        IDatabase real = connection.GetDatabase();
        const string key = "capture-expiry";
        await real.HashSetAsync(key, "last", "expires").ConfigureAwait(true);
        connection.GetServer(fixture.RedisEndpoint).Keys().ShouldContain((RedisKey)key);
        bool expired = false;
        async Task ExpireAsync(string command)
        {
            if (command != boundary || expired) return;
            expired.ShouldBeFalse();
            (await real.HashGetAsync(key, "last").ConfigureAwait(false)).ShouldBe((RedisValue)"expires");
            RedisResult[] clock = (RedisResult[])(await real.ExecuteAsync("TIME").ConfigureAwait(false))!;
            long now = (long)clock[0] * 1000 + (long)clock[1] / 1000;
            long deadline = now + 100;
            _ = await real.ExecuteAsync("HPEXPIREAT", key, deadline, "FIELDS", 1, "last").ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);
            clock = (RedisResult[])(await real.ExecuteAsync("TIME").ConfigureAwait(false))!;
            ((long)clock[0] * 1000 + (long)clock[1] / 1000).ShouldBeGreaterThan(deadline);
            // Do not read the hash here: the gated capture command must encounter natural expiry itself.
            expired = true;
        }

        // The proxy gates actual Redis commands; every result still comes from the owned real server.
        IDatabase gated = Substitute.For<IDatabase>();
        gated.KeyDumpAsync(key).Returns(async _ =>
        {
            await ExpireAsync("dump").ConfigureAwait(false);
            return await real.KeyDumpAsync(key).ConfigureAwait(false);
        });
        gated.KeyTimeToLiveAsync(key).Returns(_ => real.KeyTimeToLiveAsync(key));
        gated.KeyTypeAsync(key).Returns(async _ =>
        {
            await ExpireAsync("type").ConfigureAwait(false);
            return await real.KeyTypeAsync(key).ConfigureAwait(false);
        });
        gated.ExecuteAsync(Arg.Any<string>(), Arg.Any<object[]>()).Returns(async call =>
        {
            await ExpireAsync("hash").ConfigureAwait(false);
            return await real.ExecuteAsync(call.ArgAt<string>(0), call.ArgAt<object[]>(1)).ConfigureAwait(false);
        });

        try
        {
            var captured = await WorksSdkProofRedis.CaptureKeyAsync(gated, key).ConfigureAwait(true);
            if (captured is not null)
            {
                // Active expiry can delete the physical key; lazy expiry can leave an empty logical hash.
                // Neither Redis representation may qualify any expired field as live capture evidence.
                WorksSdkProofRedisHashSnapshot hash = captured.Value.Hash.ShouldNotBeNull();
                hash.Type.ShouldBe("hash");
                hash.Fields.ShouldBeEmpty();
            }
        }
        catch (InvalidOperationException exception)
        {
            exception.Message.ShouldBeOneOf("The stopped synthetic fleet changed during capture.",
                "A restored or captured synthetic key is missing.");
        }
        expired.ShouldBeTrue("The selected acquisition command must actually cross the expiry boundary.");
        (await real.HashGetAllAsync(key).ConfigureAwait(true)).ShouldBeEmpty();
    }
}
