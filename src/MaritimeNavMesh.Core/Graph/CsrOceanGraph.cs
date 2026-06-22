using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.Core.Routing;

namespace MaritimeNavMesh.Core.Graph;

/// <summary>
/// Immutable in-memory CSR graph loaded from the Project 1 binary artifact.
/// All arrays are parallel and indexed consistently: node arrays by node index,
/// edge arrays by edge index. Row range for node i: [RowPtr[i], RowPtr[i+1]).
/// </summary>
public sealed class CsrOceanGraph : IDisposable
{
    private sealed record ReverseAdjacency(uint[] RowPtr, uint[] ColIdx, int[] EdgeIdx);

    public int Resolution { get; }
    public int NodeCount { get; }
    public int EdgeCount { get; }

    // Node arrays (length = NodeCount)
    public IIndexedArray<long> NodeH3Int { get; }
    public float[] NodeLat { get; }
    public float[] NodeLon { get; }
    public IIndexedArray<int> NodeComponent { get; }
    public IIndexedArray<byte> NodeClass { get; }

    // CSR structure
    public uint[] RowPtr { get; }   // length = NodeCount + 1
    public uint[] ColIdx { get; }   // length = EdgeCount

    // Edge arrays (length = EdgeCount)
    public IIndexedArray<float> EdgeCost { get; }
    public float[]? EdgeMinDepthM { get; }
    public ushort[]? EdgeFlags { get; }
    private readonly IDisposable? _ownedBackingStore;
    private readonly Lazy<ReverseAdjacency> _reverseAdjacency;
    private readonly Lazy<double> _minCostPerNmLowerBound;
    private readonly Lazy<LandmarkHeuristicSet> _landmarkHeuristics;

    public CsrOceanGraph(
        int resolution,
        int nodeCount,
        int edgeCount,
        long[] nodeH3Int,
        float[] nodeLat,
        float[] nodeLon,
        int[] nodeComponent,
        byte[] nodeClass,
        uint[] rowPtr,
        uint[] colIdx,
        float[] edgeCost,
        float[]? edgeMinDepthM,
        ushort[]? edgeFlags,
        IDisposable? ownedBackingStore = null)
        : this(
            resolution,
            nodeCount,
            edgeCount,
            new ManagedArray<long>(nodeH3Int),
            nodeLat,
            nodeLon,
            new ManagedArray<int>(nodeComponent),
            new ManagedArray<byte>(nodeClass),
            rowPtr,
            colIdx,
            new ManagedArray<float>(edgeCost),
            edgeMinDepthM,
            edgeFlags,
            ownedBackingStore)
    {
    }

    public CsrOceanGraph(
        int resolution,
        int nodeCount,
        int edgeCount,
        IIndexedArray<long> nodeH3Int,
        float[] nodeLat,
        float[] nodeLon,
        IIndexedArray<int> nodeComponent,
        IIndexedArray<byte> nodeClass,
        uint[] rowPtr,
        uint[] colIdx,
        IIndexedArray<float> edgeCost,
        float[]? edgeMinDepthM,
        ushort[]? edgeFlags,
        IDisposable? ownedBackingStore = null)
    {
        Resolution = resolution;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
        NodeH3Int = nodeH3Int;
        NodeLat = nodeLat;
        NodeLon = nodeLon;
        NodeComponent = nodeComponent;
        NodeClass = nodeClass;
        RowPtr = rowPtr;
        ColIdx = colIdx;
        EdgeCost = edgeCost;
        EdgeMinDepthM = edgeMinDepthM;
        EdgeFlags = edgeFlags;
        _ownedBackingStore = ownedBackingStore;
        _reverseAdjacency = new Lazy<ReverseAdjacency>(BuildReverseAdjacency, isThreadSafe: true);
        _minCostPerNmLowerBound = new Lazy<double>(ComputeMinCostPerNmLowerBound, isThreadSafe: true);
        _landmarkHeuristics = new Lazy<LandmarkHeuristicSet>(() => LandmarkHeuristicSet.Build(this), isThreadSafe: true);
    }

    public void Dispose() => _ownedBackingStore?.Dispose();

    public uint[] ReverseRowPtr => _reverseAdjacency.Value.RowPtr;
    public uint[] ReverseColIdx => _reverseAdjacency.Value.ColIdx;
    public int[] ReverseEdgeIdx => _reverseAdjacency.Value.EdgeIdx;
    public double MinCostPerNmLowerBound => _minCostPerNmLowerBound.Value;
    public LandmarkHeuristicSet LandmarkHeuristics => _landmarkHeuristics.Value;

    /// <summary>Returns the edge index range [start, end) for outgoing edges of nodeIndex.</summary>
    public (int Start, int End) EdgeRange(int nodeIndex) =>
        ((int)RowPtr[nodeIndex], (int)RowPtr[nodeIndex + 1]);

    public (int Start, int End) ReverseEdgeRange(int nodeIndex) =>
        ((int)ReverseRowPtr[nodeIndex], (int)ReverseRowPtr[nodeIndex + 1]);

    private ReverseAdjacency BuildReverseAdjacency()
    {
        var reverseDegree = new uint[NodeCount];
        for (int edgeIndex = 0; edgeIndex < EdgeCount; edgeIndex++)
        {
            reverseDegree[checked((int)ColIdx[edgeIndex])] += 1;
        }

        var reverseRowPtr = new uint[NodeCount + 1];
        uint running = 0;
        reverseRowPtr[0] = 0;
        for (int node = 0; node < NodeCount; node++)
        {
            running += reverseDegree[node];
            reverseRowPtr[node + 1] = running;
        }

        var next = new uint[NodeCount];
        Array.Copy(reverseRowPtr, next, NodeCount);
        var reverseColIdx = new uint[EdgeCount];
        var reverseEdgeIdx = new int[EdgeCount];
        for (int fromNode = 0; fromNode < NodeCount; fromNode++)
        {
            var (start, end) = EdgeRange(fromNode);
            for (int edgeIndex = start; edgeIndex < end; edgeIndex++)
            {
                int toNode = checked((int)ColIdx[edgeIndex]);
                int slot = checked((int)next[toNode]++);
                reverseColIdx[slot] = (uint)fromNode;
                reverseEdgeIdx[slot] = edgeIndex;
            }
        }

        return new ReverseAdjacency(reverseRowPtr, reverseColIdx, reverseEdgeIdx);
    }

    private double ComputeMinCostPerNmLowerBound()
    {
        double minRatio = double.PositiveInfinity;
        for (int fromNode = 0; fromNode < NodeCount; fromNode++)
        {
            var (start, end) = EdgeRange(fromNode);
            for (int edgeIndex = start; edgeIndex < end; edgeIndex++)
            {
                int toNode = checked((int)ColIdx[edgeIndex]);
                double distNm = GeoMath.HaversineNm(
                    NodeLat[fromNode],
                    NodeLon[fromNode],
                    NodeLat[toNode],
                    NodeLon[toNode]);
                if (distNm <= 1e-9)
                    continue;
                double cost = EdgeCost[edgeIndex];
                if (cost <= 0.0)
                    continue;
                double ratio = cost / distNm;
                if (ratio < minRatio)
                    minRatio = ratio;
            }
        }

        return double.IsFinite(minRatio) ? minRatio : 0.0;
    }
}
