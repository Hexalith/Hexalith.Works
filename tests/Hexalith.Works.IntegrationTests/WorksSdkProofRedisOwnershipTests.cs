using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Partial Docker creation cleans only this fixture's independently verified resources.</summary>
public sealed class WorksSdkProofRedisOwnershipTests
{
    /// <summary>A lost creation acknowledgement still removes owned resources, including a partially created volume.</summary>
    [Theory]
    [InlineData("created-container")]
    [InlineData("missing-container")]
    [InlineData("created-volume")]
    [InlineData("complete")]
    public async Task CreationAndCleanupAreBoundToTheExactFixtureLabel(string scenario)
    {
        List<string[]> commands = [];
        string name = string.Empty;
        string volume = string.Empty;
        bool containerExists = false;
        bool volumeExists = false;
        Task<string> DockerAsync(string[] arguments, CancellationToken token)
        {
            commands.Add(arguments);
            if (arguments[0] == "run")
            {
                name = arguments[Array.IndexOf(arguments, "--name") + 1];
                arguments[Array.IndexOf(arguments, "--label") + 1].ShouldBe("hexalith.works.sdk-fixture=" + name);
                containerExists = scenario != "missing-container";
                if (scenario is "created-container" or "missing-container")
                    throw new OperationCanceledException("Synthetic lost container creation acknowledgement", token);
                return Task.FromResult("owned-id");
            }
            if (arguments[0] == "port") return Task.FromResult("127.0.0.1:47321");
            if (arguments[0] == "exec") return Task.FromResult("PONG");
            if (arguments[0] == "ps")
            {
                arguments.ShouldBe(["ps", "--all", "--filter", "label=hexalith.works.sdk-fixture=" + name, "--format", "{{.Names}}"]);
                token.CanBeCanceled.ShouldBeFalse();
                return Task.FromResult(containerExists ? name : string.Empty);
            }
            if (arguments[0] == "rm")
            {
                arguments.ShouldBe(["rm", "-f", name]);
                containerExists.ShouldBeTrue();
                containerExists = false;
                return Task.FromResult(name);
            }
            if (arguments[0] == "volume" && arguments[1] == "create")
            {
                volume = arguments[^1];
                arguments.ShouldBe(["volume", "create", "--label", "hexalith.works.sdk-fixture=" + name, volume]);
                volumeExists = true;
                if (scenario == "created-volume")
                    throw new OperationCanceledException("Synthetic lost volume creation acknowledgement", token);
                return Task.FromResult(volume);
            }
            if (arguments[0] == "volume" && arguments[1] == "ls")
            {
                arguments.ShouldBe(["volume", "ls", "--filter", "label=hexalith.works.sdk-fixture=" + name, "--format", "{{.Name}}"]);
                token.CanBeCanceled.ShouldBeFalse();
                return Task.FromResult(volumeExists ? volume : string.Empty);
            }
            arguments.ShouldBe(["volume", "rm", volume]);
            volumeExists.ShouldBeTrue();
            volumeExists = false;
            return Task.FromResult(volume);
        }

        try
        {
            if (scenario == "complete")
            {
                WorksSdkProofRedis fixture = await WorksSdkProofRedis.StartAsync(TestContext.Current.CancellationToken,
                    activateSdkWriter: true, dockerCommand: DockerAsync).ConfigureAwait(true);
                fixture.RedisEndpoint.ShouldBe("127.0.0.1:47321");
                await fixture.DisposeAsync().ConfigureAwait(true);
            }
            else
            {
                await Should.ThrowAsync<OperationCanceledException>(() => WorksSdkProofRedis.StartAsync(
                    TestContext.Current.CancellationToken, activateSdkWriter: true, dockerCommand: DockerAsync)).ConfigureAwait(true);
            }

            containerExists.ShouldBeFalse();
            volumeExists.ShouldBeFalse();
            commands.Count(command => command[0] == "rm").ShouldBe(scenario == "missing-container" ? 0 : 1);
            commands.Count(command => command[0] == "volume" && command[1] == "rm")
                .ShouldBe(scenario is "complete" or "created-volume" ? 1 : 0);
            Directory.Exists(Path.Combine(Path.GetTempPath(), name)).ShouldBeFalse();
        }
        finally
        {
            string directory = Path.Combine(Path.GetTempPath(), name);
            if (name.Length > 0 && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A substituted owner observation fails closed before any foreign removal.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousResourceOwnerIsRefused(bool volumeIsForeign)
    {
        List<string[]> commands = [];
        string name = string.Empty;
        string volume = string.Empty;
        Task<string> DockerAsync(string[] arguments, CancellationToken token)
        {
            commands.Add(arguments);
            if (arguments[0] == "run")
            {
                name = arguments[Array.IndexOf(arguments, "--name") + 1];
                return Task.FromResult("owned-id");
            }
            if (arguments[0] == "port") return Task.FromResult("127.0.0.1:47321");
            if (arguments[0] == "exec") return Task.FromResult("PONG");
            if (arguments[0] == "volume" && arguments[1] == "create")
            {
                volume = arguments[^1];
                return Task.FromResult(volume);
            }
            if (arguments[0] == "ps") return Task.FromResult(volumeIsForeign ? name : "foreign-container");
            if (arguments[0] == "volume" && arguments[1] == "ls") return Task.FromResult(volumeIsForeign ? "foreign-volume" : volume);
            return Task.FromResult("removed-owned-resource");
        }

        WorksSdkProofRedis fixture = await WorksSdkProofRedis.StartAsync(TestContext.Current.CancellationToken,
            activateSdkWriter: true, dockerCommand: DockerAsync).ConfigureAwait(true);
        try
        {
            InvalidOperationException failure = await Should.ThrowAsync<InvalidOperationException>(
                () => fixture.DisposeAsync().AsTask()).ConfigureAwait(true);
            failure.Message.ShouldContain("ownership is ambiguous");
            commands.Where(command => command[0] == "rm" || command[0] == "volume" && command[1] == "rm")
                .SelectMany(command => command).ShouldNotContain(value => value.StartsWith("foreign-", StringComparison.Ordinal));
            commands.Any(command => command[0] == "rm").ShouldBe(volumeIsForeign);
            commands.Any(command => command[0] == "volume" && command[1] == "rm").ShouldBe(!volumeIsForeign);
        }
        finally
        {
            string directory = Path.GetDirectoryName(fixture.ComponentPath)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
