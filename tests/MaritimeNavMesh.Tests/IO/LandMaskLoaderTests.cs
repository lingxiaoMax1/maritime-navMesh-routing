using System.Text;
using System.IO.Compression;
using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.IO.Loaders;

namespace MaritimeNavMesh.Tests.IO;

public sealed class LandMaskLoaderTests
{
    [Fact]
    public void Load_ReadsVersionedBitmask()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("OCNLMSK1"));
                writer.Write(1u);
                writer.Write(2u);
                writer.Write(2u);
                writer.Write(500.0);
                writer.Write(0.0);
                writer.Write(0.0);
                writer.Write(1000.0);
                writer.Write(1000.0);
                writer.Write(1u);
                writer.Write(4ul);
                writer.Write((byte)0b10000000);
            }

            var mask = LandMaskLoader.Load(path);
            Assert.Equal(2, mask.Width);
            Assert.Equal(1, mask.DilationPixels);
            Assert.True(mask.IsLand(0, 0));
            Assert.False(mask.IsLand(0, 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ReadsCompressedTiledBitmask()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] packedTile = [(byte)0b10000000];
            byte[] compressedTile;
            using (var ms = new MemoryStream())
            {
                using var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true);
                z.Write(packedTile, 0, packedTile.Length);
                z.Close();
                compressedTile = ms.ToArray();
            }
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("OCNLMSK1"));
                writer.Write(3u);
                writer.Write(2u);
                writer.Write(2u);
                writer.Write(500.0);
                writer.Write(0.0);
                writer.Write(0.0);
                writer.Write(1000.0);
                writer.Write(1000.0);
                writer.Write(1u);
                writer.Write(2u);
                writer.Write(2u);
                writer.Write(1u);
                writer.Write(1u);
                writer.Write(1u);
                writer.Write(4ul);
                writer.Write(112ul);
                writer.Write((uint)(compressedTile.Length + 1));
                writer.Write(1u);
                writer.Write(1u);
                writer.Write((byte)1);
                writer.Write(compressedTile);
            }

            var mask = LandMaskLoader.Load(path);
            Assert.Equal(2, mask.Width);
            Assert.True(mask.IsTiled);
            Assert.Equal(2, mask.TileWidthPixels);
            Assert.True(mask.IsLand(0, 0));
            Assert.False(mask.IsLand(0, 1));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
