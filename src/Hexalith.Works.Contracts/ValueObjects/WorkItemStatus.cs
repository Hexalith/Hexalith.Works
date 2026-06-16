using System.Text.Json.Serialization;

namespace Hexalith.Works.Contracts.ValueObjects;

/// <summary>
/// The nine-state lifecycle of a work item, serialized by name. Integer values are explicit and
/// stable (additive only — never renumber an existing member) and exist purely so persisted ordinals
/// stay meaningful; the wire format is the member name via <see cref="JsonStringEnumConverter{T}"/>.
/// <see cref="Unknown"/> is a pre-creation sentinel only: a lifecycle command handled against
/// <see cref="Unknown"/> (or a null state) is treated as "not created" and rejected.
/// The single source of truth for legal/illegal/idempotent transitions across these statuses is the
/// pure transition table in the server kernel, mirrored 1:1 by docs/lifecycle-transition-matrix.md.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkItemStatus>))]
public enum WorkItemStatus
{
    Unknown = 0,
    Created = 1,
    Assigned = 2,
    Queued = 3,
    InProgress = 4,
    Suspended = 5,
    Completed = 6,
    Cancelled = 7,
    Rejected = 8,
    Expired = 9,
}
