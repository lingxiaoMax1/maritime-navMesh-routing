namespace MaritimeNavMesh.Core.Models;

/// <summary>A port snapped to the nearest routable H3 cell.</summary>
public sealed class PortSnap
{
    public required string Locode { get; init; }
    public required string Name { get; init; }
    public double? PortLat { get; init; }
    public double? PortLon { get; init; }
    public required string SnappedH3Hex { get; init; }
    public float SnappedLat { get; init; }
    public float SnappedLon { get; init; }
    public double SnapDistanceNm { get; init; }
    public string? MarineAccessH3Hex { get; init; }
    public double? MarineAccessLat { get; init; }
    public double? MarineAccessLon { get; init; }
    public string? MarineAccessSource { get; init; }
    public double[][]? MarineAccessPathCoordinates { get; init; }
    public bool? MarineAccessPathIsApproximate { get; init; }
    public double? MarineAccessPathLandOverlapNm { get; init; }
    public bool? MarineAccessPathIsLandSafe { get; init; }
    public string? MarineAccessDisplayH3Hex { get; init; }
    public double? MarineAccessDisplayLat { get; init; }
    public double? MarineAccessDisplayLon { get; init; }
    public string? MarineAccessDisplaySource { get; init; }
    public int? MarineAccessDisplayResolution { get; init; }
    public double[][]? MarineAccessDisplayPathCoordinates { get; init; }
    public bool? MarineAccessDisplayPathStartsAtRawPort { get; init; }
    public double? MarineAccessDisplayPathLandOverlapNm { get; init; }
    public bool? MarineAccessDisplayPathIsLandSafe { get; init; }
    public int? ComponentId { get; init; }

    public string RoutingH3Hex =>
        string.IsNullOrWhiteSpace(MarineAccessH3Hex) ? SnappedH3Hex : MarineAccessH3Hex;
}
