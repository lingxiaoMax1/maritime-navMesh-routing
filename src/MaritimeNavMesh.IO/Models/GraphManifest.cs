using System.Text.Json.Serialization;

namespace MaritimeNavMesh.IO.Models;

public sealed class GraphManifest
{
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("h3Resolution")]
    public int H3Resolution { get; init; }

    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; init; }

    [JsonPropertyName("directedEdgeCount")]
    public int DirectedEdgeCount { get; init; }

    [JsonPropertyName("generatedAtUtc")]
    public string? GeneratedAtUtc { get; init; }

    [JsonPropertyName("binary")]
    public ManifestBinary? Binary { get; init; }

    [JsonPropertyName("stats")]
    public ManifestStats? Stats { get; init; }

    [JsonPropertyName("landMask")]
    public ManifestLandMask? LandMask { get; init; }

    [JsonPropertyName("edgePortals")]
    public ManifestEdgePortals? EdgePortals { get; init; }

    [JsonPropertyName("aisCorridorHints")]
    public ManifestAisCorridorHints? AisCorridorHints { get; init; }
}

public sealed class ManifestEdgePortals
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("edgeCount")]
    public int EdgeCount { get; init; }

    [JsonPropertyName("recordSizeBytes")]
    public int RecordSizeBytes { get; init; }
}

public sealed class ManifestAisCorridorHints
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("hintCount")]
    public int HintCount { get; init; }

    [JsonPropertyName("recordSizeBytes")]
    public int RecordSizeBytes { get; init; }
}

public sealed class ManifestLandMask
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("pixelSizeM")]
    public double PixelSizeM { get; init; }

    [JsonPropertyName("dilationPixels")]
    public int DilationPixels { get; init; }
}

public sealed class ManifestBinary
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("magic")]
    public string? Magic { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("endianness")]
    public string? Endianness { get; init; }

    [JsonPropertyName("headerSizeBytes")]
    public int HeaderSizeBytes { get; init; }

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("arrays")]
    public Dictionary<string, ManifestArrayLayout>? Arrays { get; init; }
}

public sealed class ManifestArrayLayout
{
    [JsonPropertyName("dtype")]
    public string? Dtype { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("countExpression")]
    public string? CountExpression { get; init; }

    [JsonPropertyName("offsetBytes")]
    public long OffsetBytes { get; init; }

    [JsonPropertyName("byteLength")]
    public long ByteLength { get; init; }
}

public sealed class ManifestStats
{
    [JsonPropertyName("edgeCostMin")]
    public double? EdgeCostMin { get; init; }

    [JsonPropertyName("edgeCostMax")]
    public double? EdgeCostMax { get; init; }

    [JsonPropertyName("edgeDistanceNmMin")]
    public double? EdgeDistanceNmMin { get; init; }

    [JsonPropertyName("edgeDistanceNmMax")]
    public double? EdgeDistanceNmMax { get; init; }
}
