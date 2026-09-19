using System.Text.Json;
using System.Text.RegularExpressions;

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
        workflow.ShouldContain("actionlint/cmd/actionlint@v1.7.12", Case.Sensitive);
        workflow.ShouldContain("pipx install ruff==0.13.2", Case.Sensitive);
        workflow.ShouldContain("python3 -m unittest discover -s scripts/tests", Case.Sensitive);

        // Count under the blocking input keys specifically. A bare repository-wide occurrence count cannot
        // tell a blocking tier from a comment, a non-blocking input, or an unrelated mention.
        Dictionary<string, string[]> blockingTiers = new(StringComparer.Ordinal)
        {
            ["unit-test-projects"] =
            [
                "tests/Hexalith.Works.UnitTests",
                "tests/Hexalith.Works.ArchitectureTests",
                "tests/Hexalith.Works.PropertyTests",
            ],
            ["integration-test-projects"] = ["tests/Hexalith.Works.IntegrationTests"],
        };

        var declaredProjects = new List<string>();
        foreach ((string inputKey, string[] expectedProjects) in blockingTiers)
        {
            string[] tierProjects = [.. BlockMultilineInputValues(workflow, inputKey)];
            tierProjects.ShouldBe(
                expectedProjects,
                ignoreOrder: true,
                customMessage: $"CI must declare exactly {string.Join(", ", expectedProjects)} under '{inputKey}'.");
            declaredProjects.AddRange(tierProjects);
        }

        declaredProjects.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            declaredProjects.Count,
            "No test project may appear in more than one blocking tier.");
    }

    /// <summary>
    /// Reads the non-empty entries of a YAML block-scalar workflow input (<c>key: |</c>), so an assertion
    /// binds to the declared input rather than to any occurrence of the text anywhere in the file.
    /// </summary>
    private static IEnumerable<string> BlockMultilineInputValues(string workflow, string inputKey)
    {
        string[] lines = workflow.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int start = Array.FindIndex(lines, line => line.TrimEnd().EndsWith($"{inputKey}: |", StringComparison.Ordinal));
        if (start < 0)
        {
            return [];
        }

        int indent = lines[start].Length - lines[start].TrimStart().Length;
        var values = new List<string>();
        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            int currentIndent = line.Length - line.TrimStart().Length;
            if (currentIndent <= indent)
            {
                break;
            }

            values.Add(line.Trim());
        }

        return values;
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
        workflow.ShouldContain("governed-release: false", Case.Sensitive);
        workflow.ShouldNotContain("attestations: write", Case.Sensitive);
        workflow.ShouldNotContain("id-token: write", Case.Sensitive);
    }

    [Fact]
    public void P0_PublicationPreflightsAllCollisionsBeforeANuGetWriteAndNeverSkipsDuplicates()
    {
        string root = RepositoryRoot.Locate();
        string releaseConfig = Read(root, ".releaserc.json");
        string preflight = Read(root, "scripts/validate-publication-preflight.sh");

        releaseConfig.ShouldContain("validate-publication-preflight.sh ${nextRelease.version} verify", Case.Sensitive);
        releaseConfig.ShouldContain("validate-publication-preflight.sh ${nextRelease.version} publish", Case.Sensitive);
        releaseConfig.ShouldContain("bash scripts/push-release-packages.sh", Case.Sensitive);
        releaseConfig.ShouldNotContain("dotnet nuget push", Case.Sensitive);
        releaseConfig.ShouldNotContain("--skip-duplicate", Case.Insensitive);

        // The publisher must resolve each file from the manifest rather than globbing the output directory,
        // so an unvalidated archive left there can never be published. Compare executable lines only: the
        // script documents in comments exactly why a glob and --skip-duplicate are absent.
        string publisher = Read(root, "scripts/push-release-packages.sh");
        string publisherCode = ExecutableShellLines(publisher);
        publisherCode.ShouldNotContain("--skip-duplicate", Case.Insensitive);
        publisherCode.ShouldNotContain("*.nupkg", Case.Sensitive);
        publisherCode.ShouldContain("${package_id}.${version}.nupkg", Case.Sensitive);
        publisherCode.ShouldContain("${package_id}.${version}.snupkg", Case.Sensitive);
        publisherCode.ShouldContain("release-artifacts.sha256", Case.Sensitive);
        publisherCode.ShouldContain("sha256sum --check --strict", Case.Sensitive);
        publisherCode.ShouldContain("--no-symbols", Case.Sensitive);
        publisherCode.ShouldContain("*\"409\"*", Case.Sensitive);
        publisherCode.ShouldContain("*\"Conflict\"*", Case.Sensitive);
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
        workflow.ShouldContain("if: ${{ always() }}", Case.Sensitive);
        workflow.ShouldContain("git/matching-refs/tags/v", Case.Sensitive);
        workflow.ShouldNotContain("/releases?per_page=100", Case.Sensitive);
        workflow.ShouldContain("length == 5", Case.Sensitive);
        workflow.ShouldContain("missing+=(\"${package_id} ${VERSION} (HTTP ${status})\")", Case.Sensitive);
        workflow.ShouldContain("missing+=(\"${package_id} ${VERSION} (transport error)\")", Case.Sensitive);
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
    public void P0_CodeQlCoversCSharpAndPythonReleaseTooling()
    {
        string root = RepositoryRoot.Locate();
        string workflow = Read(root, ".github/workflows/codeql.yml");

        workflow.ShouldContain(
            $"Hexalith/Hexalith.Builds/.github/workflows/codeql.yml@{ApprovedBuildsSha}",
            Case.Sensitive);
        workflow.ShouldContain("languages: csharp,python", Case.Sensitive);
    }

    [Fact]
    public void P0_PackageValidationCompilesAndInspectsTheResolvedVersionAndDependencyRanges()
    {
        string root = RepositoryRoot.Locate();
        string packer = Read(root, "scripts/pack-release-packages.py");
        string validator = Read(root, "scripts/validate-nuget-packages.py");

        packer.ShouldNotContain("\"--no-build\"", Case.Sensitive);
        packer.ShouldContain("f\"-p:Version={args.version}\"", Case.Sensitive);
        packer.ShouldContain("release-artifacts.sha256", Case.Sensitive);
        validator.ShouldContain("dependency id/version mismatch", Case.Sensitive);
        validator.ShouldContain("does not carry package version", Case.Sensitive);
    }

    [Fact]
    public void P0_DocumentedRegistryStatusNamesEveryManifestPackageWithItsObservedStatus()
    {
        string root = RepositoryRoot.Locate();
        string readme = Read(root, "README.md");

        // Assert the honesty property, not one frozen status. Pinning "(HTTP 404)" would force the README to
        // keep claiming the packages are unpublished after the first real release just to keep this gate green.
        foreach (string packageId in ManifestPackageIds(root))
        {
            readme.ShouldMatch(
                $@"`{Regex.Escape(packageId)}` \(HTTP \d{{3}}\)",
                $"README must name {packageId} with its observed registry status code.");
        }

        Occurrences(readme, "(HTTP ").ShouldBe(
            ManifestPackageIds(root).Length,
            "Every documented registry status must correspond to exactly one manifest package.");
        readme.ShouldContain("Registry absence is status only; it is not publication authority.", Case.Sensitive);
    }

    [Fact]
    public void P0_PackableProjectsAreExactlyTheManifestPackages()
    {
        string root = RepositoryRoot.Locate();
        string[] manifestProjects = [.. ManifestEntries(root)
            .Select(entry => Path.GetFullPath(Path.Combine(root, entry.Project)))];

        string[] projectFiles = [.. Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}references{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

        projectFiles.ShouldNotBeEmpty("Expected to discover Works project files to classify.");

        string[] packableProjects = [.. projectFiles
            .Where(IsPackableProject)
            .Select(Path.GetFullPath)];

        packableProjects.ShouldBe(
            manifestProjects,
            ignoreOrder: true,
            customMessage: "Every packable Works project must appear in tools/release-packages.json, and nothing else may be packable. "
                + "A new packable library would otherwise be publishable without ever entering the release manifest.");
    }

    [Fact]
    public void P0_ApprovedBuildsShaAndPackageCountAgreeEverywhereTheyAreDeclared()
    {
        string root = RepositoryRoot.Locate();
        int manifestCount = ManifestPackageIds(root).Length;

        // The approved Builds commit and the package count are each repeated across workflows, scripts and
        // this test. Nothing else proves the copies agree, so one drifting copy would silently weaken a gate.
        string[] shaBearingFiles =
        [
            ".github/workflows/ci.yml",
            ".github/workflows/codeql.yml",
            ".github/workflows/commitlint.yml",
            ".github/workflows/dependency-review.yml",
            ".github/workflows/release.yml",
            "scripts/validate-publication-preflight.sh",
        ];

        foreach (string file in shaBearingFiles)
        {
            string content = Read(root, file);
            content.ShouldContain(ApprovedBuildsSha, Case.Sensitive, $"{file} must pin the approved Builds commit.");

            // Only Hexalith.Builds references must agree. Third-party actions are pinned to their own
            // commits, so sweeping every 40-hex string here would be wrong.
            string[] buildsRefs = [.. Regex.Matches(content, @"Hexalith/Hexalith\.Builds/[^@\s]+@([0-9a-f]{40}|main)")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)];
            if (buildsRefs.Length > 0)
            {
                buildsRefs.ShouldBe(
                    [ApprovedBuildsSha],
                    $"{file} must reference Hexalith.Builds only at the approved commit, never a mutable ref.");
            }
        }

        foreach (string file in new[] { ".github/workflows/release.yml", "scripts/validate-publication-preflight.sh" })
        {
            Read(root, file).ShouldContain(
                manifestCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Case.Sensitive,
                $"{file} must declare the manifest package count {manifestCount}.");
        }

        Read(root, ".github/workflows/release.yml").ShouldContain(
            $"expected-package-count: {manifestCount}",
            Case.Sensitive);
        Read(root, "scripts/validate-publication-preflight.sh").ShouldContain(
            $"expected_package_count={manifestCount}",
            Case.Sensitive);
        Read(root, "scripts/pack-release-packages.py").ShouldContain(
            $"EXPECTED_PACKAGE_COUNT = {manifestCount}",
            Case.Sensitive);
        Read(root, "scripts/validate-nuget-packages.py").ShouldContain(
            $"EXPECTED_PACKAGE_COUNT = {manifestCount}",
            Case.Sensitive);
        _packageIds.Length.ShouldBe(manifestCount, "This test's own expectation must track the manifest.");
    }

    private static bool IsPackableProject(string projectPath)
    {
        bool evaluated = MsBuildProjectEvaluation.TryEvaluate(
            projectPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BuildingInsideVisualStudio"] = "false",
                ["Configuration"] = "Release",
                ["DesignTimeBuild"] = "false",
                ["Platform"] = "AnyCPU",
            },
            out MsBuildProjectSnapshot? snapshot,
            out string diagnostic);

        evaluated.ShouldBeTrue(diagnostic);
        return string.Equals(snapshot!.PropertyValue("IsPackable"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Id, string Project)[] ManifestEntries(string root)
    {
        using JsonDocument manifest = JsonDocument.Parse(Read(root, "tools/release-packages.json"));
        return [.. manifest.RootElement.GetProperty("packages").EnumerateArray()
            .Select(package => (
                package.GetProperty("id").GetString()!,
                package.GetProperty("project").GetString()!))];
    }

    private static string[] ManifestPackageIds(string root)
        => [.. ManifestEntries(root).Select(entry => entry.Id)];

    /// <summary>Strips shebang and whole-line shell comments so assertions bind to executable code only.</summary>
    private static string ExecutableShellLines(string script)
        => string.Join(
            '\n',
            script.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith('#')));

    private static int Occurrences(string value, string fragment)
        => value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string Read(string root, string relativePath)
        => File.ReadAllText(Path.Combine(root, relativePath));
}
