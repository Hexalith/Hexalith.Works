using System.Reflection;
using System.Runtime.Loader;

/// <summary>Loads explicitly enabled synthetic proof hosting startup into isolated test child processes.</summary>
internal static class StartupHook
{
    /// <summary>Resolves proof-only dependencies from the test output; production processes never enable this hook.</summary>
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF") != "1")
            throw new InvalidOperationException("The synthetic Works proof hook requires explicit fixture enablement.");
        string folder = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)!;
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(folder, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
    }
}
