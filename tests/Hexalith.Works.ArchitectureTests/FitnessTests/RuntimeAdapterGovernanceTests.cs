using System.Text.RegularExpressions;
using System.Xml.Linq;

using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Commands;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Story 4.5 governance guardrails for the runtime adapter edge. These keep the pure kernel pure and confine
/// every EventStore-runtime / Dapr / ASP.NET-hosting concern to the single runnable host project
/// (<c>src/Hexalith.Works</c>) and the AppHost, while freezing the production-surface vocabulary out of Works
/// source. All assertions are source/XML text based so the ArchitectureTests lane stays pure (no Dapr/Aspire).
/// </summary>
public sealed class RuntimeAdapterGovernanceTests
{
    private const string RunnableHost = "Hexalith.Works";

    [Fact]
    public void P0_RunnableHostIsTheOnlyWorksProjectReferencingEventStoreRuntimeAndDapr()
    {
        string root = RepositoryRoot.Locate();

        string[] hostReferences = ProjectReferenceNames(root, Path.Combine("src", RunnableHost, RunnableHost + ".csproj"));
        hostReferences.ShouldContain(
            "Hexalith.EventStore.DomainService",
            "The runnable Works host is the adapter edge and must consume the EventStore domain-service SDK.");

        string[] hostPackages = PackageReferenceNames(root, Path.Combine("src", RunnableHost, RunnableHost + ".csproj"));
        hostPackages.ShouldContain("Dapr.AspNetCore", "The runnable Works host owns the Dapr dependency for the proof.");
        hostPackages.ShouldContain("Dapr.Actors", "Story 4.6 date-resume reminders are Dapr actor reminders owned by the runnable Works host.");
        hostPackages.ShouldContain("Dapr.Actors.AspNetCore", "Story 4.6 maps actor reminder callbacks only in the runnable Works host.");

        // The pure kernel + Reactor must never reach EventStore runtime (anything beyond EventStore.Contracts)
        // or take a Dapr dependency. The host is the only Works src project allowed those.
        string[] kernelProjects =
        [
            "Hexalith.Works.Contracts",
            "Hexalith.Works.Server",
            "Hexalith.Works.Projections",
            "Hexalith.Works.Reactor",
        ];

        foreach (string project in kernelProjects)
        {
            string csproj = Path.Combine("src", project, project + ".csproj");
            string[] eventStoreRuntimeReferences = [.. ProjectReferenceNames(root, csproj)
                .Where(reference => reference.StartsWith("Hexalith.EventStore.", StringComparison.Ordinal)
                    && !string.Equals(reference, "Hexalith.EventStore.Contracts", StringComparison.Ordinal))];
            eventStoreRuntimeReferences.ShouldBeEmpty($"{project} must reference only EventStore.Contracts, never EventStore runtime/domain-service projects.");

            string[] daprPackages = [.. PackageReferenceNames(root, csproj)
                .Where(name => name.StartsWith("Dapr", StringComparison.Ordinal))];
            daprPackages.ShouldBeEmpty($"{project} must not take a Dapr dependency; the runtime adapter lives in {RunnableHost}.");
        }
    }

    [Fact]
    public void P0_ReminderActorAndCascadeCheckpointRuntimeAreConfinedToHostEdge()
    {
        string root = RepositoryRoot.Locate();

        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))];

        string[] runtimeTokens =
        [
            "IRemindable",
            "RegisterReminderAsync",
            "UnregisterReminderAsync",
            "DateReminderActor",
            "DateReminderReconciler",
            "CascadeCheckpoint",
            "CascadeDispatcher",
            "IWorkCommandSubmitter",
            "IEventStoreGatewayClient",
            "IReadModelStore",
        ];

        string[] misplaced = [.. sourceFiles
            .Where(path => !IsAllowedRuntimeAdapterLocation(root, path))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => runtimeTokens
                .Where(token => file.Text.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(root, file.Path)} contains host-edge runtime token '{token}'"))];

        misplaced.ShouldBeEmpty("Story 4.6 reminder actors, command submission, stream reads, and checkpoint persistence must stay in src/Hexalith.Works or AppHost/config/test/docs locations, not in pure kernel projects.");
    }

    [Fact]
    public void P0_PureProjectsRemainFreeOfActorClockLoggingNetworkFileAndEventStoreRuntimeApis()
    {
        string root = RepositoryRoot.Locate();
        string[] pureProjects =
        [
            "Hexalith.Works.Contracts",
            "Hexalith.Works.Server",
            "Hexalith.Works.Projections",
            "Hexalith.Works.Reactor",
        ];

        string[] bannedSourcePatterns =
        [
            @"^\s*using\s+Dapr\.",
            @"\bIRemindable\b",
            @"\bRegisterReminderAsync\b",
            @"\bUnregisterReminderAsync\b",
            @"\bTimeProvider\b",
            @"\bDateTimeOffset\.UtcNow\b",
            @"\bDateTimeOffset\.Now\b",
            @"\bDateTime\.UtcNow\b",
            @"\bDateTime\.Now\b",
            @"\bILogger\b",
            @"\bLoggerMessage\b",
            @"\bHttpClient\b",
            @"\bIEventStoreGatewayClient\b",
            @"\bIReadModelStore\b",
            @"\bFile\.",
            @"\bDirectory\.",
        ];

        string[] violations = [.. pureProjects
            .Select(project => Path.Combine(root, "src", project))
            .SelectMany(projectRoot => Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => bannedSourcePatterns
                .Where(pattern => Regex.IsMatch(file.Text, pattern, RegexOptions.Multiline))
                .Select(pattern => $"{Path.GetRelativePath(root, file.Path)} matches forbidden pure-project runtime pattern /{pattern}/"))];

        violations.ShouldBeEmpty("Contracts, Server, Projections, and the pure Reactor must remain free of Dapr actors, clocks, logging, network/filesystem I/O, read-model stores, and EventStore gateway/runtime APIs.");
    }

    [Fact]
    public void P0_ReminderAndCheckpointRecordsDoNotExpandDurablePolymorphicCatalog()
    {
        int polymorphicCatalogCount = typeof(AssignWorkItem).Assembly.GetTypes()
            .Count(type => !type.IsAbstract && type != typeof(Polymorphic) && typeof(Polymorphic).IsAssignableFrom(type));

        polymorphicCatalogCount.ShouldBe(36, "Story 4.6 reminder/checkpoint/read-model records are host-edge runtime records, not durable polymorphic command/event/rejection catalog types.");
    }

    [Fact]
    public void P0_AppHostProgramUsesPlatformEventStoreHelpersNotHandRolledDaprWiring()
    {
        string root = RepositoryRoot.Locate();
        string program = File.ReadAllText(Path.Combine(root, "src", "Hexalith.Works.AppHost", "Program.cs"));

        program.ShouldContain("AddHexalithEventStore", Case.Sensitive, "The AppHost must compose the shared EventStore topology via the platform helper.");
        program.ShouldContain("AddEventStoreDomainModule", Case.Sensitive, "The AppHost must attach the Works domain service via the platform helper.");

        // No hand-rolled, duplicated Dapr component wiring — the helper owns state store / pub-sub creation.
        program.ShouldNotContain("AddDaprStateStore", Case.Insensitive, "The AppHost must not hand-roll a Dapr state store; AddHexalithEventStore owns it.");
        program.ShouldNotContain("AddDaprPubSub", Case.Insensitive, "The AppHost must not hand-roll a Dapr pub/sub; AddHexalithEventStore owns it.");
    }

    [Fact]
    public void P0_WorkItemAggregateStaysPureStaticAndEventStoreAdapterLivesInHost()
    {
        string root = RepositoryRoot.Locate();

        string kernelAggregate = File.ReadAllText(Path.Combine(root, "src", "Hexalith.Works.Server", "Aggregates", "WorkItemAggregate.cs"));
        kernelAggregate.ShouldContain("static class WorkItemAggregate", Case.Sensitive, "WorkItemAggregate must remain the pure static kernel.");
        kernelAggregate.ShouldNotContain("EventStoreAggregate", Case.Sensitive, "The pure kernel aggregate must not inherit EventStore runtime types.");

        string adapter = File.ReadAllText(Path.Combine(root, "src", RunnableHost, "WorkItemEventStoreAggregate.cs"));
        adapter.ShouldContain("EventStoreAggregate<WorkItemState>", Case.Sensitive, "The host adapter aggregate must subclass EventStoreAggregate<WorkItemState>.");
        adapter.ShouldContain("[EventStoreDomain(\"work\")]", Case.Sensitive, "The host adapter aggregate must declare the canonical \"work\" domain.");
        adapter.ShouldContain("Server.Aggregates.WorkItemAggregate", Case.Sensitive, "The host adapter must delegate to the pure kernel WorkItemAggregate.");
    }

    [Fact]
    public void P0_NoProductionSurfaceVocabularyInWorksSource()
    {
        string root = RepositoryRoot.Locate();

        string[] forbiddenTypeNamePatterns =
        [
            "Mcp",
            "Chatbot",
            "EmailSurface",
            "MailSurface",
            "DataGrid",
            "WebShell",
            "RoutingEngine",
            "EligibilityScore",
            "EscalationLadder",
            "CostMeter",
            "SpendGovernance",
        ];

        var declarationRegex = new Regex(
            @"\b(?:public\s+|internal\s+|private\s+|protected\s+|sealed\s+|partial\s+|abstract\s+|static\s+|readonly\s+|file\s+)*(?:record(?:\s+(?:class|struct))?|class|enum|interface|struct)\s+([A-Za-z_]\w*)");

        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))];

        sourceFiles.ShouldNotBeEmpty("Expected to discover Works src files to govern for production-surface vocabulary.");

        string[] vocabularyViolations = [.. sourceFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => declarationRegex.Matches(file.Text)
                .Select(match => match.Groups[1].Value)
                .Where(typeName => forbiddenTypeNamePatterns.Any(pattern => typeName.Contains(pattern, StringComparison.Ordinal)))
                .Select(typeName => $"{Path.GetRelativePath(root, file.Path)} declares forbidden production-surface type '{typeName}'"))];

        vocabularyViolations.ShouldBeEmpty("Story 4.5 ships no production UI/MCP/chatbot/email/DataGrid/web-shell/routing/eligibility/escalation/cost/spend surface in Works source.");

        // No concrete IExecutorRouter implementation may appear (the abstraction-only Theme-4 seam stays impl-free).
        var routerImplRegex = new Regex(@"\bclass\s+[A-Za-z_]\w*\s*(?:<[^>]*>)?\s*:\s*[^\{]*\bIExecutorRouter\b");
        string[] routerImplViolations = [.. sourceFiles
            .Where(path => !Path.GetFileName(path).Equals("IExecutorRouter.cs", StringComparison.Ordinal))
            .Where(path => routerImplRegex.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))];

        routerImplViolations.ShouldBeEmpty("No concrete IExecutorRouter implementation may exist; executor routing stays abstraction-only (Theme 4).");
    }

    [Fact]
    public void P0_RuntimeAdapterLogsOnlyBoundedMetadataNeverPayloads()
    {
        string root = RepositoryRoot.Locate();

        string[] hostFiles = [.. Directory.GetFiles(Path.Combine(root, "src", RunnableHost), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))];

        hostFiles.ShouldNotBeEmpty("Expected to discover runnable-host source files.");

        // A log-template placeholder that names a payload-bearing field would leak event/command content.
        string[] forbiddenLogPlaceholders =
        [
            "{Payload",
            "{Obligation",
            "{Command",
            "{Secret",
            "{Token",
            "{Body",
            "{EventJson",
            "{Json",
        ];

        string[] logLeakViolations = [.. hostFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => forbiddenLogPlaceholders
                .Where(token => file.Text.Contains(token, StringComparison.OrdinalIgnoreCase))
                .Select(token => $"{Path.GetRelativePath(root, file.Path)} contains forbidden log placeholder '{token}'"))];

        logLeakViolations.ShouldBeEmpty("Adapter logs must carry only bounded metadata (ids, type names, reason/projection types) — never payloads, obligations, secrets, tokens, or event/command bodies (AC #4 / NFR-6).");

        // Structured logging only: no interpolated log calls that could embed a payload variable.
        string[] interpolatedLogViolations = [.. hostFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => Regex.IsMatch(file.Text, @"\.Log(Information|Warning|Error|Debug|Trace|Critical)\s*\(\s*\$"))
            .Select(file => Path.GetRelativePath(root, file.Path))];

        interpolatedLogViolations.ShouldBeEmpty("The runtime adapter must use compile-time LoggerMessage definitions, never interpolated log calls that can embed payloads.");
    }

    private static string[] ProjectReferenceNames(string root, string relativeProjectPath)
    {
        XDocument project = XDocument.Load(Path.Combine(root, relativeProjectPath));
        return [.. project.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(ProjectNameFromReference)];
    }

    private static string[] PackageReferenceNames(string root, string relativeProjectPath)
    {
        XDocument project = XDocument.Load(Path.Combine(root, relativeProjectPath));
        return [.. project.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value)
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))];
    }

    private static string ProjectNameFromReference(string include)
    {
        string normalized = include.Replace('\\', '/');
        string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsAllowedRuntimeAdapterLocation(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.StartsWith(Path.Combine("src", RunnableHost) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(Path.Combine("src", "Hexalith.Works.AppHost") + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
