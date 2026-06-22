using MaritimeNavMesh.Api.Models;
using MaritimeNavMesh.Api.Services;
using Microsoft.Extensions.Options;

namespace MaritimeNavMesh.Api.Endpoints;

public static class GraphEndpoints
{
    public static void MapGraphEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/graph/stats", (GraphService graphService) =>
        {
            var rt = graphService.Runtime;
            return Results.Ok(new GraphStatsResponse(
                NodeCount: rt.Graph.NodeCount,
                EdgeCount: rt.Graph.EdgeCount,
                Resolution: rt.Graph.Resolution,
                ComponentCount: rt.ComponentIndex.Stats.Count,
                PortCount: rt.AllPorts.Count));
        })
        .WithName("GraphStats")
        .WithTags("Graph")
        .Produces<GraphStatsResponse>();

        app.MapGet("/api/graph/snap", (double lat, double lon, double? maxDistNm, GraphService graphService, IOptions<GraphOptions> options) =>
        {
            double maxDist = maxDistNm ?? options.Value.DefaultMaxSnapDistanceNm;
            var results = graphService.Runtime.KdTree.QueryNearest(
                graphService.Runtime.Graph, lat, lon, maxK: 5, maxDistNm: maxDist);

            if (results.Count == 0)
                return Results.NotFound(new { Error = "No nodes found within snap distance" });

            var responses = results.Select(r => new NearestNodeResponse(
                NodeIndex: r.NodeIndex,
                Lat: r.NodeLat,
                Lon: r.NodeLon,
                SnapDistanceNm: r.SnapDistanceNm,
                ComponentId: r.ComponentId,
                NodeClass: r.NodeClass)).ToArray();

            return Results.Ok(responses);
        })
        .WithName("SnapCoordinate")
        .WithTags("Graph")
        .Produces<NearestNodeResponse[]>()
        .ProducesProblem(404);
    }
}
