using System.Text.RegularExpressions;
using System.Xml.Linq;

using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Models;
using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class ScaffoldGovernanceTests
{
    private static readonly string[] RequiredSourceProjects =
    [
        "Hexalith.Works.Contracts",
        "Hexalith.Works.Server",
        "Hexalith.Works.Projections",
        "Hexalith.Works.Reactor",
        "Hexalith.Works.ServiceDefaults",
        "Hexalith.Works.AppHost",
    ];

    private static readonly string[] RequiredTestProjects =
    [
        "Hexalith.Works.Testing",
        "Hexalith.Works.UnitTests",
        "Hexalith.Works.PropertyTests",
        "Hexalith.Works.ArchitectureTests",
        "Hexalith.Works.IntegrationTests",
    ];

    private static readonly string[] ForbiddenProjectFragments =
    [
        ".UI",
        ".Mcp",
        ".AdminPortal",
        ".ConsumerPortal",
        ".Security",
        ".Routing",
        ".Llm",
        ".LLM",
        ".CostGovernance",
        ".Email",
        ".Channel",
    ];

    [Fact]
    public void P0_ScaffoldContainsOnlyTheV1ProjectSet()
    {
        string root = RepositoryRoot.Locate();
        string[] projectFiles = Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))
            .ToArray();

        string[] projectNames = [.. projectFiles.Select(path => Path.GetFileNameWithoutExtension(path)!)];

        foreach (string requiredProject in RequiredSourceProjects)
        {
            projectNames.ShouldContain(requiredProject, $"Works requires '{requiredProject}' in the bounded-context project set.");
        }

        string[] forbiddenProjects = [.. projectNames.Where(name => ForbiddenProjectFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))];
        forbiddenProjects.ShouldBeEmpty("Works excludes UI, MCP, portal, security, routing, LLM, cost-governance, and production channel-adapter projects.");
    }

    [Fact]
    public void P0_ScaffoldContainsFocusedTestProjectSet()
    {
        string root = RepositoryRoot.Locate();
        string[] projectNames = [.. Directory.GetFiles(Path.Combine(root, "tests"), "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => Path.GetFileNameWithoutExtension(path)!)];

        foreach (string requiredProject in RequiredTestProjectSet())
        {
            projectNames.ShouldContain(requiredProject, $"Works requires focused test project '{requiredProject}'.");
        }
    }

    [Fact]
    public void P0_ScaffoldUsesSlnxAndCentralPackageManagement()
    {
        string root = RepositoryRoot.Locate();

        File.Exists(Path.Combine(root, "Hexalith.Works.slnx")).ShouldBeTrue("Works requires Hexalith.Works.slnx.");
        File.Exists(Path.Combine(root, "Hexalith.Works.sln")).ShouldBeFalse("Use .slnx, not .sln.");
        File.Exists(Path.Combine(root, "Directory.Packages.props")).ShouldBeTrue("Central package management must define package versions outside project files.");

        string[] projectFiles = Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))
            .ToArray();

        foreach (string projectFile in projectFiles)
        {
            XDocument project = XDocument.Load(projectFile);
            string[] inlineVersions = [.. project.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Where(element => element.Attribute("Version") is not null || element.Element("Version") is not null)
                .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value ?? projectFile)];

            inlineVersions.ShouldBeEmpty($"Package versions must be managed centrally, not inline in '{projectFile}'.");
        }
    }

    [Fact]
    public void P0_SlnxRegistersWorksProjectsAsBuildableProjects()
    {
        string root = RepositoryRoot.Locate();
        XDocument slnx = XDocument.Load(Path.Combine(root, "Hexalith.Works.slnx"));

        string[] csprojRegisteredAsFile = [.. slnx.Descendants()
            .Where(element => element.Name.LocalName == "File")
            .Select(element => element.Attribute("Path")?.Value)
            .OfType<string>()
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))];

        csprojRegisteredAsFile.ShouldBeEmpty("Every Works .csproj must be registered as <Project> (not a passive <File>) so 'dotnet build Hexalith.Works.slnx' actually compiles it.");
    }

    [Fact]
    public void P0_KernelProjectsStayInfrastructureFree()
    {
        string root = RepositoryRoot.Locate();
        string[] kernelProjects =
        [
            "Hexalith.Works.Contracts",
            "Hexalith.Works.Server",
            "Hexalith.Works.Projections",
        ];

        string[] forbiddenReferences =
        [
            "Dapr.Actors.AspNetCore",
            "Dapr.Client",
            "ModelContextProtocol",
            "Microsoft.AspNetCore.Components",
            "Microsoft.AspNetCore.OpenApi",
            "Swashbuckle",
            "OpenAI",
            "SemanticKernel",
        ];

        foreach (string project in kernelProjects)
        {
            string path = Path.Combine(root, "src", project, project + ".csproj");
            File.Exists(path).ShouldBeTrue($"Project file not found at '{path}'.");

            string text = File.ReadAllText(path);
            foreach (string forbidden in forbiddenReferences)
            {
                text.ShouldNotContain(forbidden, Case.Insensitive, $"{project} must remain kernel code and not reference adapter, Dapr runtime, UI, MCP, LLM, or OpenAPI packages.");
            }
        }
    }

    [Fact]
    public void P0_NestedSubmodulesRemainUninitialized()
    {
        string root = RepositoryRoot.Locate();
        string[] nestedGitMarkers = Directory.GetFileSystemEntries(root, ".git", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, Path.Combine(root, ".git"), StringComparison.Ordinal))
            .Where(path => path.Split(Path.DirectorySeparatorChar).Count(segment => segment.StartsWith("Hexalith.", StringComparison.Ordinal)) > 1)
            .ToArray();

        nestedGitMarkers.ShouldBeEmpty("Nested submodules inside root submodules must remain uninitialized; never run recursive submodule commands for this repository.");
    }

    [Fact]
    public void P0_WorkItemSliceAllowsRollUpOnlyInProjectionAndOwnsReminderRecoveryOnlyAtAdapterEdge()
    {
        string root = RepositoryRoot.Locate();
        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))];

        // Story 3.3 owns recursive roll-up, but only as explicit read-model contracts and pure
        // projection logic. Story 4.6 owns reminder/reactor recovery, but only at the runnable host edge
        // and AppHost/config/test/docs locations; the pure kernel projects remain recovery-runtime free.
        string[] rollUpOutsideOwnedLocations = [.. sourceFiles
            .Where(path => !Path.GetFileName(path).EndsWith("Assembly.cs", StringComparison.Ordinal))
            .Where(path => !IsOwnedRollUpLocation(root, path))
            .Where(path => File.ReadAllText(path).Contains("RollUp", StringComparison.OrdinalIgnoreCase))];

        rollUpOutsideOwnedLocations.ShouldBeEmpty("Story 3.3 permits roll-up only in Contracts read models and Projections strategy/input code.");

        string[] filesWithMisplacedReminderBehavior = [.. sourceFiles
            .Where(path => !Path.GetFileName(path).EndsWith("Assembly.cs", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(Path.Combine("Hexalith.Works.ServiceDefaults", "Extensions.cs"), StringComparison.Ordinal))
            .Where(path => !IsOwnedReminderRecoveryLocation(root, path))
            .Where(path => File.ReadAllText(path).Contains("Reminder", StringComparison.OrdinalIgnoreCase))];

        filesWithMisplacedReminderBehavior.ShouldBeEmpty("Story 4.6 reminder/recovery code is allowed only in the runnable host/AppHost edge, not in Contracts, Server, Projections, Reactor, or ServiceDefaults.");
    }

    [Fact]
    public void P0_WorkItemKernelRemainsPure()
    {
        string root = RepositoryRoot.Locate();
        string[] kernelRoots =
        [
            Path.Combine(root, "src", "Hexalith.Works.Contracts"),
            Path.Combine(root, "src", "Hexalith.Works.Server"),
            Path.Combine(root, "src", "Hexalith.Works.Projections"),
        ];
        string[] bannedSymbols =
        [
            "DateTime.Now",
            "DateTime.UtcNow",
            "DateTimeOffset.Now",
            "DateTimeOffset.UtcNow",
            "Stopwatch",
            "PeriodicTimer",
            "Task.Delay",
            "System.Threading.Timer",
            "Guid.NewGuid",
            "UniqueIdHelper.Generate",
            "File.",
            "Directory.",
            "HttpClient",
            "Dapr",
        ];

        string[] violations = [.. kernelRoots
            .SelectMany(path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => bannedSymbols
                .Where(symbol => File.ReadAllText(path).Contains(symbol, StringComparison.Ordinal))
                .Select(symbol => $"{Path.GetRelativePath(root, path)} contains {symbol}"))];

        violations.ShouldBeEmpty("Work item command handling, expiry, projection, and replay must remain deterministic: no clocks, timers, generated IDs, Dapr, EventStore envelope APIs, or I/O in the domain kernel.");
    }

    [Fact]
    public void P0_WorkItemRollUpDoesNotExposeCoercedAllUnitTotal()
    {
        string[] forbiddenPropertyNames =
        [
            "Total",
            "AllUnit",
            "Combined",
            "Coerced",
        ];

        string[] violations = [.. typeof(WorkItemRollUp)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => forbiddenPropertyNames.Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))];

        violations.ShouldBeEmpty("WorkItemRollUp must expose labeled per-unit subtotals and no coerced all-unit total surface.");
    }

    [Fact]
    public void P0_RollUpProjectionDiagnosticExposesOnlyMetadataNeverPayloadValues()
    {
        // AC #5: "logs include only tenant, work item, event type, and sequence metadata, never payload
        // values." Lock the diagnostic contract structurally so a future change cannot reintroduce a
        // payload-bearing field (DoneDelta, Estimated, Unit, Note, ...) onto the metadata-only record.
        string[] allowedPropertyNames =
        [
            nameof(RollUpProjectionDiagnostic.TenantId),
            nameof(RollUpProjectionDiagnostic.WorkItemId),
            nameof(RollUpProjectionDiagnostic.EventType),
            nameof(RollUpProjectionDiagnostic.Sequence),
        ];

        string[] forbiddenPayloadTokens =
        [
            "Delta",
            "Estimated",
            "Unit",
            "Note",
            "Done",
            "Remaining",
            "Payload",
            "Value",
        ];

        string[] propertyNames = [.. typeof(RollUpProjectionDiagnostic).GetProperties().Select(property => property.Name)];

        propertyNames.ShouldBe(allowedPropertyNames, ignoreOrder: true);
        propertyNames
            .Where(name => forbiddenPayloadTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ShouldBeEmpty("RollUpProjectionDiagnostic must carry only tenant, work item, event type, and sequence metadata — never payload values.");
    }

    [Fact]
    public void P0_WorkItemServerDependsOnlyOnContracts()
    {
        string root = RepositoryRoot.Locate();
        string projectFile = Path.Combine(root, "src", "Hexalith.Works.Server", "Hexalith.Works.Server.csproj");
        XDocument project = XDocument.Load(projectFile);

        string[] projectReferences = [.. project.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(ProjectNameFromReference)];

        projectReferences.ShouldBe(
            ["Hexalith.Works.Contracts"],
            ignoreOrder: true,
            customMessage: "Story 1.2 keeps EventStore.Client out of the Works server kernel until a later command-pipeline/Aspire story owns the Dapr dependency decision.");
    }

    // AC #1/#3/#5 + SM-3: every executor is one ExecutorBinding shape and no domain behavior branches on
    // executor kind. Scans production src for switch/if/pattern branching over Channel, AuthorityLevel, or
    // PartyId. AuthorityLevel is an ordered set (Read < Contribute < Coordinate < Administer), so the
    // realistic way to *enforce* authority is a relational comparison (e.g. authority >= Coordinate);
    // those operators are forbidden alongside equality/case/switch/is so "carried-not-enforced" stays
    // guarded. The value-object definitions own the catalogs and ExecutorBinding's construction-time
    // validation (Unknown/undefined rejection), so they are excluded; everything else — aggregate,
    // projections, reactor, read models — must treat the binding as opaque data, never branching on it.
    [Fact]
    public void P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority()
    {
        string root = RepositoryRoot.Locate();

        string[] allowedDefinitionFiles =
        [
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "ExecutorBinding.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "Channel.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "AuthorityLevel.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "PartyId.cs"),
        ];

        string[] forbiddenBranchPatterns =
        [
            @"case\s+Channel\.",
            @"case\s+AuthorityLevel\.",
            @"switch\s*\([^)]*\.\s*Channel\b",
            @"switch\s*\([^)]*\.\s*AuthorityLevel\b",
            @"(==|!=)\s*Channel\.",
            @"Channel\.[A-Za-z]+\s*(==|!=)",
            @"(==|!=|>=|<=|>|<)\s*AuthorityLevel\.",
            @"AuthorityLevel\.[A-Za-z]+\s*(==|!=|>=|<=|>|<)",
            @"\bis\s+Channel\.",
            @"\bis\s+AuthorityLevel\.",
            @"\.PartyId\s*(==|!=)",
        ];

        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).EndsWith("Assembly.cs", StringComparison.Ordinal))
            .Where(path => allowedDefinitionFiles.All(allowed => !Path.GetRelativePath(root, path).EndsWith(allowed, StringComparison.Ordinal)))];

        sourceFiles.ShouldNotBeEmpty("Expected to discover Works src files to govern for executor-kind branching.");

        string[] violations = [.. sourceFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => forbiddenBranchPatterns
                .Where(pattern => Regex.IsMatch(file.Text, pattern))
                .Select(pattern => $"{Path.GetRelativePath(root, file.Path)} matches forbidden executor-kind branch /{pattern}/"))];

        violations.ShouldBeEmpty("Domain behavior must not branch on executor kind, channel, authority, or party identity — everything is one ExecutorBinding shape (SM-3 zero branching on executor kind).");
    }

    // Story 4.2 / FR-17: assign, reassign, and human↔system hand-off are one uniform AssignWorkItem
    // operation; the only variation is ExecutorBinding field values. There must be NO executor-kind-
    // specific handoff/reassign/unassign/return-to-pool command or event. This guards the *vocabulary*
    // (mirroring P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority, which guards branching):
    // it matches on declared TYPE names — not raw substrings — so AssignWorkItem, LifecycleAct.Reject,
    // and XML-comment "Assign"/"Reject" mentions stay legitimate. It is paired with a frozen-catalog
    // assertion: Story 4.2 introduces no new event, command, or rejection, so the v1 catalog stays 36.
    [Fact]
    public void P0_WorkItemSurfaceHasNoExecutorKindSpecificHandoffOrReassignTypeAndCatalogStays36()
    {
        string root = RepositoryRoot.Locate();

        // Reuse the existing exclusion set: build output, generated code, assembly-info, and the
        // value-object definition files (which legitimately spell "Assign"/"Reject" in names/XML docs).
        string[] allowedDefinitionFiles =
        [
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "ExecutorBinding.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "Channel.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "AuthorityLevel.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "PartyId.cs"),
        ];

        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).EndsWith("Assembly.cs", StringComparison.Ordinal))
            .Where(path => allowedDefinitionFiles.All(allowed => !Path.GetRelativePath(root, path).EndsWith(allowed, StringComparison.Ordinal)))];

        sourceFiles.ShouldNotBeEmpty("Expected to discover Works src files to govern for hand-off/reassign vocabulary.");

        // A declared type whose NAME matches one of these is an executor-kind-specific hand-off, reassign,
        // unassign, or return-to-pool command/event — exactly the vocabulary FR-17 forbids in favor of the
        // uniform AssignWorkItem / QueueWorkItem / ClaimWorkItem trio.
        string[] forbiddenTypeNamePatterns =
        [
            "HandoffTo",        // HandoffToBot, HandoffToHuman, ...
            "ReassignTo",       // ReassignToHuman, ReassignToExternalParty, ...
            "AssignTo[A-Z]",    // AssignToBot / AssignToHuman / AssignToExternalParty kind-specific commands
            "HandedOff",        // WorkItemHandedOff and similar events
            "^Unassign",        // UnassignWorkItem and similar
            "^ReturnToPool",    // ReturnToPool* commands/events
        ];

        // Capture declared type names (record / record class|struct / class / enum / interface / struct),
        // ignoring leading modifiers, so we test the type identity rather than any substring of the file.
        var declarationRegex = new Regex(
            @"\b(?:public\s+|internal\s+|private\s+|protected\s+|sealed\s+|partial\s+|abstract\s+|static\s+|readonly\s+|file\s+)*(?:record(?:\s+(?:class|struct))?|class|enum|interface|struct)\s+([A-Za-z_]\w*)");

        string[] violations = [.. sourceFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => declarationRegex.Matches(file.Text)
                .Select(match => match.Groups[1].Value)
                .Where(typeName => forbiddenTypeNamePatterns.Any(pattern => Regex.IsMatch(typeName, pattern)))
                .Select(typeName => $"{Path.GetRelativePath(root, file.Path)} declares forbidden hand-off/reassign type '{typeName}'"))];

        violations.ShouldBeEmpty("Assign/reassign/hand-off is one uniform AssignWorkItem operation (FR-17): no HandoffTo*/ReassignTo*/AssignTo<Kind>*/HandedOff*/Unassign*/ReturnToPool* command or event may exist.");

        // The durable v1 wire surface stays frozen at 36 (14 success events + 14 commands + 8 rejection
        // events). Every catalog member derives from the empty Polymorphic base; Story 4.2 adds none. This
        // is the architecture-project-local equivalent of the IntegrationTests' WorkItemV1Catalog.Count.
        int polymorphicCatalogCount = typeof(AssignWorkItem).Assembly.GetTypes()
            .Count(type => !type.IsAbstract && type != typeof(Polymorphic) && typeof(Polymorphic).IsAssignableFrom(type));

        polymorphicCatalogCount.ShouldBe(36, "Story 4.2 adds no event, command, or rejection type; the v1 catalog (WorkItemV1Catalog.Count) stays 36.");
    }

    // Story 4.3 / AC #4 + DC1/DC4: claim is unconditional in v1 — any tenant Executor may claim a Queued
    // item, with no eligibility filter, routing score, escalation ladder, executor ranking, or AI claim
    // decision record (those are a Theme-4 routing concern). Single-claim-wins is realized as the pure
    // lifecycle + the EventStore substrate's expected-version concurrency; the loser's observable rejection
    // is the existing WorkItemTransitionRejected, NOT a new ClaimRejected/ConcurrencyRejected type (DC1).
    // This mirrors P0_WorkItemSurfaceHasNoExecutorKindSpecificHandoffOrReassignTypeAndCatalogStays36: it
    // matches on declared TYPE names (not raw substrings) so legitimate ClaimWorkItem/WorkItemClaimed and
    // XML-comment "claim"/"routing" mentions stay valid, and it is paired with the frozen-catalog assertion
    // so adding a claim-specific or concurrency rejection type breaks the build.
    [Fact]
    public void P0_WorkItemSurfaceHasNoClaimEligibilityRoutingOrConcurrencyRejectionTypeAndCatalogStays36()
    {
        string root = RepositoryRoot.Locate();

        // Reuse the existing exclusion set: build output, generated code, assembly-info, and the
        // value-object definition files (which legitimately spell "Claim"/"Routing" in names/XML docs).
        string[] allowedDefinitionFiles =
        [
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "ExecutorBinding.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "Channel.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "AuthorityLevel.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "PartyId.cs"),
        ];

        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).EndsWith("Assembly.cs", StringComparison.Ordinal))
            .Where(path => allowedDefinitionFiles.All(allowed => !Path.GetRelativePath(root, path).EndsWith(allowed, StringComparison.Ordinal)))];

        sourceFiles.ShouldNotBeEmpty("Expected to discover Works src files to govern for claim eligibility/routing vocabulary.");

        // A declared type whose NAME matches one of these is claim-eligibility, routing, escalation, ranking,
        // an AI claim-decision record, or a concurrency/claim-specific rejection — exactly the vocabulary AC
        // #4 defers to Theme 4 and DC1 forbids adding to the kernel.
        string[] forbiddenTypeNamePatterns =
        [
            "ClaimEligibility",     // ClaimEligibilityFilter / ClaimEligibilityEngine / ...
            "EligibilityFilter",    // ExecutorEligibilityFilter / EligibilityFilter / ...
            "EligibilityEngine",    // eligibility engine
            "ClaimRouter",          // ClaimRouter / ClaimRoutingService / ...
            "RoutingScore",         // RoutingScore / RoutingScoreCalculator / ...
            "ExecutorRanking",      // ExecutorRanking / ExecutorRankingStrategy / ...
            "EscalationLadder",     // EscalationLadder / EscalationLadderPolicy / ...
            "ClaimDecisionRecord",  // Ai/LLM claim decision record
            "ClaimRejected",        // DC1: do NOT add a ClaimRejected rejection type
            "ConcurrencyRejected",  // DC1: do NOT add a ConcurrencyRejected rejection type
        ];

        // Capture declared type names (record / record class|struct / class / enum / interface / struct),
        // ignoring leading modifiers, so we test the type identity rather than any substring of the file.
        var declarationRegex = new Regex(
            @"\b(?:public\s+|internal\s+|private\s+|protected\s+|sealed\s+|partial\s+|abstract\s+|static\s+|readonly\s+|file\s+)*(?:record(?:\s+(?:class|struct))?|class|enum|interface|struct)\s+([A-Za-z_]\w*)");

        string[] violations = [.. sourceFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => declarationRegex.Matches(file.Text)
                .Select(match => match.Groups[1].Value)
                .Where(typeName => forbiddenTypeNamePatterns.Any(pattern => Regex.IsMatch(typeName, pattern)))
                .Select(typeName => $"{Path.GetRelativePath(root, file.Path)} declares forbidden claim-eligibility/routing/concurrency-rejection type '{typeName}'"))];

        violations.ShouldBeEmpty("Claim is unconditional in v1 (AC #4): no ClaimEligibility*/EligibilityFilter*/ClaimRouter*/RoutingScore*/ExecutorRanking*/EscalationLadder*/ClaimDecisionRecord* type, and no new ClaimRejected/ConcurrencyRejected rejection (DC1) — single-claim-wins reuses WorkItemTransitionRejected + the EventStore expected-version substrate.");

        // The durable v1 wire surface stays frozen at 36 (14 success events + 14 commands + 8 rejection
        // events). Every catalog member derives from the empty Polymorphic base; Story 4.3 adds none.
        int polymorphicCatalogCount = typeof(AssignWorkItem).Assembly.GetTypes()
            .Count(type => !type.IsAbstract && type != typeof(Polymorphic) && typeof(Polymorphic).IsAssignableFrom(type));

        polymorphicCatalogCount.ShouldBe(36, "Story 4.3 adds no event, command, or rejection type; the v1 catalog (WorkItemV1Catalog.Count) stays 36.");
    }

    // Story 4.4 / AC #1+#4 + DC1/DC2/DC3: the tenant "what's next" queue is a pure read projection +
    // query-shaping over Works' own events — no routing engine, eligibility scoring, escalation ladder, or
    // executor ranking, and no web/DataGrid/SignalR/MCP/chatbot/email surface ships in v1 (those are
    // Theme-3/Theme-4 concerns). This mirrors the 4.2/4.3 guards: it matches on declared TYPE names (not raw
    // substrings) so legitimate WhatsNext*/IExecutorRouter (the abstraction-only Ports seam) and XML-comment
    // "routing"/"SignalR" mentions stay valid, and it is paired with the frozen-catalog assertion so adding a
    // durable what's-next type breaks the build.
    [Fact]
    public void P0_WorkItemSurfaceHasNoWhatsNextRoutingEligibilityOrLiveSurfaceTypeAndCatalogStays36()
    {
        string root = RepositoryRoot.Locate();

        // Reuse the value-object exclusion set and additionally exclude the abstraction-only IExecutorRouter
        // port (Theme-4 seam, asserted impl-free by BoundaryPortTests) so the "*Router impl" scan does not
        // flag the legitimately-named abstraction itself.
        string[] allowedDefinitionFiles =
        [
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "ExecutorBinding.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "Channel.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "AuthorityLevel.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "ValueObjects", "PartyId.cs"),
            Path.Combine("src", "Hexalith.Works.Contracts", "Ports", "IExecutorRouter.cs"),
        ];

        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).EndsWith("Assembly.cs", StringComparison.Ordinal))
            .Where(path => allowedDefinitionFiles.All(allowed => !Path.GetRelativePath(root, path).EndsWith(allowed, StringComparison.Ordinal)))];

        sourceFiles.ShouldNotBeEmpty("Expected to discover Works src files to govern for what's-next routing/surface vocabulary.");

        // A declared type whose NAME matches one of these is a routing/eligibility engine, escalation ladder,
        // executor ranking, a concrete *Router implementation, or a live web/DataGrid/SignalR/MCP/chatbot/email
        // surface — exactly the vocabulary FR-20 (projection/query only, "no routing engine") and AC #4
        // (no surface in v1) forbid in the Works kernel.
        string[] forbiddenTypeNamePatterns =
        [
            "RoutingEngine",
            "EligibilityScore",
            "EligibilityEngine",
            "EligibilityFilter",
            "EscalationLadder",
            "ExecutorRanking",
            "Router",        // a concrete *Router impl (the IExecutorRouter abstraction file is excluded)
            "DataGrid",
            "Hub",           // SignalR *Hub surface
            "SignalR",
            "WebShell",
            "MailSurface",
            "EmailSurface",
            "McpTool",
            "Chatbot",
        ];

        var declarationRegex = new Regex(
            @"\b(?:public\s+|internal\s+|private\s+|protected\s+|sealed\s+|partial\s+|abstract\s+|static\s+|readonly\s+|file\s+)*(?:record(?:\s+(?:class|struct))?|class|enum|interface|struct)\s+([A-Za-z_]\w*)");

        string[] violations = [.. sourceFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => declarationRegex.Matches(file.Text)
                .Select(match => match.Groups[1].Value)
                .Where(typeName => forbiddenTypeNamePatterns.Any(pattern => Regex.IsMatch(typeName, pattern)))
                .Select(typeName => $"{Path.GetRelativePath(root, file.Path)} declares forbidden what's-next routing/surface type '{typeName}'"))];

        violations.ShouldBeEmpty("The what's-next queue is projection/query only (FR-20): no RoutingEngine*/Eligibility*/EscalationLadder*/ExecutorRanking*/*Router impl and no *DataGrid/*Hub/*SignalR*/*WebShell/*MailSurface/*EmailSurface/*McpTool/*Chatbot surface may exist in the Works kernel (AC #1/#4).");

        // The durable v1 wire surface stays frozen at 36 (14 success events + 14 commands + 8 rejection
        // events). WhatsNextItem is a plain read-model record, not a [PolymorphicSerialization] catalog type;
        // Story 4.4 adds no durable type (DC3).
        int polymorphicCatalogCount = typeof(AssignWorkItem).Assembly.GetTypes()
            .Count(type => !type.IsAbstract && type != typeof(Polymorphic) && typeof(Polymorphic).IsAssignableFrom(type));

        polymorphicCatalogCount.ShouldBe(36, "Story 4.4 adds no event, command, or rejection type; the v1 catalog (WorkItemV1Catalog.Count) stays 36.");
    }

    // Story 4.4 / AC #5 + NFR-6: the pure kernel performs no logging — it never references ILogger or a log
    // sink, so it cannot leak payloads, PII, or obligation text. The read models carry data for consumers;
    // they must not be *logged* by the kernel. A runtime adapter (Stories 4.5/4.6) owns structured logging.
    [Fact]
    public void P0_WorkItemKernelDoesNotLogPayloadsOrPii()
    {
        string root = RepositoryRoot.Locate();
        string[] kernelRoots =
        [
            Path.Combine(root, "src", "Hexalith.Works.Contracts"),
            Path.Combine(root, "src", "Hexalith.Works.Server"),
            Path.Combine(root, "src", "Hexalith.Works.Projections"),
        ];
        string[] bannedLoggingSymbols =
        [
            "ILogger",
            "LoggerMessage",
            "LogInformation",
            "LogWarning",
            "LogError",
            "LogDebug",
            "LogTrace",
            "LogCritical",
            "Console.Write",
        ];

        string[] violations = [.. kernelRoots
            .SelectMany(path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => bannedLoggingSymbols
                .Where(symbol => File.ReadAllText(path).Contains(symbol, StringComparison.Ordinal))
                .Select(symbol => $"{Path.GetRelativePath(root, path)} contains {symbol}"))];

        violations.ShouldBeEmpty("The Works kernel (Contracts, Server, Projections) must perform no logging: no ILogger or log sink may appear, so payloads/PII/obligation text can never be logged from the pure core (NFR-6 / AC #5).");
    }

    private static IEnumerable<string> RequiredTestProjectSet() => RequiredTestProjects;

    private static string ProjectNameFromReference(string include)
    {
        string normalized = include.Replace('\\', '/');
        string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];

        return Path.GetFileNameWithoutExtension(fileName)!;
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsOwnedRollUpLocation(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.StartsWith(Path.Combine("src", "Hexalith.Works.Contracts", "Models"), StringComparison.Ordinal)
            || relative.StartsWith(Path.Combine("src", "Hexalith.Works.Projections"), StringComparison.Ordinal)
            // Story 4.5: the runnable Works domain-service host (src/Hexalith.Works) is the runtime adapter
            // that consumes the pure WorkItemRollUpProjection/WorkItemRollUpEvent at the edge. The trailing
            // separator keeps this scoped to the host project and excludes the Contracts/Server/Projections
            // siblings whose directory names also begin with "Hexalith.Works".
            || relative.StartsWith(Path.Combine("src", "Hexalith.Works") + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsOwnedReminderRecoveryLocation(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.StartsWith(Path.Combine("src", "Hexalith.Works") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(Path.Combine("src", "Hexalith.Works.AppHost") + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
