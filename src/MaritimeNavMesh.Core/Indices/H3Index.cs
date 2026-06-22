using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.Core.Indices;

/// <summary>
/// Maps H3 cell integer (int64) to its node index in the CSR graph.
/// Built once at startup from NodeH3Int array.
/// </summary>
public sealed class H3Index
{
    private readonly Dictionary<long, int> _h3ToNode;

    public int Count => _h3ToNode.Count;

    public H3Index(CsrOceanGraph graph)
    {
        _h3ToNode = new Dictionary<long, int>(graph.NodeCount);
        for (int i = 0; i < graph.NodeCount; i++)
            _h3ToNode[graph.NodeH3Int[i]] = i;
    }

    /// <summary>Returns the node index for the given H3 int, or -1 if not found.</summary>
    public int TryGetNodeIndex(long h3Int) =>
        _h3ToNode.TryGetValue(h3Int, out int idx) ? idx : -1;

    /// <summary>Parses a hex string H3 id (e.g. "85a7268bfffffff") and resolves to node index.</summary>
    public int TryGetNodeIndexFromHex(string h3Hex)
    {
        if (!TryParseH3Hex(h3Hex, out long h3Int))
            return -1;
        return TryGetNodeIndex(h3Int);
    }

    public static bool TryParseH3Hex(string hex, out long h3Int)
    {
        // H3 hex strings represent uint64 values; parse as ulong, reinterpret as long.
        if (ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong u))
        {
            h3Int = (long)u;
            return true;
        }
        h3Int = 0;
        return false;
    }

    public bool ContainsH3(long h3Int) => _h3ToNode.ContainsKey(h3Int);
}
