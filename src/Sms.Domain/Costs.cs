namespace Sms.Domain.Costs;

/// <summary>Money helpers (spec 09): minor units per ISO 4217 currency, half-away-from-zero rounding.</summary>
public static class Money
{
    private static readonly Dictionary<string, int> MinorUnitOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JPY"] = 0, ["KRW"] = 0, ["ISK"] = 0, ["HUF"] = 2, ["CLP"] = 0, ["VND"] = 0, ["XAF"] = 0, ["XOF"] = 0,
        ["BHD"] = 3, ["KWD"] = 3, ["OMR"] = 3, ["JOD"] = 3, ["TND"] = 3,
    };

    public static int MinorUnits(string currency) =>
        MinorUnitOverrides.TryGetValue(currency, out var d) ? d : 2;

    public static decimal Round(decimal value, string currency) =>
        Math.Round(value, MinorUnits(currency), MidpointRounding.AwayFromZero);

    /// <summary>Converts <paramref name="amount"/> with <paramref name="rate"/> (target per source) and rounds to the target currency.</summary>
    public static decimal Convert(decimal amount, decimal rate, string targetCurrency) =>
        Round(amount * rate, targetCurrency);

    /// <summary>
    /// Cross rate via EUR (spec 09 §9.2): rate(from→to) = PerEur(to) / PerEur(from); EUR has PerEur = 1.
    /// </summary>
    public static decimal CrossRate(decimal perEurFrom, decimal perEurTo) => perEurTo / perEurFrom;
}

public readonly record struct Participant(Guid CrewMemberId, decimal? Weight = null, decimal? ExactAmount = null);

/// <summary>Expense splitting (spec 09 §9.1): shares always sum exactly to the amount.</summary>
public static class CostSplit
{
    public static Dictionary<Guid, decimal> Split(decimal amount, string currency, SplitMode mode, IReadOnlyList<Participant> participants)
    {
        if (participants.Count == 0) throw new ArgumentException("At least one participant is required.", nameof(participants));
        var digits = Money.MinorUnits(currency);

        if (mode == SplitMode.Exact)
        {
            var exact = participants.ToDictionary(p => p.CrewMemberId, p => Money.Round(p.ExactAmount ?? throw new ArgumentException("Exact amount missing."), currency));
            if (exact.Values.Sum() != Money.Round(amount, currency))
                throw new ArgumentException("Exact amounts must sum to the expense amount.");
            return exact;
        }

        var weights = participants.ToDictionary(p => p.CrewMemberId, p => mode == SplitMode.Weights ? (p.Weight ?? 1m) : 1m);
        var totalWeight = weights.Values.Sum();
        if (totalWeight <= 0) throw new ArgumentException("Weights must be positive.");

        // Largest-remainder method on minor units.
        var factor = (decimal)Math.Pow(10, digits);
        var totalMinor = (long)Math.Round(amount * factor, MidpointRounding.AwayFromZero);
        var raw = weights.ToDictionary(w => w.Key, w => totalMinor * w.Value / totalWeight);
        var floored = raw.ToDictionary(r => r.Key, r => (long)Math.Floor(r.Value));
        var remaining = totalMinor - floored.Values.Sum();
        foreach (var id in raw.OrderByDescending(r => r.Value - Math.Floor(r.Value)).ThenBy(r => r.Key).Select(r => r.Key))
        {
            if (remaining <= 0) break;
            floored[id] += 1;
            remaining--;
        }
        return floored.ToDictionary(f => f.Key, f => f.Value / factor);
    }
}

public sealed record MemberBalance(Guid CrewMemberId, decimal Paid, decimal Share, decimal Sent, decimal Received)
{
    /// <summary>balance = paid − share + sent − received (spec 09 §9.3). &gt; 0 gets money back, &lt; 0 owes.</summary>
    public decimal Balance => Paid - Share + Sent - Received;
}

public sealed record Transfer(Guid FromCrewMemberId, Guid ToCrewMemberId, decimal Amount);

public static class Settlements
{
    public static List<MemberBalance> Balances(
        IEnumerable<Guid> members,
        IEnumerable<(Guid paidBy, decimal amountInTripCurrency, IEnumerable<(Guid crewMemberId, decimal share)> shares)> expenses,
        IEnumerable<(Guid from, Guid to, decimal amount)> confirmedSettlements)
    {
        var paid = new Dictionary<Guid, decimal>();
        var share = new Dictionary<Guid, decimal>();
        var sent = new Dictionary<Guid, decimal>();
        var received = new Dictionary<Guid, decimal>();
        foreach (var e in expenses)
        {
            paid[e.paidBy] = paid.GetValueOrDefault(e.paidBy) + e.amountInTripCurrency;
            foreach (var s in e.shares) share[s.crewMemberId] = share.GetValueOrDefault(s.crewMemberId) + s.share;
        }
        foreach (var s in confirmedSettlements)
        {
            sent[s.from] = sent.GetValueOrDefault(s.from) + s.amount;
            received[s.to] = received.GetValueOrDefault(s.to) + s.amount;
        }
        var all = members.Union(paid.Keys).Union(share.Keys).Union(sent.Keys).Union(received.Keys).Distinct();
        return all.Select(m => new MemberBalance(m, paid.GetValueOrDefault(m), share.GetValueOrDefault(m), sent.GetValueOrDefault(m), received.GetValueOrDefault(m))).ToList();
    }

    /// <summary>Greedy "who pays whom" (spec 09 §9.4): at most n − 1 transfers; residuals below one cent are ignored.</summary>
    public static List<Transfer> SuggestTransfers(IEnumerable<MemberBalance> balances, string currency)
    {
        var epsilon = 1m / (decimal)Math.Pow(10, Money.MinorUnits(currency));
        var creditors = balances.Where(b => b.Balance >= epsilon).Select(b => (id: b.CrewMemberId, amount: b.Balance)).OrderByDescending(c => c.amount).ThenBy(c => c.id).ToList();
        var debtors = balances.Where(b => b.Balance <= -epsilon).Select(b => (id: b.CrewMemberId, amount: -b.Balance)).OrderByDescending(d => d.amount).ThenBy(d => d.id).ToList();
        var result = new List<Transfer>();
        while (creditors.Count > 0 && debtors.Count > 0)
        {
            var d = debtors[0];
            var c = creditors[0];
            var x = Money.Round(Math.Min(d.amount, c.amount), currency);
            if (x >= epsilon) result.Add(new Transfer(d.id, c.id, x));
            d.amount -= x; c.amount -= x;
            debtors.RemoveAt(0); creditors.RemoveAt(0);
            if (d.amount >= epsilon) { debtors.Add(d); debtors = debtors.OrderByDescending(v => v.amount).ThenBy(v => v.id).ToList(); }
            if (c.amount >= epsilon) { creditors.Add(c); creditors = creditors.OrderByDescending(v => v.amount).ThenBy(v => v.id).ToList(); }
        }
        return result;
    }
}
