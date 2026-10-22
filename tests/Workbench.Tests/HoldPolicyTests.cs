using Workbench.Core.PastDue;

namespace Workbench.Tests;

public class HoldPolicyTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 22);
    private readonly HoldPolicy _policy = new(new HoldPolicyOptions { MinimumHoldBalance = 200m, VaProtectionDays = 90 });

    private static HoldPolicyInput Input(decimal pastDue, string? vaStatus = null, DateOnly? certified = null,
        decimal? vaExpected = null, string? plan = null, string? chapter = "33") =>
        new(pastDue, vaStatus is null ? null : chapter, vaStatus, certified, vaExpected, plan);

    [Fact]
    public void Nothing_past_due_is_not_past_due()
    {
        var result = _policy.Evaluate(Input(0m), AsOf);
        Assert.Equal(AccountAction.NotPastDue, result.Action);
    }

    [Fact]
    public void Plain_past_due_over_threshold_is_hold_eligible()
    {
        var result = _policy.Evaluate(Input(1500m), AsOf);
        Assert.Equal(AccountAction.HoldEligible, result.Action);
        Assert.Equal(1500m, result.HoldEligibleAmount);
    }

    [Fact]
    public void Former_student_is_routed_to_collections_review_not_a_registration_hold()
    {
        var result = _policy.Evaluate(Input(1500m) with { CurrentlyEnrolled = false }, AsOf);
        Assert.Equal(AccountAction.HoldEligible, result.Action);
        Assert.Contains("collections", result.Reason);
    }

    [Fact]
    public void Small_balance_under_threshold_gets_reminder_only()
    {
        var result = _policy.Evaluate(Input(150m), AsOf);
        Assert.Equal(AccountAction.BelowThreshold, result.Action);
        Assert.Equal(0m, result.HoldEligibleAmount);
    }

    [Fact]
    public void Active_payment_plan_is_not_treated_as_delinquent()
    {
        var result = _policy.Evaluate(Input(2400m, plan: "ACTIVE"), AsOf);
        Assert.Equal(AccountAction.OnPaymentPlan, result.Action);
        Assert.Equal(0m, result.HoldEligibleAmount);
    }

    [Fact]
    public void Cancelled_payment_plan_falls_back_to_normal_rules()
    {
        var result = _policy.Evaluate(Input(2400m, plan: "CANCELLED"), AsOf);
        Assert.Equal(AccountAction.HoldEligible, result.Action);
    }

    [Fact]
    public void Pending_va_payment_inside_window_is_fully_protected()
    {
        var result = _policy.Evaluate(Input(9000m, "PENDING", new DateOnly(2026, 8, 20), vaExpected: 9000m), AsOf);
        Assert.Equal(AccountAction.VaProtected, result.Action);
        Assert.Equal(9000m, result.ProtectedAmount);
        Assert.Equal(0m, result.HoldEligibleAmount);
        Assert.Contains("3679(e)", result.Reason);
    }

    [Fact]
    public void Protection_only_covers_what_va_is_expected_to_pay()
    {
        // 70% Ch. 33 eligibility: VA covers 6,300 of the 9,000, student owes the rest
        var result = _policy.Evaluate(Input(9000m, "PENDING", new DateOnly(2026, 8, 20), vaExpected: 6300m), AsOf);
        Assert.Equal(AccountAction.VaPartiallyProtected, result.Action);
        Assert.Equal(6300m, result.ProtectedAmount);
        Assert.Equal(2700m, result.HoldEligibleAmount);
    }

    [Fact]
    public void Small_remainder_after_va_portion_stays_protected()
    {
        var result = _policy.Evaluate(Input(6400m, "PENDING", new DateOnly(2026, 8, 20), vaExpected: 6300m), AsOf);
        Assert.Equal(AccountAction.VaProtected, result.Action);
    }

    [Fact]
    public void Day_90_is_still_protected_day_91_is_not()
    {
        var certified = AsOf.AddDays(-90);
        Assert.Equal(AccountAction.VaProtected,
            _policy.Evaluate(Input(5000m, "PENDING", certified, 5000m), AsOf).Action);

        Assert.Equal(AccountAction.VaFollowUp,
            _policy.Evaluate(Input(5000m, "PENDING", certified.AddDays(-1), 5000m), AsOf).Action);
    }

    [Fact]
    public void Va_already_paid_gets_no_protection()
    {
        var result = _policy.Evaluate(Input(800m, "PAID", new DateOnly(2026, 8, 20), 9000m), AsOf);
        Assert.Equal(AccountAction.HoldEligible, result.Action);
    }

    [Fact]
    public void Chapter_31_is_covered_too()
    {
        var result = _policy.Evaluate(Input(4000m, "PENDING", new DateOnly(2026, 9, 1), 4000m, chapter: "31"), AsOf);
        Assert.Equal(AccountAction.VaProtected, result.Action);
        Assert.Contains("Ch. 31", result.Reason);
    }
}
