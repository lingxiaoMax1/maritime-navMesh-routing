using MaritimeNavMesh.Api.Models;
using MaritimeNavMesh.Api.Services;

namespace MaritimeNavMesh.Api.Endpoints;

public static class PortEndpoints
{
    public static void MapPortEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ports", (GraphService graphService) =>
        {
            var ports = graphService.Runtime.AllPorts
                .Select(p => new PortResponse(
                    Locode: p.Locode,
                    Name: p.Name,
                    PortLat: p.PortLat,
                    PortLon: p.PortLon,
                    SnappedLat: p.SnappedLat,
                    SnappedLon: p.SnappedLon,
                    MarineAccessLat: p.MarineAccessLat,
                    MarineAccessLon: p.MarineAccessLon,
                    MarineAccessDisplayLat: p.MarineAccessDisplayLat,
                    MarineAccessDisplayLon: p.MarineAccessDisplayLon,
                    MarineAccessDisplayPathCoordinates: p.MarineAccessDisplayPathCoordinates))
                .ToArray();

            return Results.Ok(ports);
        })
        .WithName("ListPorts")
        .WithTags("Ports")
        .Produces<PortResponse[]>();

        app.MapGet("/api/ports/{locode}", (string locode, GraphService graphService) =>
        {
            var rt = graphService.Runtime;
            if (!rt.PortsByLocode.TryGetValue(locode, out var port))
                return Results.NotFound(new { Error = $"Port not found: {locode}" });

            return Results.Ok(new PortResponse(
                Locode: port.Locode,
                Name: port.Name,
                PortLat: port.PortLat,
                PortLon: port.PortLon,
                SnappedLat: port.SnappedLat,
                SnappedLon: port.SnappedLon,
                MarineAccessLat: port.MarineAccessLat,
                MarineAccessLon: port.MarineAccessLon,
                MarineAccessDisplayLat: port.MarineAccessDisplayLat,
                MarineAccessDisplayLon: port.MarineAccessDisplayLon,
                MarineAccessDisplayPathCoordinates: port.MarineAccessDisplayPathCoordinates));
        })
        .WithName("GetPort")
        .WithTags("Ports")
        .Produces<PortResponse>()
        .ProducesProblem(404);
    }
}
