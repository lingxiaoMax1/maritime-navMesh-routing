using MaritimeNavMesh.Core.Geometry;

namespace MaritimeNavMesh.Tests.Core;

public sealed class RasterLandMaskTests
{
    [Fact]
    public void IsSegmentLandSafe_AllWater_ReturnsTrue()
    {
        var mask = CreateMask([]);
        Assert.True(mask.IsSegmentLandSafe(0.0, 0.0, 0.03, 0.0));
    }

    [Fact]
    public void IsSegmentLandSafe_LandPixelOnSegment_ReturnsFalse()
    {
        var mask = CreateMask([(2, 3)]);
        Assert.False(mask.IsSegmentLandSafe(0.0, 0.0, 0.03, 0.0));
    }

    [Fact]
    public void IsSegmentLandSafe_OutsideMask_ReturnsFalse()
    {
        var mask = CreateMask([]);
        Assert.False(mask.IsSegmentLandSafe(-10.0, 0.0, 0.01, 0.0));
    }

    internal static RasterLandMask CreateMask((int Row, int Col)[] landCells)
    {
        const int width = 8;
        const int height = 5;
        var bits = new byte[(width * height + 7) / 8];
        foreach (var (row, col) in landCells)
        {
            int bit = row * width + col;
            bits[bit >> 3] |= (byte)(1 << (7 - (bit & 7)));
        }
        return new RasterLandMask(width, height, 1000.0, -1000.0, -2500.0, 7000.0, 2500.0, 0, bits);
    }
}
