using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.Tests.Core;

public sealed class RouteGeometryBuilderTests
{
    [Fact]
    public void BuildSegments_SplitsWhenRouteCrossesAntiMeridian()
    {
        var graph = new CsrOceanGraph(
            resolution: 5,
            nodeCount: 2,
            edgeCount: 1,
            nodeH3Int: [1L, 2L],
            nodeLat: [0f, 0f],
            nodeLon: [179f, -179f],
            nodeComponent: [0, 0],
            nodeClass: [1, 1],
            rowPtr: [0u, 1u, 1u],
            colIdx: [1u],
            edgeCost: [120f],
            edgeMinDepthM: [100f],
            edgeFlags: [0]);

        var segments = RouteGeometryBuilder.BuildSegments(graph, [0, 1]);

        Assert.Equal(2, segments.Count);
        Assert.Single(segments[0]);
        Assert.Single(segments[1]);
        Assert.Equal(179d, segments[0][0][0]);
        Assert.Equal(-179d, segments[1][0][0]);
    }

    [Fact]
    public void BuildSegmentsWithEndpoints_PrependsAndAppendsRawEndpoints()
    {
        var graph = new CsrOceanGraph(
            resolution: 5,
            nodeCount: 2,
            edgeCount: 1,
            nodeH3Int: [1L, 2L],
            nodeLat: [1f, 2f],
            nodeLon: [100f, 101f],
            nodeComponent: [0, 0],
            nodeClass: [1, 1],
            rowPtr: [0u, 1u, 1u],
            colIdx: [1u],
            edgeCost: [10f],
            edgeMinDepthM: [100f],
            edgeFlags: [0]);

        var segments = RouteGeometryBuilder.BuildSegmentsWithEndpoints(
            graph,
            [0, 1],
            rawStartLonLat: [99.5, 0.5],
            rawEndLonLat: [101.5, 2.5]);

        Assert.Single(segments);
        var line = segments[0];
        Assert.Equal(4, line.Count);
        Assert.Equal(99.5d, line[0][0]);
        Assert.Equal(0.5d, line[0][1]);
        Assert.Equal(101.5d, line[^1][0]);
        Assert.Equal(2.5d, line[^1][1]);
    }

    [Fact]
    public void BuildSegments_AllWaterMask_RemovesH3ZigZag()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);

        var segments = RouteGeometryBuilder.BuildSegments(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Shortcut,
            mask);

        Assert.Single(segments);
        Assert.Equal(2, segments[0].Count);
        Assert.Equal(0d, segments[0][0][0]);
        Assert.Equal(0.03d, segments[0][^1][0], 6);
    }

    [Fact]
    public void BuildSegmentsWithPortAccessPaths_DoesNotSmoothAccessPaths()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);
        double[][] startAccess = [[-0.02, 0.0], [-0.01, 0.0], [0.0, 0.0]];
        double[][] endAccess = [[0.05, 0.0], [0.04, 0.0], [0.03, 0.0]];

        var segments = RouteGeometryBuilder.BuildSegmentsWithPortAccessPaths(
            graph, [0, 1, 2, 3], startAccess, endAccess, RouteGeometryMode.Shortcut, mask);

        Assert.Single(segments);
        Assert.Equal(6, segments[0].Count);
        Assert.Equal(-0.02d, segments[0][0][0]);
        Assert.Equal(-0.01d, segments[0][1][0]);
        Assert.Equal(0.04d, segments[0][^2][0]);
        Assert.Equal(0.05d, segments[0][^1][0]);
    }

    [Fact]
    public void BuildSegments_FunnelMode_UsesPortalsToRemoveIntermediateTurns()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);
        var portals = new EdgePortalSet(
            directedToPortalIndex: [0u, 1u, 2u],
            ax: [0.005f, 0.015f, 0.025f],
            ay: [-0.02f, -0.02f, -0.02f],
            bx: [0.005f, 0.015f, 0.025f],
            by: [0.02f, 0.02f, 0.02f]);

        var segments = RouteGeometryBuilder.BuildSegments(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Funnel,
            mask,
            portals);

        Assert.Single(segments);
        Assert.Equal(2, segments[0].Count);
        Assert.Equal(0d, segments[0][0][0]);
        Assert.Equal(0.03d, segments[0][^1][0], 6);
    }

    [Fact]
    public void BuildSegments_FunnelMode_FallsBackToShortcutWhenPortalMissing()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);
        var portals = new EdgePortalSet(
            directedToPortalIndex: [0u, uint.MaxValue, 2u],
            ax: [0.005f, 0.015f, 0.025f],
            ay: [-0.02f, -0.02f, -0.02f],
            bx: [0.005f, 0.015f, 0.025f],
            by: [0.02f, 0.02f, 0.02f]);

        var segments = RouteGeometryBuilder.BuildSegments(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Funnel,
            mask,
            portals);

        Assert.Single(segments);
        Assert.Equal(2, segments[0].Count);
        Assert.Equal(0d, segments[0][0][0]);
        Assert.Equal(0.03d, segments[0][^1][0], 6);
    }

    [Fact]
    public void BuildSegments_FunnelMode_PostProcessesResidualTurnWhenDirectSegmentIsSafe()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);
        var portals = new EdgePortalSet(
            directedToPortalIndex: [0u, 1u, 2u],
            ax: [0.005f, 0.015f, 0.025f],
            ay: [-0.02f, 0.015f, -0.02f],
            bx: [0.005f, 0.025f, 0.025f],
            by: [0.02f, 0.015f, 0.02f]);

        var segments = RouteGeometryBuilder.BuildSegments(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Funnel,
            mask,
            portals);

        Assert.Single(segments);
        Assert.Equal(2, segments[0].Count);
        Assert.Equal(0d, segments[0][0][0]);
        Assert.Equal(0.03d, segments[0][^1][0], 6);
    }

    [Fact]
    public void BuildGraphGeometry_FunnelMode_ReportsFallbackSectionDiagnostics()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);
        var portals = new EdgePortalSet(
            directedToPortalIndex: [0u, uint.MaxValue, 2u],
            ax: [0.005f, 0.015f, 0.025f],
            ay: [-0.02f, -0.02f, -0.02f],
            bx: [0.005f, 0.015f, 0.025f],
            by: [0.02f, 0.02f, 0.02f]);

        var geometry = RouteGeometryBuilder.BuildGraphGeometry(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Funnel,
            mask,
            portals);

        Assert.Equal("funnel", geometry.Diagnostics.GeometryModeRequested);
        Assert.Equal("funnel", geometry.Diagnostics.GeometryModeUsed);
        Assert.Equal(4, geometry.Diagnostics.RawPointCount);
        Assert.Equal(2, geometry.Diagnostics.FunnelPointCount);
        Assert.Equal(2, geometry.Diagnostics.FinalPointCount);
        Assert.Equal(1, geometry.Diagnostics.FallbackSectionCount);
        Assert.Equal(0, geometry.Diagnostics.AisShapedSectionCount);
        Assert.Single(geometry.Diagnostics.Sections);
        Assert.Equal("shortcut", geometry.Diagnostics.Sections[0].FinalMode);
        Assert.Equal("missing_portal", geometry.Diagnostics.Sections[0].Reason);
    }

    [Fact]
    public void BuildGraphGeometry_FunnelMode_WithoutPortals_UsesShortcutMode()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);

        var geometry = RouteGeometryBuilder.BuildGraphGeometry(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Funnel,
            mask,
            edgePortals: null);

        Assert.Equal("funnel", geometry.Diagnostics.GeometryModeRequested);
        Assert.Equal("shortcut", geometry.Diagnostics.GeometryModeUsed);
        Assert.Null(geometry.Diagnostics.FunnelPointCount);
        Assert.Equal(0, geometry.Diagnostics.FallbackSectionCount);
        Assert.Equal(0, geometry.Diagnostics.AisShapedSectionCount);
        Assert.Equal(2, geometry.Diagnostics.FinalPointCount);
    }

    [Fact]
    public void BuildGraphGeometry_FunnelMode_AppliesMatchingStartAisHint()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);
        var portals = new EdgePortalSet(
            directedToPortalIndex: [0u, 1u, 2u],
            ax: [0.005f, 0.015f, 0.025f],
            ay: [-0.02f, -0.02f, -0.02f],
            bx: [0.005f, 0.015f, 0.025f],
            by: [0.02f, 0.02f, 0.02f]);
        var hints = new AisCorridorHintSet([
            new AisCorridorHint(
                1,
                "AUMEL",
                1,
                1,
                0.9f,
                30,
                [
                    [0.0, 0.0],
                    [0.012, 0.004],
                    [0.02, -0.005],
                ],
                [
                    new AisCorridorEdgeSpan(0, 1),
                    new AisCorridorEdgeSpan(1, 2),
                ],
                0.0,
                -0.01,
                0.02,
                0.01)
        ]);

        var geometry = RouteGeometryBuilder.BuildGraphGeometry(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Funnel,
            mask,
            portals,
            startLocode: "AUMEL",
            endLocode: null,
            aisCorridorHints: hints);

        Assert.Equal("funnel", geometry.Diagnostics.GeometryModeUsed);
        Assert.Equal(1, geometry.Diagnostics.AisShapedSectionCount);
        Assert.True(geometry.FinalGraphPoints.Count >= 3);
        Assert.Equal(0.0, geometry.FinalGraphPoints[0][0], 6);
        Assert.Equal(0.03, geometry.FinalGraphPoints[^1][0], 6);
    }

    [Fact]
    public void BuildGraphGeometry_FunnelMode_DoesNotApplyMismatchedAisHint()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);
        var portals = new EdgePortalSet(
            directedToPortalIndex: [0u, 1u, 2u],
            ax: [0.005f, 0.015f, 0.025f],
            ay: [-0.02f, -0.02f, -0.02f],
            bx: [0.005f, 0.015f, 0.025f],
            by: [0.02f, 0.02f, 0.02f]);
        var hints = new AisCorridorHintSet([
            new AisCorridorHint(
                1,
                "AUMEL",
                1,
                1,
                0.9f,
                30,
                [
                    [0.0, 0.0],
                    [0.012, 0.004],
                    [0.02, -0.005],
                ],
                [
                    new AisCorridorEdgeSpan(1, 2),
                    new AisCorridorEdgeSpan(2, 3),
                ],
                0.0,
                -0.01,
                0.03,
                0.01)
        ]);

        var geometry = RouteGeometryBuilder.BuildGraphGeometry(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Funnel,
            mask,
            portals,
            startLocode: "AUMEL",
            endLocode: null,
            aisCorridorHints: hints);

        Assert.Equal(0, geometry.Diagnostics.AisShapedSectionCount);
        Assert.Equal(2, geometry.FinalGraphPoints.Count);
    }

    [Fact]
    public void BuildGraphGeometry_FunnelMode_AppliesMatchingGenericAisWindow()
    {
        var graph = BuildFourNodeGraph();
        var mask = RasterLandMaskTests.CreateMask([]);
        var portals = new EdgePortalSet(
            directedToPortalIndex: [0u, 1u, 2u],
            ax: [0.005f, 0.015f, 0.025f],
            ay: [-0.02f, -0.02f, -0.02f],
            bx: [0.005f, 0.015f, 0.025f],
            by: [0.02f, 0.02f, 0.02f]);
        var hints = new AisCorridorHintSet([
            new AisCorridorHint(
                100,
                "",
                AisCorridorHintSet.RouteWindowKind,
                AisCorridorHintSet.BidirectionalFlag,
                0.95f,
                40,
                [
                    [0.01, 0.005],
                    [0.018, 0.008],
                    [0.03, 0.0],
                ],
                [
                    new AisCorridorEdgeSpan(1, 2),
                    new AisCorridorEdgeSpan(2, 3),
                ],
                0.01,
                0.0,
                0.03,
                0.008)
        ]);

        var geometry = RouteGeometryBuilder.BuildGraphGeometry(
            graph,
            [0, 1, 2, 3],
            RouteGeometryMode.Funnel,
            mask,
            portals,
            aisCorridorHints: hints);

        Assert.Equal("funnel", geometry.Diagnostics.GeometryModeUsed);
        Assert.Equal(1, geometry.Diagnostics.AisShapedSectionCount);
        Assert.True(geometry.FinalGraphPoints.Count >= 3);
        Assert.Equal(0.0, geometry.FinalGraphPoints[0][0], 6);
        Assert.Equal(0.03, geometry.FinalGraphPoints[^1][0], 6);
    }

    private static CsrOceanGraph BuildFourNodeGraph() => new(
        resolution: 5,
        nodeCount: 4,
        edgeCount: 3,
        nodeH3Int: [1L, 2L, 3L, 4L],
        nodeLat: [0f, 0.005f, -0.005f, 0f],
        nodeLon: [0f, 0.01f, 0.02f, 0.03f],
        nodeComponent: [0, 0, 0, 0],
        nodeClass: [1, 1, 1, 1],
        rowPtr: [0u, 1u, 2u, 3u, 3u],
        colIdx: [1u, 2u, 3u],
        edgeCost: [1f, 1f, 1f],
        edgeMinDepthM: [100f, 100f, 100f],
        edgeFlags: [0, 0, 0]);
}
