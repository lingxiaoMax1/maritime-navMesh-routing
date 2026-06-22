using System.Text;
using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.IO.Loaders;

public static class EdgePortalLoader
{
    private const string Magic = "OCNPRTL1";
    private const uint CurrentVersion = 1;
    private const uint ExplicitIndexSizeBytes = 4;
    private const uint PortalRecordSizeBytes = 16;
    private const uint MissingPortalIndex = 0xFFFFFFFF;

    public static EdgePortalSet Load(string path, CsrOceanGraph graph)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (magic != Magic)
            throw new InvalidDataException($"Invalid edge-portal magic: {magic}");
        uint version = reader.ReadUInt32();
        if (version != CurrentVersion)
            throw new InvalidDataException($"Unsupported edge-portal version: {version}");

        ulong edgeCount = reader.ReadUInt64();
        ulong explicitEdgeCount = reader.ReadUInt64();
        ulong uniqueExplicitPortalCount = reader.ReadUInt64();
        uint hasPortalBitsetBytes = reader.ReadUInt32();
        uint isExplicitBitsetBytes = reader.ReadUInt32();
        uint explicitIndexSize = reader.ReadUInt32();
        uint portalRecordSize = reader.ReadUInt32();

        if (explicitIndexSize != ExplicitIndexSizeBytes)
            throw new InvalidDataException($"Unsupported explicit-index size: {explicitIndexSize}");
        if (portalRecordSize != PortalRecordSizeBytes)
            throw new InvalidDataException($"Unsupported portal record size: {portalRecordSize}");
        if (graph.EdgeCount != checked((int)edgeCount))
            throw new InvalidDataException("Edge-portal edge count does not match the loaded CSR graph.");

        byte[] hasPortalBits = reader.ReadBytes(checked((int)hasPortalBitsetBytes));
        byte[] isExplicitBits = reader.ReadBytes(checked((int)isExplicitBitsetBytes));
        if (hasPortalBits.Length != hasPortalBitsetBytes || isExplicitBits.Length != isExplicitBitsetBytes)
            throw new InvalidDataException("Edge-portal bitset payload is shorter than its header.");

        int explicitDirectedCount = checked((int)explicitEdgeCount);
        var explicitToPortalIndex = new uint[explicitDirectedCount];
        for (int i = 0; i < explicitDirectedCount; i++)
            explicitToPortalIndex[i] = reader.ReadUInt32();

        int uniqueExplicitCount = checked((int)uniqueExplicitPortalCount);
        var ax = new List<float>(uniqueExplicitCount);
        var ay = new List<float>(uniqueExplicitCount);
        var bx = new List<float>(uniqueExplicitCount);
        var by = new List<float>(uniqueExplicitCount);
        for (int i = 0; i < uniqueExplicitCount; i++)
        {
            ax.Add(reader.ReadSingle());
            ay.Add(reader.ReadSingle());
            bx.Add(reader.ReadSingle());
            by.Add(reader.ReadSingle());
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Edge-portal payload length does not match its header.");

        return new EdgePortalSet(
            hasPortalBits,
            isExplicitBits,
            explicitToPortalIndex,
            [.. ax],
            [.. ay],
            [.. bx],
            [.. by],
            graph.NodeH3Int,
            graph.RowPtr,
            graph.ColIdx);
    }
}
