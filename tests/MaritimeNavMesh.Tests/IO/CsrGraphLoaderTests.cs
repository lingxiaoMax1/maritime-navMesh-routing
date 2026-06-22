using MaritimeNavMesh.IO.Loaders;
using MaritimeNavMesh.Tests.Fixtures;

namespace MaritimeNavMesh.Tests.IO;

public sealed class CsrGraphLoaderTests : IDisposable
{
    private readonly string _tempFile;

    public CsrGraphLoaderTests()
    {
        _tempFile = SyntheticGraphBuilder.WriteBinaryToTempFile();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public void Load_CurrentRuntimeBinary_ReturnsCorrectCounts()
    {
        var graph = CsrGraphLoader.Load(_tempFile);
        Assert.Equal(SyntheticGraphBuilder.NodeCount, graph.NodeCount);
        Assert.Equal(SyntheticGraphBuilder.EdgeCount, graph.EdgeCount);
        Assert.Equal(SyntheticGraphBuilder.Resolution, graph.Resolution);
    }

    [Fact]
    public void Load_CurrentRuntimeBinary_ReconstructsRowPtrAndCompressedColIdx()
    {
        var graph = CsrGraphLoader.Load(_tempFile);
        Assert.Equal(SyntheticGraphBuilder.H3Ints, graph.NodeH3Int.ToArray());
        Assert.Equal(SyntheticGraphBuilder.RowPtr, graph.RowPtr);
        Assert.Equal(SyntheticGraphBuilder.ColIdx, graph.ColIdx);
        Assert.Equal(SyntheticGraphBuilder.Lats.Length, graph.NodeLat.Length);
        Assert.Equal(SyntheticGraphBuilder.Lons.Length, graph.NodeLon.Length);
        Assert.Null(graph.EdgeMinDepthM);
        Assert.Null(graph.EdgeFlags);
    }

    [Fact]
    public void Load_CurrentRuntimeBinary_RowPtrIsMonotonic()
    {
        var graph = CsrGraphLoader.Load(_tempFile);
        for (int i = 1; i <= graph.NodeCount; i++)
            Assert.True(graph.RowPtr[i] >= graph.RowPtr[i - 1]);
    }

    [Fact]
    public void LoadAndVerifyHash_CorrectHash_Succeeds()
    {
        string hash = CsrGraphLoader.ComputeSha256(_tempFile);
        var graph = CsrGraphLoader.LoadAndVerifyHash(_tempFile, hash);
        Assert.Equal(SyntheticGraphBuilder.NodeCount, graph.NodeCount);
    }

    [Fact]
    public void LoadAndVerifyHash_WrongHash_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            CsrGraphLoader.LoadAndVerifyHash(_tempFile, "deadbeef"));
    }

    [Fact]
    public void Load_BadMagic_Throws()
    {
        byte[] data = SyntheticGraphBuilder.WriteBinary();
        data[0] = 0xFF;
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, data);
            Assert.Throws<InvalidDataException>(() => CsrGraphLoader.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_FileTooSmall_Throws()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [0x4F, 0x43, 0x4E]);
            Assert.Throws<InvalidDataException>(() => CsrGraphLoader.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_NonExistentFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => CsrGraphLoader.Load("/nonexistent/path.bin"));
    }
}
