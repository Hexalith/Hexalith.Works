namespace Hexalith.Works.IntegrationTests;

/// <summary>Maps only its own deadline to the existing advisory metadata failure path.</summary>
internal sealed class WorksSdkProofMetadataHandler : DelegatingHandler
{
    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return await base.SendAsync(request, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new HttpRequestException("Synthetic fixture advisory metadata probe exceeded two seconds.", exception);
        }
    }
}
