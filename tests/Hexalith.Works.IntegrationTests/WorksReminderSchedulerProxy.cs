using System.Reflection;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Adapts the SDK's internal scheduler interface to observable test scheduler operations.</summary>
public class WorksReminderSchedulerProxy : DispatchProxy
{
    private readonly object _gate = new();

    /// <summary>Gets the names currently retained by the synthetic scheduler.</summary>
    internal HashSet<string> Held { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets every accepted arm operation and its relative delay.</summary>
    internal List<(string Name, TimeSpan DueTime)> Arms { get; } = [];

    /// <summary>Gets or sets the action performed after an accepted arm operation.</summary>
    internal Action? AfterArm { get; set; }

    /// <summary>Gets or sets the action performed before a scheduler lookup completes.</summary>
    internal Action? BeforeLookup { get; set; }

    /// <summary>Gets or sets the failure selected before an arm operation mutates scheduler state.</summary>
    internal Func<string, Exception?>? ArmFailure { get; set; }

    /// <summary>Gets or sets the failure returned before a cancellation releases scheduler state.</summary>
    internal Exception? CancelFailure { get; set; }

    /// <summary>Gets or sets a failure returned after cancellation removed the external registration.</summary>
    internal Exception? CancelResponseFailure { get; set; }

    /// <summary>Gets the number of successful cancellations whose responses were lost.</summary>
    internal int LostCancellationResponses { get; private set; }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        ArgumentNullException.ThrowIfNull(args);
        string name = (string)args[0]!;
        switch (targetMethod.Name)
        {
            case "ArmAsync":
                if (ArmFailure?.Invoke(name) is { } armFailure) return Task.FromException(armFailure);
                lock (_gate)
                {
                    Arms.Add((name, (TimeSpan)args[1]!));
                    _ = Held.Add(name);
                }

                AfterArm?.Invoke();
                return Task.CompletedTask;
            case "IsArmedAsync":
                BeforeLookup?.Invoke();
                lock (_gate)
                {
                    return Task.FromResult(Held.Contains(name));
                }

            case "CancelAsync":
                if (CancelFailure is { } cancelFailure) return Task.FromException(cancelFailure);
                bool removed;
                lock (_gate)
                {
                    removed = Held.Remove(name);
                    if (CancelResponseFailure is not null && removed) LostCancellationResponses++;
                }

                if (CancelResponseFailure is { } responseFailure) return Task.FromException(responseFailure);
                return Task.CompletedTask;
            default:
                throw new NotSupportedException(targetMethod.Name);
        }
    }
}
