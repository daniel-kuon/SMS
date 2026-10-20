using NetTopologySuite.Geometries;

namespace Sms.Domain.Tests;

public class FeedbackRegressionTests
{
    private static readonly GeometryFactory F = new();
    // east-west lines at 55° N: 0.01° of longitude ≈ 640 m, 0.0001° of latitude ≈ 11 m
    private static LineString Line(double lat, double fromLng, double toLng) => F.CreateLineString([new(fromLng, lat), new(toLng, lat)]);

    [Theory]
    [InlineData("Fahrwasser (4 Tonnenpaare)", CorrectionSource.Derived, "fairway")]
    [InlineData("Annahme: Tonnenlücke", CorrectionSource.Derived, "assumed")]
    [InlineData("Enge (abgeleitet, 0.4 sm)", CorrectionSource.Derived, "narrow")]
    [InlineData("Wasserstraße (3 OSM-Wege)", CorrectionSource.Derived, "osm")]
    [InlineData("Fahrwasserfläche Grønsund", CorrectionSource.Derived, "osm")]
    [InlineData("Fahrwasser (2 Tonnenpaare)", CorrectionSource.Manual, "manual")]
    public void Categories_follow_the_derived_note(string note, CorrectionSource source, string expected) =>
        Assert.Equal(expected, FeedbackRegression.PassageCategory(note, source));

    [Fact]
    public void Coverage_is_the_share_of_length_near_a_candidate()
    {
        var drawn = Line(55, 11.90, 11.92);
        Assert.Equal(1, FeedbackRegression.Coverage(drawn, [Line(55.0003, 11.89, 11.93)], 50), 2); // 33 m off, longer
        Assert.Equal(0.5, FeedbackRegression.Coverage(drawn, [Line(55.0003, 11.90, 11.91)], 50), 1); // half of it
        Assert.Equal(0, FeedbackRegression.Coverage(drawn, [Line(55.001, 11.90, 11.92)], 50)); // 111 m off
        Assert.Equal(0, FeedbackRegression.Coverage(drawn, [], 50));
    }

    [Fact]
    public void A_missing_fairway_is_satisfied_from_80_percent_on()
    {
        var drawn = Line(55, 11.90, 11.92);
        var found = FeedbackRegression.CheckMissing(drawn, [Line(55.0002, 11.90, 11.905), Line(55.0002, 11.905, 11.918)]); // 90 %
        Assert.True(found.Satisfied);
        Assert.Equal("present", found.Status);
        var partly = FeedbackRegression.CheckMissing(drawn, [Line(55, 11.90, 11.91)]);
        Assert.False(partly.Satisfied);
        Assert.Equal("partly", partly.Status);
        Assert.Equal("missing", FeedbackRegression.CheckMissing(drawn, [Line(55.01, 11.90, 11.92)]).Status);
    }

    [Fact]
    public void A_wrong_line_still_there_is_present_and_identical()
    {
        var wrong = Line(55, 11.90, 11.92);
        var c = FeedbackRegression.CheckWrong(wrong, [Line(55.05, 11.90, 11.92), Line(55.0001, 11.90, 11.92)]);
        Assert.Equal("present", c.Status);
        Assert.False(c.Satisfied);
        Assert.True(c.Identical);
        Assert.Equal(1, c.Best);
    }

    [Fact]
    public void A_wrong_line_inside_a_longer_one_is_present_but_not_identical()
    {
        var c = FeedbackRegression.CheckWrong(Line(55, 11.90, 11.92), [Line(55, 11.85, 11.97)]);
        Assert.Equal("present", c.Status);
        Assert.False(c.Identical);
    }

    [Fact]
    public void A_removed_wrong_line_is_gone_and_a_shortened_one_partly()
    {
        var wrong = Line(55, 11.90, 11.92);
        var gone = FeedbackRegression.CheckWrong(wrong, [Line(55.01, 11.90, 11.92)]);
        Assert.Equal("gone", gone.Status);
        Assert.True(gone.Satisfied);
        var partly = FeedbackRegression.CheckWrong(wrong, [Line(55, 11.90, 11.91)]);
        Assert.Equal("partly", partly.Status);
        Assert.Null(partly.Satisfied);
    }

    [Fact]
    public void Points_are_not_judged() =>
        Assert.Equal("unchecked", FeedbackRegression.CheckWrong(F.CreatePoint(new Coordinate(11.9, 55)), [Line(55, 11.8, 12)]).Status);
}

public class CorrectedObjectRegressionTests
{
    private static readonly GeometryFactory F = new();
    private static LineString L(params (double lng, double lat)[] c) => F.CreateLineString(c.Select(p => new Coordinate(p.lng, p.lat)).ToArray());
    // the derived line makes a detour north (≈ 1.1 km off) in its middle; the correction runs straight along 55° N
    private static readonly LineString Original = L((11.90, 55), (11.91, 55), (11.915, 55.01), (11.92, 55), (11.93, 55));
    private static readonly LineString Corrected = L((11.90, 55), (11.93, 55));

    [Fact]
    public void Satisfied_when_the_new_course_exists_and_the_detour_is_gone()
    {
        var c = FeedbackRegression.CheckCorrected(Original, Corrected, [Corrected], [Corrected]);
        Assert.Equal("corrected", c.Status);
        Assert.True(c.Satisfied);
        Assert.Equal(0, c.WrongPartsCoverage);
        Assert.InRange(c.WrongPartsM, 1500, 2600); // the two legs of the detour beyond 50 m
    }

    [Fact]
    public void Not_satisfied_while_the_original_is_still_derived()
    {
        var unchanged = FeedbackRegression.CheckCorrected(Original, Corrected, [Original], [Original]);
        Assert.False(unchanged.Satisfied);
        Assert.Equal("partly", unchanged.Status); // only the straight ends lie on the corrected line
        Assert.Equal(1, unchanged.WrongPartsCoverage, 2);
        var both = FeedbackRegression.CheckCorrected(Original, Corrected, [Original, Corrected], [Original, Corrected]);
        Assert.Equal("wrongPartsRemain", both.Status);
        Assert.False(both.Satisfied);
    }

    [Fact]
    public void Missing_when_nothing_runs_along_the_correction() =>
        Assert.Equal("missing", FeedbackRegression.CheckCorrected(Original, Corrected, [], []).Status);

    [Fact]
    public void An_extension_has_no_wrong_parts()
    {
        var longer = L((11.90, 55), (11.95, 55));
        var c = FeedbackRegression.CheckCorrected(Corrected, longer, [longer], [longer]);
        Assert.True(c.Satisfied);
        Assert.Equal(0, c.WrongPartsM);
    }
}
