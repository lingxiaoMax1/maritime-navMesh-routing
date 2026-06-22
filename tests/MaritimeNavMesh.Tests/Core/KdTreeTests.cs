using MaritimeNavMesh.Core.Indices;
using MaritimeNavMesh.Tests.Fixtures;

namespace MaritimeNavMesh.Tests.Core;

public sealed class KdTreeTests
{
    private readonly global::MaritimeNavMesh.Core.Graph.CsrOceanGraph _graph = SyntheticGraphBuilder.Build();
    private readonly KdTree2D _tree;

    public KdTreeTests()
    {
        _tree = new KdTree2D(_graph);
    }

    [Fact]
    public void QueryNearest_ExactMatch_ReturnsZeroDistance()
    {
        // Node 0 is at lat=0, lon=0
        var results = _tree.QueryNearest(_graph, 0.0, 0.0, maxK: 1, maxDistNm: 100);
        Assert.Single(results);
        Assert.Equal(0, results[0].NodeIndex);
        Assert.Equal(0.0, results[0].SnapDistanceNm, precision: 3);
    }

    [Fact]
    public void QueryNearest_NearNode1_ReturnsNode1First()
    {
        // Node 1 is at lat=1, lon=1 — query very close to it
        var results = _tree.QueryNearest(_graph, 1.01, 1.01, maxK: 1, maxDistNm: 10);
        Assert.Single(results);
        Assert.Equal(1, results[0].NodeIndex);
    }

    [Fact]
    public void QueryNearest_MaxK2_ReturnsTwoResults()
    {
        var results = _tree.QueryNearest(_graph, 0.0, 0.0, maxK: 2, maxDistNm: 500);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void QueryNearest_MaxDistTooSmall_ReturnsEmpty()
    {
        // 0.001 nm is far too small to capture any synthetic node (nearest is at lat=0, lon=0)
        var results = _tree.QueryNearest(_graph, 50.0, 50.0, maxK: 5, maxDistNm: 0.001);
        Assert.Empty(results);
    }

    [Fact]
    public void QueryNearest_AllNodes_ReturnedSortedByDistance()
    {
        var results = _tree.QueryNearest(_graph, 0.0, 0.0, maxK: 4, maxDistNm: 1000);
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i].SnapDistanceNm >= results[i - 1].SnapDistanceNm,
                "Results should be sorted by ascending distance");
    }
}
