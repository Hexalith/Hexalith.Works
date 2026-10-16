using System.Text;
using System.Text.RegularExpressions;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.DependencyInjection;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Retains bounded, credential-redacted resource diagnostics for fixture-local 503 investigation.</summary>
internal static partial class WorksSdkProofDiagnostics
{
    public static async Task CaptureAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is not { Length: > 0 } path) return;
        var output = new StringBuilder();
        output.AppendLine("Ordinary-command 503 resource diagnostics at " + DateTimeOffset.UtcNow.ToString("O"));
        ResourceLoggerService logs = app.Services.GetRequiredService<ResourceLoggerService>();
        foreach (string resource in new[] { "eventstore", "eventstore-dapr", "works", "works-dapr" })
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(2));
            var lines = new List<LogLine>();
            string id = app.ResourceNotifications.TryGetCurrentState(resource, out ResourceEvent? current)
                ? current.ResourceId : resource;
            output.AppendLine(resource + " state=" + current?.Snapshot.State?.Text);
            try
            {
                await foreach (IReadOnlyList<LogLine> batch in logs.GetAllAsync(id).WithCancellation(budget.Token).ConfigureAwait(false))
                    lines.AddRange(batch);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            foreach (LogLine line in lines.TakeLast(200))
                output.AppendLine(CredentialPattern().Replace(line.Content, "[redacted]"));
        }
        await File.AppendAllTextAsync(path + "-admission.log", output.ToString(), cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"(?i)(?:bearer\s+\S+|[A-Za-z0-9+/=_-]{80,})")]
    private static partial Regex CredentialPattern();
}
