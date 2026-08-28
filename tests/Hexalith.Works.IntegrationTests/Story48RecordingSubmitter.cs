using Hexalith.Works.Runtime;

namespace Hexalith.Works.IntegrationTests;

/// <summary>A recording <see cref="IWorkCommandSubmitter"/> for Story 4.8 deterministic tests.</summary>
internal sealed class Story48RecordingSubmitter : IWorkCommandSubmitter
{
    public List<WorkCommandSubmission> Submissions { get; } = [];

    public Task SubmitAsync(WorkCommandSubmission submission, CancellationToken cancellationToken = default)
    {
        Submissions.Add(submission);
        return Task.CompletedTask;
    }
}
