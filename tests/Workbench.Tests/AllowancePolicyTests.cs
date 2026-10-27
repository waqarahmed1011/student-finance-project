using Workbench.Core.Finance;

namespace Workbench.Tests;

public class AllowancePolicyTests
{
    private static readonly AllowanceRates Rates = new();

    [Fact]
    public void Each_bucket_uses_its_own_rate()
    {
        var buckets = new AgingBuckets(NotYetDue: 10_000m, Due1To30: 1_000m, Due31To60: 1_000m, Due61To90: 1_000m, Due90Plus: 1_000m);

        // 100 + 30 + 80 + 200 + 500
        Assert.Equal(910m, AllowancePolicy.Calculate(buckets, Rates));
    }

    [Fact]
    public void Older_money_is_reserved_more_heavily()
    {
        var fresh = new AgingBuckets(0, 5_000m, 0, 0, 0);
        var old = new AgingBuckets(0, 0, 0, 0, 5_000m);

        Assert.True(AllowancePolicy.Calculate(old, Rates) > AllowancePolicy.Calculate(fresh, Rates) * 10);
    }

    [Fact]
    public void Rates_come_from_config()
    {
        var conservative = new AllowanceRates { Due90Plus = 1.0m };
        var buckets = new AgingBuckets(0, 0, 0, 0, 2_000m);

        Assert.Equal(2_000m, AllowancePolicy.Calculate(buckets, conservative));
    }

    [Fact]
    public void Dso_is_receivables_over_daily_revenue()
    {
        // $36.5M a year is $100k a day; $3M of receivables is 30 days
        Assert.Equal(30m, AllowancePolicy.DaysSalesOutstanding(3_000_000m, 36_500_000m));
    }

    [Fact]
    public void Dso_is_blank_without_revenue()
    {
        Assert.Null(AllowancePolicy.DaysSalesOutstanding(1_000m, 0m));
    }
}
