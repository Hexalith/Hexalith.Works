namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record ExecutorBinding
{
    public ExecutorBinding(string executorId, AuthorityLevel authorityLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executorId);
        ExecutorId = executorId;
        AuthorityLevel = authorityLevel;
    }

    public string ExecutorId { get; }

    public AuthorityLevel AuthorityLevel { get; }
}
