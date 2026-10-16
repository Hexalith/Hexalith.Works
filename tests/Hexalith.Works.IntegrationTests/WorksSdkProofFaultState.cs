namespace Hexalith.Works.IntegrationTests;

/// <summary>Fixture-owned one-shot transport loss after a real durable SDK target receipt.</summary>
/// <param name="FailNext">Whether the next successful effect response is lost.</param>
/// <param name="CommittedEffectId">The actual target receipt whose response was lost.</param>
internal sealed record WorksSdkProofFaultState(bool FailNext, string? CommittedEffectId = null)
{
    /// <summary>Optionally limits the one-shot loss to a particular immutable effect kind.</summary>
    public string? EffectKind { get; init; }

    /// <summary>Holds only synthetic child-completion delegation after the SDK plan is durable.</summary>
    public bool HoldDelegation { get; init; }

    /// <summary>Actual lost receipt outcome, distinguishing no-op from a state-changing target event.</summary>
    public Hexalith.EventStore.Contracts.Effects.TrustedEffectDisposition? Disposition { get; init; }

    /// <summary>Whether the real gateway returned the retained receipt on replay.</summary>
    public bool ReplayObserved { get; init; }

    /// <summary>The exact immutable request retained for the compatible-fleet rollback replay, without credentials.</summary>
    public Hexalith.EventStore.Contracts.Effects.TrustedEffectSubmission? Submission { get; init; }

    /// <summary>The committed source message used as the original replay causation.</summary>
    public string? CausationId { get; init; }
}
