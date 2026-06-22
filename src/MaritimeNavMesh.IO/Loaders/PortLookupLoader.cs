using System.Text.Json;
using MaritimeNavMesh.Core.Models;

namespace MaritimeNavMesh.IO.Loaders;

/// <summary>
/// Loads the Project 1 ports.json artifact.
/// Supports both the legacy flat JSON array and the compact runtime schema.
/// </summary>
public static class PortLookupLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static PortSnap[] Load(string portsJsonPath)
    {
        if (!File.Exists(portsJsonPath))
            throw new FileNotFoundException($"Ports JSON not found: {portsJsonPath}");

        string json = File.ReadAllText(portsJsonPath);
        var records = DeserializeRecords(json);

        var snaps = new List<PortSnap>(records.Length);
        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.Locode) || string.IsNullOrWhiteSpace(r.SnappedH3))
                continue;
            if (r.SnappedLat is null || r.SnappedLon is null)
                continue; // skip ports with missing snap coordinates rather than silently placing at (0°, 0°)

            snaps.Add(new PortSnap
            {
                Locode = r.Locode,
                Name = r.Name ?? r.Locode,
                PortLat = r.PortLat,
                PortLon = r.PortLon,
                SnappedH3Hex = r.SnappedH3,
                SnappedLat = (float)r.SnappedLat.Value,
                SnappedLon = (float)r.SnappedLon.Value,
                SnapDistanceNm = r.SnapDistanceNm ?? 0,
                MarineAccessH3Hex = r.MarineAccessH3,
                MarineAccessLat = r.MarineAccessLat,
                MarineAccessLon = r.MarineAccessLon,
                MarineAccessSource = r.MarineAccessSource,
                MarineAccessPathCoordinates = r.MarineAccessPathCoordinates,
                MarineAccessPathIsApproximate = r.MarineAccessPathIsApproximate,
                MarineAccessPathLandOverlapNm = r.MarineAccessPathLandOverlapNm,
                MarineAccessPathIsLandSafe = r.MarineAccessPathIsLandSafe,
                MarineAccessDisplayH3Hex = r.MarineAccessDisplayH3,
                MarineAccessDisplayLat = r.MarineAccessDisplayLat,
                MarineAccessDisplayLon = r.MarineAccessDisplayLon,
                MarineAccessDisplaySource = r.MarineAccessDisplaySource,
                MarineAccessDisplayResolution = r.MarineAccessDisplayResolution,
                MarineAccessDisplayPathCoordinates = r.MarineAccessDisplayPathCoordinates,
                MarineAccessDisplayPathStartsAtRawPort = r.MarineAccessDisplayPathStartsAtRawPort,
                MarineAccessDisplayPathLandOverlapNm = r.MarineAccessDisplayPathLandOverlapNm,
                MarineAccessDisplayPathIsLandSafe = r.MarineAccessDisplayPathIsLandSafe,
                ComponentId = r.ComponentId,
            });
        }
        return [.. snaps];
    }

    private static PortJsonRecord[] DeserializeRecords(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<PortJsonRecord[]>(root.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("Ports JSON deserialized to null");
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Ports JSON root must be an array or object");

        if (!root.TryGetProperty("schema", out var schemaElement) ||
            !string.Equals(schemaElement.GetString(), "ocean_ports_runtime", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported ports JSON schema");

        if (!root.TryGetProperty("schemaVersion", out var versionElement) || versionElement.GetInt32() != 2)
            throw new InvalidDataException("Unsupported ports JSON schemaVersion");

        if (!root.TryGetProperty("fields", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Compact ports JSON is missing fields array");
        if (!root.TryGetProperty("ports", out var portsElement) || portsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Compact ports JSON is missing ports array");

        var fields = fieldsElement.EnumerateArray().Select(static f => f.GetString() ?? string.Empty).ToArray();
        var fieldIndex = fields
            .Select((name, index) => new { name, index })
            .ToDictionary(static pair => pair.name, static pair => pair.index, StringComparer.Ordinal);
        var records = new List<PortJsonRecord>(portsElement.GetArrayLength());
        foreach (JsonElement row in portsElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Compact ports JSON row must be an array");
            records.Add(new PortJsonRecord
            {
                Locode = GetString(row, fieldIndex, "locode"),
                Name = GetString(row, fieldIndex, "name"),
                PortLat = GetDouble(row, fieldIndex, "port_lat"),
                PortLon = GetDouble(row, fieldIndex, "port_lon"),
                SnappedH3 = GetString(row, fieldIndex, "snapped_h3"),
                SnappedLat = GetDouble(row, fieldIndex, "snapped_lat"),
                SnappedLon = GetDouble(row, fieldIndex, "snapped_lon"),
                SnapDistanceNm = GetDouble(row, fieldIndex, "snap_distance_nm"),
                MarineAccessH3 = GetString(row, fieldIndex, "marine_access_h3"),
                MarineAccessLat = GetDouble(row, fieldIndex, "marine_access_lat"),
                MarineAccessLon = GetDouble(row, fieldIndex, "marine_access_lon"),
                MarineAccessSource = GetString(row, fieldIndex, "marine_access_source"),
                MarineAccessPathCoordinates = GetCoordinates(row, fieldIndex, "marine_access_path_coordinates"),
                MarineAccessPathIsApproximate = GetBool(row, fieldIndex, "marine_access_path_is_approximate"),
                MarineAccessPathLandOverlapNm = GetDouble(row, fieldIndex, "marine_access_path_land_overlap_nm"),
                MarineAccessPathIsLandSafe = GetBool(row, fieldIndex, "marine_access_path_is_land_safe"),
                MarineAccessDisplayH3 = GetString(row, fieldIndex, "marine_access_display_h3"),
                MarineAccessDisplayLat = GetDouble(row, fieldIndex, "marine_access_display_lat"),
                MarineAccessDisplayLon = GetDouble(row, fieldIndex, "marine_access_display_lon"),
                MarineAccessDisplaySource = GetString(row, fieldIndex, "marine_access_display_source"),
                MarineAccessDisplayResolution = GetInt(row, fieldIndex, "marine_access_display_resolution"),
                MarineAccessDisplayPathCoordinates = GetCoordinates(row, fieldIndex, "marine_access_display_path_coordinates"),
                MarineAccessDisplayPathStartsAtRawPort = GetBool(row, fieldIndex, "marine_access_display_path_starts_at_raw_port"),
                MarineAccessDisplayPathLandOverlapNm = GetDouble(row, fieldIndex, "marine_access_display_path_land_overlap_nm"),
                MarineAccessDisplayPathIsLandSafe = GetBool(row, fieldIndex, "marine_access_display_path_is_land_safe"),
                ComponentId = GetInt(row, fieldIndex, "component_id"),
            });
        }

        return [.. records];
    }

    private static JsonElement? GetElement(JsonElement row, IReadOnlyDictionary<string, int> fieldIndex, string field)
    {
        if (!fieldIndex.TryGetValue(field, out int index) || index >= row.GetArrayLength())
            return null;
        JsonElement value = row[index];
        return value.ValueKind == JsonValueKind.Null ? null : value;
    }

    private static string? GetString(JsonElement row, IReadOnlyDictionary<string, int> fieldIndex, string field) =>
        GetElement(row, fieldIndex, field)?.GetString();

    private static double? GetDouble(JsonElement row, IReadOnlyDictionary<string, int> fieldIndex, string field) =>
        GetElement(row, fieldIndex, field)?.GetDouble();

    private static int? GetInt(JsonElement row, IReadOnlyDictionary<string, int> fieldIndex, string field) =>
        GetElement(row, fieldIndex, field)?.GetInt32();

    private static bool? GetBool(JsonElement row, IReadOnlyDictionary<string, int> fieldIndex, string field) =>
        GetElement(row, fieldIndex, field)?.GetBoolean();

    private static double[][]? GetCoordinates(JsonElement row, IReadOnlyDictionary<string, int> fieldIndex, string field)
    {
        JsonElement? value = GetElement(row, fieldIndex, field);
        if (value is null)
            return null;
        return JsonSerializer.Deserialize<double[][]>(value.Value.GetRawText(), JsonOptions);
    }

    // JSON deserialization shim matching the ports.json schema
    private sealed class PortJsonRecord
    {
        public string? Locode { get; init; }
        public string? Name { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("port_lat")]
        public double? PortLat { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("port_lon")]
        public double? PortLon { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("snapped_h3")]
        public string? SnappedH3 { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("snapped_lat")]
        public double? SnappedLat { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("snapped_lon")]
        public double? SnappedLon { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("snap_distance_nm")]
        public double? SnapDistanceNm { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_h3")]
        public string? MarineAccessH3 { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_lat")]
        public double? MarineAccessLat { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_lon")]
        public double? MarineAccessLon { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_source")]
        public string? MarineAccessSource { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_path_coordinates")]
        public double[][]? MarineAccessPathCoordinates { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_path_is_approximate")]
        public bool? MarineAccessPathIsApproximate { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_path_land_overlap_nm")]
        public double? MarineAccessPathLandOverlapNm { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_path_is_land_safe")]
        public bool? MarineAccessPathIsLandSafe { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_h3")]
        public string? MarineAccessDisplayH3 { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_lat")]
        public double? MarineAccessDisplayLat { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_lon")]
        public double? MarineAccessDisplayLon { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_source")]
        public string? MarineAccessDisplaySource { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_resolution")]
        public int? MarineAccessDisplayResolution { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_path_coordinates")]
        public double[][]? MarineAccessDisplayPathCoordinates { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_path_starts_at_raw_port")]
        public bool? MarineAccessDisplayPathStartsAtRawPort { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_path_land_overlap_nm")]
        public double? MarineAccessDisplayPathLandOverlapNm { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("marine_access_display_path_is_land_safe")]
        public bool? MarineAccessDisplayPathIsLandSafe { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("component_id")]
        public int? ComponentId { get; init; }
    }
}
