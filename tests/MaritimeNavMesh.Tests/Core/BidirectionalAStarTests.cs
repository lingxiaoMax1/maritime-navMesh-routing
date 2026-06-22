using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.Core.Routing;
using MaritimeNavMesh.Tests.Fixtures;

namespace MaritimeNavMesh.Tests.Core;

public sealed class BidirectionalAStarTests
{
    private readonly global::MaritimeNavMesh.Core.Graph.CsrOceanGraph _graph = SyntheticGraphBuilder.Build();

    [Fact]
    public void Route_ShortestPath_0_to_3_IsVia_1()
    {
        var result = BidirectionalAStar.Search(_graph, new RouteRequest
        {
            FromNodeIndex = 0,
            ToNodeIndex = 3,
            Algorithm = RouteAlgorithm.BidirectionalAStar,
        });

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
        var result = BidirectionalAStar.Search(_graph, new RouteRequest
        {
            FromNodeIndex = 2,
            ToNodeIndex = 2,
            Algorithm = RouteAlgorithm.BidirectionalAStar,
        });
        Assert.Equal(RouteStatus.Success, result.Status);
        Assert.Equal(0.0, result.TotalCost);
        Assert.Equal(new[] { 2 }, result.PathNodes);
        Assert.Empty(result.PathEdges!);
    }

    [Fact]
    public void Route_InvalidFromNode_ReturnsInvalidNode()
    {
        var result = BidirectionalAStar.Search(_graph, new RouteRequest
        {
            FromNodeIndex = -1,
            ToNodeIndex = 0,
            Algorithm = RouteAlgorithm.BidirectionalAStar,
        });
        Assert.Equal(RouteStatus.InvalidNode, result.Status);
    }

    [Fact]
    public void Route_InvalidToNode_ReturnsInvalidNode()
    {
        var result = BidirectionalAStar.Search(_graph, new RouteRequest
        {
            FromNodeIndex = 0,
            ToNodeIndex = 999,
            Algorithm = RouteAlgorithm.BidirectionalAStar,
        });
        Assert.Equal(RouteStatus.InvalidNode, result.Status);
    }

    [Fact]
    public void Route_3_to_0_Directed_NoPath()
    {
        var result = BidirectionalAStar.Search(_graph, new RouteRequest
        {
            FromNodeIndex = 3,
            ToNodeIndex = 0,
            Algorithm = RouteAlgorithm.BidirectionalAStar,
        });
        Assert.Equal(RouteStatus.NoPath, result.Status);
    }

    [Fact]
    public void Route_Matches_Dijkstra_OnSyntheticGraph()
    {
        var request = new RouteRequest
        {
            FromNodeIndex = 0,
            ToNodeIndex = 3,
            Algorithm = RouteAlgorithm.BidirectionalAStar,
        };
        var bidi = BidirectionalAStar.Search(_graph, request);
        var dijkstra = Dijkstra.Search(_graph, new RouteRequest
        {
            FromNodeIndex = request.FromNodeIndex,
            ToNodeIndex = request.ToNodeIndex,
            Algorithm = RouteAlgorithm.Dijkstra,
        });

        Assert.Equal(dijkstra.Status, bidi.Status);
        Assert.Equal(dijkstra.TotalCost, bidi.TotalCost, precision: 6);
        Assert.Equal(dijkstra.TotalDistanceNm, bidi.TotalDistanceNm, precision: 6);
        Assert.Equal(dijkstra.PathNodes, bidi.PathNodes);
        Assert.Equal(dijkstra.PathEdges, bidi.PathEdges);
    }
}
