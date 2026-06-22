using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.Core.Indices;

/// <summary>Result of a nearest-node snap query.</summary>
public sealed class NearestNodeResult
{
    public int NodeIndex { get; init; }
    public float NodeLat { get; init; }
    public float NodeLon { get; init; }
    public double SnapDistanceNm { get; init; }
    public int ComponentId { get; init; }
    public byte NodeClass { get; init; }
}

/// <summary>
/// 2D KD-tree built over node lon/lat for fast nearest-node queries.
/// Suitable for the regional graph (bounded bbox, no anti-meridian crossing in Melbourne-Singapore region).
/// For a global graph this should be replaced with a 3D unit-vector or VP-tree approach.
/// </summary>
public sealed class KdTree2D
{
    private readonly float[] _nodeLat;
    private readonly float[] _nodeLon;
    private readonly int[] _nodeIndices;  // sorted by KD split
    private readonly int[] _tree;         // packed tree node: index into _nodeIndices

    private const int LeafSize = 16;

    public KdTree2D(CsrOceanGraph graph)
    {
        _nodeLat = graph.NodeLat;
        _nodeLon = graph.NodeLon;
        int n = graph.NodeCount;

        // Build sorted index array
        _nodeIndices = new int[n];
        for (int i = 0; i < n; i++) _nodeIndices[i] = i;
        _tree = _nodeIndices; // we sort in-place within the array

        BuildKd(0, n, depth: 0);
    }

    private void BuildKd(int start, int end, int depth)
    {
        if (end - start <= LeafSize) return;

        bool splitOnLon = depth % 2 == 1;
        int mid = (start + end) / 2;

        // Partial sort (nth_element equivalent via Array.Sort with range)
        Array.Sort(_tree, start, end - start,
            Comparer<int>.Create((a, b) =>
                splitOnLon
                    ? _nodeLon[a].CompareTo(_nodeLon[b])
                    : _nodeLat[a].CompareTo(_nodeLat[b])));

        BuildKd(start, mid, depth + 1);
        BuildKd(mid + 1, end, depth + 1);
    }

    /// <summary>
    /// Returns up to maxK nearest nodes within maxDistNm, optionally filtered by component.
    /// Results are sorted by ascending haversine distance.
    /// </summary>
    public List<NearestNodeResult> QueryNearest(
        CsrOceanGraph graph,
        double queryLat,
        double queryLon,
        int maxK = 5,
        double maxDistNm = double.MaxValue,
        int filterComponent = -1)
    {
        var results = new List<NearestNodeResult>(maxK * 2);
        SearchKd(graph, 0, _tree.Length, depth: 0, queryLat, queryLon, maxDistNm, results);

        // Re-rank by haversine
        results.Sort((a, b) => a.SnapDistanceNm.CompareTo(b.SnapDistanceNm));

        // Apply component filter and limit
        var filtered = new List<NearestNodeResult>(maxK);
        foreach (var r in results)
        {
            if (filtered.Count >= maxK) break;
            if (filterComponent >= 0 && r.ComponentId != filterComponent) continue;
            filtered.Add(r);
        }
        return filtered;
    }

    private void SearchKd(
        CsrOceanGraph graph,
        int start, int end, int depth,
        double queryLat, double queryLon,
        double maxDistNm,
        List<NearestNodeResult> results)
    {
        if (start >= end) return;

        // Leaf: brute-force scan
        if (end - start <= LeafSize)
        {
            for (int i = start; i < end; i++)
            {
                int ni = _tree[i];
                double d = GeoMath.HaversineNm(queryLat, queryLon, _nodeLat[ni], _nodeLon[ni]);
                if (d <= maxDistNm)
                    results.Add(new NearestNodeResult
                    {
                        NodeIndex = ni,
                        NodeLat = _nodeLat[ni],
                        NodeLon = _nodeLon[ni],
                        SnapDistanceNm = d,
                        ComponentId = graph.NodeComponent[ni],
                        NodeClass = graph.NodeClass[ni],
                    });
            }
            return;
        }

        bool splitOnLon = depth % 2 == 1;
        int mid = (start + end) / 2;
        int midNode = _tree[mid];
        double splitVal = splitOnLon ? _nodeLon[midNode] : _nodeLat[midNode];
        double queryVal = splitOnLon ? queryLon : queryLat;

        // Always check the pivot node directly — it is excluded from both recursive subtrees
        double midDist = GeoMath.HaversineNm(queryLat, queryLon, _nodeLat[midNode], _nodeLon[midNode]);
        if (midDist <= maxDistNm)
            results.Add(new NearestNodeResult
            {
                NodeIndex = midNode,
                NodeLat = _nodeLat[midNode],
                NodeLon = _nodeLon[midNode],
                SnapDistanceNm = midDist,
                ComponentId = graph.NodeComponent[midNode],
                NodeClass = graph.NodeClass[midNode],
            });

        // Visit closer half first, then check if the far half is within range.
        // For longitude splits, scale by cos(lat) since 1° lon < 60 nm at non-equatorial latitudes.
        double lonFactor = splitOnLon ? 60.0 * Math.Cos(queryLat * Math.PI / 180.0) : 60.0;
        bool goLeft = queryVal <= splitVal;
        if (goLeft)
        {
            SearchKd(graph, start, mid, depth + 1, queryLat, queryLon, maxDistNm, results);
            double axialDist = Math.Abs(queryVal - splitVal) * lonFactor;
            if (axialDist <= maxDistNm)
                SearchKd(graph, mid + 1, end, depth + 1, queryLat, queryLon, maxDistNm, results);
        }
        else
        {
            SearchKd(graph, mid + 1, end, depth + 1, queryLat, queryLon, maxDistNm, results);
            double axialDist = Math.Abs(queryVal - splitVal) * lonFactor;
            if (axialDist <= maxDistNm)
                SearchKd(graph, start, mid, depth + 1, queryLat, queryLon, maxDistNm, results);
        }
    }
}
