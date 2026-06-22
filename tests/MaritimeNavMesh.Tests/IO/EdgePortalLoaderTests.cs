using System.Text;
using MaritimeNavMesh.Core.Graph;
using MaritimeNavMesh.IO.Loaders;

namespace MaritimeNavMesh.Tests.IO;

public sealed class EdgePortalLoaderTests
{
    [Fact]
    public void Load_CurrentPortalBinary_ReconstructsImplicitAndExplicitPortals()
    {
        const long h3A = 602328092639231999L;
        const long h3B = 602328072238137343L;
        const long h3C = 601044364495421439L;
        string path = Path.GetTempFileName();
        try
        {
            var graph = new CsrOceanGraph(
                resolution: 5,
                nodeCount: 3,
                edgeCount: 3,
                nodeH3Int: [h3A, h3B, h3C],
                nodeLat: [0f, 0f, 0f],
                nodeLon: [0f, 0f, 0f],
                nodeComponent: [0, 0, 0],
                nodeClass: [1, 1, 1],
                rowPtr: [0u, 2u, 3u, 3u],
                colIdx: [1u, 2u, 0u],
                edgeCost: [1f, 1f, 1f],
                edgeMinDepthM: null,
                edgeFlags: null);

            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("OCNPRTL1"));
                writer.Write(1u);
                writer.Write(3ul);
                writer.Write(1ul);
                writer.Write(1ul);
                writer.Write(1u);
                writer.Write(1u);
                writer.Write(4u);
                writer.Write(16u);
                writer.Write((byte)0b00000111);
                writer.Write((byte)0b00000010);
                writer.Write(0u);
                writer.Write(10.0f);
                writer.Write(20.0f);
                writer.Write(30.0f);
                writer.Write(40.0f);
            }

            var portals = EdgePortalLoader.Load(path, graph);
            Assert.Equal(3, portals.EdgeCount);
            Assert.Equal(1, portals.UniquePortalCount);

            Assert.True(portals.TryGetPortal(0, out var implicitForward));
            Assert.True(portals.TryGetPortal(2, out var implicitReverse));
            Assert.Equal(implicitForward, implicitReverse);
            Assert.True(Math.Abs(implicitForward.Ax - implicitForward.Bx) + Math.Abs(implicitForward.Ay - implicitForward.By) > 0.0);

            Assert.True(portals.TryGetPortal(1, out var explicitPortal));
            Assert.Equal(10.0, explicitPortal.Ax, 6);
            Assert.Equal(20.0, explicitPortal.Ay, 6);
            Assert.Equal(30.0, explicitPortal.Bx, 6);
            Assert.Equal(40.0, explicitPortal.By, 6);
            Assert.Equal(2, portals.UniquePortalCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_BadMagic_Throws()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("badmagic"));
            var graph = new CsrOceanGraph(5, 0, 0, [], [], [], [], [], [0u], [], [], null, null);
            Assert.Throws<InvalidDataException>(() => EdgePortalLoader.Load(path, graph));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
