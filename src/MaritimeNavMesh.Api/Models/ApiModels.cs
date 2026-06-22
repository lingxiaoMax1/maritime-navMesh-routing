using MaritimeNavMesh.Core.Geometry;

namespace MaritimeNavMesh.Api.Models;

// --- Request models ---

public sealed record RouteByLocodeRequest(string FromLocode, string ToLocode);

public sealed record RouteByCoordinateRequest(
    double FromLat, double FromLon,
    double ToLat, double ToLon,
    double? MaxSnapDistanceNm = null);

// --- Response models ---

public sealed record HealthResponse(string Status, string Message, long NodeCount, long EdgeCount, int PortCount);

public sealed record GraphStatsResponse(
    int NodeCount,
    int EdgeCount,
    int Resolution,
    int ComponentCount,
    int PortCount);

public sealed record RuntimeProfileResponse(
    long StartupTotalMs,
    long GraphLoadMs,
    long PortsLoadMs,
    long GraphRuntimeBuildMs,
    long EdgePortalsLoadMs,
    long AisCorridorHintsLoadMs,
    long LandMaskLoadMs,
    long ManagedHeapBytes,
    long WorkingSetBytes,
    int NodeCount,
    int EdgeCount,
    int PortCount,
    int ComponentCount,
    bool RouteSmoothingEnabled);

public sealed record RouteBenchmarkResponse(
    string Algorithm,
    string Mode,
    string From,
    string To,
    int Iterations,
    int SuccessCount,
    int FailureCount,
    double AvgSearchMs,
    double P95SearchMs,
    double AvgTotalMs,
    double P95TotalMs,
    double AvgVisitedNodes,
    double P95VisitedNodes,
    double AvgForwardVisitedNodes,
    double AvgReverseVisitedNodes,
    int? LastNodeCount,
    double? LastDistanceNm,
    int? LastVisitedNodes,
    int? LastForwardVisitedNodes,
    int? LastReverseVisitedNodes,
    string? LastFailure);

public sealed record RouteBenchmarkScenarioResponse(
    string Name,
    string Algorithm,
    string From,
    string To,
    int Iterations,
    int SuccessCount,
    int FailureCount,
    double AvgSearchMs,
    double P95SearchMs,
    double AvgTotalMs,
    double P95TotalMs,
    double AvgVisitedNodes,
    double P95VisitedNodes,
    double AvgForwardVisitedNodes,
    double AvgReverseVisitedNodes,
    int? LastNodeCount,
    double? LastDistanceNm,
    int? LastVisitedNodes,
    int? LastForwardVisitedNodes,
    int? LastReverseVisitedNodes,
    string? LastFailure);

public sealed record RouteBenchmarkSuiteResponse(
    string Algorithm,
    string Mode,
    RouteBenchmarkScenarioResponse[] Scenarios);

public sealed record PortResponse(
    string Locode,
    string Name,
    double? PortLat,
    double? PortLon,
    double SnappedLat,
    double SnappedLon,
    double? MarineAccessLat,
    double? MarineAccessLon,
    double? MarineAccessDisplayLat,
    double? MarineAccessDisplayLon,
    double[][]? MarineAccessDisplayPathCoordinates);

public sealed record NearestNodeResponse(
    int NodeIndex,
    double Lat,
    double Lon,
    double SnapDistanceNm,
    int ComponentId,
    byte NodeClass);

public sealed record RouteResponse(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    double? TotalDistanceNm,
    double? TotalCost,
    int? NodeCount,
    long? SearchTimeMs,
    GeoJsonFeatureCollection? Geometry)
{
    public GeoJsonFeatureCollection? RawGraphGeometry { get; init; }
    public string? GeometryModeRequested { get; init; }
    public string? GeometryModeUsed { get; init; }
    public RouteGeometryDiagnostics? GeometryDiagnostics { get; init; }
}

// --- GeoJSON minimal types ---

public sealed record GeoJsonFeatureCollection(
    string Type,
    GeoJsonFeature[] Features)
{
    public static GeoJsonFeatureCollection FromCoordinates(double[][][] segments)
    {
        var features = segments.Select(seg => new GeoJsonFeature(
            "Feature",
            new GeoJsonGeometry("LineString", seg),
            new Dictionary<string, object?>())).ToArray();
        return new GeoJsonFeatureCollection("FeatureCollection", features);
    }
}

public sealed record GeoJsonFeature(
    string Type,
    GeoJsonGeometry Geometry,
    Dictionary<string, object?> Properties);

public sealed record GeoJsonGeometry(string Type, double[][] Coordinates);
