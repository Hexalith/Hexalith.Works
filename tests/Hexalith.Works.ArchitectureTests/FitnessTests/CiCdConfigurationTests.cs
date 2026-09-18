using System.Text.Json;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class CiCdConfigurationTests
{
    private const string ApprovedBuildsSha = "04d961759994396132bb2b113ee465b64740a543";

    private static readonly string[] _packageIds =
    [
        "Hexalith.Works.Contracts",
        "Hexalith.Works.Server",
        "Hexalith.Works.Projections",
        "Hexalith.Works.Reactor",
        "Hexalith.Works.Testing",
    ];

    [Fact]
    public void P0_CiPinsTheApprovedContractAndRunsPackageValidationAndFourBlockingProjects()
    {
        string root = RepositoryRoot.Locate();
        string workflow = Read(root, ".github/workflows/ci.yml");

        workflow.ShouldContain(
            $"Hexalith/Hexalith.Builds/.github/workflows/domain-ci.yml@{ApprovedBuildsSha}",
            Case.Sensitive);
        workflow.ShouldContain("run-consumer-validation: true", Case.Sensitive);
        workflow.ShouldContain("test-platform: microsoft-testing-platform", Case.Sensitive);
        foreach (string project in new[]
        {
            "tests/Hexalith.Works.UnitTests",
            "tests/Hexalith.Works.ArchitectureTests",
            "tests/Hexalith.Works.PropertyTests",
            "tests/Hexalith.Works.IntegrationTests",
        })
        {
            Occurrences(workflow, project).ShouldBe(1, $"CI must invoke {project} in exactly one blocking tier.");
        }
    }

    [Fact]
    public void P0_InvalidReleaseIsRejectedBeforeTheProtectedPublicationJob()
    {
        string root = RepositoryRoot.Locate();
        string workflow = Read(root, ".github/workflows/release.yml");
        int verificationJob = workflow.IndexOf("  verify-source:", StringComparison.Ordinal);
        int protectedJob = workflow.IndexOf("  release:", StringComparison.Ordinal);

        verificationJob.ShouldBeGreaterThan(0);
        protectedJob.ShouldBeGreaterThan(verificationJob);
        workflow.ShouldContain("needs: verify-source", Case.Sensitive);
        workflow.ShouldContain("refs/heads/main", Case.Sensitive);
        workflow.ShouldContain("git/ref/heads/main", Case.Sensitive);
        workflow.ShouldContain("event=push", Case.Sensitive);
        workflow.ShouldContain("head_sha=\"$DISPATCH_SHA\"", Case.Sensitive);
        workflow.ShouldContain("environment-name: production", Case.Sensitive);
        workflow.ShouldContain("cancel-in-progress: false", Case.Sensitive);
        workflow.ShouldContain(
            $"Hexalith/Hexalith.Builds/.github/workflows/domain-release.yml@{ApprovedBuildsSha}",
            Case.Sensitive);
        workflow.ShouldContain($"builds-execution-sha: {ApprovedBuildsSha}", Case.Sensitive);
        workflow.ShouldContain("NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}", Case.Sensitive);
    }

    [Fact]
    public void P0_PublicationPreflightsAllCollisionsBeforeANuGetWriteAndNeverSkipsDuplicates()
    {
        string root = RepositoryRoot.Locate();
        string releaseConfig = Read(root, ".releaserc.json");
        string preflight = Read(root, "scripts/validate-publication-preflight.sh");

        releaseConfig.ShouldContain("validate-publication-preflight.sh ${nextRelease.version} verify", Case.Sensitive);
        releaseConfig.ShouldContain("validate-publication-preflight.sh ${nextRelease.version} publish", Case.Sensitive);
        releaseConfig.ShouldContain("dotnet nuget push ./nupkgs/*.nupkg", Case.Sensitive);
        releaseConfig.ShouldNotContain("--skip-duplicate", Case.Insensitive);
        preflight.ShouldContain("collisions=()", Case.Sensitive);
        preflight.ShouldContain("for package_id in \"${package_ids[@]}\"", Case.Sensitive);
        preflight.ShouldContain("200) collisions+=", Case.Sensitive);
        preflight.ShouldContain("404) ;;", Case.Sensitive);
        preflight.ShouldContain("No successful push CI run exists", Case.Sensitive);
    }

    [Fact]
    public void P0_PostPublicationVerificationNamesEveryMissingExactPackage()
    {
        string root = RepositoryRoot.Locate();
        string workflow = Read(root, ".github/workflows/release.yml");

        workflow.ShouldContain("needs: release", Case.Sensitive);
        workflow.ShouldContain("length == 5", Case.Sensitive);
        workflow.ShouldContain("missing+=(\"${package_id} ${VERSION} (HTTP ${status})\")", Case.Sensitive);
        workflow.ShouldContain("Release publication is incomplete; missing exact NuGet packages", Case.Sensitive);
        workflow.ShouldContain("if [ \"${#missing[@]}\" -ne 0 ]", Case.Sensitive);
    }

    [Fact]
    public void P0_ManifestOwnsExactlyFivePackableLibrariesAndNoHostOrRunner()
    {
        string root = RepositoryRoot.Locate();
        using JsonDocument manifest = JsonDocument.Parse(Read(root, "tools/release-packages.json"));
        JsonElement[] packages = [.. manifest.RootElement.GetProperty("packages").EnumerateArray()];

        packages.Length.ShouldBe(5);
        packages.Select(package => package.GetProperty("id").GetString()).ShouldBe(_packageIds, ignoreOrder: true);
        packages.Select(package => package.GetProperty("project").GetString()).Distinct().Count().ShouldBe(5);
        foreach (JsonElement package in packages)
        {
            string id = package.GetProperty("id").GetString().ShouldNotBeNull();
            string project = package.GetProperty("project").GetString().ShouldNotBeNull();
            File.Exists(Path.Combine(root, project)).ShouldBeTrue($"Manifest project is missing for {id}.");
            id.ShouldNotContain("AppHost", Case.Insensitive);
            id.ShouldNotContain("Sample", Case.Insensitive);
            id.ShouldNotContain("Tests", Case.Insensitive);
        }
    }

    [Fact]
    public void P0_SemanticReleaseAndCommitlintUseLockedAllowedTooling()
    {
        string root = RepositoryRoot.Locate();
        using JsonDocument releaseConfig = JsonDocument.Parse(Read(root, ".releaserc.json"));
        using JsonDocument package = JsonDocument.Parse(Read(root, "package.json"));
        string commitlint = Read(root, "commitlint.config.mjs");

        string[] releasePlugins = [.. releaseConfig.RootElement.GetProperty("plugins").EnumerateArray()
            .Select(plugin => plugin.ValueKind == JsonValueKind.String
                ? plugin.GetString()
                : plugin.EnumerateArray().First().GetString())
            .Where(plugin => plugin is not null)
            .Select(plugin => plugin!)];
        releasePlugins.ShouldNotContain("@semantic-release/changelog");
        releasePlugins.ShouldNotContain("@semantic-release/git");
        package.RootElement.GetProperty("devDependencies").TryGetProperty("@semantic-release/changelog", out _).ShouldBeFalse();
        package.RootElement.GetProperty("devDependencies").TryGetProperty("@semantic-release/git", out _).ShouldBeFalse();
        File.Exists(Path.Combine(root, "package-lock.json")).ShouldBeTrue("The Node release toolchain must be lockfile-pinned.");
        commitlint.ShouldNotContain("'chore'", Case.Sensitive);
    }

    [Fact]
    public void P0_DocumentedRegistryStatusNamesFiveHonest404Results()
    {
        string root = RepositoryRoot.Locate();
        string readme = Read(root, "README.md");

        foreach (string packageId in _packageIds)
        {
            readme.ShouldContain($"`{packageId}` (HTTP 404)", Case.Sensitive);
        }
        Occurrences(readme, "(HTTP 404)").ShouldBe(5);
        readme.ShouldContain("Registry absence is status only; it is not publication authority.", Case.Sensitive);
    }

    private static int Occurrences(string value, string fragment)
        => value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string Read(string root, string relativePath)
        => File.ReadAllText(Path.Combine(root, relativePath));
}
