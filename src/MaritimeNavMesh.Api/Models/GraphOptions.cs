namespace MaritimeNavMesh.Api.Models;

public sealed class GraphOptions
{
    public const string Section = "Graph";
    public string BinaryPath { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public string PortsPath { get; init; } = "";
    public string LandMaskPath { get; init; } = "";
    public string EdgePortalsPath { get; init; } = "";
    public string AisCorridorHintsPath { get; init; } = "";
    public bool EnableRouteSmoothing { get; init; } = true;
    public string DefaultRouteGeometryMode { get; init; } = "funnel";
    public string DefaultRouteAlgorithm { get; init; } = "bidirectionalastar";
    public bool ValidateManifestHash { get; init; } = true;
    /// <summary>Default max snap distance when searching for nearest nodes from coordinates.</summary>
    public double DefaultMaxSnapDistanceNm { get; init; } = 50.0;
}
