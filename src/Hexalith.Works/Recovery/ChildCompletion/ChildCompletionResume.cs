using System.Text.Json;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Runtime;

namespace Hexalith.Works.Recovery.ChildCompletion;

/// <summary>
/// Builds a deterministic gateway submission from a pure child-completion resume intent.
/// </summary>
internal static class ChildCompletionResume
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the submission while leaving acceptance to the target aggregate's Handle method.</summary>
    internal static WorkCommandSubmission BuildSubmission(WorkItemCompleted childCompleted, ResumeWorkItem resume)
    {
        ArgumentNullException.ThrowIfNull(childCompleted);
        ArgumentNullException.ThrowIfNull(resume);

        string id = $"child-completion-resume-{resume.TenantId.Value}-{resume.WorkItemId.Value}-{childCompleted.WorkItemId.Value}-{childCompleted.Sequence}";
        return new WorkCommandSubmission(
            resume.TenantId.Value,
            resume.WorkItemId.Value,
            nameof(ResumeWorkItem),
            JsonSerializer.SerializeToElement(resume, s_web),
            id,
            id);
    }
}
