namespace Hexalith.Works.Projections;

/// <summary>Signals invalid runtime queue membership to the Works query and recovery diagnostics.</summary>
internal sealed class WorksRuntimeIndexMembershipException()
    : InvalidOperationException("The selected Works runtime queue membership is invalid.");
