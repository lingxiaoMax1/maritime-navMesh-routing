using System.IO;
using System.Text;
using MaritimeNavMesh.Core.Graph;
using MaritimeNavMesh.Core.Models;

namespace MaritimeNavMesh.Tests.Fixtures;

/// <summary>
/// Builds the current synthetic CSR binary artifact used by loader and routing tests.
/// </summary>
public static class SyntheticGraphBuilder
{
    public const int NodeCount = 4;
    public const int EdgeCount = 4;
    public const int Resolution = 5;

    public static readonly long[] H3Ints = [601042424243945471L, 601041550218100735L, 601042110711332863L, 601044398855159807L];
    public static readonly float[] Lats = [0f, 1f, -1f, 0f];
    public static readonly float[] Lons = [0f, 1f, 1f, 2f];
    public static readonly int[] Components = [0, 0, 0, 0];
    public static readonly byte[] Classes = [1, 1, 1, 1];

    public static readonly uint[] RowPtr = [0, 2, 3, 4, 4];
    public static readonly byte[] Degree = [2, 1, 1, 0];
    public static readonly uint[] ColIdx = [1, 2, 3, 3];
    public static readonly short[] TargetDelta = [1, 2, 2, 1];
    public static readonly uint[] OverflowEdgePos = [];
    public static readonly uint[] OverflowColIdx = [];

    public static readonly float[] Cost = [1.0f, 2.0f, 1.0f, 1.0f];

    public static CsrOceanGraph Build() => new(
        Resolution, NodeCount, EdgeCount,
        H3Ints, Lats, Lons, Components, Classes,
        RowPtr, ColIdx, Cost, edgeMinDepthM: null, edgeFlags: null);

    public static byte[] WriteBinary()
    {
        const int headerSize = 96;
        long off0 = headerSize;
        long off1 = off0 + NodeCount * 8;
        long off2 = off1 + NodeCount * 4;
        long off3 = off2 + NodeCount * 1;
        long off4 = off3 + NodeCount * 1;
        long off5 = off4 + EdgeCount * 2;
        long off6 = off5 + EdgeCount * 4;
        long off7 = off6 + OverflowEdgePos.Length * 4;
        long totalSize = off7 + OverflowColIdx.Length * 4;

        using var ms = new MemoryStream((int)totalSize);
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write("OCNCSR1\0"u8);
        w.Write(1u);
        w.Write((uint)Resolution);
        w.Write((ulong)NodeCount);
        w.Write((ulong)EdgeCount);
        w.Write((ulong)off0); w.Write((ulong)off1); w.Write((ulong)off2); w.Write((ulong)off3);
        w.Write((ulong)off4); w.Write((ulong)off5); w.Write((ulong)off6); w.Write((ulong)off7);
        foreach (long v in H3Ints) w.Write(v);
        foreach (int v in Components) w.Write(v);
        foreach (byte v in Classes) w.Write(v);
        foreach (byte v in Degree) w.Write(v);
        foreach (short v in TargetDelta) w.Write(v);
        foreach (float v in Cost) w.Write(v);
        foreach (uint v in OverflowEdgePos) w.Write(v);
        foreach (uint v in OverflowColIdx) w.Write(v);
        return ms.ToArray();
    }

    public static string WriteBinaryToTempFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test-csr-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, WriteBinary());
        return path;
    }

    public static PortSnap[] BuildPorts() =>
    [
        new PortSnap { Locode = "TESTA", Name = "Port A", SnappedH3Hex = "85754e67fffffff", SnappedLat = 0f, SnappedLon = 0f, SnapDistanceNm = 0, ComponentId = 0 },
        new PortSnap { Locode = "TESTD", Name = "Port D", SnappedH3Hex = "85756b23fffffff", SnappedLat = 0f, SnappedLon = 2f, SnapDistanceNm = 0, ComponentId = 0 },
    ];
}
