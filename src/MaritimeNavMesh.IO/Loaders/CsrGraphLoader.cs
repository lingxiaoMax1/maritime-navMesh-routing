using System.Buffers.Binary;
using System.Security.Cryptography;
using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.IO.Loaders;

/// <summary>
/// Loads and validates the current Project 1 CSR binary artifact.
/// </summary>
public static class CsrGraphLoader
{
    private static readonly byte[] ExpectedMagic = "OCNCSR1\0"u8.ToArray();
    private const int CurrentVersion = 1;
    private const int HeaderSize = 96;
    private const int ArrayCount = 8;
    private const short DeltaOverflowSentinel = short.MinValue;

    public static CsrOceanGraph Load(string binaryPath)
    {
        if (!File.Exists(binaryPath))
            throw new FileNotFoundException($"CSR binary not found: {binaryPath}");
        return ParseAndValidate(binaryPath);
    }

    public static CsrOceanGraph LoadAndVerifyHash(string binaryPath, string expectedSha256)
    {
        if (!File.Exists(binaryPath))
            throw new FileNotFoundException($"CSR binary not found: {binaryPath}");

        string actualHash = ComputeSha256(binaryPath);

        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"CSR binary SHA-256 mismatch. Expected: {expectedSha256} Actual: {actualHash}");

        return ParseAndValidate(binaryPath);
    }

    private static CsrOceanGraph ParseAndValidate(string binaryPath)
    {
        long fileLength = new FileInfo(binaryPath).Length;
        if (fileLength < HeaderSize)
            throw new InvalidDataException($"File too small for header: {fileLength} bytes");

        byte[] headerBytes = new byte[HeaderSize];
        using var headerStream = File.OpenRead(binaryPath);
        int bytesRead = headerStream.Read(headerBytes, 0, HeaderSize);
        if (bytesRead != HeaderSize)
            throw new InvalidDataException($"Failed to read full CSR header from file: {binaryPath}");
        ReadOnlySpan<byte> header = headerBytes;
        for (int i = 0; i < ExpectedMagic.Length; i++)
        {
            if (header[i] != ExpectedMagic[i])
                throw new InvalidDataException(
                    $"Invalid CSR magic. Expected 'OCNCSR1\\0', got: {System.Text.Encoding.ASCII.GetString(headerBytes, 0, 8)}");
        }

        int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (version != CurrentVersion)
            throw new InvalidDataException($"Unsupported CSR version: {version}. Expected: {CurrentVersion}");

        int resolution = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        long nodeCountLong = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
        long edgeCountLong = BinaryPrimitives.ReadInt64LittleEndian(header[24..]);
        if (nodeCountLong < 0 || nodeCountLong > int.MaxValue)
            throw new InvalidDataException($"Node count out of range: {nodeCountLong}");
        if (edgeCountLong < 0 || edgeCountLong > int.MaxValue)
            throw new InvalidDataException($"Edge count out of range: {edgeCountLong}");
        int nodeCount = (int)nodeCountLong;
        int edgeCount = (int)edgeCountLong;

        var backingStore = new MappedGraphBuffer(binaryPath);
        try
        {
            var offsets = new long[ArrayCount];
            for (int i = 0; i < ArrayCount; i++)
                offsets[i] = BinaryPrimitives.ReadInt64LittleEndian(header[(32 + i * 8)..]);
            if (offsets[0] != HeaderSize)
                throw new InvalidDataException($"First array must start immediately after header at {HeaderSize}, got {offsets[0]}");
            for (int i = 1; i < ArrayCount; i++)
            {
                if (offsets[i] < offsets[i - 1])
                    throw new InvalidDataException($"Array offsets are not monotonic at index {i}");
            }

            var nodeH3Int = backingStore.Slice<long>(offsets[0], nodeCount);
            var nodeComponent = backingStore.Slice<int>(offsets[1], nodeCount);
            var nodeClass = backingStore.Slice<byte>(offsets[2], nodeCount);
            var degree = backingStore.Slice<byte>(offsets[3], nodeCount);
            var edgeTargetDelta = backingStore.Slice<short>(offsets[4], edgeCount);
            var edgeCost = backingStore.Slice<float>(offsets[5], edgeCount);

            int overflowCount = CountItems(offsets[6], offsets[7], sizeof(uint));
            int overflowTargetCount = CountItems(offsets[7], fileLength, sizeof(uint));
            if (overflowCount != overflowTargetCount)
                throw new InvalidDataException($"Overflow edge/target count mismatch: {overflowCount} vs {overflowTargetCount}");
            var overflowEdgePos = backingStore.Slice<uint>(offsets[6], overflowCount);
            var overflowColIdx = backingStore.Slice<uint>(offsets[7], overflowTargetCount);

            uint[] rowPtr = BuildRowPtrFromDegree(degree, edgeCount);
            uint[] colIdx = BuildColIdxFromCompressed(rowPtr, edgeTargetDelta, overflowEdgePos, overflowColIdx, nodeCount);
            (float[] nodeLat, float[] nodeLon) = BuildCoordinatesFromH3(nodeH3Int);

            if (rowPtr[0] != 0)
                throw new InvalidDataException($"row_ptr[0] must be 0, got {rowPtr[0]}");
            if (rowPtr[nodeCount] != (uint)edgeCount)
                throw new InvalidDataException($"row_ptr[{nodeCount}] must equal edgeCount ({edgeCount}), got {rowPtr[nodeCount]}");
            for (int i = 1; i <= nodeCount; i++)
            {
                if (rowPtr[i] < rowPtr[i - 1])
                    throw new InvalidDataException($"row_ptr is not monotonic at index {i}");
            }
            for (int i = 0; i < colIdx.Length; i++)
            {
                if (colIdx[i] >= (uint)nodeCount)
                    throw new InvalidDataException($"col_idx contains out-of-range node index {colIdx[i]} (nodeCount={nodeCount})");
            }

            return new CsrOceanGraph(
                resolution, nodeCount, edgeCount,
                nodeH3Int, nodeLat, nodeLon, nodeComponent, nodeClass,
                rowPtr, colIdx, edgeCost, edgeMinDepthM: null, edgeFlags: null, ownedBackingStore: backingStore);
        }
        catch
        {
            backingStore.Dispose();
            throw;
        }
    }

    private static int CountItems(long startOffset, long endOffset, int itemSize)
    {
        long byteLength = endOffset - startOffset;
        if (byteLength < 0 || byteLength % itemSize != 0)
            throw new InvalidDataException($"Array payload size {byteLength} is not aligned to item size {itemSize}");
        if (byteLength / itemSize > int.MaxValue)
            throw new InvalidDataException($"Array item count out of range: {byteLength / itemSize}");
        return (int)(byteLength / itemSize);
    }

    private static uint[] BuildRowPtrFromDegree(IIndexedArray<byte> degree, int edgeCount)
    {
        var rowPtr = new uint[degree.Length + 1];
        uint running = 0;
        rowPtr[0] = 0;
        for (int i = 0; i < degree.Length; i++)
        {
            running += degree[i];
            rowPtr[i + 1] = running;
        }
        if (running != (uint)edgeCount)
            throw new InvalidDataException($"degree sum must equal edgeCount ({edgeCount}), got {running}");
        return rowPtr;
    }

    private static uint[] BuildColIdxFromCompressed(
        uint[] rowPtr,
        IIndexedArray<short> edgeTargetDelta,
        IIndexedArray<uint> overflowEdgePos,
        IIndexedArray<uint> overflowColIdx,
        int nodeCount)
    {
        if (overflowEdgePos.Length != overflowColIdx.Length)
            throw new InvalidDataException("overflow_edge_pos and overflow_col_idx length mismatch");
        var colIdx = new uint[edgeTargetDelta.Length];
        int overflowPtr = 0;
        for (int fromNode = 0; fromNode < rowPtr.Length - 1; fromNode++)
        {
            int start = (int)rowPtr[fromNode];
            int end = (int)rowPtr[fromNode + 1];
            for (int edgePos = start; edgePos < end; edgePos++)
            {
                int target;
                short delta = edgeTargetDelta[edgePos];
                if (delta == DeltaOverflowSentinel)
                {
                    if (overflowPtr >= overflowEdgePos.Length || overflowEdgePos[overflowPtr] != edgePos)
                        throw new InvalidDataException("Missing overflow target for compressed col_idx edge");
                    target = checked((int)overflowColIdx[overflowPtr]);
                    overflowPtr += 1;
                }
                else
                {
                    target = fromNode + delta;
                }
                if (target < 0 || target >= nodeCount)
                    throw new InvalidDataException($"Reconstructed col_idx contains out-of-range node index {target}");
                colIdx[edgePos] = (uint)target;
            }
        }
        if (overflowPtr != overflowEdgePos.Length)
            throw new InvalidDataException("Unused overflow target entries remain after col_idx reconstruction");
        return colIdx;
    }

    private static (float[] NodeLat, float[] NodeLon) BuildCoordinatesFromH3(IIndexedArray<long> nodeH3Int)
    {
        var nodeLat = new float[nodeH3Int.Length];
        var nodeLon = new float[nodeH3Int.Length];
        for (int i = 0; i < nodeH3Int.Length; i++)
        {
            var index = new H3.H3Index(unchecked((ulong)nodeH3Int[i]));
            var latLng = index.ToLatLng();
            nodeLat[i] = (float)latLng.LatitudeDegrees;
            nodeLon[i] = (float)latLng.LongitudeDegrees;
        }
        return (nodeLat, nodeLon);
    }

    public static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
