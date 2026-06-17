using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Models;

/// <summary>
/// The smallest contract-level read model that exposes a work item's current executor binding as data,
/// so future read surfaces (for example a single Party chip) can render any executor uniformly. It
/// carries only the data Works owns — the work item identity plus <see cref="PartyId"/>,
/// <see cref="Channel"/>, and <see cref="AuthorityLevel"/> through <see cref="ExecutorBinding"/>.
/// <para>
/// There is deliberately no executor-kind discriminator and no display name, party profile, contact
/// detail, presentation colour, or adapter metadata: "bot / person / external" is resolved outside the
/// Works kernel from Party identity or adapter-side data. <see cref="ExecutorBinding"/> is nullable
/// because a work item may have no executor bound yet. The tenant "what's next" queue projection that
/// would populate this view at scale is owned by Story 4.4.
/// </para>
/// </summary>
public sealed record WorkItemExecutorBindingView(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ExecutorBinding? ExecutorBinding);
