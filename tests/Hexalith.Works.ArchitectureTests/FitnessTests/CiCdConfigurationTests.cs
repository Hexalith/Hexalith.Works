using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class CiCdConfigurationTests
{
    private const string ApprovedBuildsSha = "04d961759994396132bb2b113ee465b64740a543";

    private const string ForceSkipProbeEnvironment = "HEXALITH_WORKS_FORCE_SKIP_PROBE";

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
        workflow.ShouldContain("build-timeout-minutes: 60", Case.Sensitive);
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

    [Fact]
    public void P0_EveryBlockingProjectCopiesTheFailOnSkipRunnerConfiguration()
    {
        string root = RepositoryRoot.Locate();
        using JsonDocument runnerConfiguration = JsonDocument.Parse(Read(root, "xunit.runner.json"));
        runnerConfiguration.RootElement.GetProperty("failSkips").GetBoolean().ShouldBeTrue();

        foreach (string project in new[]
        {
            "tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj",
            "tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj",
            "tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj",
            "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj",
        })
        {
            string projectFile = Read(root, project);
            projectFile.ShouldContain("xunit.runner.json", Case.Sensitive);
            projectFile.ShouldContain("CopyToOutputDirectory=\"PreserveNewest\"", Case.Sensitive);
        }
    }

    [Fact]
    public async Task P0_BuiltMicrosoftTestingPlatformHostFailsWhenAFactSkipsAsync()
    {
        string assemblyName = typeof(CiCdConfigurationTests).Assembly.GetName().Name
            ?? throw new InvalidOperationException("Could not resolve the Architecture test assembly name.");
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            assemblyName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        string configuration = Path.Combine(AppContext.BaseDirectory, "xunit.runner.json");
        File.Exists(executable).ShouldBeTrue($"The built Microsoft.Testing.Platform host is missing: {executable}");
        File.Exists(configuration).ShouldBeTrue($"The built fail-on-skip configuration is missing: {configuration}");

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.Environment[ForceSkipProbeEnvironment] = "1";
        start.ArgumentList.Add(configuration);
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add($"{typeof(CiCdConfigurationTests).FullName}.{nameof(P0_DeliberatelySkippedFactForRunnerProbe)}");
        start.ArgumentList.Add("-noColor");
        start.ArgumentList.Add("-noLogo");

        using Process process = Process.Start(start).ShouldNotBeNull();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitForExitOrKillAsync(process, cancellationToken).ConfigureAwait(true);
        string output = await outputTask.ConfigureAwait(true) + await errorTask.ConfigureAwait(true);

        process.ExitCode.ShouldNotBe(0, $"A skipped blocking fact must fail the built test host.{Environment.NewLine}{output}");
        output.ShouldContain("deliberate fail-on-skip probe", Case.Insensitive);
    }

    [Fact]
    public void P0_DeliberatelySkippedFactForRunnerProbe()
    {
        if (string.Equals(Environment.GetEnvironmentVariable(ForceSkipProbeEnvironment), "1", StringComparison.Ordinal))
        {
            Assert.Skip("Deliberate fail-on-skip probe.");
        }
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
        string sourceProof = Read(root, "scripts/validate-release-source.sh");
        string regressionTests = Read(root, "scripts/tests/test_release_tooling.py");
        int verificationJob = workflow.IndexOf("  verify-source:", StringComparison.Ordinal);
        int protectedJob = workflow.IndexOf("  release:", StringComparison.Ordinal);

        verificationJob.ShouldBeGreaterThan(0);
        protectedJob.ShouldBeGreaterThan(verificationJob);
        workflow.ShouldContain("needs: verify-source", Case.Sensitive);
        workflow.ShouldContain("bash scripts/validate-release-source.sh", Case.Sensitive);
        workflow[verificationJob..protectedJob].ShouldContain("timeout-minutes: 5", Case.Sensitive);
        sourceProof.ShouldContain("Hexalith/Hexalith.Works", Case.Sensitive);
        sourceProof.ShouldContain("unexpected repository", Case.Sensitive);
        sourceProof.ShouldContain("refs/heads/main", Case.Sensitive);
        sourceProof.ShouldContain("git/ref/heads/main", Case.Sensitive);
        sourceProof.ShouldContain("event=push", Case.Sensitive);
        sourceProof.ShouldContain("head_sha=\"$dispatch_sha\"", Case.Sensitive);
        workflow.ShouldContain("environment-name: production", Case.Sensitive);
        workflow.ShouldContain("cancel-in-progress: false", Case.Sensitive);
        workflow.ShouldContain(
            $"Hexalith/Hexalith.Builds/.github/workflows/domain-release.yml@{ApprovedBuildsSha}",
            Case.Sensitive);
        workflow.ShouldContain($"builds-execution-sha: {ApprovedBuildsSha}", Case.Sensitive);
        workflow.ShouldContain("NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}", Case.Sensitive);
        workflow.ShouldContain("governed-release: false", Case.Sensitive);
        workflow.ShouldContain("if: ${{ always() && needs.release.result != 'skipped' }}", Case.Sensitive);
        workflow.ShouldNotContain("attestations: write", Case.Sensitive);
        workflow.ShouldNotContain("id-token: write", Case.Sensitive);
        foreach (string executableBranch in new[]
        {
            "test_source_proof_rejects_non_main_before_external_queries",
            "test_source_proof_rejects_unexpected_repository_before_external_queries",
            "test_source_proof_rejects_stale_main",
            "test_source_proof_rejects_main_without_exact_successful_push_ci",
            "test_source_proof_accepts_exact_current_main_with_successful_push_ci",
        })
        {
            regressionTests.ShouldContain(executableBranch, Case.Sensitive);
        }
    }

    [Fact]
    public void P0_PublicationPreflightsAllCollisionsBeforeANuGetWriteAndNeverSkipsDuplicates()
    {
        string root = RepositoryRoot.Locate();
        string releaseConfig = Read(root, ".releaserc.json");
        string preflight = Read(root, "scripts/validate-publication-preflight.sh");
        string regressionTests = Read(root, "scripts/tests/test_release_tooling.py");

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
        publisherCode.ShouldContain("max_attempts=3", Case.Sensitive);
        publisherCode.ShouldContain("is_retryable_failure", Case.Sensitive);
        publisherCode.ShouldContain("HTTP 409", Case.Sensitive);
        publisherCode.ShouldContain("remote bytes cannot be authenticated", Case.Sensitive);
        publisherCode.ShouldContain("actual_archive_names", Case.Sensitive);
        publisherCode.ShouldNotContain("ambiguous_attempt", Case.Sensitive);
        publisherCode.ShouldNotContain("HEXALITH_RELEASE_NUGET_SOURCE", Case.Sensitive);
        preflight.ShouldContain("collisions=()", Case.Sensitive);
        preflight.ShouldContain("for package_id in \"${package_ids[@]}\"", Case.Sensitive);
        preflight.ShouldContain("200) collisions+=", Case.Sensitive);
        preflight.ShouldContain("404) ;;", Case.Sensitive);
        preflight.ShouldContain("No successful push CI run exists", Case.Sensitive);
        foreach (string executableBranch in new[]
        {
            "test_preflight_rejects_unexpected_repository_before_any_external_query",
            "test_publisher_rejects_first_attempt_409_as_collision",
            "test_publisher_rejects_409_after_same_invocation_ambiguous_result",
            "test_publisher_retries_transient_failure_then_succeeds",
            "test_publisher_rejects_candidate_changed_between_retry_attempts",
            "test_publisher_reports_retry_exhaustion_and_requires_new_patch",
            "test_publisher_does_not_retry_an_unclassified_error_containing_500",
            "test_publisher_rejects_an_extra_release_archive_before_any_push",
            "test_packer_ledger_is_accepted_by_the_real_publisher_validation_path",
        })
        {
            regressionTests.ShouldContain(executableBranch, Case.Sensitive);
        }
    }

    [Fact]
    public void P0_PostPublicationVerificationNamesEveryMissingExactPackage()
    {
        string root = RepositoryRoot.Locate();
        string workflow = Read(root, ".github/workflows/release.yml");
        string verifier = Read(root, "scripts/verify-release-publication.sh");
        string regressionTests = Read(root, "scripts/tests/test_release_tooling.py");

        workflow.ShouldContain("needs: release", Case.Sensitive);
        workflow.ShouldContain("if: ${{ always() && needs.release.result != 'skipped' }}", Case.Sensitive);
        workflow.ShouldContain("bash scripts/verify-release-publication.sh", Case.Sensitive);
        workflow.ShouldNotContain("HEXALITH_RELEASE_PUBLISH_ENABLED", Case.Sensitive);
        verifier.ShouldContain("git/matching-refs/tags/v", Case.Sensitive);
        verifier.ShouldNotContain("/releases?per_page=100", Case.Sensitive);
        verifier.ShouldContain("length == $expected", Case.Sensitive);
        verifier.ShouldContain("missing+=(\"${package_id} ${version} (HTTP ${status})\")", Case.Sensitive);
        verifier.ShouldContain("missing+=(\"${package_id} ${version} (transport error)\")", Case.Sensitive);
        verifier.ShouldContain("Release publication is incomplete; missing exact NuGet packages", Case.Sensitive);
        verifier.ShouldContain("if [ \"${#missing[@]}\" -ne 0 ]", Case.Sensitive);
        verifier.ShouldContain("could not be resolved; exact publication verification cannot continue", Case.Sensitive);

        foreach (string executableBranch in new[]
        {
            "test_post_publication_matching_tag_verifies_all_packages",
            "test_post_publication_unrelated_tag_is_a_successful_no_op",
            "test_post_publication_zero_tags_is_a_successful_no_op",
            "test_post_publication_unresolvable_tag_fails_closed",
            "test_post_publication_multiple_matching_tags_fail",
            "test_post_publication_names_missing_package",
            "test_post_publication_reports_transport_error",
            "test_post_publication_retries_visibility_and_succeeds_on_second_pass",
            "test_post_publication_rejects_tag_that_moves_after_visibility",
            "test_post_publication_rejects_zero_delay_with_unlimited_attempts",
        })
        {
            regressionTests.ShouldContain(executableBranch, Case.Sensitive);
        }
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
        string releaseConfigText = Read(root, ".releaserc.json");
        releaseConfigText.ShouldContain("\"failTitle\": false", Case.Sensitive);
        releaseConfigText.ShouldContain("\"failComment\": false", Case.Sensitive);
    }

    [Fact]
    public void P0_DependencyAutomationCoversNuGetNpmActionsAndRootSubmodules()
    {
        string root = RepositoryRoot.Locate();
        string dependabot = Read(root, ".github/dependabot.yml");

        foreach (string ecosystem in new[] { "nuget", "npm", "github-actions", "gitsubmodule" })
        {
            Occurrences(dependabot, $"package-ecosystem: {ecosystem}").ShouldBe(
                1,
                $"Dependabot must configure exactly one {ecosystem} update lane.");
        }
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
        string consumerValidator = Read(root, "scripts/validate-consumer-package-references.py");

        packer.ShouldNotContain("\"--no-build\"", Case.Sensitive);
        packer.ShouldContain("f\"-p:Version={args.version}\"", Case.Sensitive);
        packer.ShouldContain("release-artifacts.sha256", Case.Sensitive);
        packer.ShouldContain("validate_output_inventory", Case.Sensitive);
        packer.ShouldContain(".symbols.nupkg", Case.Sensitive);
        validator.ShouldContain("dependency id/version mismatch", Case.Sensitive);
        validator.ShouldContain("non-empty id and version", Case.Sensitive);
        validator.ShouldContain("license_type", Case.Sensitive);
        validator.ShouldContain("license must be the exact expression MIT", Case.Sensitive);
        validator.ShouldNotContain("version_bytes", Case.Sensitive);
        consumerValidator.ShouldContain("AssemblyInformationalVersionAttribute", Case.Sensitive);
        consumerValidator.ShouldContain("dotnet", Case.Sensitive);
        consumerValidator.ShouldContain("run", Case.Sensitive);
        consumerValidator.ShouldContain("--no-build", Case.Sensitive);
        consumerValidator.ShouldContain("Captured consumer stdout", Case.Sensitive);
        consumerValidator.ShouldContain("Captured consumer stderr", Case.Sensitive);
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
            .Where(path => IsRepositoryOwnedProject(root, path))];

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

        foreach (string file in new[]
        {
            ".github/workflows/release.yml",
            "scripts/validate-publication-preflight.sh",
            "scripts/push-release-packages.sh",
            "scripts/verify-release-publication.sh",
        })
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
        Read(root, "scripts/push-release-packages.sh").ShouldContain(
            $"expected_package_count={manifestCount}",
            Case.Sensitive);
        Read(root, "scripts/verify-release-publication.sh").ShouldContain(
            $"expected_package_count={manifestCount}",
            Case.Sensitive);
        _packageIds.Length.ShouldBe(manifestCount, "This test's own expectation must track the manifest.");
    }

    [Theory]
    [InlineData(".github/workflows/ci.yml", "domain-ci.yml")]
    [InlineData(".github/workflows/release.yml", "domain-release.yml")]
    public async Task P0_ReusableWorkflowInputsExistAtTheExactPinnedBuildsObjectAsync(
        string callerPath,
        string sharedWorkflowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedWorkflowName);

        string root = RepositoryRoot.Locate();
        string caller = Read(root, callerPath);
        string reference = $"Hexalith/Hexalith.Builds/.github/workflows/{sharedWorkflowName}@{ApprovedBuildsSha}";
        int referenceIndex = caller.IndexOf(reference, StringComparison.Ordinal);
        referenceIndex.ShouldBeGreaterThanOrEqualTo(0);
        string[] suppliedInputs = MappingKeys(caller, "with:", referenceIndex);

        string buildsRoot = RepositoryRoot.DependencyRoot("Hexalith.Builds");
        string sharedWorkflow = await ReadGitObjectAsync(
            buildsRoot,
            $"{ApprovedBuildsSha}:.github/workflows/{sharedWorkflowName}").ConfigureAwait(true);
        string[] declaredInputs = MappingKeys(sharedWorkflow, "inputs:");

        suppliedInputs.ShouldNotBeEmpty($"{callerPath} must pass explicit reusable-workflow inputs.");
        declaredInputs.ShouldNotBeEmpty($"The pinned {sharedWorkflowName} workflow_call schema must declare inputs.");
        suppliedInputs.Except(declaredInputs, StringComparer.Ordinal).ShouldBeEmpty(
            $"{callerPath} passes an input not declared by {sharedWorkflowName} at {ApprovedBuildsSha}.");
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

    private static bool IsRepositoryOwnedProject(string root, string projectPath)
    {
        string[] segments = Path.GetRelativePath(root, projectPath).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return !segments.Any(static segment => segment is "_bmad-output" or "references" or "bin" or "obj");
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

    private static string[] MappingKeys(string yaml, string marker, int searchStart = 0)
    {
        string normalized = yaml.Replace("\r\n", "\n", StringComparison.Ordinal);
        int markerIndex = normalized.IndexOf(marker, searchStart, StringComparison.Ordinal);
        markerIndex.ShouldBeGreaterThanOrEqualTo(0, $"Could not find YAML mapping marker '{marker}'.");
        int lineStart = normalized.LastIndexOf('\n', markerIndex);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int lineEnd = normalized.IndexOf('\n', markerIndex);
        lineEnd = lineEnd < 0 ? normalized.Length : lineEnd;
        string markerLine = normalized[lineStart..lineEnd];
        markerLine.Trim().ShouldBe(marker);
        int markerIndent = markerLine.Length - markerLine.TrimStart().Length;

        var keys = new List<string>();
        foreach (string line in normalized[lineEnd..].Split('\n').Skip(1))
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            int indent = line.Length - line.TrimStart().Length;
            if (indent <= markerIndent)
            {
                break;
            }

            string trimmed = line.Trim();
            if (indent == markerIndent + 2 && trimmed.EndsWith(':'))
            {
                keys.Add(trimmed[..^1]);
            }
            else if (indent == markerIndent + 2 && trimmed.Contains(':'))
            {
                keys.Add(trimmed[..trimmed.IndexOf(':')]);
            }
        }

        return [.. keys];
    }

    private static async Task<string> ReadGitObjectAsync(string repository, string objectPath)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(repository);
        start.ArgumentList.Add("show");
        start.ArgumentList.Add(objectPath);

        using Process process = Process.Start(start).ShouldNotBeNull();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitForExitOrKillAsync(process, cancellationToken).ConfigureAwait(true);
        string output = await outputTask.ConfigureAwait(true);
        string error = await errorTask.ConfigureAwait(true);
        process.ExitCode.ShouldBe(0, $"Could not read pinned Builds object {objectPath}: {error}");
        return output;
    }

    private static async Task WaitForExitOrKillAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The child exited between the state check and the kill request; it still must be reaped below.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }

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
