using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Domain.Costs;
using Sms.Infrastructure;

namespace Sms.Api;

/// <summary>Exchange-rate source for cost conversion; the ECB client is the only implementation today, swap by registration.</summary>
public interface IExchangeRates
{
    /// <summary>Rate from → to for the payment date; null when a currency is not covered.</summary>
    Task<(decimal rate, DateOnly rateDate)?> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct);
    /// <summary>Pulls the provider's rates into the ExchangeRates table (history = full bootstrap); returns the number of rows added.</summary>
    Task<int> ImportAsync(bool history, CancellationToken ct);
}

/// <summary>ECB euro reference rates (spec 09 §9.2): daily import, historical bootstrap, rate lookup with fallback to the latest earlier rate.</summary>
public sealed class EcbRates(HttpClient http, IServiceScopeFactory scopes, ILogger<EcbRates> logger) : IExchangeRates
{
    public const string DailyUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";
    public const string HistoryUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist.xml";
    private static readonly XNamespace Ns = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    /// <summary>Rate from → to for the payment date; null when a currency is not covered by the ECB.</summary>
    public async Task<(decimal rate, DateOnly rateDate)?> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct)
    {
        if (from == to) return (1m, date);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        async Task<ExchangeRate?> Latest(string currency) => currency == "EUR"
            ? new ExchangeRate { Currency = "EUR", Date = date, PerEur = 1 }
            : await db.ExchangeRates.AsNoTracking().Where(r => r.Currency == currency && r.Date <= date && r.Source == RateSource.Ecb).OrderByDescending(r => r.Date).FirstOrDefaultAsync(ct);
        var a = await Latest(from);
        var b = await Latest(to);
        if (a is null || b is null) return null;
        var rateDate = a.Date < b.Date ? a.Date : b.Date; // both legs on the same (earliest common) published date
        if (from != "EUR" && a.Date != rateDate) a = await db.ExchangeRates.AsNoTracking().FirstOrDefaultAsync(r => r.Currency == from && r.Date == rateDate && r.Source == RateSource.Ecb, ct) ?? a;
        if (to != "EUR" && b.Date != rateDate) b = await db.ExchangeRates.AsNoTracking().FirstOrDefaultAsync(r => r.Currency == to && r.Date == rateDate && r.Source == RateSource.Ecb, ct) ?? b;
        return (Money.CrossRate(a.PerEur, b.PerEur), rateDate);
    }

    public async Task<int> ImportAsync(bool history, CancellationToken ct)
    {
        var xml = await http.GetStringAsync(history ? HistoryUrl : DailyUrl, ct);
        var doc = XDocument.Parse(xml);
        var rows = new List<ExchangeRate>();
        foreach (var day in doc.Descendants(Ns + "Cube").Where(c => c.Attribute("time") != null))
        {
            var date = DateOnly.Parse(day.Attribute("time")!.Value, CultureInfo.InvariantCulture);
            foreach (var c in day.Elements(Ns + "Cube"))
                rows.Add(new ExchangeRate { Date = date, Currency = c.Attribute("currency")!.Value, PerEur = decimal.Parse(c.Attribute("rate")!.Value, CultureInfo.InvariantCulture), Source = RateSource.Ecb });
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var dates = rows.Select(r => r.Date).Distinct().ToList();
        var existing = await db.ExchangeRates.Where(r => dates.Contains(r.Date) && r.Source == RateSource.Ecb).Select(r => new { r.Date, r.Currency }).ToListAsync(ct);
        var known = existing.Select(e => (e.Date, e.Currency)).ToHashSet();
        var fresh = rows.Where(r => !known.Contains((r.Date, r.Currency))).ToList();
        db.ExchangeRates.AddRange(fresh);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("ECB rates imported: {Count} new rows ({Mode})", fresh.Count, history ? "history" : "daily");
        return fresh.Count;
    }
}

/// <summary>Bootstraps the ECB history once, then refreshes daily after the ~16:00 CET publication.</summary>
public sealed class EcbRateJob(IExchangeRates rates, IServiceScopeFactory scopes, ILogger<EcbRateJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), ct); // let migrations finish
        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool empty;
                await using (var scope = scopes.CreateAsyncScope())
                    empty = !await scope.ServiceProvider.GetRequiredService<SmsDbContext>().ExchangeRates.AnyAsync(ct);
                await rates.ImportAsync(history: empty, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ECB rate import failed; retrying later");
            }
            await Task.Delay(TimeSpan.FromHours(6), ct);
        }
    }
}
