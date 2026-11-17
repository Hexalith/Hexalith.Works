using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Reads only the independently frozen actual Works deployment for retained owner metadata.</summary>
internal sealed class WorksQualifiedRuntimeContext : AssemblyLoadContext, IDisposable
{
    internal const string ManifestVariable = "WORKS_PLATFORM_QUALIFIED_RUNTIME_MANIFEST";
    private readonly string _manifest;
    private readonly string _manifestHash;
    private readonly string _directory;
    private readonly string _entry;
    private readonly Dictionary<string, string> _files;
    private readonly Dictionary<string, string> _sources;
    private readonly Dictionary<Assembly, string> _shared = [];
    private readonly Dictionary<Assembly, string> _loaded = [];

    internal WorksQualifiedRuntimeContext(string manifest, string project, string directory) : base(isCollectible: true)
    {
        _manifest = Path.GetFullPath(manifest); _directory = Path.GetFullPath(directory);
        _entry = Path.Combine(_directory, "Hexalith.Works.dll");
        byte[] bytes = WorksManifestReader.Read(_manifest); _manifestHash = Digest(bytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        string[] keys = ["schema", "projectPath", "entryPath", "entrySha256", "files", "sourceFiles"];
        if (!root.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal).SequenceEqual(keys.Order(StringComparer.Ordinal))
            || root.GetProperty("schema").GetString() != "hexalith.works-qualified-runtime.v1"
            || root.GetProperty("projectPath").GetString() != Path.GetFullPath(project) || root.GetProperty("entryPath").GetString() != _entry)
            throw new InvalidOperationException("The retained metadata requires its exact qualified Works runtime context.");
        _files = root.GetProperty("files").EnumerateObject().ToDictionary(file => file.Name, file => file.Value.GetString()!, StringComparer.Ordinal);
        _sources = root.GetProperty("sourceFiles").EnumerateObject().ToDictionary(file => file.Name, file => file.Value.GetString()!, StringComparer.Ordinal);
        if (!_sources.ContainsKey(Path.GetFullPath(project)) || !_files.TryGetValue(_entry, out string? entryHash)
            || entryHash != root.GetProperty("entrySha256").GetString()
            || _files.Keys.Any(path => !Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path || !path.StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new InvalidOperationException("The qualified Works runtime inventory is incomplete or foreign.");
        Verify();
    }

    internal Assembly ReadOwner()
    {
        Verify();
        foreach (Assembly assembly in Default.Assemblies.Where(assembly => assembly.GetName().Name?.StartsWith("Hexalith.EventStore.", StringComparison.Ordinal) == true)
            .Concat([typeof(System.Text.Json.JsonSerializerOptions).Assembly, typeof(System.Text.Encodings.Web.JavaScriptEncoder).Assembly,
                typeof(System.IO.Pipelines.PipeReader).Assembly]).Distinct())
        {
            string path = Path.Combine(_directory, assembly.GetName().Name + ".dll");
            bool coreContract = IsJsonContract(assembly.GetName().Name) && !File.Exists(path)
                && Path.GetDirectoryName(assembly.Location) == Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (!coreContract && (!Matches(path) || LoadedDigest(assembly) != _files[path]))
                throw new InvalidOperationException("The qualified Works SDK differs from the actual loaded public SDK.");
            _shared[assembly] = LoadedDigest(assembly);
        }
        Assembly owner = LoadFromAssemblyPath(_entry);
        if (owner.GetName().Name != "Hexalith.Works" || LoadedDigest(owner) != _files[_entry]) throw new InvalidOperationException("The qualified Works owner image differs from its frozen body.");
        _loaded.Add(owner, _files[_entry]);
        Verify(); return owner;
    }

    internal bool Matches(string path) => _files.TryGetValue(path, out string? hash) && Ordinary(path) && Digest(File.ReadAllBytes(path)) == hash;
    internal void Verify()
    {
        if (!Ordinary(_manifest) || Digest(WorksManifestReader.Read(_manifest)) != _manifestHash
            || _files.Any(file => !Matches(file.Key)) || _sources.Any(file => !Ordinary(file.Key) || Digest(File.ReadAllBytes(file.Key)) != file.Value)
            || !Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.Ordinal).SetEquals(_files.Keys)
            || _shared.Any(shared => LoadedDigest(shared.Key) != shared.Value)
            || _loaded.Any(loaded => LoadedDigest(loaded.Key) != loaded.Value))
            throw new InvalidOperationException("The qualified Works runtime, source or metadata SDK changed.");
    }

    internal void ObserveStarts(IDistributedApplicationBuilder builder, ProjectResource project)
        => builder.Eventing.Subscribe<BeforeResourceStartedEvent>((startup, _) =>
        { if (ReferenceEquals(startup.Resource, project)) Verify(); return Task.CompletedTask; });

    protected override Assembly? Load(AssemblyName name)
    {
        Verify();
        string path = Path.Combine(_directory, name.Name + ".dll");
        if (name.Name?.StartsWith("Hexalith.EventStore.", StringComparison.Ordinal) == true || IsJsonContract(name.Name))
        {
            Assembly shared = Default.LoadFromAssemblyName(name);
            string digest = LoadedDigest(shared);
            bool coreContract = IsJsonContract(name.Name) && !File.Exists(path)
                && Path.GetDirectoryName(shared.Location) == Path.GetDirectoryName(typeof(object).Assembly.Location)
                && _shared.TryGetValue(shared, out string? captured) && captured == digest;
            if (shared.FullName != name.FullName || (!coreContract && (!Matches(path) || _files[path] != digest)))
                throw new InvalidOperationException("The retained metadata SDK load differs from its exact frozen runtime body.");
            _shared.TryAdd(shared, digest); return shared;
        }
        if (File.Exists(path))
        {
            if (!Matches(path) || AssemblyName.GetAssemblyName(path).FullName != name.FullName)
                throw new InvalidOperationException("The retained metadata dependency is outside its frozen runtime body.");
            Assembly loaded = LoadFromAssemblyPath(path);
            if (LoadedDigest(loaded) != _files[path])
                throw new InvalidOperationException("The retained metadata dependency file differs from its loaded image.");
            _loaded.TryAdd(loaded, _files[path]); Verify(); return loaded;
        }
        Assembly framework = Default.LoadFromAssemblyName(name);
        string core = Path.GetDirectoryName(typeof(object).Assembly.Location)! + Path.DirectorySeparatorChar;
        string aspnet = Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location)! + Path.DirectorySeparatorChar;
        if (string.IsNullOrEmpty(framework.Location) || !(framework.Location.StartsWith(core, StringComparison.Ordinal)
            || framework.Location.StartsWith(aspnet, StringComparison.Ordinal)))
            throw new InvalidOperationException("The retained metadata dependency is outside its qualified shared framework.");
        return framework;
    }

    private static string LoadedDigest(Assembly assembly)
    {
        byte[] bytes = File.ReadAllBytes(assembly.Location);
        using var stream = new MemoryStream(bytes, writable: false); using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != assembly.ManifestModule.ModuleVersionId)
            throw new InvalidOperationException("The retained metadata file differs from its actual loaded module.");
        return Digest(bytes);
    }
    private static bool IsJsonContract(string? name) => name is "System.Text.Json" or "System.Text.Encodings.Web" or "System.IO.Pipelines";
    private static bool Ordinary(string path) => File.Exists(path) && new FileInfo(path).LinkTarget is null;
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public void Dispose()
    {
        try { Verify(); }
        finally { Unload(); }
    }
}
