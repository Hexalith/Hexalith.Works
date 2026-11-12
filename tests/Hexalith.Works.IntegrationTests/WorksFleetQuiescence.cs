using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.Platform.Composition;

using Microsoft.Extensions.DependencyInjection;

using StackExchange.Redis;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Test owner proof for the complete same-machine stopped fleet, never a timeout or copied observation.</summary>
internal static class WorksFleetQuiescence
{
    internal const string OwnerName = "quiescence-owner.json";
    internal const string ProofName = "stopped-fleet.json";
    internal const string OwnerEnvironment = "WORKS_SDK_PROOF_QUIESCENCE_OWNER";

    internal static string Boot() => File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
    internal static string PidNamespace() => new FileInfo("/proc/self/ns/pid").LinkTarget
        ?? throw new InvalidOperationException("The fixture cannot bind its actual PID namespace.");

    internal static Dictionary<int, WorksFleetProcess> Processes()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("This stopped-fleet fixture requires its qualified Linux PID namespace.");
        var result = new Dictionary<int, WorksFleetProcess>();
        foreach (string entry in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)) continue;
            try
            {
                string stat = File.ReadAllText(Path.Combine(entry, "stat"));
                string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                result[pid] = new(pid, ulong.Parse(fields[19], CultureInfo.InvariantCulture), int.Parse(fields[1], CultureInfo.InvariantCulture),
                    int.Parse(fields[2], CultureInfo.InvariantCulture), int.Parse(fields[3], CultureInfo.InvariantCulture), fields[0]);
            }
            catch (IOException)
            {
                if (!Disappeared(() => File.GetAttributes(entry)))
                    throw new InvalidOperationException("A process remains present but cannot be independently inspected.");
            }
            catch (UnauthorizedAccessException) { throw new InvalidOperationException("The fixture cannot independently inspect its process namespace."); }
        }
        return result;
    }

    internal static bool Disappeared(Func<FileAttributes> inspect)
    {
        try { _ = inspect(); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    internal static WorksFleetProcess[] ClosedProcesses(IReadOnlyList<int> roots, IReadOnlyDictionary<int, WorksFleetProcess> current, int ownerPid)
    {
        if (!current.TryGetValue(ownerPid, out var owner) || roots.Count == 0 || roots.Distinct().Count() != roots.Count
            || roots.Any(pid => !current.TryGetValue(pid, out var process) || process.State is "Z" or "X"))
            throw new InvalidOperationException("Every selected runtime and sidecar must have an independently observed live PID.");
        int[] rootSessions = roots.Select(pid => current[pid].Session).Distinct().ToArray();
        if (rootSessions.Length != 1 || rootSessions[0] == owner.Session
            || !current.TryGetValue(rootSessions[0], out var leader) || leader.Pid != leader.Group || leader.Pid != leader.Session
            || leader.State is "Z" or "X")
            throw new InvalidOperationException("The actual AppHost roots must bind one observed dedicated non-owner DCP session.");
        // The public model resource snapshots bind actual DCP ProcessIds through this
        // AppHost's private kubeconfig. DCP intentionally daemonizes; current PPID is not
        // that ownership contract. Include its WHOLE isolated session, not only resources.
        var sessions = rootSessions.ToHashSet();
        var owned = current.Values.Where(process => sessions.Contains(process.Session)).Select(process => process.Pid).ToHashSet();
        bool added;
        do
        {
            added = false;
            foreach (var process in current.Values)
                if ((owned.Contains(process.Parent) || sessions.Contains(process.Session)) && owned.Add(process.Pid)) added = true;
            foreach (int session in owned.Select(pid => current[pid].Session).Distinct().ToArray())
                if (sessions.Add(session)) added = true;
        } while (added);
        if (owned.Contains(ownerPid) || sessions.Contains(owner.Session) || owned.Any(pid => current[pid].Group == owner.Group))
            throw new InvalidOperationException("The fixture cannot claim its owner's inherited process session or group.");
        return owned.Select(pid => current[pid]).OrderBy(process => process.Pid).ToArray();
    }

    internal static void RequireStopped(WorksFleetScope scope, string boot, string pidNamespace, IReadOnlyDictionary<int, WorksFleetProcess> current)
    {
        if (scope.Boot != boot || scope.PidNamespace != pidNamespace || scope.Processes.Length == 0 || scope.Groups.Length == 0
            || scope.Processes.Any(old => current.TryGetValue(old.Pid, out var process) && process.Started == old.Started && process.State is not ("Z" or "X"))
            || current.Values.Any(process => (scope.Groups.Contains(process.Group) || scope.Processes.Any(old => old.Session == process.Session)) && process.State is not ("Z" or "X")))
            throw new RouteCatalogUnavailableException("Actual same-namespace old runtime and sidecar process groups are not quiescent. Remaining="
                + string.Join(',', current.Values.Where(process => (scope.Groups.Contains(process.Group)
                    || scope.Processes.Any(old => old.Session == process.Session)) && process.State is not ("Z" or "X"))
                    .Select(process => $"{process.Pid}/{process.Parent}/{process.Group}/{process.Session}/{process.State}")));
    }

    internal static async Task WaitStoppedAsync(WorksFleetScope scope, string boot, string pidNamespace,
        Func<IReadOnlyDictionary<int, WorksFleetProcess>> observe, CancellationToken token)
    {
        if (scope.Boot != boot || scope.PidNamespace != pidNamespace)
            throw new RouteCatalogUnavailableException("The owner proof cannot cross kernel or PID namespace boundaries.");
        RouteCatalogUnavailableException? lastRefusal = null;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var current = observe(); // Unknown IO/access failures must never become disappearance.
                try { RequireStopped(scope, boot, pidNamespace, current); return; }
                catch (RouteCatalogUnavailableException refusal)
                {
                    lastRefusal = refusal;
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && lastRefusal is not null)
        {
            // Preserve the last independently observed PID/group/session/state refusal; cancellation is never proof.
            throw new OperationCanceledException("Stopped-fleet observation ended before actual disappearance. "
                + lastRefusal.Message, lastRefusal, token);
        }
    }

    internal static async Task<WorksFleetOwner> ReadOwnerAsync(string path, CancellationToken token)
    {
        WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(path)!);
        var owner = JsonSerializer.Deserialize<WorksFleetOwner>(await WorksProofCallerProtocol.ReadAsync(path, token).ConfigureAwait(false))!;
        if (!Guid.TryParseExact(owner.OwnerId, "N", out _) || Convert.FromBase64String(owner.SigningKey).Length != 32
            || !Path.IsPathFullyQualified(owner.ProfilePath) || Path.GetDirectoryName(owner.ProfilePath) != Path.GetDirectoryName(path))
            throw new InvalidOperationException("The isolated fixture owner binding is invalid.");
        return owner;
    }

    internal static async Task<WorksFleetScope> CaptureAsync(DistributedApplication app, string ownerPath, CancellationToken token)
    {
        var owner = await ReadOwnerAsync(ownerPath, token).ConfigureAwait(false);
        var profile = JsonSerializer.Deserialize<WorksDevelopmentCatalogProfile>(await WorksProofCallerProtocol.ReadAsync(owner.ProfilePath, token).ConfigureAwait(false))!;
        string[] hosts = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ProjectResource>()
            .Where(resource => resource.Name is "eventstore" or "works" or "works-ha" or "eventstore-admin" or "eventstore-operations").Select(resource => resource.Name).Order(StringComparer.Ordinal).ToArray();
        var roots = new List<int>();
        var snapshots = new Dictionary<string, (int Pid, DateTime? Started)>();
        foreach (var project in app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ProjectResource>().Where(project => hosts.Contains(project.Name)))
        {
            string sidecar = project.Annotations.OfType<CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarAnnotation>().Single().Sidecar.Name + "-cli";
            foreach (string resource in new[] { project.Name, sidecar })
            {
                if (!app.ResourceNotifications.TryGetCurrentState(resource, out var actual)) throw new InvalidOperationException("A selected physical fixture resource has no runtime PID snapshot.");
                // Aspire.Hosting 13.6.4 (c8935270): its internal KnownProperties.Executable.Pid
                // names this property on the public CustomResourceSnapshot.Properties API.
                int pid = ResourcePid(actual);
                roots.Add(pid);
                snapshots.Add(resource, (pid, actual.Snapshot.StartTimeStamp));
            }
        }
        var processes = ClosedProcesses(roots, Processes(), Environment.ProcessId);
        using var redis = await ConnectAsync(owner, token).ConfigureAwait(false);
        var state = await ReadStateAsync(redis, profile, token).ConfigureAwait(false);
        var committed = state.Committed ?? throw new InvalidOperationException("The actual fixture has no committed catalog.");
        if (state.Pending is not null || !committed.RequiredHosts.Select(host => host.HostId).Order(StringComparer.Ordinal).SequenceEqual(hosts, StringComparer.Ordinal))
            throw new InvalidOperationException("The complete independently running fixture does not match its actual committed roster.");
        foreach (var entry in snapshots)
            if (!app.ResourceNotifications.TryGetCurrentState(entry.Key, out var actual) || ResourcePid(actual) != entry.Value.Pid
                || actual.Snapshot.StartTimeStamp != entry.Value.Started)
                throw new InvalidOperationException("An actual selected AppHost resource changed during fleet capture.");
        var rechecked = Processes();
        if (roots.Any(pid => !rechecked.TryGetValue(pid, out var actual) || actual.State is "Z" or "X"
            || actual.Started != processes.Single(process => process.Pid == pid).Started
            || actual.Session != processes.Single(process => process.Pid == pid).Session))
            throw new InvalidOperationException("An actual resource PID/start/session changed while capturing its owned DCP fleet.");
        return new(Boot(), PidNamespace(), committed.RootDigest, state.Epoch, RouteCatalogActivationBinding.ComputeDigest(committed), hosts,
            processes, processes.Select(process => process.Group).Distinct().Order().ToArray());
    }

    private static int ResourcePid(ResourceEvent actual)
    {
        object? value = actual.Snapshot.Properties.Where(property => property.Name == "executable.pid").Select(property => property.Value).SingleOrDefault();
        return value is JsonElement json ? json.GetInt32() : Convert.ToInt32(value
            ?? throw new InvalidOperationException("A physical fixture PID is absent."), CultureInfo.InvariantCulture);
    }

    internal static async Task CompleteAsync(WorksFleetScope scope, string ownerPath, CancellationToken token)
    {
        var owner = await ReadOwnerAsync(ownerPath, token).ConfigureAwait(false);
        var profile = JsonSerializer.Deserialize<WorksDevelopmentCatalogProfile>(await WorksProofCallerProtocol.ReadAsync(owner.ProfilePath, token).ConfigureAwait(false))!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await WaitStoppedAsync(scope, Boot(), PidNamespace(), Processes, deadline.Token).ConfigureAwait(false);
        using var redis = await ConnectAsync(owner, deadline.Token).ConfigureAwait(false);
        var server = redis.GetServer(redis.GetEndPoints().Single());
        while ((await server.ClientListAsync().WaitAsync(deadline.Token).ConfigureAwait(false)).Any(client => client.Name != owner.OwnerId))
            await Task.Delay(50, deadline.Token).ConfigureAwait(false);
        var state = await ReadStateAsync(redis, profile, deadline.Token).ConfigureAwait(false);
        if (state.Pending is not null || state.Committed?.RootDigest != scope.Root || state.Epoch != scope.Epoch
            || RouteCatalogActivationBinding.ComputeDigest(state.Committed) != scope.Binding
            || state.Leases.Any(fence => fence.RootDigest != scope.Root || fence.Epoch != scope.Epoch || !scope.Hosts.Contains(fence.HostId, StringComparer.Ordinal)))
            throw new InvalidOperationException("The actual stopped backend does not match the independently observed old fleet and fences.");
        string current = Path.Combine(profile.Catalog.CustodyDirectory, "current.json");
        using var retained = JsonDocument.Parse(await WorksProofCallerProtocol.ReadAsync(current, token).ConfigureAwait(false));
        string generation = retained.RootElement.GetProperty("generation").GetString()!;
        byte[] backend = await WorksProofCallerProtocol.ReadAsync(Path.Combine(profile.Catalog.CustodyDirectory, generation, "authority.backend.json"), token).ConfigureAwait(false);
        var document = new WorksStoppedFleetDocument(Guid.NewGuid().ToString("N"), owner.OwnerId, scope, state.Leases.ToArray(), Convert.ToHexStringLower(SHA256.HashData(backend)), "");
        RequireStopped(scope, Boot(), PidNamespace(), Processes());
        document = document with { Signature = Sign(owner, document) };
        string path = Path.Combine(Path.GetDirectoryName(ownerPath)!, ProofName), fresh = path + ".next-" + document.Retirement;
        await WorksProofCallerProtocol.WriteNewAsync(fresh, document, token).ConfigureAwait(false);
        File.Move(fresh, path, overwrite: true);
    }

    internal static string Sign(WorksFleetOwner owner, WorksStoppedFleetDocument document)
        => Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(owner.SigningKey), JsonSerializer.SerializeToUtf8Bytes(document with { Signature = "" })));

    internal static void ValidateDocument(WorksFleetOwner owner, WorksStoppedFleetDocument document, RouteCatalogActivationState state)
    {
        var scope = document.Scope;
        if (!Guid.TryParseExact(document.Retirement, "N", out _) || document.Owner != owner.OwnerId
            || document.Signature.Length != 64 || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(document.Signature), Convert.FromHexString(Sign(owner, document)))
            || state.Committed?.RootDigest != scope.Root || state.Epoch != scope.Epoch
            || RouteCatalogActivationBinding.ComputeDigest(state.Committed) != scope.Binding
            || !state.Committed.RequiredHosts.Select(host => host.HostId).Order(StringComparer.Ordinal).SequenceEqual(scope.Hosts, StringComparer.Ordinal)
            || document.Fences.Select(fence => fence.LeaseId).Distinct(StringComparer.Ordinal).Count() != document.Fences.Length
            || document.Fences.Any(fence => fence.RootDigest != scope.Root || fence.Epoch != scope.Epoch || !scope.Hosts.Contains(fence.HostId, StringComparer.Ordinal))
            // The original signed roster is immutable. Legitimate earlier reconciliations may
            // remove members; a new or replaced fence can never join that remaining subset.
            || state.Leases.Any(fence => !document.Fences.Contains(fence)))
            throw new RouteCatalogUnavailableException("The stopped-fleet proof does not bind this exact retained authority and remaining fence subset.");
    }

    internal static string FenceProof(WorksFleetOwner owner, WorksStoppedFleetDocument document, RouteCatalogDispatchFence fence)
    {
        if (!document.Fences.Contains(fence)) throw new RouteCatalogUnavailableException("The fence is not in the stopped owner's original roster.");
        return Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(owner.SigningKey),
            JsonSerializer.SerializeToUtf8Bytes(new { document.Retirement, document.Signature, Fence = fence })));
    }

    internal static async Task<ConnectionMultiplexer> ConnectAsync(WorksFleetOwner owner, CancellationToken token)
    {
        var options = ConfigurationOptions.Parse(owner.Backend); options.ClientName = owner.OwnerId; options.ConnectTimeout = 2000; options.AllowAdmin = true;
        return await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(token).ConfigureAwait(false);
    }

    internal static async Task<RouteCatalogActivationState> ReadStateAsync(ConnectionMultiplexer redis, WorksDevelopmentCatalogProfile profile, CancellationToken token)
    {
        RedisValue value = await redis.GetDatabase().HashGetAsync(profile.Catalog.AuthorityComponentName + "||" + profile.Catalog.AuthorityStateKey, "data").WaitAsync(token).ConfigureAwait(false);
        return value.HasValue ? JsonSerializer.Deserialize<RouteCatalogActivationState>((byte[])value!, JsonSerializerOptions.Web)!
            : throw new InvalidOperationException("The isolated actual authority cell is absent.");
    }
}
