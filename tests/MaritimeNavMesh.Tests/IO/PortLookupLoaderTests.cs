using System.Text.Json;
using MaritimeNavMesh.IO.Loaders;

namespace MaritimeNavMesh.Tests.IO;

public sealed class PortLookupLoaderTests
{
    [Fact]
    public void Load_CompactRuntimeSchema_ParsesPorts()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test-ports-{Guid.NewGuid():N}.json");
        try
        {
            var payload = new
            {
                schema = "ocean_ports_runtime",
                schemaVersion = 2,
                fields = new[]
                {
                    "locode",
                    "name",
                    "port_lat",
                    "port_lon",
                    "snapped_h3",
                    "snapped_lat",
                    "snapped_lon",
                    "snap_distance_nm",
                    "marine_access_h3",
                    "marine_access_lat",
                    "marine_access_lon",
                    "marine_access_source",
                    "marine_access_path_coordinates",
                    "marine_access_path_is_approximate",
                    "marine_access_path_land_overlap_nm",
                    "marine_access_path_is_land_safe",
                    "marine_access_display_path_coordinates",
                    "component_id",
                },
                ports = new object[][]
                {
                    new object[]
                    {
                        "TESTA",
                        "Port A",
                        1.25,
                        2.5,
                        "64",
                        1.0,
                        2.0,
                        0.5,
                        "65",
                        1.1,
                        2.1,
                        "graph_neighbor_search",
                        new double[][] { [2.5, 1.25], [2.1, 1.1] },
                        true,
                        0.0,
                        true,
                        new double[][] { [2.5, 1.25], [2.4, 1.2] },
                        0,
                    },
                },
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload));

            var ports = PortLookupLoader.Load(path);

            Assert.Single(ports);
            Assert.Equal("TESTA", ports[0].Locode);
            Assert.Equal("64", ports[0].SnappedH3Hex);
            Assert.Equal("65", ports[0].MarineAccessH3Hex);
            Assert.Equal("graph_neighbor_search", ports[0].MarineAccessSource);
            Assert.True(ports[0].MarineAccessPathIsLandSafe);
            Assert.NotNull(ports[0].MarineAccessPathCoordinates);
            Assert.Equal(0, ports[0].ComponentId);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
