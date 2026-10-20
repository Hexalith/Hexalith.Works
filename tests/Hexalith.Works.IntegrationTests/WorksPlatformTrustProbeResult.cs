namespace Hexalith.Works.IntegrationTests;

/// <summary>One mutual-TLS probe handshake against an owned sidecar endpoint.</summary>
/// <param name="ServerIdentity">The single SPIFFE identity the server presented, when one was observed.</param>
/// <param name="ClientRejectedServer">Whether the server remains untrusted because configured-root and identity validation refused it or no server certificate was validated.</param>
/// <param name="ServerAccepted">Whether the server completed HTTP/2 setup by sending its SETTINGS frame.</param>
/// <param name="Failure">The observed refusal, or <see langword="null"/> when the server accepted the probe.</param>
internal sealed record WorksPlatformTrustProbeResult(string? ServerIdentity, bool ClientRejectedServer, bool ServerAccepted, string? Failure);
