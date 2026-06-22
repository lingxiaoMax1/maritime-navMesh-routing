using System.Collections.Concurrent;
using H3.Extensions;
using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.Core.Geometry;

public readonly record struct PortalSegment(double Ax, double Ay, double Bx, double By);

public sealed class EdgePortalSet
{
    private const uint MissingPortalIndex = 0xFFFFFFFF;
    private const int ExplicitRankBlockSize = 1024;

    private readonly uint[]? _directedToPortalIndex;
    private readonly float[] _ax;
    private readonly float[] _ay;
    private readonly float[] _bx;
    private readonly float[] _by;

    private readonly byte[]? _hasPortalBits;
    private readonly byte[]? _isExplicitBits;
    private readonly uint[]? _explicitToPortalIndex;
    private readonly int[]? _explicitRankPrefix;
    private readonly IIndexedArray<long>? _nodeH3Int;
    private readonly uint[]? _rowPtr;
    private readonly uint[]? _colIdx;
    private readonly ConcurrentDictionary<ulong, PortalSegment>? _implicitPortalCache;

    public int EdgeCount => _directedToPortalIndex?.Length ?? checked((int)(_colIdx?.Length ?? 0));
    public int UniquePortalCount => _ax.Length + (_implicitPortalCache?.Count ?? 0);

    public EdgePortalSet(uint[] directedToPortalIndex, float[] ax, float[] ay, float[] bx, float[] by)
    {
        ValidatePayloadArrays(ax, ay, bx, by);
        foreach (uint portalIndex in directedToPortalIndex)
        {
            if (portalIndex == MissingPortalIndex)
                continue;
            if (portalIndex >= ax.Length)
                throw new ArgumentException("Directed portal index references a portal outside the unique portal payload.");
        }

        _directedToPortalIndex = directedToPortalIndex;
        _ax = ax;
        _ay = ay;
        _bx = bx;
        _by = by;
    }

    public EdgePortalSet(
        byte[] hasPortalBits,
        byte[] isExplicitBits,
        uint[] explicitToPortalIndex,
        float[] ax,
        float[] ay,
        float[] bx,
        float[] by,
        IIndexedArray<long> nodeH3Int,
        uint[] rowPtr,
        uint[] colIdx)
    {
        ValidatePayloadArrays(ax, ay, bx, by);
        _hasPortalBits = hasPortalBits;
        _isExplicitBits = isExplicitBits;
        _explicitToPortalIndex = explicitToPortalIndex;
        _ax = ax;
        _ay = ay;
        _bx = bx;
        _by = by;
        _nodeH3Int = nodeH3Int;
        _rowPtr = rowPtr;
        _colIdx = colIdx;
        _implicitPortalCache = new ConcurrentDictionary<ulong, PortalSegment>();
        _explicitRankPrefix = BuildExplicitRankPrefix(isExplicitBits, colIdx.Length);
        ValidateExplicitMappings();
    }

    public bool TryGetPortal(int edgeIndex, out PortalSegment portal)
    {
        if (_directedToPortalIndex is not null)
            return TryGetDirectPortal(edgeIndex, out portal);
        return TryGetLazyPortal(edgeIndex, out portal);
    }

    private bool TryGetDirectPortal(int edgeIndex, out PortalSegment portal)
    {
        if (edgeIndex < 0 || edgeIndex >= _directedToPortalIndex!.Length)
        {
            portal = default;
            return false;
        }

        uint portalIndex = _directedToPortalIndex[edgeIndex];
        if (portalIndex == MissingPortalIndex)
        {
            portal = default;
            return false;
        }

        portal = ReadExplicitPortal(checked((int)portalIndex));
        return true;
    }

    private bool TryGetLazyPortal(int edgeIndex, out PortalSegment portal)
    {
        if (edgeIndex < 0 || edgeIndex >= _colIdx!.Length)
        {
            portal = default;
            return false;
        }

        if (!GetBit(_hasPortalBits!, edgeIndex))
        {
            portal = default;
            return false;
        }

        if (GetBit(_isExplicitBits!, edgeIndex))
        {
            int explicitRank = GetExplicitRank(edgeIndex);
            if ((uint)explicitRank >= _explicitToPortalIndex!.Length)
                throw new InvalidDataException("Explicit portal rank is out of bounds.");
            uint portalIndex = _explicitToPortalIndex[explicitRank];
            if (portalIndex >= _ax.Length)
                throw new InvalidDataException("Explicit portal payload index is out of bounds.");
            portal = ReadExplicitPortal(checked((int)portalIndex));
            return true;
        }

        int fromNode = FindSourceNode(edgeIndex);
        int toNode = checked((int)_colIdx[edgeIndex]);
        ulong pairKey = MakeUndirectedPairKey(fromNode, toNode);
        portal = _implicitPortalCache!.GetOrAdd(pairKey, _ => DeriveImplicitPortal(fromNode, toNode));
        return true;
    }

    private PortalSegment ReadExplicitPortal(int portalIndex) => new(_ax[portalIndex], _ay[portalIndex], _bx[portalIndex], _by[portalIndex]);

    private PortalSegment DeriveImplicitPortal(int fromNode, int toNode)
    {
        var origin = new H3.H3Index(unchecked((ulong)_nodeH3Int![fromNode]));
        var destination = new H3.H3Index(unchecked((ulong)_nodeH3Int![toNode]));
        var directedEdge = H3DirectedEdgeExtensions.ToDirectedEdge(origin, destination);
        if (!H3DirectedEdgeExtensions.IsValidDirectedEdge(directedEdge))
            throw new InvalidDataException("Implicit edge-portal could not be reconstructed from H3 neighbors.");

        var boundary = H3DirectedEdgeExtensions.GetDirectedEdgeBoundaryVertices(directedEdge).ToArray();
        if (boundary.Length < 2)
            throw new InvalidDataException("Implicit edge-portal boundary is malformed.");

        var first = boundary[0];
        var last = boundary[^1];
        return NormalizePortal(
            new PortalSegment(
                first.LongitudeDegrees,
                first.LatitudeDegrees,
                last.LongitudeDegrees,
                last.LatitudeDegrees));
    }

    private static PortalSegment NormalizePortal(PortalSegment portal)
    {
        if (portal.Ax < portal.Bx) return portal;
        if (portal.Ax > portal.Bx) return new PortalSegment(portal.Bx, portal.By, portal.Ax, portal.Ay);
        if (portal.Ay <= portal.By) return portal;
        return new PortalSegment(portal.Bx, portal.By, portal.Ax, portal.Ay);
    }

    private int FindSourceNode(int edgeIndex)
    {
        int low = 0;
        int high = _rowPtr!.Length - 1;
        while (low + 1 < high)
        {
            int mid = low + ((high - low) / 2);
            if (_rowPtr[mid] <= (uint)edgeIndex)
                low = mid;
            else
                high = mid;
        }
        return low;
    }

    private int GetExplicitRank(int edgeIndex)
    {
        int block = edgeIndex / ExplicitRankBlockSize;
        int rank = _explicitRankPrefix![block];
        int start = block * ExplicitRankBlockSize;
        for (int i = start; i < edgeIndex; i++)
        {
            if (GetBit(_isExplicitBits!, i))
                rank += 1;
        }
        return rank;
    }

    private void ValidateExplicitMappings()
    {
        int explicitCount = CountSetBits(_isExplicitBits!, _colIdx!.Length);
        if (explicitCount != _explicitToPortalIndex!.Length)
            throw new ArgumentException("Explicit portal mapping count does not match the explicit-edge bitset.");
        foreach (uint portalIndex in _explicitToPortalIndex)
        {
            if (portalIndex >= _ax.Length)
                throw new ArgumentException("Explicit portal index references a portal outside the unique portal payload.");
        }
    }

    private static int[] BuildExplicitRankPrefix(byte[] isExplicitBits, int edgeCount)
    {
        int blockCount = (edgeCount + ExplicitRankBlockSize - 1) / ExplicitRankBlockSize;
        var prefix = new int[blockCount];
        int running = 0;
        for (int block = 0; block < blockCount; block++)
        {
            prefix[block] = running;
            int start = block * ExplicitRankBlockSize;
            int end = Math.Min(edgeCount, start + ExplicitRankBlockSize);
            running += CountSetBitsInRange(isExplicitBits, start, end);
        }
        return prefix;
    }

    private static int CountSetBits(byte[] bitset, int bitCount) => CountSetBitsInRange(bitset, 0, bitCount);

    private static int CountSetBitsInRange(byte[] bitset, int startBit, int endBitExclusive)
    {
        int count = 0;
        for (int i = startBit; i < endBitExclusive; i++)
        {
            if (GetBit(bitset, i))
                count += 1;
        }
        return count;
    }

    private static bool GetBit(byte[] bitset, int index) => ((bitset[index / 8] >> (index % 8)) & 1) == 1;

    private static ulong MakeUndirectedPairKey(int a, int b)
    {
        uint min = (uint)Math.Min(a, b);
        uint max = (uint)Math.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    private static void ValidatePayloadArrays(float[] ax, float[] ay, float[] bx, float[] by)
    {
        if (ay.Length != ax.Length || bx.Length != ax.Length || by.Length != ax.Length)
            throw new ArgumentException("Unique portal arrays must all have identical lengths.");
    }
}
