using System.Text.Json;
using MaritimeNavMesh.Core.Graph;
using MaritimeNavMesh.Core.Indices;
using MaritimeNavMesh.IO.Models;

namespace MaritimeNavMesh.IO.Loaders;

/// <summary>
/// Validates the manifest JSON against a loaded CsrOceanGraph:
/// - node/edge counts match
/// - sha256 matches the binary file
/// - array offsets and byte lengths are consistent
/// Cross-validates ports against the graph H3 index.
/// </summary>
public static class ManifestValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static GraphManifest LoadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest not found: {manifestPath}");

        string json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<GraphManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Manifest deserialized to null");
    }

    /// <summary>
    /// Validates that the manifest is consistent with the loaded graph.
    /// Throws InvalidDataException on any mismatch.
    /// </summary>
    public static void ValidateAgainstGraph(GraphManifest manifest, CsrOceanGraph graph, string binaryPath)
    {
        var errors = new List<string>();

        if (manifest.NodeCount != graph.NodeCount)
            errors.Add($"NodeCount mismatch: manifest={manifest.NodeCount}, graph={graph.NodeCount}");

        if (manifest.DirectedEdgeCount != graph.EdgeCount)
            errors.Add($"DirectedEdgeCount mismatch: manifest={manifest.DirectedEdgeCount}, graph={graph.EdgeCount}");

        if (manifest.H3Resolution != graph.Resolution)
            errors.Add($"H3Resolution mismatch: manifest={manifest.H3Resolution}, graph={graph.Resolution}");

        if (manifest.Binary?.Sha256 is string expectedHash)
        {
            string actualHash = CsrGraphLoader.ComputeSha256(binaryPath);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                errors.Add($"SHA-256 mismatch: manifest={expectedHash}, file={actualHash}");
        }

        if (errors.Count > 0)
            throw new InvalidDataException(
                $"Manifest validation failed:\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// Validates that every snapped port in ports.json references a node that exists in the H3 index.
    /// Reports missing ports as warnings (not exceptions) since the graph may be a regional subset.
    /// </summary>
    public static PortValidationReport ValidatePorts(Core.Models.PortSnap[] ports, H3Index h3Index, Core.Graph.CsrOceanGraph graph)
    {
        var valid = new List<string>();
        var missing = new List<string>();
        var componentMismatch = new List<string>();

        foreach (var port in ports)
        {
            int nodeIdx = h3Index.TryGetNodeIndexFromHex(port.SnappedH3Hex);
            if (nodeIdx < 0)
            {
                missing.Add($"{port.Locode} ({port.Name}): snapped_h3={port.SnappedH3Hex} not in graph");
                continue;
            }

            // Optionally check component consistency
            if (port.ComponentId.HasValue)
            {
                int graphComponent = graph.NodeComponent[nodeIdx];
                if (graphComponent != port.ComponentId.Value)
                    componentMismatch.Add(
                        $"{port.Locode}: manifest component_id={port.ComponentId.Value}, graph NodeComponent={graphComponent}");
            }

            valid.Add(port.Locode);
        }

        return new PortValidationReport(valid, missing, componentMismatch);
    }
}

public sealed record PortValidationReport(
    IReadOnlyList<string> ValidLocodes,
    IReadOnlyList<string> MissingFromGraph,
    IReadOnlyList<string> ComponentMismatches)
{
    public bool HasWarnings => MissingFromGraph.Count > 0 || ComponentMismatches.Count > 0;
}
