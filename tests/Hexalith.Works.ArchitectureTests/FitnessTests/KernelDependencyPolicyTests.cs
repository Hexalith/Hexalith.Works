using System.Text.Json.Nodes;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Verifies fail-closed evaluated dependency parsing and kernel dependency classification.
/// </summary>
public sealed class KernelDependencyPolicyTests
{
    /// <summary>
    /// Verifies the canonical classification partitions every source project exactly once.
    /// </summary>
    [Fact]
    public void SourceProjectClassificationIsCompleteAndDisjoint()
    {
        KernelDependencyPolicy.GovernedProjects.ShouldNotBeEmpty();
        KernelDependencyPolicy.AdapterProjects.ShouldNotBeEmpty();
        KernelDependencyPolicy.GovernedProjects
            .Concat(KernelDependencyPolicy.AdapterProjects)
            .ShouldBe(KernelDependencyPolicy.SourceProjects, ignoreOrder: true);

        // The architecture governs exactly these four kernel projects. Demoting any of them to a
        // deliberate adapter silently removes it from every purity, logging, dependency, and closure
        // gate, so the membership itself is the standing guarantee and must fail here first.
        KernelDependencyPolicy.GovernedProjects.ShouldBe(
            [
                "Hexalith.Works.Contracts",
                "Hexalith.Works.Server",
                "Hexalith.Works.Projections",
                "Hexalith.Works.Reactor",
            ],
            ignoreOrder: true,
            customMessage: "Contracts, Server, Projections, and Reactor are the governed kernel; re-classifying one is an architecture decision, not a test edit.");
        KernelDependencyPolicy.AdapterProjects.ShouldBe(
            [
                "Hexalith.Works",
                "Hexalith.Works.AppHost",
                "Hexalith.Works.ServiceDefaults",
            ],
            ignoreOrder: true,
            customMessage: "The runnable host, AppHost, and ServiceDefaults are the deliberate adapters; every other source project must be governed.");
    }

    /// <summary>
    /// Verifies the repository's current top-level source layout is non-vacuous and fully classified.
    /// </summary>
    [Fact]
    public void CurrentSourceLayoutIsFullyClassified()
    {
        string root = RepositoryRoot.Locate();

        KernelDependencyPolicy.ReconcileSourceProjects(root).ShouldBeEmpty();
        KernelDependencyPolicy.SourceProjects.ShouldAllBe(project =>
            File.Exists(Path.Combine(root, "src", project, project + ".csproj")));
    }

    /// <summary>
    /// Verifies governed source roots cover the canonical governed set exactly.
    /// </summary>
    [Fact]
    public void GovernedProjectRootsCoverCanonicalGovernedSetExactly()
    {
        string root = RepositoryRoot.Locate();
        string[] governedRoots = KernelDependencyPolicy.GovernedProjectRoots(root);

        governedRoots
            .Select(Path.GetFileName)
            .ShouldBe(KernelDependencyPolicy.GovernedProjects, ignoreOrder: true);
        governedRoots.Distinct(StringComparer.Ordinal).Count().ShouldBe(governedRoots.Length);

        // Observe the repository rather than restating the implementation: every governed root must be a
        // real source directory owning its project file, and no adapter root may appear among them.
        governedRoots.ShouldAllBe(governedRoot => Directory.Exists(governedRoot)
            && File.Exists(Path.Combine(governedRoot, Path.GetFileName(governedRoot) + ".csproj")));
        governedRoots
            .Select(Path.GetFileName)
            .Intersect(KernelDependencyPolicy.AdapterProjects, StringComparer.Ordinal)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies an additional source project remains ungoverned until deliberately classified.
    /// </summary>
    [Fact]
    public void NewSyntheticSourceProjectIsReportedAsUnclassified()
    {
        DirectoryInfo temporaryRoot = CreateClassifiedSourceLayout();
        try
        {
            string projectPath = CreateSourceProject(temporaryRoot.FullName, "Hexalith.Works.NewAdapter");

            string[] violations = KernelDependencyPolicy.ReconcileSourceProjects(temporaryRoot.FullName);

            violations.ShouldHaveSingleItem();
            violations[0].ShouldContain("Hexalith.Works.NewAdapter", Case.Sensitive);
            violations[0].ShouldContain(projectPath, Case.Sensitive);
            violations[0].ShouldContain("unclassified", Case.Insensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a source project outside the top-level layout is reported instead of escaping classification.
    /// </summary>
    [Fact]
    public void MisplacedSyntheticSourceProjectIsReported()
    {
        DirectoryInfo temporaryRoot = CreateClassifiedSourceLayout();
        try
        {
            string nestedDirectory = Path.Combine(temporaryRoot.FullName, "src", "Group", "Nested");
            Directory.CreateDirectory(nestedDirectory);
            string nestedPath = Path.Combine(nestedDirectory, "Hexalith.Works.Nested.csproj");
            File.WriteAllText(nestedPath, "<Project />");

            string[] violations = KernelDependencyPolicy.ReconcileSourceProjects(temporaryRoot.FullName);

            violations.ShouldContain(violation => violation.Contains(nestedPath, StringComparison.Ordinal)
                && violation.Contains("top-level", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a project file directly under the source root is reported instead of escaping classification.
    /// </summary>
    [Fact]
    public void SourceRootLevelProjectFileIsReported()
    {
        DirectoryInfo temporaryRoot = CreateClassifiedSourceLayout();
        try
        {
            string strayPath = Path.Combine(temporaryRoot.FullName, "src", "Hexalith.Works.Stray.csproj");
            File.WriteAllText(strayPath, "<Project />");

            string[] violations = KernelDependencyPolicy.ReconcileSourceProjects(temporaryRoot.FullName);

            violations.ShouldContain(violation => violation.Contains(strayPath, StringComparison.Ordinal)
                && violation.Contains("top-level", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a missing classified project reports the expected name and path.
    /// </summary>
    [Fact]
    public void MissingSyntheticSourceProjectIsReported()
    {
        DirectoryInfo temporaryRoot = CreateClassifiedSourceLayout();
        try
        {
            string project = KernelDependencyPolicy.GovernedProjects[0];
            string projectPath = Path.Combine(temporaryRoot.FullName, "src", project, project + ".csproj");
            File.Delete(projectPath);

            string[] violations = KernelDependencyPolicy.ReconcileSourceProjects(temporaryRoot.FullName);

            violations.ShouldContain(violation => violation.Contains(project, StringComparison.Ordinal)
                && violation.Contains(projectPath, StringComparison.Ordinal)
                && violation.Contains("missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a classified project basename discovered at two source paths fails reconciliation.
    /// </summary>
    [Fact]
    public void DuplicateClassifiedProjectBasenameIsReported()
    {
        DirectoryInfo temporaryRoot = CreateClassifiedSourceLayout();
        try
        {
            string project = KernelDependencyPolicy.GovernedProjects[0];
            string duplicateDirectory = Path.Combine(temporaryRoot.FullName, "src", "Duplicate.Contracts");
            Directory.CreateDirectory(duplicateDirectory);
            string duplicatePath = Path.Combine(duplicateDirectory, project + ".csproj");
            File.WriteAllText(duplicatePath, "<Project />");

            string[] violations = KernelDependencyPolicy.ReconcileSourceProjects(temporaryRoot.FullName);

            violations.ShouldContain(violation => violation.Contains(project, StringComparison.Ordinal)
                && violation.Contains("discovered 2 times", StringComparison.OrdinalIgnoreCase));
            violations.ShouldContain(violation => violation.Contains(duplicatePath, StringComparison.Ordinal)
                && violation.Contains("does not match", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies empty and mismatched synthetic discovery shapes fail closed with paths.
    /// </summary>
    [Fact]
    public void EmptyAndMismatchedSyntheticSourceLayoutsAreReported()
    {
        DirectoryInfo emptyRoot = Directory.CreateTempSubdirectory("Hexalith.Works.SourceClassificationTests-");
        DirectoryInfo mismatchedRoot = CreateClassifiedSourceLayout();
        try
        {
            Directory.CreateDirectory(Path.Combine(emptyRoot.FullName, "src"));
            KernelDependencyPolicy.ReconcileSourceProjects(emptyRoot.FullName)
                .ShouldContain(violation => violation.Contains("returned no", StringComparison.OrdinalIgnoreCase));

            string mismatchedDirectory = Path.Combine(mismatchedRoot.FullName, "src", "Hexalith.Works.Mismatch");
            Directory.CreateDirectory(mismatchedDirectory);
            string mismatchedPath = Path.Combine(mismatchedDirectory, "Hexalith.Works.Other.csproj");
            File.WriteAllText(mismatchedPath, "<Project />");

            string[] violations = KernelDependencyPolicy.ReconcileSourceProjects(mismatchedRoot.FullName);
            violations.ShouldContain(violation => violation.Contains(mismatchedPath, StringComparison.Ordinal)
                && violation.Contains("does not match", StringComparison.OrdinalIgnoreCase));
            violations.ShouldContain(violation => violation.Contains("Hexalith.Works.Other", StringComparison.Ordinal)
                && violation.Contains("unclassified", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(emptyRoot.FullName, recursive: true);
            Directory.Delete(mismatchedRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies approved low-level dependencies and framework references remain allowed.
    /// </summary>
    [Fact]
    public void SafeEvaluatedClosureIsAccepted()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/project.assets.json",
            CreateAssetsJson("Hexalith.EventStore.Contracts"));

        violations.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies every independently required forbidden family is classified and reported.
    /// </summary>
    [Theory]
    [InlineData("Dapr.Client", "Dapr runtime")]
    [InlineData("Hexalith.EventStore.DomainService", "EventStore client/runtime")]
    [InlineData("Hexalith.Parties.UI", "UI adapter")]
    [InlineData("ModelContextProtocol", "MCP adapter")]
    [InlineData("Microsoft.SemanticKernel", "LLM adapter")]
    [InlineData("Microsoft.Extensions.AI.Abstractions", "LLM adapter")]
    [InlineData("Azure.AI.OpenAI", "LLM adapter")]
    [InlineData("Radzen.Blazor", "UI adapter")]
    [InlineData("Blazorise", "UI adapter")]
    [InlineData("Serilog.Sinks.Console", "telemetry adapter")]
    [InlineData("NLog", "telemetry adapter")]
    [InlineData("Microsoft.OpenApi", "OpenAPI adapter")]
    [InlineData("Microsoft.Extensions.Hosting", "hosting runtime")]
    [InlineData("OpenTelemetry", "telemetry adapter")]
    [InlineData("Microsoft.EntityFrameworkCore", "persistence adapter")]
    [InlineData("System.Data.SqlClient", "persistence adapter")]
    [InlineData("Contoso.Security", "named adapter family")]
    [InlineData("Contoso.Channel", "named adapter family")]
    [InlineData("Hexalith.Parties.Picker", "named adapter family")]
    [InlineData("Hexalith.Tenants.Client", "named adapter family")]
    [InlineData("Azure.Storage.Blobs", "network/messaging client")]
    [InlineData("Azure.Messaging.ServiceBus", "network/messaging client")]
    [InlineData("RabbitMQ.Client", "network/messaging client")]
    [InlineData("Confluent.Kafka", "network/messaging client")]
    [InlineData("MassTransit", "network/messaging client")]
    [InlineData("Grpc.Net.Client", "network/messaging client")]
    [InlineData("Refit", "network/messaging client")]
    [InlineData("AWSSDK.S3", "network/messaging client")]
    [InlineData("Contoso.AdminPortal", "named adapter family")]
    [InlineData("Contoso.ConsumerPortal", "named adapter family")]
    [InlineData("Contoso.CostGovernance", "named adapter family")]
    [InlineData("Contoso.Email", "named adapter family")]
    [InlineData("Contoso.Routing", "named adapter family")]
    [InlineData("Contoso.Llm", "LLM adapter")]
    [InlineData("Contoso.AppHost", "hosting runtime")]
    [InlineData("Contoso.ServiceDefaults", "hosting runtime")]
    [InlineData("Microsoft.Contoso.Mcp", "MCP adapter")]
    [InlineData("Microsoft.Contoso.Client", "named adapter family")]
    [InlineData("Microsoft.Contoso.UI", "UI adapter")]

    // Microsoft.* carries no blanket exemption from generic segment matching: only System.* does.
    [InlineData("Microsoft.Contoso.Security", "named adapter family")]
    [InlineData("Microsoft.Contoso.Routing", "named adapter family")]
    [InlineData("Microsoft.Contoso.Email", "named adapter family")]
    [InlineData("Microsoft.Contoso.Llm", "LLM adapter")]
    public void ForbiddenDependencyFamilyIsReported(string dependencyName, string expectedFamily)
    {
        string[] evaluatedViolations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Server",
            "synthetic/project.assets.json",
            CreateAssetsJson(dependencyName));

        string[] directViolations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Server",
            "synthetic/Hexalith.Works.Server.csproj",
            $"<Project><ItemGroup><PackageReference Include=\"{dependencyName}\" /></ItemGroup></Project>");

        evaluatedViolations.ShouldHaveSingleItem();
        directViolations.ShouldHaveSingleItem();
        evaluatedViolations[0].ShouldContain(dependencyName, Case.Sensitive);
        directViolations[0].ShouldContain(dependencyName, Case.Sensitive);
        evaluatedViolations[0].ShouldContain(expectedFamily, Case.Sensitive);
        directViolations[0].ShouldContain(expectedFamily, Case.Sensitive);
        evaluatedViolations[0].ShouldContain("Hexalith.Works.Server", Case.Sensitive);
        directViolations[0].ShouldContain("Hexalith.Works.Server", Case.Sensitive);
    }

    /// <summary>
    /// Verifies namespace-aware adapter matching does not reject safe near-matches.
    /// </summary>
    [Theory]
    [InlineData("System.Security.Cryptography")]
    [InlineData("System.Threading.Channels")]
    [InlineData("System.Net.Http")]
    [InlineData("Microsoft.Extensions.DependencyInjection.Abstractions")]
    [InlineData("Hexalith.EventStore.Contracts")]
    [InlineData("Acme.ClientUtilities")]
    [InlineData("Acme.SecurityTools")]
    [InlineData("Acme.Serilogic")]
    [InlineData("Microsoft.Extensions.AIfoundation")]
    [InlineData("Microsoft.Contoso.McpTools")]
    [InlineData("Microsoft.Contoso.ClientUtilities")]
    [InlineData("Microsoft.Contoso.UserInterface")]
    [InlineData("System.Contoso.Client")]
    public void SafeNearMatchDependencyIsAccepted(string dependencyName)
    {
        string[] evaluatedViolations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/project.assets.json",
            CreateAssetsJson(dependencyName));

        string[] directViolations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/safe-near-match.csproj",
            $"<Project><ItemGroup><PackageReference Include=\"{dependencyName}\" /></ItemGroup></Project>");

        evaluatedViolations.ShouldBeEmpty();
        directViolations.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies project, package, and framework declarations all use the shared forbidden-family classifier.
    /// </summary>
    [Theory]
    [InlineData("ProjectReference", "../Hexalith.EventStore.Client/Hexalith.EventStore.Client.csproj", "Hexalith.EventStore.Client", "EventStore client/runtime")]
    [InlineData("PackageReference", "Dapr.Client", "Dapr.Client", "Dapr runtime")]
    [InlineData("FrameworkReference", "Microsoft.AspNetCore.App", "Microsoft.AspNetCore.App", "hosting runtime")]
    [InlineData("Reference", "Dapr.Client, Version=1.18.5.0, Culture=neutral, PublicKeyToken=null", "Dapr.Client", "Dapr runtime")]
    public void EveryDirectReferenceKindUsesSharedFamilyClassification(
        string referenceKind,
        string declaration,
        string dependencyName,
        string expectedFamily)
    {
        string[] violations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/Hexalith.Works.Contracts.csproj",
            $"<Project><ItemGroup><{referenceKind} Include=\"{declaration}\" /></ItemGroup></Project>");

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain(referenceKind, Case.Sensitive);
        violations[0].ShouldContain(dependencyName, Case.Sensitive);
        violations[0].ShouldContain(expectedFamily, Case.Sensitive);
        violations[0].ShouldContain("Hexalith.Works.Contracts", Case.Sensitive);
    }

    /// <summary>
    /// Verifies every dependency in a semicolon-delimited MSBuild item list is inspected.
    /// </summary>
    [Theory]
    [InlineData("ProjectReference", "../Hexalith.EventStore.Client/Hexalith.EventStore.Client.csproj;../Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj", "Hexalith.EventStore.Client", "EventStore client/runtime")]
    [InlineData("ProjectReference", "../Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj;../Hexalith.EventStore.Client/Hexalith.EventStore.Client.csproj", "Hexalith.EventStore.Client", "EventStore client/runtime")]
    [InlineData("PackageReference", "Dapr.Client;System.Collections.Immutable", "Dapr.Client", "Dapr runtime")]
    [InlineData("PackageReference", "System.Collections.Immutable;Dapr.Client", "Dapr.Client", "Dapr runtime")]
    [InlineData("FrameworkReference", "Microsoft.AspNetCore.App;Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "hosting runtime")]
    public void DirectItemSpecListsInspectEveryDependency(
        string referenceKind,
        string declaration,
        string forbiddenDependency,
        string expectedFamily)
    {
        string[] violations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/item-list.csproj",
            $"<Project><ItemGroup><{referenceKind} Include=\"{declaration}\" /></ItemGroup></Project>");

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain(forbiddenDependency, Case.Sensitive);
        violations[0].ShouldContain(expectedFamily, Case.Sensitive);
    }

    /// <summary>
    /// Verifies one item list reports every forbidden direct dependency rather than stopping at the first.
    /// </summary>
    [Fact]
    public void DirectItemSpecListReportsMultipleForbiddenDependencies()
    {
        string[] violations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/multiple-forbidden-items.csproj",
            "<Project><ItemGroup><PackageReference Include=\"Dapr.Client;Microsoft.AspNetCore.App\" /></ItemGroup></Project>");

        violations.Length.ShouldBe(2);
        violations.ShouldContain(violation => violation.Contains("Dapr.Client", StringComparison.Ordinal)
            && violation.Contains("Dapr runtime", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("Microsoft.AspNetCore.App", StringComparison.Ordinal)
            && violation.Contains("hosting runtime", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies supported MSBuild item kinds are canonicalized case-insensitively.
    /// </summary>
    [Theory]
    [InlineData("pRoJeCtReFeReNcE", "../Hexalith.EventStore.Client/Hexalith.EventStore.Client.csproj", "ProjectReference", "Hexalith.EventStore.Client")]
    [InlineData("pAcKaGeReFeReNcE", "Dapr.Client", "PackageReference", "Dapr.Client")]
    [InlineData("fRaMeWoRkReFeReNcE", "Microsoft.AspNetCore.App", "FrameworkReference", "Microsoft.AspNetCore.App")]
    [InlineData("rEfErEnCe", "Dapr.Client, Version=1.0.0.0", "Reference", "Dapr.Client")]
    public void CaseVariantDirectItemKindsAreClassified(
        string itemKind,
        string declaration,
        string canonicalKind,
        string dependencyName)
    {
        string[] violations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/case-variant-kind.csproj",
            $"<Project><ItemGroup><{itemKind} Include=\"{declaration}\" /></ItemGroup></Project>");

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain($"direct {canonicalKind}", Case.Sensitive);
        violations[0].ShouldContain(dependencyName, Case.Sensitive);
    }

    /// <summary>
    /// Verifies every literal the retired kernel text scan guarded is still classified by the shared
    /// forbidden-family classifier that replaced it.
    /// </summary>
    [Theory]
    [InlineData("Dapr.Actors.AspNetCore")]
    [InlineData("Dapr.Client")]
    [InlineData("ModelContextProtocol")]
    [InlineData("Microsoft.AspNetCore.Components")]
    [InlineData("Microsoft.AspNetCore.OpenApi")]
    [InlineData("Swashbuckle")]
    [InlineData("OpenAI")]
    [InlineData("SemanticKernel")]
    public void RetiredKernelTextScanLiteralsRemainForbidden(string retiredLiteral)
    {
        string[] violations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/retired-literal.csproj",
            $"<Project><ItemGroup><PackageReference Include=\"{retiredLiteral}\" /></ItemGroup></Project>");

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain(retiredLiteral, Case.Sensitive);
        violations[0].ShouldContain("is forbidden", Case.Sensitive);
    }

    /// <summary>
    /// Verifies a hint-path style assembly reference is classified by its assembly name, not its path.
    /// </summary>
    [Theory]
    [InlineData("..\\libs\\Dapr.Client.dll")]
    [InlineData("../libs/Dapr.Client.dll")]
    [InlineData("../libs/Dapr.Client.dll, Version=1.18.5.0")]
    public void HintPathStyleAssemblyReferenceIsClassified(string declaration)
    {
        string[] violations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/hint-path-reference.csproj",
            $"<Project><ItemGroup><Reference Include=\"{declaration}\" /></ItemGroup></Project>");

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("direct Reference", Case.Sensitive);
        violations[0].ShouldContain("Dapr.Client", Case.Sensitive);
        violations[0].ShouldContain("Dapr runtime", Case.Sensitive);
    }

    /// <summary>
    /// Verifies the file-backed project evaluator propagates direct-reference family diagnostics.
    /// </summary>
    [Fact]
    public void ProjectFileWrapperReportsForbiddenDirectReference()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DirectReferenceTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Hexalith.Works.Contracts.csproj");
            File.WriteAllText(
                projectPath,
                "<Project><ItemGroup><Reference Include=\"Dapr.Client, Version=1.18.5.0\" /></ItemGroup></Project>");

            string[] violations = KernelDependencyPolicy.EvaluateProjectFile(
                "Hexalith.Works.Contracts",
                projectPath);

            violations.ShouldHaveSingleItem();
            violations[0].ShouldContain("Hexalith.Works.Contracts", Case.Sensitive);
            violations[0].ShouldContain(projectPath, Case.Sensitive);
            violations[0].ShouldContain("direct Reference", Case.Sensitive);
            violations[0].ShouldContain("Dapr.Client", Case.Sensitive);
            violations[0].ShouldContain("Dapr runtime", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the file-backed evaluator fails closed on an absent, unreadable, or empty project file
    /// instead of surfacing an unhandled IO exception from the governed-project scan.
    /// </summary>
    [Fact]
    public void UnusableGovernedProjectFileIsReportedAsAPolicyViolation()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DirectReferenceTests-");
        try
        {
            string missingPath = Path.Combine(temporaryRoot.FullName, "Absent.csproj");
            string[] missing = KernelDependencyPolicy.EvaluateProjectFile("Hexalith.Works.Contracts", missingPath);
            missing.ShouldHaveSingleItem();
            missing[0].ShouldContain("Hexalith.Works.Contracts", Case.Sensitive);
            missing[0].ShouldContain(missingPath, Case.Sensitive);
            missing[0].ShouldContain("is missing", Case.Sensitive);

            string emptyPath = Path.Combine(temporaryRoot.FullName, "Empty.csproj");
            File.WriteAllText(emptyPath, "   ");
            string[] empty = KernelDependencyPolicy.EvaluateProjectFile("Hexalith.Works.Contracts", emptyPath);
            empty.ShouldHaveSingleItem();
            empty[0].ShouldContain("the project XML is empty", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the direct-family gate deliberately ignores conditions: a conditioned forbidden reference
    /// is still classified, so a condition cannot be used to hide an adapter dependency from kernel purity.
    /// This is intentionally stricter than the exact dependency-direction discovery, which fails closed on
    /// a condition instead of reading through it.
    /// </summary>
    [Fact]
    public void ConditionalForbiddenDeclarationIsStillClassifiedByTheFamilyGate()
    {
        string[] violations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/conditional-forbidden.csproj",
            "<Project><ItemGroup Condition=\"'$(Configuration)'=='Release'\"><PackageReference Include=\"Dapr.Client\" Condition=\"'$(X)'=='1'\" /></ItemGroup></Project>");

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Dapr.Client", Case.Sensitive);
        violations[0].ShouldContain("Dapr runtime", Case.Sensitive);
    }

    /// <summary>
    /// Verifies the shared declared-reference discovery is kind-generic, so every gate that reads project
    /// XML — not only the exact dependency-direction gate — splits item lists and fails closed alike.
    /// </summary>
    [Theory]
    [InlineData("PackageReference", "<PackageReference Include=\"System.Text.Json;Dapr.Client\" />", "Dapr.Client")]
    [InlineData("PackageReference", "<pAcKaGeReFeReNcE Include=\"Dapr.Client\" />", "Dapr.Client")]
    [InlineData("PackageReference", "<PackageReference Include=\"Dapr.Client\" Condition=\"'$(X)'=='1'\" />", "<conditional PackageReference 'Dapr.Client'>")]
    [InlineData("PackageReference", "<PackageReference Include=\"$(Opaque)\" />", "<malformed PackageReference '$(Opaque)'>")]
    [InlineData("ProjectReference", "<ProjectReference Include=\"../A/A.csproj;../B/B.csproj\" />", "B")]
    public void SharedDeclaredReferenceDiscoveryIsKindGeneric(
        string referenceKind,
        string itemXml,
        string expectedName)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DeclaredReferenceTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Synthetic.csproj");
            File.WriteAllText(projectPath, $"<Project><ItemGroup>{itemXml}</ItemGroup></Project>");

            KernelDependencyPolicy.DeclaredReferenceNames(projectPath, referenceKind)
                .ShouldContain(expectedName);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies malformed direct XML and malformed declarations fail closed.
    /// </summary>
    [Theory]
    [InlineData("<Project>", "could not be parsed")]
    [InlineData("<NotAProject />", "root is not a Project")]
    [InlineData("<Project><ItemGroup><PackageReference /></ItemGroup></Project>", "without a dependency name")]
    [InlineData("<Project><ItemGroup><ProjectReference Include=\"not-a-project.txt\" /></ItemGroup></Project>", "malformed ProjectReference")]
    [InlineData("<Project><ItemGroup><ProjectReference Include=\"$(OpaqueProject)\" /></ItemGroup></Project>", "malformed ProjectReference")]
    [InlineData("<Project><ItemGroup><ProjectReference Include=\"../$(OpaqueProject).csproj\" /></ItemGroup></Project>", "malformed ProjectReference")]
    [InlineData("<Project><ItemGroup><ProjectReference Include=\"@(OpaqueProject).csproj\" /></ItemGroup></Project>", "malformed ProjectReference")]
    [InlineData("<Project><ItemGroup><ProjectReference Include=\"%(OpaqueProject.Identity).csproj\" /></ItemGroup></Project>", "malformed ProjectReference")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"$(OpaquePackage)\" /></ItemGroup></Project>", "malformed PackageReference")]
    [InlineData("<Project><ItemGroup><FrameworkReference Include=\"@(OpaqueFramework)\" /></ItemGroup></Project>", "malformed FrameworkReference")]
    [InlineData("<Project><ItemGroup><Reference Include=\"$(OpaqueAssembly)\" /></ItemGroup></Project>", "malformed Reference")]
    [InlineData("<Project><ItemGroup><Reference Include=\", Version=1.0.0.0\" /></ItemGroup></Project>", "malformed Reference")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Dapr.*\" /></ItemGroup></Project>", "malformed PackageReference")]
    [InlineData("<Project><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.?\" /></ItemGroup></Project>", "malformed FrameworkReference")]
    public void MalformedDirectReferenceInputIsReported(string projectXml, string expectedReason)
    {
        string[] violations = KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/malformed.csproj",
            projectXml);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Hexalith.Works.Contracts", Case.Sensitive);
        violations[0].ShouldContain("synthetic/malformed.csproj", Case.Sensitive);
        violations[0].ShouldContain(expectedReason, Case.Insensitive);
    }

    /// <summary>
    /// Verifies comments and unrelated XML text are not treated as declared dependencies.
    /// </summary>
    [Fact]
    public void UnrelatedProjectXmlTextIsNotADeclaredDependency()
    {
        const string projectXml =
            """
            <Project>
              <!-- Dapr.Client is forbidden when declared, but comments are not references. -->
              <PropertyGroup>
                <Description>Microsoft.Contoso.UI</Description>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="System.Contoso.Client" />
              </ItemGroup>
            </Project>
            """;

        KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/unrelated-text.csproj",
            projectXml)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies MSBuild removal items and item-definition defaults are not treated as added dependencies.
    /// </summary>
    [Fact]
    public void RemovalAndItemDefinitionMetadataAreNotDeclaredDependencies()
    {
        const string projectXml =
            """
            <Project>
              <ItemGroup>
                <PackageReference Remove="Dapr.Client" />
                <FrameworkReference Update="Microsoft.AspNetCore.App" />
              </ItemGroup>
              <ItemDefinitionGroup>
                <PackageReference Include="Dapr.Client" />
                <ProjectReference Include="../Hexalith.EventStore.Client/Hexalith.EventStore.Client.csproj" />
              </ItemDefinitionGroup>
            </Project>
            """;

        KernelDependencyPolicy.EvaluateProjectXml(
            "Hexalith.Works.Contracts",
            "synthetic/non-declarations.csproj",
            projectXml)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the original synthetic transitive EventStore Client to Dapr chain remains non-vacuous.
    /// </summary>
    [Fact]
    public void ForbiddenTransitiveEventStoreClientAndDaprDependenciesAreReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Server",
            "synthetic/project.assets.json",
            _forbiddenTransitiveAssetsJson);

        violations.Length.ShouldBe(2);
        violations.ShouldContain(violation => violation.Contains("Hexalith.Works.Server", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("Hexalith.EventStore.Client", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("Dapr.Client", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies forbidden evaluated framework references are governed alongside target libraries.
    /// </summary>
    [Fact]
    public void ForbiddenFrameworkReferenceIsReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Reactor",
            "synthetic/framework-reference-project.assets.json",
            CreateAssetsJson("Hexalith.Works.Contracts", "Microsoft.AspNetCore.App"));

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Microsoft.AspNetCore.App", Case.Sensitive);
        violations[0].ShouldContain("framework reference", Case.Sensitive);
        violations[0].ShouldContain("hosting runtime", Case.Sensitive);
    }

    /// <summary>
    /// Verifies surrounding whitespace cannot hide a forbidden evaluated framework reference.
    /// </summary>
    [Fact]
    public void WhitespacePaddedFrameworkReferenceIsReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Reactor",
            "synthetic/padded-framework-reference-project.assets.json",
            CreateAssetsJson("Hexalith.Works.Contracts", " Microsoft.AspNetCore.App "));

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Microsoft.AspNetCore.App", Case.Sensitive);
        violations[0].ShouldContain("hosting runtime", Case.Sensitive);
    }

    /// <summary>
    /// Verifies every case-variant matching target object is inspected.
    /// </summary>
    [Fact]
    public void CaseVariantMatchingTargetGraphsAreAllInspected()
    {
        const string caseVariantTargetsJson =
            """
            {
              "project": {
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0",
                    "frameworkReferences": {
                      "Microsoft.NETCore.App": {}
                    }
                  }
                }
              },
              "targets": {
                "net10.0": {
                  "Hexalith.EventStore.Contracts/3.97.0": {}
                },
                "NET10.0": {
                  "Dapr.Client/1.18.5": {}
                }
              }
            }
            """;

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/case-variant-project.assets.json",
            caseVariantTargetsJson);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Dapr.Client", Case.Sensitive);
    }

    /// <summary>
    /// Verifies malformed framework entries and evaluated framework metadata fail closed.
    /// </summary>
    [Theory]
    [InlineData("[]", "framework entry")]
    [InlineData("{ \"targetAlias\": 42 }", "targetAlias")]
    [InlineData("{ \"targetAlias\": \" \" }", "targetAlias")]
    [InlineData("{ \"targetAlias\": \"net10.0\", \"frameworkReferences\": [] }", "frameworkReferences")]
    public void MalformedFrameworkMetadataIsReported(string frameworkJson, string expectedReason)
    {
        string assetsJson =
            $$"""
            {
              "project": {
                "frameworks": {
                  "net10.0": {{frameworkJson}}
                }
              },
              "targets": {
                "net10.0": {
                  "Hexalith.EventStore.Contracts/3.97.0": {}
                }
              }
            }
            """;

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/malformed-framework-project.assets.json",
            assetsJson);

        violations.ShouldContain(violation => violation.Contains(expectedReason, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a real assets artifact identifying another project fails closed.
    /// </summary>
    [Fact]
    public void MismatchedAssetsProjectIdentityIsReported()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Hexalith.Works.Contracts.csproj");
            string assetsPath = Path.Combine(temporaryRoot.FullName, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
            File.WriteAllText(projectPath, "<Project />");
            File.WriteAllText(
                assetsPath,
                CreateAssetsJson(
                    "Hexalith.EventStore.Contracts",
                    restoreProjectPath: Path.Combine(temporaryRoot.FullName, "Hexalith.Works.Server.csproj")));
            SetFreshness(projectPath, assetsPath, assetsIsStale: false);

            string[] violations = KernelDependencyPolicy.EvaluateFile(
                "Hexalith.Works.Contracts",
                projectPath,
                assetsPath);

            violations.ShouldHaveSingleItem();
            violations[0].ShouldContain("identifies project", Case.Sensitive);
            violations[0].ShouldContain("Hexalith.Works.Server.csproj", Case.Sensitive);
            violations[0].ShouldContain("Hexalith.Works.Contracts.csproj", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an assets artifact older than its governed project fails closed.
    /// </summary>
    [Fact]
    public void StaleAssetsFileIsReported()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Hexalith.Works.Reactor.csproj");
            string assetsPath = Path.Combine(temporaryRoot.FullName, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
            File.WriteAllText(projectPath, "<Project />");
            File.WriteAllText(
                assetsPath,
                CreateAssetsJson("Hexalith.EventStore.Contracts", restoreProjectPath: projectPath));
            SetFreshness(projectPath, assetsPath, assetsIsStale: true);

            string[] violations = KernelDependencyPolicy.EvaluateFile(
                "Hexalith.Works.Reactor",
                projectPath,
                assetsPath);

            violations.ShouldHaveSingleItem();
            violations[0].ShouldContain("stale", Case.Insensitive);
            violations[0].ShouldContain(projectPath, Case.Sensitive);
            violations[0].ShouldContain(assetsPath, Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a missing restored artifact cannot make the policy pass vacuously.
    /// </summary>
    [Fact]
    public void MissingEvaluatedGraphIsReported()
    {
        string root = RepositoryRoot.Locate();
        string projectPath = Path.Combine(root, "src", "Hexalith.Works.Reactor", "Hexalith.Works.Reactor.csproj");
        string missingPath = Path.Combine(root, "missing-kernel-graph", "project.assets.json");

        File.Exists(missingPath).ShouldBeFalse("The negative fixture must remain absent so the missing-artifact proof is non-vacuous.");

        string[] violations = KernelDependencyPolicy.EvaluateFile(
            "Hexalith.Works.Reactor",
            projectPath,
            missingPath);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Hexalith.Works.Reactor", Case.Sensitive);
        violations[0].ShouldContain(missingPath, Case.Sensitive);
        violations[0].ShouldContain("missing", Case.Insensitive);
    }

    /// <summary>
    /// Verifies invalid JSON is reported with the artifact path.
    /// </summary>
    [Fact]
    public void MalformedEvaluatedGraphIsReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Projections",
            "synthetic/malformed-project.assets.json",
            "{ not-json }");

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("synthetic/malformed-project.assets.json", Case.Sensitive);
        violations[0].ShouldContain("could not be parsed", Case.Insensitive);
    }

    /// <summary>
    /// Verifies an empty restored artifact cannot make the policy pass vacuously.
    /// </summary>
    [Fact]
    public void EmptyEvaluatedGraphIsReportedWithItsArtifactPath()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Server",
            "synthetic/empty-project.assets.json",
            string.Empty);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("synthetic/empty-project.assets.json", Case.Sensitive);
        violations[0].ShouldContain("empty", Case.Insensitive);
    }

    /// <summary>
    /// Verifies a graph without the declared framework target is unusable.
    /// </summary>
    [Fact]
    public void EvaluatedGraphWithoutMatchingTargetIsReported()
    {
        const string noMatchingTargetJson =
            """
            {
              "project": {
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0"
                  }
                }
              },
              "targets": {
                "net9.0": {
                  "Hexalith.EventStore.Contracts/3.97.0": {}
                }
              }
            }
            """;

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/no-target-project.assets.json",
            noMatchingTargetJson);

        violations.Length.ShouldBe(2);
        violations.ShouldContain(violation => violation.Contains("no target graph matches declared framework 'net10.0'", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("target graph 'net9.0' matches no declared framework", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a target graph no declared framework claims is never left uninspected.
    /// </summary>
    [Fact]
    public void UnclaimedTargetGraphIsReported()
    {
        const string unclaimedTargetJson =
            """
            {
              "project": {
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0"
                  }
                }
              },
              "targets": {
                "net10.0": {
                  "Hexalith.EventStore.Contracts/3.97.0": {}
                },
                "net9.0": {
                  "Dapr.Client/1.18.5": {}
                }
              }
            }
            """;

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/unclaimed-target-project.assets.json",
            unclaimedTargetJson);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("target graph 'net9.0' matches no declared framework", Case.Sensitive);
    }

    /// <summary>
    /// Verifies a forbidden shared framework carried by a transitive library is reported with its carrier.
    /// </summary>
    [Fact]
    public void TransitiveLibraryFrameworkReferenceIsReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Server",
            "synthetic/transitive-framework-reference.assets.json",
            CreateAssetsJson(
                "Grpc.AspNetCore.Server",
                libraryFrameworkReferences: """["Microsoft.AspNetCore.App"]"""));

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Microsoft.AspNetCore.App", Case.Sensitive);
        violations[0].ShouldContain("transitive framework reference (via Grpc.AspNetCore.Server)", Case.Sensitive);
        violations[0].ShouldContain("hosting runtime", Case.Sensitive);
        violations[0].ShouldContain("Hexalith.Works.Server", Case.Sensitive);
    }

    /// <summary>
    /// Verifies an allowed shared framework carried by a transitive library stays accepted.
    /// </summary>
    [Fact]
    public void AllowedTransitiveLibraryFrameworkReferenceIsAccepted()
    {
        KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/transitive-framework-reference.assets.json",
            CreateAssetsJson(
                "Hexalith.EventStore.Contracts",
                libraryFrameworkReferences: """["Microsoft.NETCore.App"]"""))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies malformed transitive framework-reference metadata fails closed.
    /// </summary>
    [Theory]
    [InlineData("{}", "has malformed frameworkReferences")]
    [InlineData("[42]", "contains a malformed framework reference")]
    [InlineData("""[" "]""", "contains a malformed framework reference")]
    public void MalformedTransitiveLibraryFrameworkReferenceIsReported(string frameworkReferencesJson, string expectedReason)
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Reactor",
            "synthetic/malformed-transitive-framework-reference.assets.json",
            CreateAssetsJson("Hexalith.EventStore.Contracts", libraryFrameworkReferences: frameworkReferencesJson));

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain(expectedReason, Case.Sensitive);
        violations[0].ShouldContain("Hexalith.EventStore.Contracts", Case.Sensitive);
    }

    /// <summary>
    /// Verifies a whitespace-padded library key is classified rather than treated as a safe name.
    /// </summary>
    [Fact]
    public void WhitespacePaddedLibraryKeyIsClassified()
    {
        var document = new JsonObject
        {
            ["project"] = new JsonObject
            {
                ["frameworks"] = new JsonObject
                {
                    ["net10.0"] = new JsonObject { ["targetAlias"] = "net10.0" },
                },
            },
            ["targets"] = new JsonObject
            {
                ["net10.0"] = new JsonObject { [" Dapr.Client /1.18.5"] = new JsonObject() },
            },
        };

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/whitespace-library-project.assets.json",
            document.ToJsonString());

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("'Dapr.Client' is forbidden (Dapr runtime)", Case.Sensitive);
    }

    /// <summary>
    /// Verifies a malformed library key cannot be silently skipped as a safe dependency.
    /// </summary>
    [Theory]
    [InlineData(" /1.0.0")]
    [InlineData("/1.0.0")]
    [InlineData("Dapr.Client/")]
    [InlineData("Dapr.Client")]
    public void MalformedLibraryKeyIsReported(string libraryKey)
    {
        var document = new JsonObject
        {
            ["project"] = new JsonObject
            {
                ["frameworks"] = new JsonObject
                {
                    ["net10.0"] = new JsonObject { ["targetAlias"] = "net10.0" },
                },
            },
            ["targets"] = new JsonObject
            {
                ["net10.0"] = new JsonObject { [libraryKey] = new JsonObject() },
            },
        };

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/malformed-library-project.assets.json",
            document.ToJsonString());

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("malformed library entry", Case.Sensitive);
    }

    /// <summary>
    /// Verifies the repository-layout entry point the architecture gate calls reports a forbidden closure.
    /// </summary>
    [Fact]
    public void GovernedProjectLayoutReportsForbiddenEvaluatedClosure()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            (string projectPath, string assetsPath) = CreateGovernedProjectLayout(
                temporaryRoot.FullName,
                "Hexalith.Works.Server",
                _forbiddenTransitiveAssetsJson);

            string[] violations = KernelDependencyPolicy.EvaluateGovernedProject(
                temporaryRoot.FullName,
                "Hexalith.Works.Server");

            violations.Length.ShouldBe(2);
            violations.ShouldAllBe(violation => violation.Contains(assetsPath, StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("Hexalith.EventStore.Client", StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("Dapr.Client", StringComparison.Ordinal));
            File.Exists(projectPath).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the repository-layout entry point accepts a clean governed closure.
    /// </summary>
    [Fact]
    public void GovernedProjectLayoutAcceptsCleanEvaluatedClosure()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            _ = CreateGovernedProjectLayout(
                temporaryRoot.FullName,
                "Hexalith.Works.Contracts",
                CreateAssetsJson("Hexalith.EventStore.Contracts"));

            KernelDependencyPolicy.EvaluateGovernedProject(temporaryRoot.FullName, "Hexalith.Works.Contracts")
                .ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an artifact older than a shared restore input fails closed even when the governed project file is untouched.
    /// </summary>
    [Theory]
    [InlineData("Directory.Packages.props")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Solution.props")]
    [InlineData("global.json")]
    [InlineData("NuGet.Config")]
    public void AssetsOlderThanSharedRestoreInputAreReported(string sharedRestoreInput)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            (string projectPath, string assetsPath) = CreateGovernedProjectLayout(
                temporaryRoot.FullName,
                "Hexalith.Works.Projections",
                CreateAssetsJson("Hexalith.EventStore.Contracts"));

            string sharedInputPath = Path.Combine(temporaryRoot.FullName, sharedRestoreInput);
            File.WriteAllText(sharedInputPath, "<Project />");

            var baseline = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(projectPath, baseline.AddMinutes(-10));
            File.SetLastWriteTimeUtc(assetsPath, baseline);
            File.SetLastWriteTimeUtc(sharedInputPath, baseline.AddMinutes(10));

            string[] violations = KernelDependencyPolicy.EvaluateGovernedProject(
                temporaryRoot.FullName,
                "Hexalith.Works.Projections");

            violations.ShouldHaveSingleItem();
            violations[0].ShouldContain("stale", Case.Insensitive);
            violations[0].ShouldContain(sharedInputPath, Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    private const string _forbiddenTransitiveAssetsJson =
        """
        {
          "project": {
            "frameworks": {
              "net10.0": {
                "targetAlias": "net10.0",
                "frameworkReferences": {
                  "Microsoft.NETCore.App": {}
                }
              }
            }
          },
          "targets": {
            "net10.0": {
              "Hexalith.Works.Contracts/1.0.0": {
                "dependencies": {
                  "Hexalith.EventStore.Client": "3.97.0"
                }
              },
              "Hexalith.EventStore.Client/3.97.0": {
                "dependencies": {
                  "Dapr.Client": "1.18.5"
                }
              },
              "Dapr.Client/1.18.5": {}
            }
          }
        }
        """;

    private static string CreateAssetsJson(
        string dependencyName,
        string? forbiddenFrameworkReference = null,
        string? restoreProjectPath = null,
        string? libraryFrameworkReferences = null)
    {
        var frameworkReferences = new JsonObject
        {
            ["Microsoft.NETCore.App"] = new JsonObject(),
        };
        if (forbiddenFrameworkReference is not null)
        {
            frameworkReferences[forbiddenFrameworkReference] = new JsonObject();
        }

        var project = new JsonObject
        {
            ["frameworks"] = new JsonObject
            {
                ["net10.0"] = new JsonObject
                {
                    ["targetAlias"] = "net10.0",
                    ["frameworkReferences"] = frameworkReferences,
                },
            },
        };
        if (restoreProjectPath is not null)
        {
            project["restore"] = new JsonObject
            {
                ["projectPath"] = restoreProjectPath,
            };
        }

        var library = new JsonObject();
        if (libraryFrameworkReferences is not null)
        {
            library["frameworkReferences"] = JsonNode.Parse(libraryFrameworkReferences);
        }

        var document = new JsonObject
        {
            ["project"] = project,
            ["targets"] = new JsonObject
            {
                ["net10.0"] = new JsonObject
                {
                    [$"{dependencyName}/1.0.0"] = library,
                },
            },
        };

        return document.ToJsonString();
    }

    private static (string ProjectPath, string AssetsPath) CreateGovernedProjectLayout(
        string repositoryRoot,
        string governedProject,
        string assetsJson)
    {
        string projectDirectory = Path.Combine(repositoryRoot, "src", governedProject);
        string projectPath = Path.Combine(projectDirectory, governedProject + ".csproj");
        string assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
        File.WriteAllText(projectPath, "<Project />");
        File.WriteAllText(assetsPath, InjectRestoreProjectPath(assetsJson, projectPath));
        SetFreshness(projectPath, assetsPath, assetsIsStale: false);

        return (projectPath, assetsPath);
    }

    private static DirectoryInfo CreateClassifiedSourceLayout()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.SourceClassificationTests-");
        foreach (string project in KernelDependencyPolicy.SourceProjects)
        {
            _ = CreateSourceProject(temporaryRoot.FullName, project);
        }

        return temporaryRoot;
    }

    private static string CreateSourceProject(string repositoryRoot, string project)
    {
        string projectDirectory = Path.Combine(repositoryRoot, "src", project);
        string projectPath = Path.Combine(projectDirectory, project + ".csproj");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(projectPath, "<Project />");
        return projectPath;
    }

    private static string InjectRestoreProjectPath(string assetsJson, string projectPath)
    {
        JsonNode document = JsonNode.Parse(assetsJson)!;
        document["project"]!["restore"] = new JsonObject
        {
            ["projectPath"] = projectPath,
        };

        return document.ToJsonString();
    }

    private static void SetFreshness(string projectPath, string assetsPath, bool assetsIsStale)
    {
        var baseline = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(projectPath, baseline);
        File.SetLastWriteTimeUtc(assetsPath, assetsIsStale ? baseline.AddMinutes(-1) : baseline.AddMinutes(1));
    }
}
