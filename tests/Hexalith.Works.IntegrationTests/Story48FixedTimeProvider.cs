namespace Hexalith.Works.IntegrationTests;

/// <summary>A fixed clock for Story 4.8 deterministic tests.</summary>
internal sealed class Story48FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
