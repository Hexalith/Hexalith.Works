namespace Hexalith.Works.IntegrationTests;

internal sealed record WorksFleetScope(string Boot, string PidNamespace, string Root, long Epoch, string Binding,
    string[] Hosts, WorksFleetProcess[] Processes, int[] Groups);
