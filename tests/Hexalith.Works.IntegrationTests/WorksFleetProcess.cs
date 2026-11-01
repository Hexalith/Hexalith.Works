namespace Hexalith.Works.IntegrationTests;

internal sealed record WorksFleetProcess(int Pid, ulong Started, int Parent, int Group, int Session, string State);
