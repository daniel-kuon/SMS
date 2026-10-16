using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;

namespace Sms.Api;

/// <summary>
/// Development-only seed (Program.cs runs it in Development): platform admin, content pages and a few
/// western-Baltic harbours (legacy area) so the map is not empty. Credentials come from configuration
/// (Seed:AdminEmail / Seed:AdminPassword); the defaults are for the local developer machine only.
/// </summary>
public static class Seed
{
    public static async Task RunAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<SmsDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var cfg = sp.GetRequiredService<IConfiguration>();
        var adminEmail = cfg["Seed:AdminEmail"] ?? "admin@localhost.dev";
        var adminPassword = cfg["Seed:AdminPassword"] ?? "SailingPortal2026!";

        if (await users.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var result = await users.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
                db.Profiles.Add(new UserProfile { UserId = admin.Id, DisplayName = "Admin", IsAdmin = true, Language = "de" });
        }

        if (!await db.ContentPages.AnyAsync())
        {
            db.ContentPages.AddRange(
                new ContentPage { Slug = "impressum", Title = "Impressum", Content = "<p>Angaben gemäß § 5 TMG – bitte ausfüllen.</p>" },
                new ContentPage { Slug = "datenschutz", Title = "Datenschutzerklärung", Content = "<p>Datenschutzerklärung – bitte ausfüllen.</p>" },
                new ContentPage { Slug = "hilfe", Title = "Hilfe", Content = "<p>Schiff anlegen → Törn planen → Crew einladen → Logbuch, Fotos und Kosten teilen.</p>" },
                new ContentPage { Slug = "hilfe", Language = "en", Title = "Help", Content = "<p>Create a ship → plan a trip → invite crew → share logbook, photos and costs.</p>" });
        }

        if (!await db.Harbours.AnyAsync())
        {
            (string name, double lat, double lng, string? cat)[] harbours =
            [
                ("Kiel Düsternbrook", 54.3390, 10.1530, "marina"), ("Kiel Schilksee", 54.4245, 10.1710, "marina"), ("Strande", 54.4330, 10.1740, "marina"),
                ("Laboe", 54.4105, 10.2190, "marina"), ("Möltenort", 54.3780, 10.1940, "marina"), ("Lippe (Hohenfelde)", 54.3980, 10.4340, "marina"),
                ("Eckernförde", 54.4720, 9.8380, "marina"), ("Damp", 54.5780, 10.0290, "marina"), ("Maasholm", 54.6830, 9.9900, "marina"),
                ("Kappeln", 54.6620, 9.9330, "marina"), ("Schleimünde", 54.6730, 10.0370, "harbour"), ("Heiligenhafen", 54.3740, 10.9800, "marina"),
                ("Burgtiefe (Fehmarn)", 54.4010, 11.2020, "marina"), ("Grömitz", 54.1420, 10.9680, "marina"), ("Neustadt in Holstein", 54.1040, 10.8150, "marina"),
                ("Travemünde", 53.9620, 10.8720, "marina"), ("Sønderborg", 54.9120, 9.7830, "marina"), ("Marstal", 54.8510, 10.5190, "marina"),
                ("Ærøskøbing", 54.8900, 10.4110, "marina"), ("Bagenkop", 54.7510, 10.6730, "marina"), ("Faaborg", 55.0950, 10.2420, "marina"),
                ("Svendborg", 55.0590, 10.6120, "marina"), ("Rødbyhavn", 54.6560, 11.3560, "harbour"), ("Gedser", 54.5730, 11.9260, "marina"),
            ];
            foreach (var (name, lat, lng, cat) in harbours)
                db.Harbours.Add(new Harbour { Name = name, Location = Geo.Point(lat, lng), Country = name.Contains("borg") || name.Contains("Marstal") || name.Contains("Ærø") || name.Contains("Bagenkop") || name.Contains("Faaborg") || name.Contains("Rødby") || name.Contains("Gedser") ? "DK" : "DE", Category = cat, Source = HarbourSource.Legacy, Description = "<p>Seed-Daten (Position ungefähr) – bitte prüfen und ergänzen.</p>" });
        }

        await db.SaveChangesAsync();
    }
}
