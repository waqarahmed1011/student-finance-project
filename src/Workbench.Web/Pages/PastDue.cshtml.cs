using System.Globalization;
using CsvHelper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Workbench.Core.PastDue;
using Workbench.Web.Data;
using Workbench.Web.Services;

namespace Workbench.Web.Pages;

public class PastDueModel : PageModel
{
    public const int PageSize = 50;

    private readonly PastDueService _pastDue;
    private readonly ReportRepository _reports;
    private readonly AuditRepository _audit;

    public PastDueModel(PastDueService pastDue, ReportRepository reports, AuditRepository audit)
    {
        _pastDue = pastDue;
        _reports = reports;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true)] public DateOnly? AsOf { get; set; }
    [BindProperty(SupportsGet = true)] public int? InstitutionId { get; set; }
    [BindProperty(SupportsGet = true)] public string? TermCode { get; set; }
    [BindProperty(SupportsGet = true)] public string? Bucket { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public AccountAction? Action { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<LookupItem> Institutions { get; private set; } = [];
    public IReadOnlyList<LookupItem> Terms { get; private set; } = [];
    public IReadOnlyList<PastDueLine> AllLines { get; private set; } = [];
    public IReadOnlyList<PastDueLine> FilteredLines { get; private set; } = [];
    public IReadOnlyList<PastDueLine> PageLines { get; private set; } = [];
    public int PageCount { get; private set; }

    public DateOnly EffectiveAsOf => AsOf ?? DateOnly.FromDateTime(DateTime.Today);

    public static readonly AccountAction[] ActionOrder =
    [
        AccountAction.HoldEligible, AccountAction.VaFollowUp, AccountAction.VaPartiallyProtected,
        AccountAction.VaProtected, AccountAction.OnPaymentPlan, AccountAction.BelowThreshold
    ];

    public static readonly string[] BucketOptions = ["1-30", "31-60", "61-90", "90+"];

    public async Task OnGetAsync()
    {
        Institutions = await _reports.GetInstitutionsAsync();
        Terms = await _reports.GetTermsAsync();

        // summary cards always show every action; the action filter only narrows the table
        AllLines = await _pastDue.GetAsync(new PastDueFilter(EffectiveAsOf, InstitutionId, TermCode, Bucket, null));
        FilteredLines = Action is null ? AllLines : AllLines.Where(l => l.Decision.Action == Action).ToList();

        PageCount = Math.Max(1, (int)Math.Ceiling(FilteredLines.Count / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, PageCount);
        PageLines = FilteredLines.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var lines = await _pastDue.GetAsync(new PastDueFilter(EffectiveAsOf, InstitutionId, TermCode, Bucket, Action));

        await using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        await using (var csv = new CsvWriter(buffer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(lines.Select(l => new
            {
                l.Row.StudentId,
                l.Row.FirstName,
                l.Row.LastName,
                l.Row.Email,
                Institution = l.Row.InstitutionCode,
                Program = l.Row.ProgramName,
                Term = l.Row.TermCode,
                l.Row.EnrollmentStatus,
                l.Row.Balance,
                l.Row.Due1To30,
                l.Row.Due31To60,
                l.Row.Due61To90,
                l.Row.Due90Plus,
                l.Row.PastDueAmount,
                l.Row.DaysPastDue,
                VaChapter = l.Row.VaChapter ?? "",
                VaStatus = l.Row.VaStatus ?? "",
                PaymentPlan = l.Row.PaymentPlanStatus ?? "",
                RecommendedAction = PastDueService.Label(l.Decision.Action),
                l.Decision.ProtectedAmount,
                l.Decision.HoldEligibleAmount,
                l.Decision.Reason
            }));
        }

        await _audit.WriteAsync("demo-user", "PAST_DUE_EXPORTED", "Report", "PastDue",
            $"{lines.Count} rows. AsOf={EffectiveAsOf:yyyy-MM-dd} Institution={InstitutionId} Term={TermCode} Bucket={Bucket} Action={Action}");

        var fileName = $"past-due_{EffectiveAsOf:yyyy-MM-dd}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(buffer.ToString()), "text/csv", fileName);
    }

    public Dictionary<string, string?> RouteWith(AccountAction? action = null, int? page = null, bool clearAction = false) => new()
    {
        ["asOf"] = AsOf?.ToString("yyyy-MM-dd"),
        ["institutionId"] = InstitutionId?.ToString(),
        ["termCode"] = TermCode,
        ["bucket"] = Bucket,
        ["status"] = clearAction ? null : (action ?? Action)?.ToString(),
        ["p"] = page?.ToString()
    };
}
