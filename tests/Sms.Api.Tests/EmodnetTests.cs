using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

// EMODnet vessel density and DTM (spec-emodnet-density §9): readers, projection, the per-edge rules

public class GeoTiffTests
{
    private static GeoTiff Fixture(string name) { using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", name)); return GeoTiff.Open(s); }

    [Fact]
    public void Reads_a_deflate_stripped_little_endian_float_tiff()
    {
        var t = Fixture("vesseldensity_04_2023_fehmarn.tif"); // EPSG:3035, 1 km cells, one row per strip
        Assert.Equal((50, 40), (t.Width, t.Height));
        Assert.Equal((4_420_000.0, 3_530_000.0, 1000.0, 1000.0), (t.OriginX, t.OriginY, t.PixelW, t.PixelH));
        Assert.Equal(-3.4028235e38f, t.NoData);
        var v = t.ReadAll();
        Assert.Equal(1118.29, v.Max(), 2);
        Assert.Equal(20, v.Count(x => x >= 10));
        Assert.Equal(479, v.Count(x => x == t.NoData));
        Assert.Equal(v, t.Rows().SelectMany(r => r.values).ToArray());
    }

    [Fact]
    public void Reads_an_uncompressed_tiled_big_endian_wcs_tiff()
    {
        var t = Fixture("dtm_sthlm_wcs.tif"); // EMODnet Bathymetry WCS, EPSG:4326, 1/960° cells, 160 × 48 tiles
        Assert.Equal((163, 48), (t.Width, t.Height));
        Assert.Equal(18.1697917, t.OriginX, 6); Assert.Equal(59.3302083, t.OriginY, 6);
        Assert.Equal(1.0 / 960, t.PixelW, 9); Assert.Equal(1.0 / 960, t.PixelH, 9);
        var v = t.ReadAll();
        int row = (int)Math.Floor((t.OriginY - 59.3010) / t.PixelH), col = (int)Math.Floor((18.3150 - t.OriginX) / t.PixelW);
        Assert.Equal((28, 139), (row, col));
        Assert.Equal(-45.2f, v[row * t.Width + col], 3);
        Assert.DoesNotContain(v, float.IsNaN);
        Assert.Equal(-58.1f, v.Min(), 3); Assert.Equal(74.3f, v.Max(), 1);
        Assert.Throws<NotSupportedException>(() => t.Rows().ToList()); // row access is for stripped files
    }

    [Fact]
    public void Dtm_values_become_decimetres_of_depth_and_land_is_no_data()
    {
        Assert.Equal(27, DtmImport.ToDecimetres(-2.74f));
        Assert.Equal(short.MinValue, DtmImport.ToDecimetres(-2.74f, excluded: true));
        Assert.Equal(short.MinValue, DtmImport.ToDecimetres(5f));
        Assert.Equal(short.MinValue, DtmImport.ToDecimetres(0f));
        Assert.Equal(short.MinValue, DtmImport.ToDecimetres(float.NaN));
        // the fixture placed into its 1° chunk by the geotransform: the cell at 59.3010 N 18.3150 E keeps its −45.2 m, the chunk outside the fixture is no data
        var t = Fixture("dtm_sthlm_wcs.tif");
        var chunk = DtmImport.Chunk(DtmRaster.FromGeoTiff(t), 18, 59).Decimetres(measuredOnly: true);
        Assert.Equal(452, chunk[(int)Math.Floor((60 - 59.3010) * 960) * 960 + (int)Math.Floor(0.3150 * 960)]);
        Assert.Equal(short.MinValue, chunk[0]);
    }
}

public class LaeaTests
{
    [Theory]
    [InlineData(4_321_000, 3_210_000, 10.0, 52.0)]
    [InlineData(4_420_500, 3_529_500, 11.5490702, 54.8615490)]
    [InlineData(4_469_500, 3_490_500, 12.2918762, 54.4995199)]
    [InlineData(4_500_000, 3_600_000, 12.8298942, 55.4730939)]
    public void Inverse_matches_proj(double x, double y, double lng, double lat)
    {
        var (l, p) = Laea.Inverse(x, y);
        Assert.Equal(lng, l, 6); Assert.Equal(lat, p, 6);
    }
}

public class TrafficAreasTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static readonly double Dy = 1000 / 111_320.0, Dx = Dy / Math.Cos(54.5 * Math.PI / 180);

    [Fact]
    public void Edge_half_inside_a_dense_cell_is_traffic()
    {
        var dense = F.CreatePolygon([new(10, 54.5), new(10 + Dx, 54.5), new(10 + Dx, 54.5 + Dy), new(10, 54.5 + Dy), new(10, 54.5)]);
        var traffic = new TrafficAreas([dense]); // the next cell east (sailing < threshold) is not loaded at all
        LineString L(double from, double to) => F.CreateLineString([new(10 + from * Dx, 54.5 + Dy / 2), new(10 + to * Dx, 54.5 + Dy / 2)]);
        Assert.True(traffic.Along(L(0.4, 1.4)));   // 60 % inside
        Assert.False(traffic.Along(L(0.6, 1.6)));  // 40 % inside
        Assert.True(traffic.Contains(new Coordinate(10 + Dx / 2, 54.5 + Dy / 2)));
        Assert.False(TrafficAreas.Empty.Along(L(0.4, 1.4)));
    }

    [Fact]
    public void Density_file_cells_are_averaged_projected_and_named()
    {
        Assert.Equal(("04", 2023), TrafficImport.ParseName("vesseldensity_04_2023.tif"));
        Assert.Equal(("05", 2021), TrafficImport.ParseName("vesseldensity_05_2021_fehmarn.tif"));
        Assert.Null(TrafficImport.ParseName("vesseldensity_08_2023.tif"));
        Assert.Null(TrafficImport.ParseName("density.tif"));
        GeoTiff Open() { using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "vesseldensity_04_2023_fehmarn.tif")); return GeoTiff.Open(s); }
        var region = new Envelope(4.5, 19.5, 52, 60);
        var cells = TrafficImport.Cells([("04", Open())], region);
        Assert.Equal(20, cells.Count(c => c.Sailing >= 10));
        Assert.Equal(1093, cells.Count); // sailing ≥ 0.1, no pleasure file
        Assert.All(cells, c => { Assert.InRange(c.Cell.Centroid.X, 11.4, 12.4); Assert.InRange(c.Cell.Centroid.Y, 54.4, 54.9); Assert.Equal(0, c.Pleasure); });
        // the cell polygon is the projected 1 km square: about 1 km² in a local metric frame
        var cell = cells[0].Cell; var cos = Math.Cos(cell.Centroid.Y * Math.PI / 180);
        Assert.InRange(cell.Area * cos * 111_320 * 111_320, 0.97e6, 1.03e6);
        // two files of a kind are averaged (the same file twice: unchanged); a pleasure file alone fills pleasure only
        var twice = TrafficImport.Cells([("04", Open()), ("04", Open())], region);
        Assert.Equal(cells.Max(c => c.Sailing), twice.Max(c => c.Sailing), 6);
        Assert.All(TrafficImport.Cells([("05", Open())], region), c => Assert.Equal(0, c.Sailing));
        Assert.Empty(TrafficImport.Cells([("04", Open())], new Envelope(4.5, 6, 52, 53))); // outside the region
    }
}

public class DtmGridTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private const int N = 6; // a 6 × 6 chunk: cells of 1/6°
    private static Coordinate Centre(int lng0, int row, int col) => new(lng0 + (col + 0.5) / N, 54 + 1 - (row + 0.5) / N);

    private static DtmGrid Grid(bool withEast)
    {
        var west = Enumerable.Repeat((short)30, N * N).ToArray();
        west[0] = short.MinValue; // land in the north-west corner
        west[3 * N + 3] = 25;     // one cell 2.5 m deep
        var east = Enumerable.Repeat((short)40, N * N).ToArray();
        return new DtmGrid(k => k == (10, 54) ? west : k == (11, 54) && withEast ? east : null, N);
    }

    [Fact]
    public void A_water_cell_counts_on_its_own_and_min_is_the_shallowest()
    {
        var g = Grid(withEast: true);
        Assert.Null(g.Depth(Centre(10, 0, 0).Y, Centre(10, 0, 0).X)); // land
        Assert.Equal(3.0, g.Depth(Centre(10, 1, 1).Y, Centre(10, 1, 1).X)); // next to land: a one-cell-wide sound counts
        Assert.Equal(2.5, g.Depth(Centre(10, 3, 3).Y, Centre(10, 3, 3).X));
        Assert.Null(g.Depth(Centre(10, 3, 3).Y + 1, Centre(10, 3, 3).X)); // no chunk there
        LineString L(Coordinate a, Coordinate b) => F.CreateLineString([a, b]);
        Assert.Equal(2.5, g.MinDepth(L(Centre(10, 3, 1), Centre(10, 3, 4))));
        Assert.Equal(3.0, g.MinDepth(L(Centre(10, 1, 0), Centre(10, 1, 2)))); // along the shore
        Assert.Null(g.MinDepth(L(Centre(10, 0, 0), Centre(10, 0, 2))));        // a quarter of it over the land cell: < 90 % water
        var across = L(Centre(10, 4, 4), Centre(11, 4, 1));                    // across the chunk border into the 4 m chunk
        Assert.Equal(3.0, g.MinDepth(across));
        Assert.Null(Grid(withEast: false).MinDepth(across));
        Assert.Null(DtmGrid.Empty.MinDepth(across));
    }

    [Fact]
    public void Erddap_subset_keeps_the_water_cells_with_their_quality_fields()
    {
        // ERDDAP griddap bathymetry_dtm_2024.nc, all seven variables, Baggensstäket 59.2955–59.3026 N 18.25–18.30 E
        var r = DtmRaster.FromNetCdf(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "dtm_baggensstaket_erddap.nc")));
        Assert.Equal((49, 8), (r.Width, r.Height));
        Assert.Equal(18.25, r.West, 6); Assert.Equal(59.303125, r.North, 6); Assert.Equal(1.0 / 960, r.StepX, 9);
        Assert.Equal(DtmImport.Variables.Length, r.Vars.Count);
        Assert.Equal(-18.88, r.Vars["elevation"][5 * 49 + 12], 2); Assert.Equal(0, r.Vars["interpolation_flag"][5 * 49 + 12]); Assert.Equal(7430, r.Vars["cdi_index"][5 * 49 + 12]);
        var chunk = DtmImport.Chunk(r, 18, 59);
        Assert.Equal(49, chunk.Count);                                   // water cells only
        Assert.Equal(8, chunk.Interpolated.Count(f => f == 0));          // with soundings
        Assert.All(chunk.Elevation, e => Assert.True(e < 0));
        var measured = chunk.Decimetres(measuredOnly: true); var all = chunk.Decimetres(measuredOnly: false);
        int At(double lat, double lng) => (int)Math.Floor((60 - lat) * 960) * 960 + (int)Math.Floor((lng - 18) * 960);
        Assert.Equal(189, measured[At(59.297396, 18.263021)]);
        Assert.Equal(8, measured.Count(v => v != short.MinValue)); Assert.Equal(49, all.Count(v => v != short.MinValue));
    }
}
