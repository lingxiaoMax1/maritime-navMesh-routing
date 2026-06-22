using MaritimeNavMesh.Api.Models;
using MaritimeNavMesh.Api.Services;
using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.Core.Models;
using MaritimeNavMesh.Core.Routing;
using Microsoft.Extensions.Options;

namespace MaritimeNavMesh.Api.Endpoints;

public static class RouteEndpoints
{
    public static void MapRouteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/route/by-locode", (string from, string to, string? geometryMode, string? algorithm, GraphService graphService, IOptions<GraphOptions> opts) =>
        {
            var rt = graphService.Runtime;
            var resolvedGeometryMode = ResolveGeometryMode(geometryMode, opts.Value.DefaultRouteGeometryMode);
            var resolvedAlgorithm = ResolveRouteAlgorithm(algorithm, opts.Value.DefaultRouteAlgorithm);
            if (!rt.PortsByLocode.TryGetValue(from.ToUpperInvariant(), out var fromPort))
            {
                return Results.Ok(new RouteResponse(
                    Success: false, ErrorCode: "PORT_NOT_FOUND",
                    ErrorMessage: $"From-port not found or not in graph: {from}",
                    TotalDistanceNm: null, TotalCost: null, NodeCount: null, SearchTimeMs: null, Geometry: null));
            }

            if (!rt.PortsByLocode.TryGetValue(to.ToUpperInvariant(), out var toPort))
            {
                return Results.Ok(new RouteResponse(
                    Success: false, ErrorCode: "PORT_NOT_FOUND",
                    ErrorMessage: $"To-port not found or not in graph: {to}",
                    TotalDistanceNm: null, TotalCost: null, NodeCount: null, SearchTimeMs: null, Geometry: null));
            }

            int fromNode = rt.SnapPortToNode(from);
            int toNode = rt.SnapPortToNode(to);
            if (fromNode < 0)
            {
                return Results.Ok(new RouteResponse(
                    Success: false, ErrorCode: "PORT_NOT_FOUND",
                    ErrorMessage: $"From-port not found or not in graph: {from}",
                    TotalDistanceNm: null, TotalCost: null, NodeCount: null, SearchTimeMs: null, Geometry: null));
            }

            if (toNode < 0)
            {
                return Results.Ok(new RouteResponse(
                    Success: false, ErrorCode: "PORT_NOT_FOUND",
                    ErrorMessage: $"To-port not found or not in graph: {to}",
                    TotalDistanceNm: null, TotalCost: null, NodeCount: null, SearchTimeMs: null, Geometry: null));
            }

            return ExecuteNamedPortRoute(
                rt.Graph,
                fromNode,
                toNode,
                fromPort,
                toPort,
                resolvedAlgorithm,
                resolvedGeometryMode,
                graphService.LandMask,
                graphService.EdgePortals,
                graphService.AisCorridorHints);
        })
        .WithName("RouteByLocode")
        .WithTags("Route")
        .Produces<RouteResponse>();

        app.MapGet("/api/route/by-coordinate", (
            double fromLat, double fromLon,
            double toLat, double toLon,
            double? maxSnapDistNm,
            string? geometryMode,
            string? algorithm,
            GraphService graphService,
            IOptions<GraphOptions> opts) =>
        {
            var rt = graphService.Runtime;
            double maxDist = maxSnapDistNm ?? opts.Value.DefaultMaxSnapDistanceNm;
            var resolvedGeometryMode = ResolveGeometryMode(geometryMode, opts.Value.DefaultRouteGeometryMode);
            var resolvedAlgorithm = ResolveRouteAlgorithm(algorithm, opts.Value.DefaultRouteAlgorithm);

            int fromNode = rt.SnapCoordinateToNode(fromLat, fromLon, maxDist);
            int toNode = rt.SnapCoordinateToNode(toLat, toLon, maxDist);

            if (fromNode < 0)
                return Results.Ok(new RouteResponse(
                    Success: false, ErrorCode: "SNAP_FAILED",
                    ErrorMessage: $"No graph node within {maxDist} nm of from-coordinate",
                    TotalDistanceNm: null, TotalCost: null, NodeCount: null, SearchTimeMs: null, Geometry: null));
            if (toNode < 0)
                return Results.Ok(new RouteResponse(
                    Success: false, ErrorCode: "SNAP_FAILED",
                    ErrorMessage: $"No graph node within {maxDist} nm of to-coordinate",
                    TotalDistanceNm: null, TotalCost: null, NodeCount: null, SearchTimeMs: null, Geometry: null));

            return ExecuteRoute(
                rt.Graph,
                fromNode,
                toNode,
                rawStartLonLat: [fromLon, fromLat],
                rawEndLonLat: [toLon, toLat],
                algorithm: resolvedAlgorithm,
                mode: resolvedGeometryMode,
                landMask: graphService.LandMask,
                edgePortals: graphService.EdgePortals,
                aisCorridorHints: graphService.AisCorridorHints);
        })
        .WithName("RouteByCoordinate")
        .WithTags("Route")
        .Produces<RouteResponse>();
    }

    private static IResult ExecuteNamedPortRoute(
        Core.Graph.CsrOceanGraph graph,
        int fromNode,
        int toNode,
        PortSnap fromPort,
        PortSnap toPort,
        RouteAlgorithm algorithm,
        RouteGeometryMode mode,
        RasterLandMask? landMask,
        EdgePortalSet? edgePortals,
        AisCorridorHintSet? aisCorridorHints)
    {
        var result = RoutePlanner.Search(graph, new RouteRequest
        {
            FromNodeIndex = fromNode,
            ToNodeIndex = toNode,
            Algorithm = algorithm,
        });

        if (result.Status != RouteStatus.Success)
        {
            return Results.Ok(new RouteResponse(
                Success: false,
                ErrorCode: result.Status.ToString().ToUpperInvariant(),
                ErrorMessage: result.FailureReason,
                TotalDistanceNm: null,
                TotalCost: null,
                NodeCount: null,
                SearchTimeMs: null,
                Geometry: null));
        }

        var geometry = RouteGeometryBuilder.BuildGraphGeometry(
            graph,
            result.PathNodes!,
            mode,
            landMask,
            edgePortals,
            fromPort.Locode,
            toPort.Locode,
            aisCorridorHints);
        var segments = RouteGeometryBuilder.BuildSegmentsWithPortAccessPoints(
            geometry.FinalGraphPoints,
            fromPort.MarineAccessPathCoordinates,
            toPort.MarineAccessPathCoordinates)
            .Select(seg => seg.ToArray())
            .ToArray();
        var geoJson = GeoJsonFeatureCollection.FromCoordinates(segments);
        var rawGraphGeoJson = GeoJsonFeatureCollection.FromCoordinates(
            RouteGeometryBuilder.BuildSegments(graph, result.PathNodes!)
                .Select(seg => seg.ToArray()).ToArray());

        return Results.Ok(new RouteResponse(
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            TotalDistanceNm: result.TotalDistanceNm,
            TotalCost: result.TotalCost,
            NodeCount: result.PathNodes!.Length,
            SearchTimeMs: result.ElapsedMs,
            Geometry: geoJson)
        {
            GeometryModeRequested = geometry.Diagnostics.GeometryModeRequested,
            GeometryModeUsed = geometry.Diagnostics.GeometryModeUsed,
            GeometryDiagnostics = geometry.Diagnostics,
            RawGraphGeometry = rawGraphGeoJson,
        });
    }

    private static IResult ExecuteRoute(
        Core.Graph.CsrOceanGraph graph,
        int fromNode,
        int toNode,
        double[]? rawStartLonLat = null,
        double[]? rawEndLonLat = null,
        RouteAlgorithm algorithm = RouteAlgorithm.BidirectionalAStar,
        RouteGeometryMode mode = RouteGeometryMode.Raw,
        RasterLandMask? landMask = null,
        EdgePortalSet? edgePortals = null,
        AisCorridorHintSet? aisCorridorHints = null)
    {
        var result = RoutePlanner.Search(graph, new RouteRequest
        {
            FromNodeIndex = fromNode,
            ToNodeIndex = toNode,
            Algorithm = algorithm,
        });

        if (result.Status != RouteStatus.Success)
        {
            return Results.Ok(new RouteResponse(
                Success: false,
                ErrorCode: result.Status.ToString().ToUpperInvariant(),
                ErrorMessage: result.FailureReason,
                TotalDistanceNm: null,
                TotalCost: null,
                NodeCount: null,
                SearchTimeMs: null,
                Geometry: null));
        }

        var geometry = RouteGeometryBuilder.BuildGraphGeometry(
            graph,
            result.PathNodes!,
            mode,
            landMask,
            edgePortals,
            aisCorridorHints: aisCorridorHints);
        var segments = RouteGeometryBuilder.BuildSegmentsWithEndpointPoints(
            geometry.FinalGraphPoints,
            rawStartLonLat,
            rawEndLonLat)
            .Select(seg => seg.ToArray())
            .ToArray();
        var geoJson = GeoJsonFeatureCollection.FromCoordinates(segments);
        var rawGraphGeoJson = GeoJsonFeatureCollection.FromCoordinates(
            RouteGeometryBuilder.BuildSegments(graph, result.PathNodes!)
                .Select(seg => seg.ToArray()).ToArray());

        return Results.Ok(new RouteResponse(
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            TotalDistanceNm: result.TotalDistanceNm,
            TotalCost: result.TotalCost,
            NodeCount: result.PathNodes!.Length,
            SearchTimeMs: result.ElapsedMs,
            Geometry: geoJson)
        {
            GeometryModeRequested = geometry.Diagnostics.GeometryModeRequested,
            GeometryModeUsed = geometry.Diagnostics.GeometryModeUsed,
            GeometryDiagnostics = geometry.Diagnostics,
            RawGraphGeometry = rawGraphGeoJson,
        });
    }

    private static RouteGeometryMode ResolveGeometryMode(string? requested, string? configuredDefault)
    {
        if (TryParseGeometryMode(requested, out var explicitMode))
            return explicitMode;
        if (TryParseGeometryMode(configuredDefault, out var defaultMode))
            return defaultMode;
        return RouteGeometryMode.Funnel;
    }

    private static bool TryParseGeometryMode(string? value, out RouteGeometryMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = RouteGeometryMode.Raw;
            return false;
        }
        return Enum.TryParse(value, ignoreCase: true, out mode);
    }

    private static RouteAlgorithm ResolveRouteAlgorithm(string? requested, string? configuredDefault)
    {
        if (TryParseRouteAlgorithm(requested, out var explicitAlgorithm))
            return explicitAlgorithm;
        if (TryParseRouteAlgorithm(configuredDefault, out var defaultAlgorithm))
            return defaultAlgorithm;
        return RouteAlgorithm.BidirectionalAStar;
    }

    private static bool TryParseRouteAlgorithm(string? value, out RouteAlgorithm algorithm)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            algorithm = RouteAlgorithm.Dijkstra;
            return false;
        }
        return Enum.TryParse(value.Replace("-", string.Empty), ignoreCase: true, out algorithm);
    }
}
