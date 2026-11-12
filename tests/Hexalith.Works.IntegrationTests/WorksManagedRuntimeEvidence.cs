using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Observes loaded managed assemblies in the exact qualification host and its owned resources.</summary>
internal static class WorksManagedRuntimeEvidence
{
    internal static bool IsRuntimeResource(string resource)
        => resource is "works" or "works-ha" or "eventstore" or "eventstore-operations" or "eventstore-admin";

    internal static JsonElement Capture(DistributedApplication app, IEnumerable<ProjectResource> projects, string configuration)
    {
        var resources = projects.Where(project => IsRuntimeResource(project.Name)).OrderBy(project => project.Name, StringComparer.Ordinal).Select(project =>
        {
            string resource = project.Name;
            if (!app.ResourceNotifications.TryGetCurrentState(resource, out ResourceEvent? current))
                throw new InvalidOperationException($"Managed runtime evidence requires the actual {resource} process snapshot.");
            int launcher = ResourcePid(current);
            string entry = Path.Combine(WorksPlatformRuntimeArtifacts.DirectoryFor(project.GetProjectMetadata().ProjectPath, configuration),
                Path.GetFileNameWithoutExtension(project.GetProjectMetadata().ProjectPath) + ".dll");
            int pid = ResolveServicePid(launcher, entry);
            JsonElement observed = CaptureProcess(pid);
            if (ResolveServicePid(launcher, entry) != pid)
                throw new InvalidOperationException($"The {resource} entry process changed during managed runtime capture.");
            // Recheck DCP's identity after reading the live mappings; never attribute an old process to a restarted resource.
            if (!app.ResourceNotifications.TryGetCurrentState(resource, out ResourceEvent? after)
                || ResourcePid(after) != launcher || after.Snapshot.StartTimeStamp != current.Snapshot.StartTimeStamp)
                throw new InvalidOperationException($"The {resource} process changed during managed runtime capture.");
            return new
            {
                resource,
                pid,
                coreVersion = observed.GetProperty("coreVersion").GetString(),
                coreLib = observed.GetProperty("coreLib"),
                json = observed.GetProperty("json"),
            };
        }).ToArray();
        if (resources.Select(resource => resource.pid).Distinct().Count() != resources.Length)
            throw new InvalidOperationException("Qualified services must have distinct actual managed process identities.");
        return JsonSerializer.SerializeToElement(new
        {
            schema = "hexalith.works.managed-runtime.v1",
            host = CaptureHost(),
            resources,
        });
    }

    internal static JsonElement CaptureHost()
        => ProcessRecord(Environment.ProcessId, typeof(object).Assembly.Location, typeof(JsonSerializer).Assembly.Location);

    internal static JsonElement CaptureProcess(int pid)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Canonical managed runtime qualification requires Linux process mappings.");
        if (pid <= 0) throw new InvalidOperationException("Managed runtime evidence requires a positive actual resource PID.");
        return CaptureMappings(pid, File.ReadAllLines($"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/maps"));
    }

    internal static int ResolveServicePid(int launcher, string entry)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Canonical managed runtime qualification requires Linux process mappings.");
        if (launcher <= 0 || !Path.IsPathFullyQualified(entry) || !File.Exists(entry))
            throw new InvalidOperationException("An owned launcher and an exact existing runtime entry are required.");
        var pending = new Queue<int>();
        var seen = new HashSet<int>();
        var matching = new List<int>();
        pending.Enqueue(launcher);
        while (pending.TryDequeue(out int pid))
        {
            if (!seen.Add(pid)) continue;
            if (seen.Count > 128) throw new InvalidOperationException("The owned service launcher tree exceeds the qualification bound.");
            try
            {
                string[] mappings = File.ReadAllLines($"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/maps");
                if (MappedPaths(mappings).Contains(entry, StringComparer.Ordinal)) matching.Add(pid);
                string children = File.ReadAllText($"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/task/{pid.ToString(CultureInfo.InvariantCulture)}/children");
                foreach (string child in children.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    pending.Enqueue(int.Parse(child, CultureInfo.InvariantCulture));
            }
            catch (IOException) when (pid != launcher)
            {
                // A child that exited during this read cannot supply a live service observation.
            }
        }
        if (matching.Count != 1 || matching[0] == Environment.ProcessId)
            throw new InvalidOperationException("Exactly one owned service process must map the selected entry assembly.");
        return matching[0];
    }

    private static string[] MappedPaths(IEnumerable<string> mappings)
        => mappings.Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length == 6).Select(fields => fields[5]).Distinct(StringComparer.Ordinal).ToArray();

    internal static JsonElement CaptureMappings(int pid, IEnumerable<string> mappings)
    {
        string[] paths = MappedPaths(mappings);
        string Require(string name)
        {
            string[] found = paths.Where(path => Path.GetFileName(path) == name).ToArray();
            if (found.Length != 1 || !Path.IsPathFullyQualified(found[0]) || !File.Exists(found[0]))
                throw new InvalidOperationException($"PID {pid} must have exactly one readable loaded {name} mapping.");
            return found[0];
        }
        return ProcessRecord(pid, Require("System.Private.CoreLib.dll"), Require("System.Text.Json.dll"));
    }

    private static JsonElement ProcessRecord(int pid, string core, string json)
    {
        object FileRecord(string path) => new { path, sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
        Version coreVersion = AssemblyName.GetAssemblyName(core).Version
            ?? throw new InvalidOperationException("The loaded CoreLib has no assembly version.");
        return JsonSerializer.SerializeToElement(new
        {
            pid,
            coreVersion = coreVersion.ToString(3),
            coreLib = FileRecord(core),
            json = FileRecord(json),
        });
    }

    private static int ResourcePid(ResourceEvent current)
    {
        object? value = current.Snapshot.Properties.Where(property => property.Name == "executable.pid")
            .Select(property => property.Value).SingleOrDefault();
        int pid = value is JsonElement json ? json.GetInt32() : Convert.ToInt32(value
            ?? throw new InvalidOperationException("The selected physical resource has no actual PID."), CultureInfo.InvariantCulture);
        return pid > 0 ? pid : throw new InvalidOperationException("The selected physical resource PID is invalid.");
    }
}
