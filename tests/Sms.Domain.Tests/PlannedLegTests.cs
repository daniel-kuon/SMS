using static Sms.Domain.PlannedLegRules;

namespace Sms.Domain.Tests;

public class PlannedLegTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Fact]
    public void Leg_after_an_alternative_may_start_at_the_alternative_harbour()
    {
        // step 0: A→B or A→C; step 1 starts at C (the alternative's harbour)
        Assert.Null(Validate([new(1, 0, C, D), new(0, 0, A, B), new(0, 1, A, C)], A));
        Assert.Null(Validate([new(0, 0, B, C)], null)); // no planned start: any
    }

    [Fact]
    public void Broken_chains_gaps_and_duplicates_are_rejected()
    {
        Assert.NotNull(Validate([new(0, 0, A, B), new(1, 0, C, D)], null)); // C not reached by step 0
        Assert.NotNull(Validate([new(0, 0, B, C)], A));                     // not the planned start
        Assert.NotNull(Validate([new(0, 0, A, B), new(2, 0, B, C)], null)); // gap
        Assert.NotNull(Validate([new(0, 0, A, B), new(0, 0, A, C)], null)); // duplicate
        Assert.NotNull(Validate([new(1, 0, A, B)], null));                  // no step 0
    }
}
