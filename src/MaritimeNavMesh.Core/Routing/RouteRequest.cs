namespace MaritimeNavMesh.Core.Routing;

public sealed class RouteRequest
{
    public int FromNodeIndex { get; init; }
    public int ToNodeIndex { get; init; }
    public RouteAlgorithm Algorithm { get; init; } = RouteAlgorithm.Dijkstra;
}
