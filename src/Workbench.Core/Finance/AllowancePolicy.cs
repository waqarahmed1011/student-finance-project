namespace Workbench.Core.Finance;

public sealed record AgingBuckets(decimal NotYetDue, decimal Due1To30, decimal Due31To60, decimal Due61To90, decimal Due90Plus)
{
    public decimal Total => NotYetDue + Due1To30 + Due31To60 + Due61To90 + Due90Plus;
}

/// <summary>
/// Expected loss rate for each aging bucket. Placeholders a finance team would set from its own
/// collection history; they live in config so they can change without a code change.
/// </summary>
public sealed class AllowanceRates
{
    public decimal NotYetDue { get; set; } = 0.01m;
    public decimal Due1To30 { get; set; } = 0.03m;
    public decimal Due31To60 { get; set; } = 0.08m;
    public decimal Due61To90 { get; set; } = 0.20m;
    public decimal Due90Plus { get; set; } = 0.50m;
}

/// <summary>
/// Allowance for credit losses using the aging (or "percentage of receivables") method:
/// each bucket of what students owe is multiplied by the share expected never to be collected.
/// </summary>
public static class AllowancePolicy
{
    public static decimal Calculate(AgingBuckets buckets, AllowanceRates rates) =>
        Math.Round(
            buckets.NotYetDue * rates.NotYetDue
            + buckets.Due1To30 * rates.Due1To30
            + buckets.Due31To60 * rates.Due31To60
            + buckets.Due61To90 * rates.Due61To90
            + buckets.Due90Plus * rates.Due90Plus, 2);

    /// <summary>Days sales outstanding: how many days of revenue are sitting in receivables.</summary>
    public static decimal? DaysSalesOutstanding(decimal netReceivable, decimal trailing12MonthRevenue) =>
        trailing12MonthRevenue <= 0 ? null : Math.Round(netReceivable / (trailing12MonthRevenue / 365m), 1);
}
