using Workbench.Core.PastDue;
using Workbench.Web.Data;

namespace Workbench.Web.Services;

public sealed record PastDueLine(PastDueRow Row, HoldDecision Decision);

public sealed record PastDueFilter(DateOnly AsOf, int? InstitutionId, string? TermCode, string? AgingBucket, AccountAction? Action);

/// <summary>Pulls the past-due rows from SQL and runs each one through the hold policy.</summary>
public sealed class PastDueService
{
    private readonly ReportRepository _reports;
    private readonly HoldPolicy _policy;

    public PastDueService(ReportRepository reports, HoldPolicy policy)
    {
        _reports = reports;
        _policy = policy;
    }

    public async Task<IReadOnlyList<PastDueLine>> GetAsync(PastDueFilter filter)
    {
        var rows = await _reports.GetPastDueAsync(filter.AsOf, filter.InstitutionId, filter.TermCode, filter.AgingBucket);

        var lines = rows.Select(r => new PastDueLine(r, _policy.Evaluate(new HoldPolicyInput(
                r.PastDueAmount,
                r.VaChapter,
                r.VaStatus,
                r.VaCertifiedDate is null ? null : DateOnly.FromDateTime(r.VaCertifiedDate.Value),
                r.VaExpectedAmount,
                r.PaymentPlanStatus,
                CurrentlyEnrolled: r.EnrollmentStatus == "Active"), filter.AsOf)));

        if (filter.Action is not null)
            lines = lines.Where(l => l.Decision.Action == filter.Action);

        return lines.ToList();
    }

    public static string Label(AccountAction action) => action switch
    {
        AccountAction.HoldEligible => "Hold eligible",
        AccountAction.VaProtected => "VA protected",
        AccountAction.VaPartiallyProtected => "VA partial",
        AccountAction.VaFollowUp => "VA follow-up",
        AccountAction.OnPaymentPlan => "On payment plan",
        AccountAction.BelowThreshold => "Reminder only",
        _ => "Not past due"
    };

    public static string BadgeClass(AccountAction action) => action switch
    {
        AccountAction.HoldEligible => "bg-danger",
        AccountAction.VaProtected => "bg-success",
        AccountAction.VaPartiallyProtected => "bg-warning text-dark",
        AccountAction.VaFollowUp => "bg-warning text-dark",
        AccountAction.OnPaymentPlan => "bg-info text-dark",
        _ => "bg-secondary"
    };
}
