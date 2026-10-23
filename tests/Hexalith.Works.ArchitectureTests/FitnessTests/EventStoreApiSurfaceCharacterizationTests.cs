using System.Diagnostics;
using System.Text.RegularExpressions;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class EventStoreApiSurfaceCharacterizationTests
{
    [Fact]
    public void P1_EventStoreExposesConcurrencyAndProjectionRebuildSurfacesNeededByWorks()
    {
        foreach (string eventStoreRoot in EffectiveEventStoreSourceRoots())
        {
            Directory.Exists(eventStoreRoot).ShouldBeTrue("The root Hexalith.EventStore submodule must be initialized non-recursively before Works implementation depends on it.");

            File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Server", "Commands", "ConcurrencyConflictException.cs"))
                .ShouldBeTrue("Works claim commit-conflict handling needs the EventStore ETag concurrency surface.");

            File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Server", "Actors", "AggregateActor.cs"))
                .ShouldBeTrue("Works command handling depends on the EventStore aggregate actor pipeline.");

            File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Contracts", "Streams", "ProjectionRebuildOperation.cs"))
                .ShouldBeTrue("Works projection rebuild planning depends on the EventStore rebuild operation contract.");

            File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Contracts", "Streams", "ProjectionRebuildCheckpoint.cs"))
                .ShouldBeTrue("Works online rebuild planning depends on checkpointed projection rebuild support.");

            File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Contracts", "Projections", "ProjectionChangedNotification.cs"))
                .ShouldBeTrue("Works read-model freshness depends on projection-change notification support.");
        }
    }

    [Fact]
    public void P1_EventStoreExposesETagBackedProjectionInvalidationSurfaces()
    {
        foreach (string eventStoreRoot in EffectiveEventStoreSourceRoots())
        {
            Directory.Exists(eventStoreRoot).ShouldBeTrue("The root Hexalith.EventStore submodule must be initialized non-recursively before Works implementation depends on it.");

            string projectionWriteActor = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Actors", "IProjectionWriteActor.cs");
            projectionWriteActor.ShouldContain("UpdateProjectionAsync");
            projectionWriteActor.ShouldContain("ETag", Case.Insensitive);

            string etagActor = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Actors", "IETagActor.cs");
            etagActor.ShouldContain("GetCurrentETagAsync");
            etagActor.ShouldContain("RegenerateAsync");

            string notifier = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Projections", "DaprProjectionChangeNotifier.cs");
            notifier.ShouldContain("ProjectionChangedNotification");
            notifier.ShouldContain("RegenerateAsync");
        }
    }

    [Fact]
    public void P1_EventStorePersistsRejectionsAndUsesEnvelopeCanonicalSequencing()
    {
        foreach (string eventStoreRoot in EffectiveEventStoreSourceRoots())
        {
            string aggregateActor = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Actors", "AggregateActor.cs");
            int commandPath = aggregateActor.IndexOf("private async Task<CommandProcessingResult> ProcessCommandCoreAsync", StringComparison.Ordinal);
            commandPath.ShouldBeGreaterThanOrEqualTo(0, "EventStore must still route command handling through AggregateActor.ProcessCommandCoreAsync; the pipeline Works characterizes has moved or been renamed upstream.");

            int noOpGuard = aggregateActor.IndexOf("if (domainResult.IsNoOp)", commandPath, StringComparison.Ordinal);
            noOpGuard.ShouldBeGreaterThan(commandPath, "AggregateActor.ProcessCommandCoreAsync must still short-circuit only on IsNoOp before persisting; the no-op guard Works anchors its rejection characterization on is gone.");

            int persistCall = aggregateActor.IndexOf(".PersistEventsAsync(", noOpGuard, StringComparison.Ordinal);
            persistCall.ShouldBeGreaterThan(noOpGuard, "AggregateActor.ProcessCommandCoreAsync must still call PersistEventsAsync after the no-op guard; Works depends on that persist hop for envelope-canonical sequencing.");

            // Two complementary drift guards, because a rejection short-circuit can be injected anywhere ahead
            // of the persist call, not only after the no-op guard:
            //   1. across the whole command path, reject a rejection-CONDITIONAL short-circuit (the ternary at
            //      the pipeline's log line legitimately reads domainResult.IsRejection, so match `if (`);
            //   2. inside the narrow no-op-guard..persist window, reject the token outright.
            aggregateActor[commandPath..persistCall]
                .Contains("if (domainResult.IsRejection", StringComparison.Ordinal)
                .ShouldBeFalse("EventStore must not branch on IsRejection anywhere between entering ProcessCommandCoreAsync and PersistEventsAsync; Works depends on every rejection consuming an envelope position.");
            aggregateActor[noOpGuard..persistCall]
                .Contains("IsRejection", StringComparison.Ordinal)
                .ShouldBeFalse("EventStore must not branch on IsRejection between the no-op guard and PersistEventsAsync; Works depends on every rejection consuming an envelope position.");
            aggregateActor.ShouldContain(
                "string? rejectionEventType = domainResult.IsRejection",
                customMessage: "EventStore must still classify a persisted rejection by event type after the persist hop; Works reads that classification as evidence rejections travel the same pipeline as successes.");

            string eventPersister = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Events", "EventPersister.cs");
            eventPersister.ShouldContain(
                "long currentSequence = metadataResult.HasValue ? metadataResult.Value.CurrentSequence : 0;",
                customMessage: "EventPersister must still derive the current envelope position from persisted aggregate metadata, never from a payload field.");
            eventPersister.ShouldContain(
                "long sequenceNumber = currentSequence + 1 + i;",
                customMessage: "EventPersister must still assign one gapless envelope position per persisted event; Works' envelope/payload divergence depends on it.");
            eventPersister.ShouldContain(
                "SequenceNumber: sequenceNumber",
                customMessage: "EventPersister must still stamp the derived envelope position onto the stored envelope.");
            eventPersister.ShouldContain(
                "long newSequence = checked(currentSequence + domainResult.Events.Count);",
                customMessage: "EventPersister must still advance the metadata watermark by the full event count, rejections included.");

            string aggregateReplayer = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Client", "Aggregates", "AggregateReplayer.cs");
            aggregateReplayer.ShouldContain(
                "eligible[0].SequenceNumber != 1",
                customMessage: "AggregateReplayer must reject replay that does not start at envelope sequence 1.");
            aggregateReplayer.ShouldContain(
                "if (eligible[i].SequenceNumber != expectedSequence)",
                customMessage: "AggregateReplayer must reject reordered or gapped envelope positions; a tolerated gap would hide a dropped rejection envelope.");
            aggregateReplayer.ShouldContain(
                "lastApplied = evt.SequenceNumber;",
                customMessage: "AggregateReplayer must still report the last applied ENVELOPE position; Works asserts it lands at 2 after rejection-then-create.");
        }
    }

    /// <summary>Both maintained markers bind the owning gitlink to every actually selected source checkout.</summary>
    [Fact]
    public void P0_CurrentSourceRevisionMarkersMatchGitlinkAndConsumedCheckout()
        => CurrentSourceRevisionViolations(RepositoryRoot.Locate()).ShouldBeEmpty();

    private static string[] CurrentSourceRevisionViolations(
        string root, IReadOnlyDictionary<string, string>? globalOverrides = null)
    {
        const string dependencyPath = "references/Hexalith.EventStore";
        if (!TryGitValue(root, ["ls-tree", "HEAD", "--", dependencyPath], out string entry, out string diagnostic))
        {
            return [diagnostic];
        }

        if (!TryGitlinkRevision(entry, out string gitlink, out diagnostic))
        {
            return [diagnostic];
        }

        if (!TryEffectiveEventStoreSourceRoots(root, globalOverrides, out string[] roots, out diagnostic))
        {
            return [diagnostic];
        }

        var violations = new List<string>();
        string api = File.ReadAllText(Path.Combine(root, "docs", "eventstore-api-surface-constraints.md"));
        string boundary = File.ReadAllText(Path.Combine(root, "docs", "boundary-decision-record.md"));
        foreach (string consumedRoot in roots)
        {
            if (!TryGitValue(consumedRoot, ["rev-parse", "HEAD"], out string checkout, out diagnostic))
            {
                violations.Add($"Cannot inspect effective consumed EventStore root '{consumedRoot}': {diagnostic}");
                continue;
            }

            violations.AddRange(RevisionBindingViolations(api, boundary, gitlink, checkout)
                .Select(violation => $"Effective consumed EventStore root '{consumedRoot}': {violation}"));
        }

        return [.. violations.Distinct(StringComparer.Ordinal)];
    }

    private static string[] EffectiveEventStoreSourceRoots(
        string? root = null, IReadOnlyDictionary<string, string>? globalOverrides = null)
    {
        TryEffectiveEventStoreSourceRoots(root ?? RepositoryRoot.Locate(), globalOverrides,
            out string[] roots, out string diagnostic).ShouldBeTrue(diagnostic);
        return roots;
    }

    private static bool TryEffectiveEventStoreSourceRoots(
        string root, IReadOnlyDictionary<string, string>? globalOverrides, out string[] roots, out string diagnostic)
    {
        string owner = Path.Combine(root, "src", "Hexalith.Works", "Hexalith.Works.csproj");
        if (!MsBuildProjectEvaluation.TryEvaluateIndependentSupportedLanes(owner,
            globalOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            out IReadOnlyList<MsBuildProjectSnapshot> snapshots, out diagnostic))
        {
            roots = [];
            diagnostic = $"Cannot establish effective EventStore source dependency: {diagnostic}";
            return false;
        }

        string[] selected = [.. snapshots.Where(snapshot => snapshot.PropertyValue("HexalithEventStoreFromSource") == "true")
            .Select(snapshot => snapshot.PropertyValue("HexalithEventStoreRoot"))];
        if (selected.Length == 0 || selected.Any(string.IsNullOrWhiteSpace))
        {
            roots = [];
            diagnostic = $"Project '{owner}' has no usable effective EventStore source dependency root; source characterization and revision binding require the same consumed checkout.";
            return false;
        }

        roots = [.. selected.Select(path => Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar), Path.GetDirectoryName(owner)!))
            .Distinct(MsBuildProjectEvaluation.PathComparer)];
        diagnostic = string.Empty;
        return true;
    }

    /// <summary>Each independently changed marker, gitlink or checkout must fail without rewriting historical claims.</summary>
    /// <param name="mutation">The binding input to change.</param>
    [Theory]
    [InlineData("api")]
    [InlineData("boundary")]
    [InlineData("gitlink")]
    [InlineData("checkout")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public void CurrentSourceRevisionBindingRejectsIndependentDrift(string mutation)
    {
        string revision = new('a', 40);
        string changed = new('b', 40);
        string marker = $"<!-- EventStore-current-source: {revision} -->";
        string api = marker + "\nHistorical proof: " + changed;
        string boundary = marker;
        string gitlink = revision;
        string checkout = revision;
        RevisionBindingViolations(api, boundary, gitlink, checkout).ShouldBeEmpty();
        switch (mutation)
        {
            case "api": api = api.Replace(revision, changed, StringComparison.Ordinal); break;
            case "boundary": boundary = boundary.Replace(revision, changed, StringComparison.Ordinal); break;
            case "gitlink": gitlink = changed; break;
            case "checkout": checkout = changed; break;
            case "missing": api = "Historical proof: " + revision; break;
            case "duplicate": boundary += "\n" + marker; break;
        }

        RevisionBindingViolations(api, boundary, gitlink, checkout).ShouldNotBeEmpty();
    }

    /// <summary>Missing, malformed and non-gitlink tree entries produce actionable failures before hash extraction.</summary>
    /// <param name="entry">The simulated root tree entry.</param>
    [Theory]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("040000 tree aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\treferences/Hexalith.EventStore")]
    [InlineData("100644 blob aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\treferences/Hexalith.EventStore")]
    [InlineData("160000 commit invalid\treferences/Hexalith.EventStore")]
    [InlineData("160000 commit aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\treferences/Wrong")]
    public void InvalidGitlinkShapeFailsWithActionableDiagnostic(string entry)
    {
        TryGitlinkRevision(entry, out string revision, out string diagnostic).ShouldBeFalse();
        revision.ShouldBeEmpty();
        diagnostic.ShouldContain("references/Hexalith.EventStore");
        diagnostic.ShouldContain("160000 commit");
        TryGitlinkRevision("160000 commit aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\treferences/Hexalith.EventStore",
            out string valid, out _).ShouldBeTrue();
        valid.ShouldBe(new string('a', 40));
    }

    /// <summary>An explicit source-root override must inspect the checkout that compilation actually selects.</summary>
    /// <param name="linkedWorktree">Whether the object-sharing source is an owned linked worktree.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectiveConsumedRootOverrideDriftIsRejected(bool linkedWorktree)
    {
        string root = RepositoryRoot.Locate();
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Revision & Drift-");
        try
        {
            // An independent temporary checkout shares read-only objects with the owning repository,
            // but selects a different existing revision. No user repository ref or gitlink is mutated.
            TryGitValue(root, ["rev-parse", "--path-format=absolute", "--git-path", "objects"], out string rootObjects, out string diagnostic).ShouldBeTrue(diagnostic);
            TryGitValue(root, ["rev-parse", "HEAD"], out string otherRevision, out diagnostic).ShouldBeTrue(diagnostic);
            string objectSource = root;
            if (linkedWorktree)
            {
                string ownedRepository = Path.Combine(temporary.FullName, "object-source");
                Directory.CreateDirectory(ownedRepository);
                TryGitValue(ownedRepository, ["init", "--quiet"], out _, out diagnostic).ShouldBeTrue(diagnostic);
                string ownedInfo = Path.Combine(ownedRepository, ".git", "objects", "info");
                Directory.CreateDirectory(ownedInfo);
                File.WriteAllText(Path.Combine(ownedInfo, "alternates"), rootObjects + "\n");
                TryGitValue(ownedRepository, ["update-ref", "HEAD", otherRevision], out _, out diagnostic).ShouldBeTrue(diagnostic);
                objectSource = Path.Combine(temporary.FullName, "linked-worktree");
                TryGitValue(ownedRepository, ["worktree", "add", "--detach", "--no-checkout", objectSource, otherRevision],
                    out _, out diagnostic).ShouldBeTrue(diagnostic);
            }

            TryGitValue(objectSource, ["rev-parse", "--path-format=absolute", "--git-path", "objects"], out string actualObjects, out diagnostic).ShouldBeTrue(diagnostic);
            string consumed = Path.Combine(temporary.FullName, "consumed");
            Directory.CreateDirectory(consumed);
            TryGitValue(consumed, ["init", "--quiet"], out _, out diagnostic).ShouldBeTrue(diagnostic);
            string info = Path.Combine(consumed, ".git", "objects", "info");
            Directory.CreateDirectory(info);
            File.WriteAllText(Path.Combine(info, "alternates"), actualObjects + "\n");
            TryGitValue(consumed, ["update-ref", "HEAD", otherRevision], out _, out diagnostic).ShouldBeTrue(diagnostic);
            foreach (string name in new[] { "Hexalith.EventStore.Contracts", "Hexalith.EventStore.DomainService" })
            {
                string project = Path.Combine(consumed, "src", name, name + ".csproj");
                Directory.CreateDirectory(Path.GetDirectoryName(project)!);
                File.WriteAllText(project, "<Project />");
            }

            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HexalithEventStoreRoot"] = consumed,
            };
            string selectedRoot = EffectiveEventStoreSourceRoots(root, overrides).ShouldHaveSingleItem();
            selectedRoot.ShouldBe(consumed);
            File.WriteAllText(Path.Combine(consumed, "src", "characterization-probe.cs"), "effective consumed source");
            ReadEventStoreSource(selectedRoot, "characterization-probe.cs").ShouldBe("effective consumed source");
            string[] violations = CurrentSourceRevisionViolations(root, overrides);
            violations.ShouldContain(violation => violation.Contains(consumed, StringComparison.Ordinal)
                && violation.Contains(otherRevision, StringComparison.Ordinal)
                && violation.Contains("differs from root gitlink", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    private static bool TryGitlinkRevision(string entry, out string revision, out string diagnostic)
    {
        Match match = Regex.Match(entry, @"^160000 commit ([0-9a-f]{40})\treferences/Hexalith\.EventStore$");
        if (!match.Success)
        {
            revision = string.Empty;
            diagnostic = "Root gitlink 'references/Hexalith.EventStore' is missing or malformed: expected exactly "
                + $"'160000 commit <40-character SHA>\treferences/Hexalith.EventStore', received '{entry}'.";
            return false;
        }

        revision = match.Groups[1].Value;
        diagnostic = string.Empty;
        return true;
    }

    private static string[] RevisionBindingViolations(string api, string boundary, string gitlink, string checkout)
    {
        var violations = new List<string>();
        foreach ((string name, string text) in new[] { ("API surface", api), ("boundary record", boundary) })
        {
            MatchCollection markers = Regex.Matches(text, @"(?m)^<!-- EventStore-current-source: ([0-9a-f]{40}) -->\r?$");
            if (markers.Count != 1)
            {
                violations.Add($"{name} must contain exactly one valid EventStore-current-source marker; found {markers.Count}.");
            }
            else if (markers[0].Groups[1].Value != gitlink)
            {
                violations.Add($"{name} current-source marker differs from EventStore gitlink '{gitlink}'.");
            }
        }

        if (!Regex.IsMatch(gitlink, "^[0-9a-f]{40}$") || checkout != gitlink)
        {
            violations.Add($"Consumed EventStore checkout '{checkout}' differs from root gitlink '{gitlink}'.");
        }

        return [.. violations];
    }

    private static bool TryGitValue(string directory, string[] arguments, out string output, out string diagnostic)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git revision inspection.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new InvalidOperationException("Git revision inspection timed out after 15 seconds.");
            }

            output = standardOutput.GetAwaiter().GetResult().Trim();
            diagnostic = $"git {string.Join(' ', arguments)} in '{directory}' exited {process.ExitCode}: {standardError.GetAwaiter().GetResult().Trim()}";
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            output = string.Empty;
            diagnostic = $"Cannot inspect git revision in '{directory}': {exception.Message}";
            return false;
        }
    }

    private static string ReadEventStoreSource(string eventStoreRoot, params string[] relativeSegments)
    {
        string path = Path.Combine([eventStoreRoot, "src", .. relativeSegments]);
        File.Exists(path).ShouldBeTrue($"Expected EventStore source '{path}' to exist; the API-surface contract Works depends on may have moved or been renamed upstream.");

        return File.ReadAllText(path);
    }
}
