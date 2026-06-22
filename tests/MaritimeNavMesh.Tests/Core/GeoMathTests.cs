using MaritimeNavMesh.Core.Geometry;

namespace MaritimeNavMesh.Tests.Core;

public sealed class GeoMathTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]                           // same point
    [InlineData(-37.81, 144.96, 1.35, 103.82, 3274, 50)] // Melbourne to Singapore ~3274 nm
    [InlineData(51.5, -0.12, 48.85, 2.35, 181, 5)]        // London to Paris ~181 nm
    public void HaversineNm_KnownValues(double lat1, double lon1, double lat2, double lon2,
        double expectedNm, double toleranceNm)
    {
        double result = GeoMath.HaversineNm(lat1, lon1, lat2, lon2);
        Assert.InRange(result, expectedNm - toleranceNm, expectedNm + toleranceNm);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(180, 180)]
    [InlineData(-180, -180)]
    public void HaversineNm_SamePoint_IsZero(double lat, double lon)
    {
        Assert.Equal(0, GeoMath.HaversineNm(lat, lon, lat, lon), precision: 6);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(-5, -5)]
    [InlineData(175, 175)]
    [InlineData(-175, -175)]
    public void NormalizeLonDelta_SmallDelta_Unchanged(double delta, double expected)
    {
        // Small deltas should pass through unchanged
        double normalized = GeoMath.NormalizeLonDelta(delta);
        Assert.Equal(expected, normalized, precision: 6);
    }

    [Fact]
    public void NormalizeLonDelta_CrossAntiMeridian_WrapsCorrectly()
    {
        // Ship at lon=179, moves to lon=-179: actual delta = -2°, raw delta = -358°
        double rawDelta = -179.0 - 179.0; // = -358
        double normalized = GeoMath.NormalizeLonDelta(rawDelta);
        Assert.Equal(2.0, normalized, precision: 6);
    }
}
