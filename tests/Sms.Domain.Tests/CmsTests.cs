using Sms.Domain.Cms;

namespace Sms.Domain.Tests;

public class CmsTests
{
    private sealed record P(Guid Id, Guid? ParentId, string Slug, int Order);

    [Fact]
    public void Slugs_are_normalised_and_validated()
    {
        Assert.Equal("unsere-crew-2026", Slugs.From("Unsere Crew 2026!"));
        Assert.Equal("toern-nach-daenemark", Slugs.From("Törn nach Dänemark"));
        Assert.True(Slugs.IsValid("logbuch"));
        Assert.False(Slugs.IsValid("Logbuch"));
        Assert.False(Slugs.IsValid(""));
        Assert.False(Slugs.IsValid("a/b"));
    }

    [Fact]
    public void Hosts_are_normalised_and_validated()
    {
        Assert.Equal("kairos.example.com", Slugs.NormalizeHost("HTTPS://Kairos.Example.com:8443/path"));
        Assert.Equal("kairos.example.com", Slugs.NormalizeHost("kairos.example.com."));
        Assert.Null(Slugs.NormalizeHost("not a host"));
        Assert.Null(Slugs.NormalizeHost("-bad.example.com"));
        Assert.Null(Slugs.NormalizeHost(""));
    }

    [Fact]
    public void Tree_builds_paths_orders_and_recovers_orphans()
    {
        var root = Guid.NewGuid(); var child = Guid.NewGuid(); var grand = Guid.NewGuid(); var orphan = Guid.NewGuid();
        var pages = new[]
        {
            new P(child, root, "crew", 2), new P(root, null, "ueber-uns", 1), new P(grand, child, "skipper", 0),
            new P(Guid.NewGuid(), root, "boot", 1), new P(orphan, Guid.NewGuid(), "lost", 0),
        };
        var tree = PageTree.Build(pages, p => p.Id, p => p.ParentId, p => p.Slug, p => p.Order);
        Assert.Equal(2, tree.Count); // root + orphan
        var ueber = tree.Single(n => n.Path == "ueber-uns");
        Assert.Equal(["boot", "crew"], ueber.Children.Select(c => c.Page.Slug));
        Assert.Equal("ueber-uns/crew/skipper", ueber.Children[1].Children[0].Path);
        Assert.Equal(grand, PageTree.Find(tree, "/ueber-uns/crew/skipper/")!.Page.Id);
        Assert.Null(PageTree.Find(tree, "ueber-uns/nope"));
        Assert.Contains(tree, n => n.Page.Id == orphan && n.Path == "lost");
    }

    [Fact]
    public void Cycle_detection()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [a] = null, [b] = a, [c] = b };
        Assert.True(PageTree.WouldCycle(a, c, id => parents.GetValueOrDefault(id)));   // a under c (c→b→a)
        Assert.False(PageTree.WouldCycle(c, a, id => parents.GetValueOrDefault(id)));  // c under a is fine
        Assert.True(PageTree.WouldCycle(a, a, id => parents.GetValueOrDefault(id)));   // self
    }
}
