using System.Reflection;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Adapts the SDK's internal scheduler interface to observable test scheduler operations.</summary>
public class WorksReminderSchedulerProxy : DispatchProxy
{
    /// <summary>Gets the names currently retained by the synthetic scheduler.</summary>
    internal HashSet<string> Held { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets every accepted arm operation and its relative delay.</summary>
    internal List<(string Name, TimeSpan DueTime)> Arms { get; } = [];

    /// <summary>Gets or sets the action performed after an accepted arm operation.</summary>
    internal Action? AfterArm { get; set; }

    /// <summary>Gets or sets the action performed before a scheduler lookup completes.</summary>
    internal Action? BeforeLookup { get; set; }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        ArgumentNullException.ThrowIfNull(args);
        string name = (string)args[0]!;
        switch (targetMethod.Name)
        {
            case "ArmAsync":
                Arms.Add((name, (TimeSpan)args[1]!));
                _ = Held.Add(name);
                AfterArm?.Invoke();
                return Task.CompletedTask;
            case "IsArmedAsync":
                BeforeLookup?.Invoke();
                return Task.FromResult(Held.Contains(name));
            case "CancelAsync":
                _ = Held.Remove(name);
                return Task.CompletedTask;
            default:
                throw new NotSupportedException(targetMethod.Name);
        }
    }
}
