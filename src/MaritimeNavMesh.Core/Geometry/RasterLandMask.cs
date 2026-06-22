using System.IO.Compression;

namespace MaritimeNavMesh.Core.Geometry;

public readonly record struct LandMaskTileEntry(long Offset, int ByteLength, int UncompressedByteLength, int LandPixelCount);

/// <summary>Conservative Web Mercator land-occupancy mask used for route geometry shortcuts.</summary>
public sealed class RasterLandMask
{
    private const double EarthRadiusM = 6378137.0;
    private const double MaxMercatorLat = 85.05112878;
    private readonly byte[]? _packedBits;
    private readonly string? _tileFilePath;
    private readonly LandMaskTileEntry[]? _tileEntries;
    private readonly Dictionary<int, byte[]>? _tilePayloadCache;
    private readonly object? _tileLoadLock;
    private readonly int _tileCodec;

    public int Width { get; }
    public int Height { get; }
    public double PixelSizeM { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public int DilationPixels { get; }
    public int TileWidthPixels { get; }
    public int TileHeightPixels { get; }
    public int TileColumnCount { get; }
    public int TileRowCount { get; }
    public bool IsTiled => _tileEntries is not null;

    public RasterLandMask(int width, int height, double pixelSizeM, double minX, double minY,
        double maxX, double maxY, int dilationPixels, byte[] packedBits)
    {
        if (width <= 0 || height <= 0 || pixelSizeM <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Land-mask dimensions and pixel size must be positive.");
        int expectedBytes = checked((width * height + 7) / 8);
        if (packedBits.Length != expectedBytes)
            throw new ArgumentException("Packed land-mask payload does not match its dimensions.", nameof(packedBits));
        Width = width;
        Height = height;
        PixelSizeM = pixelSizeM;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        DilationPixels = dilationPixels;
        _packedBits = packedBits;
        _tileCodec = 0;
        TileWidthPixels = width;
        TileHeightPixels = height;
        TileColumnCount = 1;
        TileRowCount = 1;
    }

    private RasterLandMask(
        int width,
        int height,
        double pixelSizeM,
        double minX,
        double minY,
        double maxX,
        double maxY,
        int dilationPixels,
        int tileWidthPixels,
        int tileHeightPixels,
        int tileColumnCount,
        int tileRowCount,
        string tileFilePath,
        LandMaskTileEntry[] tileEntries,
        int tileCodec)
    {
        if (width <= 0 || height <= 0 || pixelSizeM <= 0 || tileWidthPixels <= 0 || tileHeightPixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Land-mask dimensions, tile size, and pixel size must be positive.");
        if (tileEntries.Length != tileColumnCount * tileRowCount)
            throw new ArgumentException("Tile-entry count does not match tile grid dimensions.", nameof(tileEntries));
        Width = width;
        Height = height;
        PixelSizeM = pixelSizeM;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        DilationPixels = dilationPixels;
        TileWidthPixels = tileWidthPixels;
        TileHeightPixels = tileHeightPixels;
        TileColumnCount = tileColumnCount;
        TileRowCount = tileRowCount;
        _tileFilePath = tileFilePath;
        _tileEntries = tileEntries;
        _tileCodec = tileCodec;
        _tilePayloadCache = new Dictionary<int, byte[]>();
        _tileLoadLock = new object();
    }

    public static RasterLandMask CreateTiled(
        int width,
        int height,
        double pixelSizeM,
        double minX,
        double minY,
        double maxX,
        double maxY,
        int dilationPixels,
        int tileWidthPixels,
        int tileHeightPixels,
        int tileColumnCount,
        int tileRowCount,
        string tileFilePath,
        LandMaskTileEntry[] tileEntries,
        int tileCodec)
    {
        return new RasterLandMask(
            width,
            height,
            pixelSizeM,
            minX,
            minY,
            maxX,
            maxY,
            dilationPixels,
            tileWidthPixels,
            tileHeightPixels,
            tileColumnCount,
            tileRowCount,
            tileFilePath,
            tileEntries,
            tileCodec);
    }

    public bool IsSegmentLandSafe(double startLon, double startLat, double endLon, double endLat)
    {
        if (Math.Abs(endLon - startLon) > 180.0)
            return false;
        var (startX, startY) = Project(startLon, startLat);
        var (endX, endY) = Project(endLon, endLat);
        if (!Contains(startX, startY) || !Contains(endX, endY))
            return false;

        int col = Column(startX);
        int row = Row(startY);
        int endCol = Column(endX);
        int endRow = Row(endY);
        if (IsLand(row, col))
            return false;

        double dx = endX - startX;
        double dy = endY - startY;
        int stepX = Math.Sign(dx);
        int stepY = -Math.Sign(dy);
        double tDeltaX = stepX == 0 ? double.PositiveInfinity : PixelSizeM / Math.Abs(dx);
        double tDeltaY = stepY == 0 ? double.PositiveInfinity : PixelSizeM / Math.Abs(dy);
        double nextBoundaryX = stepX > 0 ? MinX + (col + 1) * PixelSizeM : MinX + col * PixelSizeM;
        double nextBoundaryY = stepY > 0 ? MaxY - (row + 1) * PixelSizeM : MaxY - row * PixelSizeM;
        double tMaxX = stepX == 0 ? double.PositiveInfinity : (nextBoundaryX - startX) / dx;
        double tMaxY = stepY == 0 ? double.PositiveInfinity : (nextBoundaryY - startY) / dy;

        while (col != endCol || row != endRow)
        {
            if (Math.Abs(tMaxX - tMaxY) <= 1e-12)
            {
                if (stepX != 0 && IsLand(row, col + stepX)) return false;
                if (stepY != 0 && IsLand(row + stepY, col)) return false;
                col += stepX;
                row += stepY;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                col += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                row += stepY;
                tMaxY += tDeltaY;
            }
            if (IsLand(row, col)) return false;
        }
        return true;
    }

    public bool IsLand(int row, int col)
    {
        if (row < 0 || row >= Height || col < 0 || col >= Width) return true;
        if (_packedBits is not null)
        {
            int bitIndex = checked(row * Width + col);
            return ReadPackedBit(_packedBits, bitIndex);
        }

        int tileCol = col / TileWidthPixels;
        int tileRow = row / TileHeightPixels;
        int tileIndex = checked(tileRow * TileColumnCount + tileCol);
        var tileEntries = _tileEntries!;
        LandMaskTileEntry entry = tileEntries[tileIndex];
        if (entry.ByteLength == 0)
            return false;
        byte[] payload = GetTilePayload(tileIndex, entry);
        int localCol = col - (tileCol * TileWidthPixels);
        int localRow = row - (tileRow * TileHeightPixels);
        int actualTileWidth = Math.Min(TileWidthPixels, Width - (tileCol * TileWidthPixels));
        int bitIndexInTile = checked(localRow * actualTileWidth + localCol);
        return ReadPackedBit(payload, bitIndexInTile);
    }

    private static bool ReadPackedBit(byte[] payload, int bitIndex)
    {
        return (payload[bitIndex >> 3] & (1 << (7 - (bitIndex & 7)))) != 0;
    }

    private byte[] GetTilePayload(int tileIndex, LandMaskTileEntry entry)
    {
        var cache = _tilePayloadCache!;
        lock (_tileLoadLock!)
        {
            if (cache.TryGetValue(tileIndex, out var cached))
                return cached;
        byte[] payload = new byte[entry.ByteLength];
        using var stream = File.OpenRead(_tileFilePath!);
        stream.Seek(entry.Offset, SeekOrigin.Begin);
            int read = 0;
            while (read < payload.Length)
            {
                int bytesRead = stream.Read(payload, read, payload.Length - read);
                if (bytesRead <= 0)
                    throw new InvalidDataException("Land-mask tile payload ended before expected byte count.");
                read += bytesRead;
            }
            byte[] tilePayload;
            if (entry.UncompressedByteLength == 0 || _tileCodec == 0)
            {
                tilePayload = payload;
            }
            else if (_tileCodec == 1)
            {
                using var compressed = new MemoryStream(payload, writable: false);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var output = new MemoryStream(entry.UncompressedByteLength);
                zlib.CopyTo(output);
                tilePayload = output.ToArray();
            }
            else
            {
                throw new InvalidDataException("Unsupported land-mask tile codec.");
            }
            if (entry.UncompressedByteLength != 0 && tilePayload.Length != entry.UncompressedByteLength)
                throw new InvalidDataException("Land-mask tile payload length does not match its header.");
            cache[tileIndex] = tilePayload;
            return tilePayload;
        }
    }

    private bool Contains(double x, double y) => x >= MinX && x < MaxX && y >= MinY && y < MaxY;
    private int Column(double x) => Math.Clamp((int)((x - MinX) / PixelSizeM), 0, Width - 1);
    private int Row(double y) => Math.Clamp((int)((MaxY - y) / PixelSizeM), 0, Height - 1);

    private static (double X, double Y) Project(double lon, double lat)
    {
        double clampedLat = Math.Clamp(lat, -MaxMercatorLat, MaxMercatorLat);
        double x = EarthRadiusM * lon * Math.PI / 180.0;
        double y = EarthRadiusM * Math.Log(Math.Tan(Math.PI / 4.0 + clampedLat * Math.PI / 360.0));
        return (x, y);
    }
}
