using System.Text.RegularExpressions;

namespace Sms.Domain.Cms;

/// <summary>A page of a ship's own website (tree via ParentId; "sub sites" are sub-trees).</summary>
public class ShipPage : Entity
{
    public Guid ShipId { get; set; }
    public Guid? ParentId { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int Order { get; set; }
    public bool Published { get; set; } = true;
    public bool ShowInNav { get; set; } = true;
    public string Language { get; set; } = "de";
}

/// <summary>A domain name that serves one ship's site only (spec extension, tenant mode).</summary>
public class ShipDomain
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ShipId { get; set; }
    public string Host { get; set; } = "";
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static partial class Slugs
{
    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonSlug();
    [GeneratedRegex(@"^(?=.{1,253}$)(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))*$")] private static partial Regex HostName();

    /// <summary>"Unsere Crew 2026!" → "unsere-crew-2026" (umlauts transliterated).</summary>
    public static string From(string text)
    {
        var t = text.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        return NonSlug().Replace(t, "-").Trim('-');
    }

    public static bool IsValid(string slug) => slug.Length is > 0 and <= 80 && slug == From(slug);

    /// <summary>Normalises a host (lower case, no port, no scheme) and validates it as a DNS name.</summary>
    public static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        var h = host.Trim().ToLowerInvariant();
        if (h.Contains("://")) h = h[(h.IndexOf("://", StringComparison.Ordinal) + 3)..];
        h = h.Split('/')[0].Split(':')[0].TrimEnd('.');
        return HostName().IsMatch(h) ? h : null;
    }
}

/// <summary>Tree helpers for page hierarchies (ship pages and global content pages).</summary>
public static class PageTree
{
    public sealed record Node<T>(T Page, string Path, List<Node<T>> Children);

    /// <summary>Builds the tree from a flat list; roots are pages without parent. Paths are slash-joined slugs.</summary>
    public static List<Node<T>> Build<T>(IEnumerable<T> pages, Func<T, Guid> id, Func<T, Guid?> parentId, Func<T, string> slug, Func<T, int> order)
    {
        var list = pages.ToList();
        var byParent = list.GroupBy(p => parentId(p) ?? Guid.Empty).ToDictionary(g => g.Key, g => g.OrderBy(order).ThenBy(slug).ToList());
        List<Node<T>> Children(Guid parent, string prefix, HashSet<Guid> seen) =>
            byParent.GetValueOrDefault(parent, []).Where(p => seen.Add(id(p)))
                .Select(p => new Node<T>(p, prefix + slug(p), Children(id(p), prefix + slug(p) + "/", seen))).ToList();
        var roots = Children(Guid.Empty, "", []);
        // pages whose parent does not exist (deleted) become roots so nothing is lost
        var attached = new HashSet<Guid>(); void Walk(Node<T> n) { attached.Add(id(n.Page)); n.Children.ForEach(Walk); }
        roots.ForEach(Walk);
        foreach (var orphan in list.Where(p => !attached.Contains(id(p)))) roots.Add(new Node<T>(orphan, slug(orphan), []));
        return roots;
    }

    /// <summary>Finds the node for "a/b/c".</summary>
    public static Node<T>? Find<T>(List<Node<T>> roots, string path)
    {
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var level = roots; Node<T>? current = null;
        foreach (var part in parts)
        {
            current = level.FirstOrDefault(n => n.Path.Split('/')[^1] == part);
            if (current is null) return null;
            level = current.Children;
        }
        return current;
    }

    /// <summary>True when setting <paramref name="newParent"/> for <paramref name="pageId"/> would create a cycle.</summary>
    public static bool WouldCycle(Guid pageId, Guid? newParent, Func<Guid, Guid?> parentOf)
    {
        var p = newParent; var guard = 0;
        while (p is { } id && guard++ < 1000) { if (id == pageId) return true; p = parentOf(id); }
        return false;
    }
}
