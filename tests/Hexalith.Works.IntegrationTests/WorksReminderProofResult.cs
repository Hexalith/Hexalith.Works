using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Routing;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Actual registrar outcomes bound to the same serving Works generation.</summary>
internal sealed record WorksReminderProofResult(ReminderConvergenceResult[] Outcomes, RouteCatalogGenerationReadback Generation);
