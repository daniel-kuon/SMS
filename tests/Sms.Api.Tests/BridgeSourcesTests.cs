using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

/// <summary>Parsers of the bridge clearance imports (docs: data-sources plan A/C) on saved pages: Danske Havnelods bridge regulations, the Wikidata list.</summary>
public class BridgeSourcesTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Fact]
    public void Havnelods_list_has_the_46_danish_bridges()
    {
        var list = Havnelods.ParseList(Fixture("havnelods-broer.js"));
        Assert.Equal(46, list.Count);
        var f = Assert.Single(list, e => e.Name == "Kong Frederik IX's Bro");
        Assert.Equal(54.7588, f.Lat, 3);
        Assert.Equal(11.8662, f.Lng, 3);
        Assert.Equal("https://www.danskehavnelods.dk/Havnelodsen/bridges/c741b465-9807-4b19-b1b5-db9e00c332af.html", f.Url);
    }

    [Fact]
    public void Frederik_ix_is_an_opening_bridge_4_m_closed_3_6_m_under_the_fixed_spans()
    {
        var p = Havnelods.ParseBridge(Fixture("havnelods-frederik-ix.html"));
        Assert.True(p.Opening);
        Assert.Equal("Enkeltfløjet klapbro", p.Type);
        Assert.Equal(4.0, p.ClearanceM);
        Assert.Equal(3.6, p.FixedSpanM);
        Assert.StartsWith("Skibe og fartøjer kan vederlagsfrit", p.OpeningHours);
        var note = Havnelods.Note(new Havnelods.ListEntry("c741b465-9807-4b19-b1b5-db9e00c332af", "Kong Frederik IX's Bro", 54.75885, 11.8662), p, p.ClearanceM);
        Assert.StartsWith("Havnelods: Kong Frederik IX's Bro – bewegliche Brücke (Enkeltfløjet klapbro), geschlossen 4.0 m, feste Felder 3.6 m", note);
        Assert.Contains("0700 - 2000", note);
        Assert.EndsWith(Havnelods.Attribution + ", https://www.danskehavnelods.dk/Havnelodsen/bridges/c741b465-9807-4b19-b1b5-db9e00c332af.html", note);
    }

    [Fact]
    public void Kronprinsesse_marys_bro_is_fixed_22_m()
    {
        var p = Havnelods.ParseBridge(Fixture("havnelods-kronprinsesse-marys.html"));
        Assert.False(p.Opening);
        Assert.Null(p.Type); // the page has no Brotype section
        Assert.Equal(22.0, p.ClearanceM);
        Assert.Null(p.OpeningHours);
    }

    [Theory]
    [InlineData("De midterste 80 m af gennemsejlingsfaget har en fri højde på 25,6 m.", 25.6)]                          // Dronning Alexandrines Bro: a width first
    [InlineData("Fri sidehøjde ved åben bro er 20 m. Ved lukket bro er den frie højde til underkant af broklappens dragere 2,5 m ved daglig vandstand.", 2.5)] // Karrebæksminde: open first
    [InlineData("I gennemsejlingsfaget (fag nr. 3):\nVed bredde på maks. 3,00 meter er gennemsejlingshøjden 2,25 meter.", 2.25)] // Cirkelbroen
    [InlineData("I lukket tilstand er den frie gennemsejlingshøjde i en bredde på 10 m omkring kanalens centerlinie 8 m ved daglig vande.", 8.0)] // Næstved
    [InlineData("Ved daglig vande, 4 m under broklapper, og 2,5 m under den faste bro.", 4.0)]                            // Guldborgbroen
    [InlineData("Ved daglig vandstand er den fri højde under broklapperne 3,1 m.", 3.1)]                                  // Kronprins Frederiks Bro
    public void Clearance_skips_widths_and_open_heights(string text, double expected) => Assert.Equal(expected, Havnelods.Clearance(text).clearance);

    [Fact]
    public void Clearance_reads_the_fixed_spans_and_the_lowest_value()
    {
        Assert.Equal<(double?, double?, double?)>((4.0, 2.5, 2.5), Havnelods.Clearance("Ved daglig vande, 4 m under broklapper, og 2,5 m under den faste bro."));
        Assert.Equal<(double?, double?, double?)>((16.0, null, 6.0), Havnelods.Clearance("Kalvebodbroen: mindste gennemsejlingshøjde i gennemsejlingsfagene er 16 m ved middelvandstand.\nSorterendebroen: mindste gennemsejlingshøjde er 6 m ved middelvandstand."));
        Assert.Equal<(double?, double?, double?)>((null, null, null), Havnelods.Clearance("Se broplan."));
    }

    [Fact]
    public void Wikidata_csv_reproduces_the_71_items()
    {
        var items = WikidataBridges.Parse(Fixture("wikidata-clearances.csv"));
        Assert.Equal(71, items.Count);
        Assert.Equal(26.0, Assert.Single(items, i => i.Label == "Storstrømsbroen").ClearanceM);
        var marys = Assert.Single(items, i => i.Label == "Kronprinsesse Marys Bro");
        Assert.Equal(22.0, marys.ClearanceM);
        Assert.Equal("Q21817057", marys.Qid);
        Assert.Equal(55.8128, marys.Lat, 4);
        Assert.False(items.Single(i => i.Label == "Brücke Am Bockelsberg").InRange); // 575: a road underpass height
        Assert.All(items, i => Assert.False(i.Movable));                           // the saved list has no movable column
    }

    [Fact]
    public void Wikidata_sparql_csv_dedupes_items_and_reads_movable()
    {
        const string csv = "item,label,clearance_m,crosses,lat,lon,movable\r\n"
            + "http://www.wikidata.org/entity/Q1146022,Limfjordsbroen,9.5,Limfjorden,5.70547E1,9.92028E0,true\r\n"
            + "http://www.wikidata.org/entity/Q1146022,Limfjordsbroen,9.5,Limfjorden,5.70547E1,9.92028E0,true\r\n"
            + "http://www.wikidata.org/entity/Q1,\"Bro, gammel\",12.0,,5.5E1,1.0E1,false\r\n";
        var items = WikidataBridges.Parse(csv);
        Assert.Equal(2, items.Count);
        Assert.True(items[0].Movable);
        Assert.Equal(57.0547, items[0].Lat, 4);
        Assert.Equal("Bro, gammel", items[1].Label);
        Assert.Null(items[1].Crosses);
    }

    [Theory]
    [InlineData("Kronprinsesse Marys Bro", "Kronprinsesse Marys Bro", true)]
    [InlineData("Storstrømsbroen", "Storstrømsvej", true)]
    [InlineData("Øresundsbroen", "Øresundsbron", true)]
    [InlineData("Limfjordsbroen", "Jernbanebroen over Limfjorden", false)]
    [InlineData("Drammensbroen", "Drammenbanen", false)]                  // the railway next to it
    [InlineData("Rügenbron", "Rügenbrücke", true)]
    [InlineData("Svinesundbroen", null, false)]
    public void Wikidata_label_matches_the_osm_name(string label, string? osm, bool expected) => Assert.Equal(expected, WikidataBridges.NameMatches(label, osm));
}
