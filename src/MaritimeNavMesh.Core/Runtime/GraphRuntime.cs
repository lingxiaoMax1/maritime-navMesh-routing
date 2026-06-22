using MaritimeNavMesh.Core.Graph;
using MaritimeNavMesh.Core.Indices;
using MaritimeNavMesh.Core.Models;
using MaritimeNavMesh.Core.Routing;

namespace MaritimeNavMesh.Core.Runtime;

/// <summary>
/// Combines all loaded artifacts into a single queryable runtime.
/// Builds indices (H3Index, ComponentIndex, KdTree2D) from the raw graph,
/// validates ports against the H3 index, and provides the routing entry point.
/// </summary>
public sealed class GraphRuntime : IDisposable
{
    public CsrOceanGraph Graph { get; }
    public H3Index H3Index { get; }
    public ComponentIndex ComponentIndex { get; }
    public KdTree2D KdTree { get; }
    public IReadOnlyDictionary<string, PortSnap> PortsByLocode { get; }
    public IReadOnlyList<PortSnap> AllPorts { get; }

    /// <summary>Warnings generated during port validation (missing from graph, component mismatch).</summary>
    public IReadOnlyList<string> StartupWarnings { get; }

    public GraphRuntime(CsrOceanGraph graph, PortSnap[] ports)
    {
        Graph = graph;
        _ = graph.ReverseRowPtr;
        _ = graph.MinCostPerNmLowerBound;
        _ = graph.LandmarkHeuristics;
        H3Index = new H3Index(graph);
        ComponentIndex = new ComponentIndex(graph);
        KdTree = new KdTree2D(graph);

        AllPorts = ports;
        PortsByLocode = ports.ToDictionary(p => p.Locode, StringComparer.OrdinalIgnoreCase);

        StartupWarnings = ValidatePorts(ports);
    }

    private List<string> ValidatePorts(PortSnap[] ports)
    {
        var warnings = new List<string>();
        foreach (var port in ports)
        {
            int nodeIdx = H3Index.TryGetNodeIndexFromHex(port.RoutingH3Hex);
            if (nodeIdx < 0)
            {
                warnings.Add($"Port {port.Locode} ({port.Name}): routing_h3={port.RoutingH3Hex} not found in graph (regional build?)");
                continue;
            }
            if (port.ComponentId.HasValue && Graph.NodeComponent[nodeIdx] != port.ComponentId.Value)
            {
                warnings.Add($"Port {port.Locode}: component_id mismatch — ports.json={port.ComponentId.Value}, graph={Graph.NodeComponent[nodeIdx]}");
            }
        }
        return warnings;
    }

    /// <summary>Snap a LOCODE to its node index. Returns -1 if not found or not in graph.</summary>
    public int SnapPortToNode(string locode)
    {
        if (!PortsByLocode.TryGetValue(locode, out var port)) return -1;
        return H3Index.TryGetNodeIndexFromHex(port.RoutingH3Hex);
    }

    /// <summary>
    /// Snap a coordinate to the nearest routable node.
    /// Returns -1 if nothing found within maxDistNm.
    /// </summary>
    public int SnapCoordinateToNode(double lat, double lon, double maxDistNm = 50.0)
    {
        var results = KdTree.QueryNearest(Graph, lat, lon, maxK: 5, maxDistNm: maxDistNm);
        // Prefer a node that belongs to a routable component
        foreach (var r in results)
        {
            // Accept any node in a valid component (component_id >= 0)
            if (r.ComponentId >= 0)
                return r.NodeIndex;
        }
        return results.Count > 0 ? results[0].NodeIndex : -1;
    }

    /// <summary>Run Dijkstra between two node indices.</summary>
    public RouteResult Route(int fromNode, int toNode)
        => RoutePlanner.Search(Graph, new RouteRequest { FromNodeIndex = fromNode, ToNodeIndex = toNode });

    public void Dispose() => Graph.Dispose();
}
