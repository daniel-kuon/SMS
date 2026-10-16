using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Domain.Costs;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record ShareInput(Guid CrewMemberId, decimal? Weight, decimal? ExactAmount);
public record ExpenseUpsert(decimal Amount, string Currency, DateOnly PaidOn, decimal? ExchangeRate, string Category, string? Description, Guid? PaidByCrewMemberId,
    SplitMode SplitMode, List<ShareInput>? Participants, Guid? ReceiptMediaId);
public record ShareDto(Guid CrewMemberId, string Name, decimal? Weight, decimal? ExactAmount, decimal ComputedShare);
public record ExpenseDto(Guid Id, Guid TripId, Guid PaidByCrewMemberId, string PaidBy, decimal Amount, string Currency, DateOnly PaidOn, decimal ExchangeRate, DateOnly RateDate,
    RateSource RateSource, decimal AmountInTripCurrency, string Category, string? Description, string? ReceiptUrl, SplitMode SplitMode, IReadOnlyList<ShareDto> Shares, Guid? CreatedById, bool Deleted);
public record BalanceDto(Guid CrewMemberId, string Name, decimal Paid, decimal Share, decimal Sent, decimal Received, decimal Balance);
public record TransferDto(Guid FromCrewMemberId, string From, Guid ToCrewMemberId, string To, decimal Amount);
public record SettlementDto(Guid Id, Guid FromCrewMemberId, string From, Guid ToCrewMemberId, string To, decimal Amount, DateTimeOffset PaidAt, SettlementStatus Status, string? Note);
public record SettlementCreate(Guid ToCrewMemberId, decimal Amount, string? Note, Guid? FromCrewMemberId);
public record BalancesResponse(string Currency, decimal Total, Guid? MyCrewMemberId, IReadOnlyList<BalanceDto> Balances, IReadOnlyList<TransferDto> SuggestedTransfers, IReadOnlyList<SettlementDto> Settlements, DateTimeOffset? LockedAt);

public static class CostEndpoints
{
    public static readonly string[] Categories = ["Hafengeld", "Diesel", "Proviant", "Restaurant", "Charter", "Sonstiges"];

    public static void MapCosts(this IEndpointRouteBuilder api)
    {
        var trips = api.MapGroup("/trips/{tripId:guid}").WithTags("Costs").RequireAuthorization();

        trips.MapGet("/expenses", async (Guid tripId, string? category, Guid? member, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            if (!await access.IsCrewAsync(tripId, ct)) return Results.Forbid();
            var q = db.Expenses.AsNoTracking().IgnoreQueryFilters().Include(e => e.Shares).Where(e => e.TripId == tripId);
            if (category is { Length: > 0 }) q = q.Where(e => e.Category == category);
            if (member is { } m) q = q.Where(e => e.PaidByCrewMemberId == m || e.Shares.Any(s => s.CrewMemberId == m));
            var items = await q.OrderByDescending(e => e.PaidOn).ThenByDescending(e => e.CreatedAt).ToListAsync(ct);
            var names = await LogbookEndpoints.CrewNames(db, tripId, ct);
            return Results.Ok(new { categories = Categories, items = items.Select(e => ToDto(e, names)) });
        });

        trips.MapPost("/expenses", async (Guid tripId, ExpenseUpsert req, SmsDbContext db, CurrentUser current, Access access, EcbRates rates, CancellationToken ct) =>
        {
            var me = await access.CrewAsync(tripId, ct);
            if (me is null) return Results.Forbid();
            var trip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == tripId, ct);
            if (trip.CostsLockedAt is not null) return Results.Problem(statusCode: 409, title: "Die Abrechnung ist abgeschlossen.");
            var expense = new Expense { TripId = tripId, CreatedById = current.Id };
            var problem = await ApplyAsync(expense, req, trip, me, db, rates, ct);
            if (problem is not null) return problem;
            db.Expenses.Add(expense);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/expenses/{expense.Id}", new { expense.Id });
        });

        api.MapPut("/expenses/{id:guid}", async (Guid id, ExpenseUpsert req, SmsDbContext db, CurrentUser current, Access access, EcbRates rates, CancellationToken ct) =>
        {
            var expense = await db.Expenses.Include(e => e.Shares).Include(e => e.Trip).FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null) return Results.NotFound();
            var me = await access.CrewAsync(expense.TripId, ct);
            if (me is null || (expense.CreatedById != current.Id && me.Role != CrewRole.Skipper)) return Results.Forbid();
            if (expense.Trip.CostsLockedAt is not null) return Results.Problem(statusCode: 409, title: "Die Abrechnung ist abgeschlossen.");
            db.ExpenseShares.RemoveRange(expense.Shares);
            expense.Shares.Clear();
            var problem = await ApplyAsync(expense, req, expense.Trip, me, db, rates, ct);
            if (problem is not null) return problem;
            expense.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Costs");

        api.MapDelete("/expenses/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var expense = await db.Expenses.Include(e => e.Trip).FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null) return Results.NotFound();
            var me = await access.CrewAsync(expense.TripId, ct);
            if (me is null || (expense.CreatedById != current.Id && me.Role != CrewRole.Skipper)) return Results.Forbid();
            if (expense.Trip.CostsLockedAt is not null) return Results.Problem(statusCode: 409, title: "Die Abrechnung ist abgeschlossen.");
            expense.DeletedAt = DateTimeOffset.UtcNow; expense.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Costs");

        trips.MapGet("/balances", async (Guid tripId, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            var me = await access.CrewAsync(tripId, ct);
            if (me is null) return Results.Forbid();
            return Results.Ok(await BalancesAsync(tripId, me.Id, db, ct));
        });

        trips.MapPost("/settlements", async (Guid tripId, SettlementCreate req, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var me = await access.CrewAsync(tripId, ct);
            if (me is null) return Results.Forbid();
            var from = req.FromCrewMemberId is { } f && me.Role == CrewRole.Skipper ? f : me.Id;
            if (req.Amount <= 0) return Invalid("amount", "Betrag muss positiv sein.");
            if (!await db.CrewMembers.AnyAsync(c => c.Id == req.ToCrewMemberId && c.TripId == tripId, ct)) return Invalid("toCrewMemberId", "Empfänger nicht in der Crew.");
            var trip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == tripId, ct);
            var s = new Settlement { TripId = tripId, FromCrewMemberId = from, ToCrewMemberId = req.ToCrewMemberId, Amount = Money.Round(req.Amount, trip.Currency), Note = req.Note, CreatedById = current.Id };
            db.Settlements.Add(s);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/settlements/{s.Id}", new { s.Id });
        });

        api.MapPost("/settlements/{id:guid}/confirm", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var s = await db.Settlements.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var me = await access.CrewAsync(s.TripId, ct);
            if (me is null || (me.Id != s.ToCrewMemberId && me.Role != CrewRole.Skipper)) return Results.Forbid();
            s.Status = SettlementStatus.Confirmed; s.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Costs");

        api.MapDelete("/settlements/{id:guid}", async (Guid id, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            var s = await db.Settlements.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var me = await access.CrewAsync(s.TripId, ct);
            if (me is null || (me.Id != s.FromCrewMemberId && me.Role != CrewRole.Skipper) || s.Status == SettlementStatus.Confirmed) return Results.Forbid();
            db.Settlements.Remove(s);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Costs");

        trips.MapPost("/costs/{action:regex(^(lock|unlock)$)}", async (Guid tripId, string action, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            if (!await access.IsTripSkipperAsync(tripId, ct)) return Results.Forbid();
            var trip = await db.Trips.FirstAsync(t => t.Id == tripId, ct);
            trip.CostsLockedAt = action == "lock" ? DateTimeOffset.UtcNow : null;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        trips.MapGet("/costs/export", async (Guid tripId, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            var me = await access.CrewAsync(tripId, ct);
            if (me is null) return Results.Forbid();
            var trip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == tripId, ct);
            var names = await LogbookEndpoints.CrewNames(db, tripId, ct);
            var expenses = await db.Expenses.AsNoTracking().Include(e => e.Shares).Where(e => e.TripId == tripId).OrderBy(e => e.PaidOn).ToListAsync(ct);
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.AppendLine("Datum;Kategorie;Beschreibung;Bezahlt von;Betrag;Währung;Kurs;Kursdatum;Kursquelle;Betrag " + trip.Currency + ";Aufteilung");
            foreach (var e in expenses)
                sb.AppendLine(string.Join(';', e.PaidOn.ToString("yyyy-MM-dd"), e.Category, Csv(e.Description), Csv(names.GetValueOrDefault(e.PaidByCrewMemberId, "?")),
                    e.Amount.ToString(inv), e.Currency, e.ExchangeRate.ToString(inv), e.RateDate.ToString("yyyy-MM-dd"), e.RateSource,
                    e.AmountInTripCurrency.ToString(inv), Csv(string.Join(", ", e.Shares.Select(s => $"{names.GetValueOrDefault(s.CrewMemberId, "?")} {s.ComputedShare.ToString(inv)}")))));
            sb.AppendLine();
            var balances = await BalancesAsync(tripId, me.Id, db, ct);
            sb.AppendLine("Mitglied;Bezahlt;Anteil;Saldo");
            foreach (var b in balances.Balances) sb.AppendLine($"{Csv(b.Name)};{b.Paid.ToString(inv)};{b.Share.ToString(inv)};{b.Balance.ToString(inv)}");
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"kosten-{trip.Name}.csv");
        });

        api.MapGet("/exchange-rates", async (string from, string to, DateOnly? date, EcbRates rates, CancellationToken ct) =>
        {
            var r = await rates.GetRateAsync(from.ToUpperInvariant(), to.ToUpperInvariant(), date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct);
            return r is null ? Results.NotFound(new { message = "Kein Kurs verfügbar – bitte manuell eingeben." }) : Results.Ok(new { rate = r.Value.rate, rateDate = r.Value.rateDate, source = RateSource.Ecb });
        }).WithTags("Costs");
    }

    private static string Csv(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";

    private static async Task<IResult?> ApplyAsync(Expense e, ExpenseUpsert r, Trip trip, CrewMember me, SmsDbContext db, EcbRates rates, CancellationToken ct)
    {
        if (r.Amount <= 0) return Invalid("amount", "Betrag muss positiv sein.");
        if (r.Currency is not { Length: 3 }) return Invalid("currency", "ISO-4217-Code (3 Buchstaben).");
        var currency = r.Currency.ToUpperInvariant();
        var crew = await db.CrewMembers.AsNoTracking().Where(c => c.TripId == trip.Id).ToListAsync(ct);
        var paidBy = r.PaidByCrewMemberId ?? me.Id;
        if (paidBy != me.Id && me.Role != CrewRole.Skipper) return Invalid("paidByCrewMemberId", "Nur der Skipper darf für andere eintragen.");
        if (crew.All(c => c.Id != paidBy)) return Invalid("paidByCrewMemberId", "Zahler nicht in der Crew.");

        e.Amount = Money.Round(r.Amount, currency); e.Currency = currency; e.PaidOn = r.PaidOn; e.PaidByCrewMemberId = paidBy;
        e.Category = string.IsNullOrWhiteSpace(r.Category) ? "Sonstiges" : r.Category.Trim(); e.Description = r.Description; e.ReceiptMediaId = r.ReceiptMediaId; e.SplitMode = r.SplitMode;

        if (currency == trip.Currency) { e.ExchangeRate = 1; e.RateDate = r.PaidOn; e.RateSource = RateSource.Same; }
        else if (r.ExchangeRate is { } manual && manual > 0) { e.ExchangeRate = manual; e.RateDate = r.PaidOn; e.RateSource = RateSource.Manual; }
        else
        {
            var rate = await rates.GetRateAsync(currency, trip.Currency, r.PaidOn, ct);
            if (rate is null) return Invalid("exchangeRate", $"Kein EZB-Kurs für {currency} – bitte Kurs manuell eingeben.");
            e.ExchangeRate = rate.Value.rate; e.RateDate = rate.Value.rateDate; e.RateSource = RateSource.Ecb;
        }
        e.AmountInTripCurrency = Money.Convert(e.Amount, e.ExchangeRate, trip.Currency);

        var inputs = r.Participants is { Count: > 0 } ? r.Participants : crew.Where(c => c.IsAboardOn(r.PaidOn)).Select(c => new ShareInput(c.Id, null, null)).ToList();
        if (inputs.Count == 0) return Invalid("participants", "Keine Teilnehmer.");
        if (inputs.Any(i => crew.All(c => c.Id != i.CrewMemberId))) return Invalid("participants", "Teilnehmer nicht in der Crew.");
        Dictionary<Guid, decimal> shares;
        try
        {
            shares = CostSplit.Split(e.AmountInTripCurrency, trip.Currency, r.SplitMode, inputs.Select(i => new Participant(i.CrewMemberId, i.Weight, i.ExactAmount)).ToList());
        }
        catch (ArgumentException ex) { return Invalid("participants", ex.Message); }
        foreach (var i in inputs)
            e.Shares.Add(new ExpenseShare { ExpenseId = e.Id, CrewMemberId = i.CrewMemberId, Weight = i.Weight, ExactAmount = i.ExactAmount, ComputedShare = shares[i.CrewMemberId] });
        return null;
    }

    private static async Task<BalancesResponse> BalancesAsync(Guid tripId, Guid myCrewId, SmsDbContext db, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == tripId, ct);
        var names = await LogbookEndpoints.CrewNames(db, tripId, ct);
        var members = await db.CrewMembers.AsNoTracking().Where(c => c.TripId == tripId && c.Status != CrewStatus.Declined && c.Status != CrewStatus.Invited).Select(c => c.Id).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Include(e => e.Shares).Where(e => e.TripId == tripId).ToListAsync(ct);
        var settlements = await db.Settlements.AsNoTracking().Where(s => s.TripId == tripId).OrderByDescending(s => s.PaidAt).ToListAsync(ct);
        var balances = Settlements.Balances(members,
            expenses.Select(e => (e.PaidByCrewMemberId, e.AmountInTripCurrency, e.Shares.Select(s => (s.CrewMemberId, s.ComputedShare)))),
            settlements.Where(s => s.Status == SettlementStatus.Confirmed).Select(s => (s.FromCrewMemberId, s.ToCrewMemberId, s.Amount)));
        var transfers = Settlements.SuggestTransfers(balances, trip.Currency);
        string N(Guid id) => names.GetValueOrDefault(id, "?");
        return new BalancesResponse(trip.Currency, expenses.Sum(e => e.AmountInTripCurrency), myCrewId,
            balances.OrderBy(b => N(b.CrewMemberId)).Select(b => new BalanceDto(b.CrewMemberId, N(b.CrewMemberId), b.Paid, b.Share, b.Sent, b.Received, b.Balance)).ToList(),
            transfers.Select(t => new TransferDto(t.FromCrewMemberId, N(t.FromCrewMemberId), t.ToCrewMemberId, N(t.ToCrewMemberId), t.Amount)).ToList(),
            settlements.Select(s => new SettlementDto(s.Id, s.FromCrewMemberId, N(s.FromCrewMemberId), s.ToCrewMemberId, N(s.ToCrewMemberId), s.Amount, s.PaidAt, s.Status, s.Note)).ToList(),
            trip.CostsLockedAt);
    }

    private static ExpenseDto ToDto(Expense e, Dictionary<Guid, string> names) => new(
        e.Id, e.TripId, e.PaidByCrewMemberId, names.GetValueOrDefault(e.PaidByCrewMemberId, "?"), e.Amount, e.Currency, e.PaidOn, e.ExchangeRate, e.RateDate, e.RateSource,
        e.AmountInTripCurrency, e.Category, e.Description, e.ReceiptMediaId is { } r ? $"/api/media/{r}/thumb" : null, e.SplitMode,
        e.Shares.Select(s => new ShareDto(s.CrewMemberId, names.GetValueOrDefault(s.CrewMemberId, "?"), s.Weight, s.ExactAmount, s.ComputedShare)).ToList(), e.CreatedById, e.DeletedAt != null);
}
