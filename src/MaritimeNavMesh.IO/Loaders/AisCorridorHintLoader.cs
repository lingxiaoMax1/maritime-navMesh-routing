using System.Text;
using MaritimeNavMesh.Core.Geometry;

namespace MaritimeNavMesh.IO.Loaders;

public static class AisCorridorHintLoader
{
    private const string Magic = "OCNAISH1";
    private const uint Version = 1;
    private const uint FixedRecordSizeBytes = 56;

    public static AisCorridorHintSet Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (magic != Magic)
            throw new InvalidDataException($"Invalid AIS corridor-hint magic: {magic}");
        uint version = reader.ReadUInt32();
        if (version != Version)
            throw new InvalidDataException($"Unsupported AIS corridor-hint version: {version}");
        uint hintCount = reader.ReadUInt32();

        var hints = new List<AisCorridorHint>(checked((int)hintCount));
        for (int i = 0; i < hintCount; i++)
        {
            int corridorId = reader.ReadInt32();
            string locode = DecodeLocode(reader.ReadBytes(16));
            byte kind = reader.ReadByte();
            byte flags = reader.ReadByte();
            reader.ReadUInt16();
            float confidence = reader.ReadSingle();
            int supportCount = checked((int)reader.ReadUInt32());
            int pointCount = checked((int)reader.ReadUInt32());
            int edgeSpanCount = checked((int)reader.ReadUInt32());
            double minLon = reader.ReadSingle();
            double minLat = reader.ReadSingle();
            double maxLon = reader.ReadSingle();
            double maxLat = reader.ReadSingle();

            var coordinates = new double[pointCount][];
            for (int p = 0; p < pointCount; p++)
                coordinates[p] = [reader.ReadSingle(), reader.ReadSingle()];

            var edgeSpans = new AisCorridorEdgeSpan[edgeSpanCount];
            for (int e = 0; e < edgeSpanCount; e++)
                edgeSpans[e] = new AisCorridorEdgeSpan(checked((int)reader.ReadUInt32()), checked((int)reader.ReadUInt32()));

            hints.Add(new AisCorridorHint(
                corridorId,
                locode,
                kind,
                flags,
                confidence,
                supportCount,
                coordinates,
                edgeSpans,
                minLon,
                minLat,
                maxLon,
                maxLat));
        }

        if (stream.Position != stream.Length)
            throw new InvalidDataException("AIS corridor-hint payload length does not match its header.");

        return new AisCorridorHintSet(hints);
    }

    public static uint GetFixedRecordSizeBytes() => FixedRecordSizeBytes;

    private static string DecodeLocode(byte[] raw)
    {
        int end = Array.IndexOf(raw, (byte)0);
        if (end < 0)
            end = raw.Length;
        return Encoding.ASCII.GetString(raw, 0, end);
    }
}
