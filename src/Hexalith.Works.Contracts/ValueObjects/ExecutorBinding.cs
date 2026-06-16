namespace Hexalith.Works.Contracts.ValueObjects;

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

        PartyId = partyId;
        Channel = channel;
        AuthorityLevel = authorityLevel;
    }

    public PartyId PartyId { get; }

    public Channel Channel { get; }

    public AuthorityLevel AuthorityLevel { get; }
}
