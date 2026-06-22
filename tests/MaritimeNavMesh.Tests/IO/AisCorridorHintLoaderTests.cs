using System.Text;
using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.IO.Loaders;

namespace MaritimeNavMesh.Tests.IO;

public sealed class AisCorridorHintLoaderTests
{
    [Fact]
    public void Load_ReadsSingleHintRecord()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
            {
                writer.Write(Encoding.ASCII.GetBytes("OCNAISH1"));
                writer.Write((uint)1);
                writer.Write((uint)1);
                writer.Write(1);
                writer.Write(EncodeLocode("AUMEL"));
                writer.Write((byte)1);
                writer.Write((byte)1);
                writer.Write((ushort)0);
                writer.Write(0.9f);
                writer.Write((uint)12);
                writer.Write((uint)2);
                writer.Write((uint)1);
                writer.Write(144.9f);
                writer.Write(-37.9f);
                writer.Write(145.0f);
                writer.Write(-37.8f);
                writer.Write(144.90f);
                writer.Write(-37.90f);
                writer.Write(145.00f);
                writer.Write(-37.80f);
                writer.Write((uint)10);
                writer.Write((uint)11);
            }

            var set = AisCorridorHintLoader.Load(path);
            Assert.True(set.TryGetPortApproach("AUMEL", out var hint));
            Assert.Equal(1, hint.CorridorId);
            Assert.Equal(0.9f, hint.Confidence);
            Assert.Equal(2, hint.Coordinates.Length);
            Assert.Single(hint.EdgeSpans);
            Assert.Equal(10, hint.EdgeSpans[0].FromNode);
            Assert.Equal(11, hint.EdgeSpans[0].ToNode);
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public void Load_ReadsGenericRouteWindowHint()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
            {
                writer.Write(Encoding.ASCII.GetBytes("OCNAISH1"));
                writer.Write((uint)1);
                writer.Write((uint)1);
                writer.Write(9);
                writer.Write(EncodeLocode(""));
                writer.Write((byte)AisCorridorHintSet.RouteWindowKind);
                writer.Write((byte)AisCorridorHintSet.BidirectionalFlag);
                writer.Write((ushort)0);
                writer.Write(0.8f);
                writer.Write((uint)20);
                writer.Write((uint)3);
                writer.Write((uint)2);
                writer.Write(100.0f);
                writer.Write(1.0f);
                writer.Write(100.2f);
                writer.Write(1.2f);
                writer.Write(100.00f);
                writer.Write(1.00f);
                writer.Write(100.10f);
                writer.Write(1.10f);
                writer.Write(100.20f);
                writer.Write(1.20f);
                writer.Write((uint)3);
                writer.Write((uint)4);
                writer.Write((uint)4);
                writer.Write((uint)5);
            }

            var set = AisCorridorHintLoader.Load(path);
            Assert.Single(set.RouteWindows);
            Assert.Equal(9, set.RouteWindows[0].CorridorId);
            Assert.Equal(2, set.RouteWindows[0].EdgeSpans.Length);
            Assert.Equal(3, set.RouteWindows[0].Coordinates.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] EncodeLocode(string locode)
    {
        var bytes = new byte[16];
        Encoding.ASCII.GetBytes(locode, 0, locode.Length, bytes, 0);
        return bytes;
    }
}
