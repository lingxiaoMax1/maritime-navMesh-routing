using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.Core.Routing;

public static class RoutePlanner
{
    public static RouteResult Search(CsrOceanGraph graph, RouteRequest request) =>
        request.Algorithm switch
        {
            RouteAlgorithm.Dijkstra => Dijkstra.Search(graph, request),
            RouteAlgorithm.BidirectionalAStar => BidirectionalAStar.Search(graph, request),
            _ => Dijkstra.Search(graph, request),
        };
}
