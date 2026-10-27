using System.Globalization;

namespace Workbench.Core.PastDue;

public enum AccountAction
{
    NotPastDue,
    BelowThreshold,
    OnPaymentPlan,
    VaProtected,
    VaPartiallyProtected,
    VaFollowUp,
    HoldEligible
}

public sealed record HoldPolicyInput(
    decimal PastDueAmount,
    string? VaChapter,
    string? VaStatus,
    DateOnly? VaCertifiedDate,
    decimal? VaExpectedAmount,
    string? PaymentPlanStatus,
    bool CurrentlyEnrolled = true);

public sealed record HoldDecision(
    AccountAction Action,
    decimal ProtectedAmount,
    decimal HoldEligibleAmount,
    string Reason);

public sealed class HoldPolicyOptions
{
    /// <summary>Past-due amounts under this don't get a registration hold. Placeholder, business would set it.</summary>
    public decimal MinimumHoldBalance { get; set; } = 200m;

    /// <summary>38 U.S.C. 3679(e): protection runs up to 90 days after the school certifies tuition and fees.</summary>
    public int VaProtectionDays { get; set; } = 90;
}

/// <summary>
/// Decides what Student Finance is allowed to do about a past-due balance.
///
/// The VA rule (38 U.S.C. 3679(e)): a Chapter 33 or Chapter 31 student whose VA payment is pending
/// can't be charged a late fee, blocked from class, or have a hold placed because of the delayed VA payment.
/// That protection lasts until VA pays or 90 days after certification, whichever comes first, and it only
/// covers the amount VA is expected to pay. The student still owes anything above that.
///
/// Simplified for the demo: it assumes the student already gave the school their certificate of eligibility.
/// </summary>
public sealed class HoldPolicy
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private readonly HoldPolicyOptions _options;

    public HoldPolicy(HoldPolicyOptions options)
    {
        _options = options;
    }

    public HoldDecision Evaluate(HoldPolicyInput input, DateOnly asOfDate)
    {
        var pastDue = input.PastDueAmount;

        if (pastDue <= 0)
            return new HoldDecision(AccountAction.NotPastDue, 0, 0, "Nothing past due.");

        if (string.Equals(input.PaymentPlanStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            return new HoldDecision(AccountAction.OnPaymentPlan, 0, 0,
                "Active payment plan with the plan vendor. Balance is collected on the plan schedule.");
        }

        var vaPending = string.Equals(input.VaStatus, "PENDING", StringComparison.OrdinalIgnoreCase)
                        && input.VaCertifiedDate is not null;

        if (vaPending)
        {
            var chapter = input.VaChapter is null ? "VA" : $"Ch. {input.VaChapter}";
            var windowEnd = input.VaCertifiedDate!.Value.AddDays(_options.VaProtectionDays);

            if (asOfDate > windowEnd)
            {
                return new HoldDecision(AccountAction.VaFollowUp, 0, pastDue,
                    string.Create(Us, $"{chapter} payment still pending {_options.VaProtectionDays}+ days after certification ") +
                    string.Create(Us, $"({input.VaCertifiedDate:MM/dd/yyyy}). Protection ended {windowEnd:MM/dd/yyyy}. ") +
                    "Check with the School Certifying Official before placing a hold.");
            }

            var protectedAmount = Math.Min(pastDue, input.VaExpectedAmount ?? pastDue);
            var remaining = pastDue - protectedAmount;

            if (remaining < _options.MinimumHoldBalance)
            {
                return new HoldDecision(AccountAction.VaProtected, protectedAmount, 0,
                    string.Create(Us, $"{chapter} payment pending. No hold or late fee through {windowEnd:MM/dd/yyyy} (38 U.S.C. 3679(e))."));
            }

            return new HoldDecision(AccountAction.VaPartiallyProtected, protectedAmount, remaining,
                string.Create(Us, $"{chapter} covers {protectedAmount:C} and is protected through {windowEnd:MM/dd/yyyy}. ") +
                string.Create(Us, $"Student is responsible for the remaining {remaining:C}."));
        }

        if (pastDue < _options.MinimumHoldBalance)
        {
            return new HoldDecision(AccountAction.BelowThreshold, 0, 0,
                string.Create(Us, $"Under the {_options.MinimumHoldBalance:C} hold threshold. Reminder only."));
        }

        return new HoldDecision(AccountAction.HoldEligible, 0, pastDue,
            input.CurrentlyEnrolled
                ? "Eligible for registration hold and outreach."
                : "No longer enrolled. Eligible for outreach and collections review.");
    }
}
