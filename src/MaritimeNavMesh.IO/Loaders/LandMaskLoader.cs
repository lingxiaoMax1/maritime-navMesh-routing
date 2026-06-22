using System.Text;
using MaritimeNavMesh.Core.Geometry;

namespace MaritimeNavMesh.IO.Loaders;

public static class LandMaskLoader
{
    private const string Magic = "OCNLMSK1";
    private const uint Version1 = 1;
    private const uint Version2 = 2;
    private const uint Version3 = 3;

    public static RasterLandMask Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (magic != Magic) throw new InvalidDataException($"Invalid land-mask magic: {magic}");
        uint version = reader.ReadUInt32();
        if (version == Version1)
            return LoadV1(stream, reader);
        if (version == Version2)
            return LoadV2(path, stream, reader);
        if (version == Version3)
            return LoadV3(path, stream, reader);
        throw new InvalidDataException($"Unsupported land-mask version: {version}");
    }

    private static RasterLandMask LoadV1(FileStream stream, BinaryReader reader)
    {
        uint width = reader.ReadUInt32();
        uint height = reader.ReadUInt32();
        double pixelSizeM = reader.ReadDouble();
        double minX = reader.ReadDouble();
        double minY = reader.ReadDouble();
        double maxX = reader.ReadDouble();
        double maxY = reader.ReadDouble();
        uint dilationPixels = reader.ReadUInt32();
        ulong bitCount = reader.ReadUInt64();
        if (bitCount != checked((ulong)width * height))
            throw new InvalidDataException("Land-mask bit count does not match its dimensions.");
        int payloadBytes = checked((int)((bitCount + 7) / 8));
        byte[] payload = reader.ReadBytes(payloadBytes);
        if (payload.Length != payloadBytes || stream.Position != stream.Length)
            throw new InvalidDataException("Land-mask payload length does not match its header.");
        return new RasterLandMask(checked((int)width), checked((int)height), pixelSizeM,
            minX, minY, maxX, maxY, checked((int)dilationPixels), payload);
    }

    private static RasterLandMask LoadV2(string path, FileStream stream, BinaryReader reader)
    {
        uint width = reader.ReadUInt32();
        uint height = reader.ReadUInt32();
        double pixelSizeM = reader.ReadDouble();
        double minX = reader.ReadDouble();
        double minY = reader.ReadDouble();
        double maxX = reader.ReadDouble();
        double maxY = reader.ReadDouble();
        uint dilationPixels = reader.ReadUInt32();
        uint tileWidth = reader.ReadUInt32();
        uint tileHeight = reader.ReadUInt32();
        uint tileColumns = reader.ReadUInt32();
        uint tileRows = reader.ReadUInt32();
        ulong bitCount = reader.ReadUInt64();
        if (bitCount != checked((ulong)width * height))
            throw new InvalidDataException("Land-mask bit count does not match its dimensions.");
        int tileCount = checked((int)(tileColumns * tileRows));
        var tileEntries = new LandMaskTileEntry[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            ulong offset = reader.ReadUInt64();
            uint byteLength = reader.ReadUInt32();
            uint landPixelCount = reader.ReadUInt32();
            tileEntries[i] = new LandMaskTileEntry(checked((long)offset), checked((int)byteLength), 0, checked((int)landPixelCount));
        }
        if (stream.Position > stream.Length)
            throw new InvalidDataException("Land-mask tile index exceeds file length.");
        foreach (var entry in tileEntries)
        {
            if (entry.ByteLength == 0)
                continue;
            long end = checked(entry.Offset + entry.ByteLength);
            if (end > stream.Length)
                throw new InvalidDataException("Land-mask tile payload exceeds file length.");
        }
        return RasterLandMask.CreateTiled(
            checked((int)width),
            checked((int)height),
            pixelSizeM,
            minX,
            minY,
            maxX,
            maxY,
            checked((int)dilationPixels),
            checked((int)tileWidth),
            checked((int)tileHeight),
            checked((int)tileColumns),
            checked((int)tileRows),
            path,
            tileEntries,
            tileCodec: 0);
    }

    private static RasterLandMask LoadV3(string path, FileStream stream, BinaryReader reader)
    {
        uint width = reader.ReadUInt32();
        uint height = reader.ReadUInt32();
        double pixelSizeM = reader.ReadDouble();
        double minX = reader.ReadDouble();
        double minY = reader.ReadDouble();
        double maxX = reader.ReadDouble();
        double maxY = reader.ReadDouble();
        uint dilationPixels = reader.ReadUInt32();
        uint tileWidth = reader.ReadUInt32();
        uint tileHeight = reader.ReadUInt32();
        uint tileColumns = reader.ReadUInt32();
        uint tileRows = reader.ReadUInt32();
        uint tileCodec = reader.ReadUInt32();
        ulong bitCount = reader.ReadUInt64();
        if (bitCount != checked((ulong)width * height))
            throw new InvalidDataException("Land-mask bit count does not match its dimensions.");
        if (tileCodec is not 0 and not 1)
            throw new InvalidDataException($"Unsupported land-mask tile codec: {tileCodec}");
        int tileCount = checked((int)(tileColumns * tileRows));
        var tileEntries = new LandMaskTileEntry[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            ulong offset = reader.ReadUInt64();
            uint byteLength = reader.ReadUInt32();
            uint uncompressedByteLength = reader.ReadUInt32();
            uint landPixelCount = reader.ReadUInt32();
            tileEntries[i] = new LandMaskTileEntry(
                checked((long)offset),
                checked((int)byteLength),
                checked((int)uncompressedByteLength),
                checked((int)landPixelCount));
        }
        if (stream.Position > stream.Length)
            throw new InvalidDataException("Land-mask tile index exceeds file length.");
        foreach (var entry in tileEntries)
        {
            if (entry.ByteLength == 0)
                continue;
            long end = checked(entry.Offset + entry.ByteLength);
            if (end > stream.Length)
                throw new InvalidDataException("Land-mask tile payload exceeds file length.");
        }
        return RasterLandMask.CreateTiled(
            checked((int)width),
            checked((int)height),
            pixelSizeM,
            minX,
            minY,
            maxX,
            maxY,
            checked((int)dilationPixels),
            checked((int)tileWidth),
            checked((int)tileHeight),
            checked((int)tileColumns),
            checked((int)tileRows),
            path,
            tileEntries,
            checked((int)tileCodec));
    }
}
