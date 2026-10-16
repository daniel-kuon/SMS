using Sms.Domain;
using Sms.Domain.Costs;

namespace Sms.Domain.Tests;

public class CostsTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Carla = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void Equal_split_distributes_remaining_cents_to_lowest_ids()
    {
        var shares = CostSplit.Split(10.00m, "EUR", SplitMode.Equal, [new(Anna), new(Ben), new(Carla)]);
        Assert.Equal(3.34m, shares[Anna]);
        Assert.Equal(3.33m, shares[Ben]);
        Assert.Equal(3.33m, shares[Carla]);
        Assert.Equal(10.00m, shares.Values.Sum());
    }

    [Fact]
    public void Shares_always_sum_to_amount_for_random_inputs()
    {
        var rnd = new Random(42);
        for (var i = 0; i < 2000; i++)
        {
            var amount = Math.Round((decimal)rnd.NextDouble() * 1000, 2);
            var n = rnd.Next(1, 8);
            var participants = Enumerable.Range(0, n).Select(_ => new Participant(Guid.NewGuid(), rnd.Next(1, 5) / 2m)).ToList();
            var mode = rnd.Next(2) == 0 ? SplitMode.Equal : SplitMode.Weights;
            var shares = CostSplit.Split(amount, "EUR", mode, participants);
            Assert.Equal(amount, shares.Values.Sum());
            Assert.All(shares.Values, s => Assert.Equal(s, Math.Round(s, 2)));
        }
    }

    [Fact]
    public void Exact_split_must_sum_to_amount()
    {
        Assert.Throws<ArgumentException>(() => CostSplit.Split(10m, "EUR", SplitMode.Exact, [new(Anna, ExactAmount: 4m), new(Ben, ExactAmount: 5m)]));
        var ok = CostSplit.Split(10m, "EUR", SplitMode.Exact, [new(Anna, ExactAmount: 4m), new(Ben, ExactAmount: 6m)]);
        Assert.Equal(6m, ok[Ben]);
    }

    [Fact]
    public void Jpy_has_no_minor_units()
    {
        var shares = CostSplit.Split(1000m, "JPY", SplitMode.Equal, [new(Anna), new(Ben), new(Carla)]);
        Assert.Equal(1000m, shares.Values.Sum());
        Assert.All(shares.Values, s => Assert.Equal(s, Math.Floor(s)));
        Assert.Equal(88.89m, Money.Convert(1000m, 1m / 11.25m, "EUR"));
    }

    [Fact]
    public void Worked_example_from_spec_09()
    {
        var expenses = new[]
        {
            (Anna, 36.00m, CostSplit.Split(36m, "EUR", SplitMode.Equal, [new(Anna), new(Ben), new(Carla)]).Select(s => (s.Key, s.Value))),
            (Ben, 90.00m, CostSplit.Split(90m, "EUR", SplitMode.Equal, [new(Anna), new(Ben), new(Carla)]).Select(s => (s.Key, s.Value))),
            (Carla, 50.00m, CostSplit.Split(50m, "EUR", SplitMode.Equal, [new(Anna), new(Ben)]).Select(s => (s.Key, s.Value))),
        };
        var balances = Settlements.Balances([Anna, Ben, Carla], expenses, []);
        Assert.Equal(-31m, balances.Single(b => b.CrewMemberId == Anna).Balance);
        Assert.Equal(23m, balances.Single(b => b.CrewMemberId == Ben).Balance);
        Assert.Equal(8m, balances.Single(b => b.CrewMemberId == Carla).Balance);
        Assert.Equal(0m, balances.Sum(b => b.Balance));

        var transfers = Settlements.SuggestTransfers(balances, "EUR");
        Assert.Equal(2, transfers.Count);
        Assert.Contains(transfers, t => t.FromCrewMemberId == Anna && t.ToCrewMemberId == Ben && t.Amount == 23m);
        Assert.Contains(transfers, t => t.FromCrewMemberId == Anna && t.ToCrewMemberId == Carla && t.Amount == 8m);
    }

    [Fact]
    public void Transfers_settle_everyone_with_at_most_n_minus_1_transfers()
    {
        var rnd = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var members = Enumerable.Range(0, rnd.Next(2, 9)).Select(_ => Guid.NewGuid()).ToList();
            var expenses = Enumerable.Range(0, rnd.Next(1, 10)).Select(_ =>
            {
                var amount = Math.Round((decimal)rnd.NextDouble() * 500, 2);
                var payer = members[rnd.Next(members.Count)];
                var shares = CostSplit.Split(amount, "EUR", SplitMode.Equal, members.Select(m => new Participant(m)).ToList());
                return (payer, amount, shares.Select(s => (s.Key, s.Value)));
            }).ToList();
            var balances = Settlements.Balances(members, expenses, []);
            Assert.Equal(0m, balances.Sum(b => b.Balance));
            var transfers = Settlements.SuggestTransfers(balances, "EUR");
            Assert.True(transfers.Count <= members.Count - 1);
            var after = balances.ToDictionary(b => b.CrewMemberId, b => b.Balance);
            foreach (var t in transfers) { after[t.FromCrewMemberId] += t.Amount; after[t.ToCrewMemberId] -= t.Amount; }
            Assert.All(after.Values, v => Assert.True(Math.Abs(v) < 0.01m, $"residual {v}"));
        }
    }

    [Fact]
    public void Effective_visibility_is_the_minimum()
    {
        Assert.Equal(Visibility.Crew, VisibilityRules.Effective(Visibility.Public, Visibility.Crew, Visibility.Public));
        Assert.Equal(Visibility.Crew, VisibilityRules.Effective(Visibility.Public, Visibility.Public, Visibility.Private));
        Assert.Equal(Visibility.Private, VisibilityRules.Effective(Visibility.Private, Visibility.Public, Visibility.Public));
        Assert.False(VisibilityRules.CanView(Visibility.Crew, isAuthor: false, isAcceptedCrew: false, isAdmin: false));
        Assert.True(VisibilityRules.CanView(Visibility.Crew, isAuthor: false, isAcceptedCrew: true, isAdmin: false));
    }
}
