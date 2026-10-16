using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Domain.Cms;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record ShipPageUpsert(string Title, string? Slug, string Content, Guid? ParentId, int Order = 0, bool Published = true, bool ShowInNav = true, string? Language = null);
public record ShipPageDto(Guid Id, Guid ShipId, Guid? ParentId, string Slug, string Path, string Title, string Content, int Order, bool Published, bool ShowInNav, string Language, DateTimeOffset UpdatedAt, List<ShipPageDto> Children);
public record DomainCreate(string Host, bool IsPrimary = false);
public record DomainDto(Guid Id, string Host, bool IsPrimary);

/// <summary>
/// CMS: every ship can have its own website (page tree, sub-sites = sub-trees) and domains. When the portal
/// is opened through such a domain, /api/site answers in "ship" mode and the SPA shows only that ship.
/// </summary>
public static class CmsEndpoints
{
    public static void MapCms(this IEndpointRouteBuilder api)
    {
        var ships = api.MapGroup("/ships/{shipId:guid}").WithTags("CMS");

        // --- site overview for the owner (all pages) or the public (published pages)
        ships.MapGet("/site", async (Guid shipId, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var ship = await db.Ships.AsNoTracking().Where(viewer.Ships()).FirstOrDefaultAsync(s => s.Id == shipId, ct);
            if (ship is null) return Results.NotFound();
            var owner = await access.IsShipOwnerAsync(shipId, ct) || viewer.IsAdmin;
            var pages = await db.ShipPages.AsNoTracking().Where(p => p.ShipId == shipId && (owner || p.Published)).ToListAsync(ct);
            var domains = owner ? await db.ShipDomains.AsNoTracking().Where(d => d.ShipId == shipId).OrderBy(d => d.Host).Select(d => new DomainDto(d.Id, d.Host, d.IsPrimary)).ToListAsync(ct) : [];
            return Results.Ok(new { pages = Tree(pages), domains, canEdit = owner });
        });

        ships.MapGet("/pages/{*path}", async (Guid shipId, string path, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            if (!await db.Ships.AsNoTracking().Where(viewer.Ships()).AnyAsync(s => s.Id == shipId, ct)) return Results.NotFound();
            var owner = await access.IsShipOwnerAsync(shipId, ct) || viewer.IsAdmin;
            var pages = await db.ShipPages.AsNoTracking().Where(p => p.ShipId == shipId && (owner || p.Published)).ToListAsync(ct);
            var node = PageTree.Find(PageTree.Build(pages, p => p.Id, p => p.ParentId, p => p.Slug, p => p.Order), path);
            return node is null ? Results.NotFound() : Results.Ok(ToDto(node));
        });

        ships.MapPost("/pages", async (Guid shipId, ShipPageUpsert req, SmsDbContext db, CurrentUser current, Access access, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(shipId, ct)) return Results.Forbid();
            var page = new ShipPage { ShipId = shipId, CreatedById = current.Id };
            if (await ApplyAsync(page, req, db, html, ct) is { } problem) return problem;
            db.ShipPages.Add(page);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/ship-pages/{page.Id}", new { page.Id, page.Slug });
        }).RequireAuthorization();

        api.MapPut("/ship-pages/{id:guid}", async (Guid id, ShipPageUpsert req, SmsDbContext db, CurrentUser current, Access access, HtmlSanitizerService html, CancellationToken ct) =>
        {
            var page = await db.ShipPages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return Results.NotFound();
            if (!await access.IsShipOwnerAsync(page.ShipId, ct)) return Results.Forbid();
            if (await ApplyAsync(page, req, db, html, ct) is { } problem) return problem;
            page.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("CMS");

        api.MapDelete("/ship-pages/{id:guid}", async (Guid id, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            var page = await db.ShipPages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return Results.NotFound();
            if (!await access.IsShipOwnerAsync(page.ShipId, ct)) return Results.Forbid();
            // children move up one level so a sub-site is not silently lost
            await db.ShipPages.Where(p => p.ParentId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.ParentId, page.ParentId), ct);
            db.ShipPages.Remove(page);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("CMS");

        // --- domains (owners)
        ships.MapGet("/domains", async (Guid shipId, SmsDbContext db, Access access, CancellationToken ct) =>
            await access.IsShipOwnerAsync(shipId, ct)
                ? Results.Ok(await db.ShipDomains.AsNoTracking().Where(d => d.ShipId == shipId).OrderBy(d => d.Host).Select(d => new DomainDto(d.Id, d.Host, d.IsPrimary)).ToListAsync(ct))
                : Results.Forbid()).RequireAuthorization();

        ships.MapPost("/domains", async (Guid shipId, DomainCreate req, SmsDbContext db, Access access, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(shipId, ct)) return Results.Forbid();
            var host = Slugs.NormalizeHost(req.Host);
            if (host is null) return Invalid("host", "Ungültiger Hostname.");
            var reserved = (cfg["Web:BaseUrl"] is { } b && Uri.TryCreate(b, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : null);
            if (host == reserved || host is "localhost") return Invalid("host", "Diese Domain gehört dem Portal.");
            if (await db.ShipDomains.AnyAsync(d => d.Host == host, ct)) return Results.Problem(statusCode: 409, title: "Domain ist bereits einem Schiff zugeordnet.");
            if (req.IsPrimary) await db.ShipDomains.Where(d => d.ShipId == shipId).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsPrimary, false), ct);
            var d = new ShipDomain { ShipId = shipId, Host = host, IsPrimary = req.IsPrimary || !await db.ShipDomains.AnyAsync(x => x.ShipId == shipId, ct) };
            db.ShipDomains.Add(d);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/ships/{shipId}/domains/{d.Id}", new DomainDto(d.Id, d.Host, d.IsPrimary));
        }).RequireAuthorization();

        ships.MapDelete("/domains/{id:guid}", async (Guid shipId, Guid id, SmsDbContext db, Access access, CancellationToken ct) =>
            await access.IsShipOwnerAsync(shipId, ct)
                ? (await db.ShipDomains.Where(d => d.Id == id && d.ShipId == shipId).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound())
                : Results.Forbid()).RequireAuthorization();

        // --- tenant resolution: which site does this host show?
        api.MapGet("/site", async (HttpRequest http, SmsDbContext db, CurrentUser current, Access access, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var host = Slugs.NormalizeHost(http.Headers["X-Forwarded-Host"].FirstOrDefault() ?? http.Host.Host);
            if (env.IsDevelopment() && http.Headers["X-Tenant-Host"].FirstOrDefault() is { Length: > 0 } dev) host = Slugs.NormalizeHost(dev); // local preview of a ship domain
            var domain = host is null ? null : await db.ShipDomains.AsNoTracking().FirstOrDefaultAsync(d => d.Host == host, ct);
            if (domain is null) return Results.Ok(new { mode = "portal", host });
            var site = await ShipSiteAsync(domain.ShipId, db, current, access, ct);
            return site is null ? Results.Ok(new { mode = "portal", host }) : Results.Ok(site);
        }).WithTags("CMS");

        // same payload for the owner's preview inside the portal
        ships.MapGet("/site-preview", async (Guid shipId, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
            await ShipSiteAsync(shipId, db, current, access, ct) is { } site ? Results.Ok(site) : Results.NotFound());
    }

    private static async Task<object?> ShipSiteAsync(Guid shipId, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct)
    {
        var viewer = await Viewer.ForAsync(current, access, ct);
        var ship = await db.Ships.AsNoTracking().Include(s => s.HomeHarbour).Where(viewer.Ships()).FirstOrDefaultAsync(s => s.Id == shipId, ct);
        if (ship is null) return null;
        var owner = await access.IsShipOwnerAsync(shipId, ct) || viewer.IsAdmin;
        var pages = await db.ShipPages.AsNoTracking().Where(p => p.ShipId == shipId && p.Published).ToListAsync(ct);
        var entries = await db.LogbookEntries.AsNoTracking().Include(l => l.StartHarbour).Include(l => l.EndHarbour).Include(l => l.Crew).Include(l => l.Ship).Include(l => l.Trip)
            .Where(l => l.ShipId == shipId).Where(viewer.Logbook()).OrderByDescending(l => l.DepartedAt).Take(10).ToListAsync(ct);
        var names = await ShipEndpoints.DisplayNames(db, entries.SelectMany(e => e.Crew.Where(c => c.UserId != null).Select(c => c.UserId!.Value)), ct);
        var crewNames = await LogbookEndpoints.CrewNames(db, entries.Select(e => e.TripId).Where(t => t != null).Select(t => t!.Value).Distinct(), ct);
        var trips = await db.Trips.AsNoTracking().Include(t => t.Ship).Include(t => t.Crew).Where(t => t.ShipId == shipId).Where(viewer.Trips()).OrderByDescending(t => t.StartDate).Take(20).ToListAsync(ct);
        return new
        {
            mode = "ship",
            ship = new { ship.Id, ship.Name, ship.Type, ship.Description, ship.LengthM, ship.BeamM, ship.DraftM, ship.SailNumber, homeHarbour = ship.HomeHarbour?.Name, homeHarbourId = ship.HomeHarbourId, photoUrl = ship.PhotoMediaId is { } p ? $"/api/media/{p}/preview" : null, canEdit = owner },
            pages = Tree(pages).Where(p => p.ShowInNav).ToList(),
            latestLogbook = entries.Select(e => LogbookEndpoints.ToDto(e, names, crewNames, current.Id)),
            trips = trips.Select(TripEndpoints.Summary),
        };
    }

    private static async Task<IResult?> ApplyAsync(ShipPage page, ShipPageUpsert r, SmsDbContext db, HtmlSanitizerService html, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) return Invalid("title", "Titel fehlt.");
        var slug = string.IsNullOrWhiteSpace(r.Slug) ? Slugs.From(r.Title) : Slugs.From(r.Slug);
        if (!Slugs.IsValid(slug)) return Invalid("slug", "Ungültiger Slug.");
        if (r.ParentId is { } parent)
        {
            if (!await db.ShipPages.AnyAsync(p => p.Id == parent && p.ShipId == page.ShipId, ct)) return Invalid("parentId", "Übergeordnete Seite nicht gefunden.");
            var parents = await db.ShipPages.Where(p => p.ShipId == page.ShipId).Select(p => new { p.Id, p.ParentId }).ToDictionaryAsync(p => p.Id, p => p.ParentId, ct);
            if (PageTree.WouldCycle(page.Id, parent, id => parents.GetValueOrDefault(id))) return Invalid("parentId", "Eine Seite kann nicht unter sich selbst hängen.");
        }
        if (await db.ShipPages.AnyAsync(p => p.ShipId == page.ShipId && p.ParentId == r.ParentId && p.Slug == slug && p.Id != page.Id, ct)) return Invalid("slug", "Slug auf dieser Ebene bereits vergeben.");
        page.Title = r.Title.Trim(); page.Slug = slug; page.Content = html.Sanitize(r.Content); page.ParentId = r.ParentId; page.Order = r.Order;
        page.Published = r.Published; page.ShowInNav = r.ShowInNav; page.Language = r.Language is "en" ? "en" : "de";
        return null;
    }

    internal static List<ShipPageDto> Tree(List<ShipPage> pages) =>
        PageTree.Build(pages, p => p.Id, p => p.ParentId, p => p.Slug, p => p.Order).Select(ToDto).ToList();

    private static ShipPageDto ToDto(PageTree.Node<ShipPage> n) => new(n.Page.Id, n.Page.ShipId, n.Page.ParentId, n.Page.Slug, n.Path, n.Page.Title, n.Page.Content, n.Page.Order, n.Page.Published, n.Page.ShowInNav, n.Page.Language, n.Page.UpdatedAt, n.Children.Select(ToDto).ToList());
}
