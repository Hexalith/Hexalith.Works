using Aspire.Hosting.ApplicationModel;

namespace Hexalith.Works.IntegrationTests;

internal sealed class BrokenCredential : IValueProvider
{
    public ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromException<string?>(new InvalidOperationException("Credential unavailable."));
}
