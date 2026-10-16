using Hexalith.EventStore.Contracts.Effects;

namespace Hexalith.Works.Recovery;

/// <summary>Named, closed delegated Works effect purposes; no caller can select a more privileged purpose.</summary>
internal static class WorksEffectPurposes
{
    /// <summary>The attested Works effect workload.</summary>
    internal const string Workload = "works";

    /// <summary>Maps an immutable effect family to its independently authorized purpose.</summary>
    internal static string ForKind(string kind) => kind switch
    {
        EffectKindCatalog.DateResume => "works.date-resume",
        EffectKindCatalog.Expiry => "works.expiry",
        EffectKindCatalog.CascadeCancel => "works.cascade-cancel",
        EffectKindCatalog.CascadeExpire => "works.cascade-expire",
        EffectKindCatalog.ChildCompletionResume => "works.child-completion-resume",
        _ => throw new InvalidOperationException("Works effect family is not authorized."),
    };
}
