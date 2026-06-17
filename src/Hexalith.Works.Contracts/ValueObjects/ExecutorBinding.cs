namespace Hexalith.Works.Contracts.ValueObjects;

/// <summary>
/// The single, uniform way Works represents whoever performs work: a stable <see cref="PartyId"/>
/// reference, an interaction <see cref="Channel"/>, and a carried <see cref="AuthorityLevel"/>. System
/// agents, internal users, and external parties all use this one shape and differ only by field values;
/// there is intentionally no executor-kind subtype or branch discriminator. <see cref="AuthorityLevel"/>
/// is carried, not enforced, in v1. Both <see cref="Channel.Unknown"/> and
/// <see cref="AuthorityLevel.Unknown"/> are deserialization sentinels only and are rejected here.
/// </summary>
public sealed record ExecutorBinding
{
    public ExecutorBinding(PartyId partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        ArgumentNullException.ThrowIfNull(partyId);
        if (channel == Channel.Unknown || !Enum.IsDefined(channel))
        {
            throw new ArgumentException(
                "Executor channel must be a known channel; Unknown is a deserialization sentinel only.",
                nameof(channel));
        }

        if (authorityLevel == AuthorityLevel.Unknown || !Enum.IsDefined(authorityLevel))
        {
            throw new ArgumentException(
                "Executor authority level must be a known level; Unknown is a deserialization sentinel only.",
                nameof(authorityLevel));
        }

        PartyId = partyId;
        Channel = channel;
        AuthorityLevel = authorityLevel;
    }

    public PartyId PartyId { get; }

    public Channel Channel { get; }

    public AuthorityLevel AuthorityLevel { get; }
}
