using MaritimeNavMesh.Api.Models;
using MaritimeNavMesh.Api.Services;

namespace MaritimeNavMesh.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", (GraphService graphService) =>
        {
            if (!graphService.IsLoaded)
                return Results.Json(new { Status = "loading", Message = "Graph not yet ready" }, statusCode: 503);

            var rt = graphService.Runtime;
            return Results.Ok(new HealthResponse(
                Status: "ok",
                Message: "Graph loaded and ready",
                NodeCount: rt.Graph.NodeCount,
                EdgeCount: rt.Graph.EdgeCount,
                PortCount: rt.AllPorts.Count));
        })
        .WithName("Health")
        .WithTags("Health")
        .Produces<HealthResponse>()
        .ProducesProblem(503);
    }
}
