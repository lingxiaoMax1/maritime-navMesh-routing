using MaritimeNavMesh.Core.Routing;
using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.Tests.Fixtures;

namespace MaritimeNavMesh.Tests.Core;

public sealed class DijkstraTests
{
    private readonly global::MaritimeNavMesh.Core.Graph.CsrOceanGraph _graph = SyntheticGraphBuilder.Build();

    [Fact]
    public void Route_ShortestPath_0_to_3_IsVia_1()
    {
        // Path via 0→1→3 has cost 2.0, path via 0→2→3 has cost 3.0
        var result = Dijkstra.Search(_graph, new RouteRequest { FromNodeIndex = 0, ToNodeIndex = 3 });

        Assert.Equal(RouteStatus.Success, result.Status);
        Assert.Equal(2.0, result.TotalCost, precision: 6);
        double expectedDistance =
            GeoMath.HaversineNm(0, 0, 1, 1) +
            GeoMath.HaversineNm(1, 1, 0, 2);
        Assert.Equal(expectedDistance, result.TotalDistanceNm, precision: 6);
        Assert.Equal(new[] { 0, 1, 3 }, result.PathNodes);
    }

    [Fact]
    public void Route_SameNode_ReturnsSuccess_NoCost()
    {
        var result = Dijkstra.Search(_graph, new RouteRequest { FromNodeIndex = 2, ToNodeIndex = 2 });
        Assert.Equal(RouteStatus.Success, result.Status);
        Assert.Equal(0.0, result.TotalCost);
        Assert.Equal(new[] { 2 }, result.PathNodes);
        Assert.Empty(result.PathEdges!);
    }

    [Fact]
    public void Route_InvalidFromNode_ReturnsInvalidNode()
    {
        var result = Dijkstra.Search(_graph, new RouteRequest { FromNodeIndex = -1, ToNodeIndex = 0 });
        Assert.Equal(RouteStatus.InvalidNode, result.Status);
    }

    [Fact]
    public void Route_InvalidToNode_ReturnsInvalidNode()
    {
        var result = Dijkstra.Search(_graph, new RouteRequest { FromNodeIndex = 0, ToNodeIndex = 999 });
        Assert.Equal(RouteStatus.InvalidNode, result.Status);
    }

    [Fact]
    public void Route_0_to_1_DirectEdge()
    {
        var result = Dijkstra.Search(_graph, new RouteRequest { FromNodeIndex = 0, ToNodeIndex = 1 });
        Assert.Equal(RouteStatus.Success, result.Status);
        Assert.Equal(new[] { 0, 1 }, result.PathNodes);
        Assert.Equal(1.0, result.TotalCost, precision: 6);
    }

    [Fact]
    public void Route_3_to_0_Directed_NoPath()
    {
        // Graph is directed: node 3 has no outgoing edges, so 3→0 has no path
        var result = Dijkstra.Search(_graph, new RouteRequest { FromNodeIndex = 3, ToNodeIndex = 0 });
        Assert.Equal(RouteStatus.NoPath, result.Status);
    }

    [Fact]
    public void Route_PathNodes_StartAndEnd_Correct()
    {
        var result = Dijkstra.Search(_graph, new RouteRequest { FromNodeIndex = 0, ToNodeIndex = 3 });
        Assert.Equal(RouteStatus.Success, result.Status);
        Assert.Equal(0, result.PathNodes![0]);
        Assert.Equal(3, result.PathNodes[^1]);
    }

    [Fact]
    public void Route_VisitedCount_IsPositive()
    {
        var result = Dijkstra.Search(_graph, new RouteRequest { FromNodeIndex = 0, ToNodeIndex = 3 });
        Assert.True(result.VisitedNodes > 0);
    }
}
