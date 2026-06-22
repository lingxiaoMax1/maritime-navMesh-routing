using MaritimeNavMesh.Core.Models;
using MaritimeNavMesh.Core.Runtime;
using MaritimeNavMesh.Tests.Fixtures;

namespace MaritimeNavMesh.Tests.Core;

public sealed class GraphRuntimeTests
{
    [Fact]
    public void SnapPortToNode_PrefersMarineAccessNode()
    {
        var port = new PortSnap
        {
            Locode = "TESTA",
            Name = "Port A",
            SnappedH3Hex = "85754e67fffffff",
            SnappedLat = 0f,
            SnappedLon = 0f,
            MarineAccessH3Hex = "857541affffffff",
            MarineAccessLat = 1,
            MarineAccessLon = 1,
            SnapDistanceNm = 0,
            ComponentId = 0,
        };
        var runtime = new GraphRuntime(SyntheticGraphBuilder.Build(), [port]);

        Assert.Equal(1, runtime.SnapPortToNode("TESTA"));
        Assert.Empty(runtime.StartupWarnings);
    }

    [Fact]
    public void SnapPortToNode_FallsBackToSnappedNode()
    {
        var port = new PortSnap
        {
            Locode = "TESTA",
            Name = "Port A",
            SnappedH3Hex = "85754e67fffffff",
            SnappedLat = 0f,
            SnappedLon = 0f,
            SnapDistanceNm = 0,
            ComponentId = 0,
        };
        var runtime = new GraphRuntime(SyntheticGraphBuilder.Build(), [port]);

        Assert.Equal(0, runtime.SnapPortToNode("TESTA"));
    }
}
