using System.Globalization;
using CsvHelper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Workbench.Web.Data;

namespace Workbench.Web.Pages;

public class MonthModel : PageModel
{
    public const int PageSize = 50;

    private readonly ReportRepository _reports;
    private readonly AuditRepository _audit;

    public MonthModel(ReportRepository reports, AuditRepository audit)
    {
        _reports = reports;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true, Name = "m")] public string? MonthKey { get; set; }
    [BindProperty(SupportsGet = true)] public string? Source { get; set; }
    [BindProperty(SupportsGet = true)] public string? School { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public DateOnly MonthStart { get; private set; }
    public IReadOnlyList<MonthReceipt> Receipts { get; private set; } = [];
    public IReadOnlyList<MonthReceipt> Filtered { get; private set; } = [];
    public IReadOnlyList<MonthReceipt> PageRows { get; private set; } = [];
    public int PageCount { get; private set; }

    public decimal Total => Receipts.Sum(r => r.Amount);
    public MonthComparison? PriorMonth { get; private set; }
    public MonthComparison? PriorYear { get; private set; }

    public IReadOnlyList<(string Key, string Label, decimal Amount, int Count)> BySource { get; private set; } = [];
    public IReadOnlyList<(string Key, decimal Amount, int Count, int Students)> BySchool { get; private set; } = [];
    public object DailyChart { get; private set; } = new();

    public string MonthLabel => MonthStart.ToString("MMMM yyyy");
    public string PrevKey => MonthStart.AddMonths(-1).ToString("yyyy-MM");
    public string NextKey => MonthStart.AddMonths(1).ToString("yyyy-MM");
    public bool HasNext => MonthStart.AddMonths(1) <= DateOnly.FromDateTime(DateTime.Today);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!TryParseMonth(out var monthStart))
            return RedirectToPage(new { m = DateTime.Today.ToString("yyyy-MM") });
        MonthStart = monthStart;

        var receiptsTask = _reports.GetMonthReceiptsAsync(MonthStart);
        var comparisonsTask = _reports.GetMonthComparisonsAsync(MonthStart);
        await Task.WhenAll(receiptsTask, comparisonsTask);

        Receipts = receiptsTask.Result;
        PriorMonth = comparisonsTask.Result.SingleOrDefault(c => c.Label == "PriorMonth");
        PriorYear = comparisonsTask.Result.SingleOrDefault(c => c.Label == "PriorYear");

        BySource = Receipts.GroupBy(r => r.FundSource)
            .Select(g => (g.Key, IndexModel.FundSourceLabel(g.Key), g.Sum(r => r.Amount), g.Count()))
            .OrderByDescending(x => x.Item3).ToList();

        BySchool = Receipts.GroupBy(r => r.InstitutionCode)
            .Select(g => (g.Key, g.Sum(r => r.Amount), g.Count(), g.Select(r => r.StudentId).Distinct().Count()))
            .OrderByDescending(x => x.Item2).ToList();

        var days = Enumerable.Range(1, DateTime.DaysInMonth(MonthStart.Year, MonthStart.Month)).ToList();
        DailyChart = new
        {
            labels = days.Select(d => d.ToString()),
            datasets = BySource.Select(src => new
            {
                label = src.Label,
                data = days.Select(d => Receipts.Where(r => r.FundSource == src.Key && r.EffectiveDate.Day == d).Sum(r => r.Amount))
            })
        };

        Filtered = ApplyFilters(Receipts);
        PageCount = Math.Max(1, (int)Math.Ceiling(Filtered.Count / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, PageCount);
        PageRows = Filtered.OrderByDescending(r => r.Amount).Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        if (!TryParseMonth(out var monthStart))
            return BadRequest();

        var rows = ApplyFilters(await _reports.GetMonthReceiptsAsync(monthStart));

        await using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        await using (var csv = new CsvWriter(buffer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(rows.Select(r => new
            {
                Date = r.EffectiveDate.ToString("yyyy-MM-dd"),
                r.StudentId,
                r.StudentName,
                Institution = r.InstitutionCode,
                Term = r.TermCode,
                r.DetailCode,
                r.Description,
                r.FundSource,
                r.Amount,
                VendorRef = r.ExternalRef ?? ""
            }));
        }

        await _audit.WriteAsync("demo-user", "MONTH_RECEIPTS_EXPORTED", "Report", monthStart.ToString("yyyy-MM"),
            $"{rows.Count} rows. Source={Source} School={School}");

        return File(System.Text.Encoding.UTF8.GetBytes(buffer.ToString()), "text/csv", $"receipts_{monthStart:yyyy-MM}.csv");
    }

    public static string ChangeText(decimal current, MonthComparison? prior)
    {
        if (prior is null || !prior.HasData)
            return "before the data in this demo starts";
        if (prior.Amount is null or 0)
            return "nothing received that month";
        var change = (current - prior.Amount.Value) / prior.Amount.Value;
        // aid lands in term-start months, so month-to-month swings are seasonal rather than a trend
        return Math.Abs(change) >= 3
            ? $"this month is {current / prior.Amount.Value:0.0}x that (aid disbursement timing)"
            : $"this month is {change:+0%;-0%} vs that";
    }

    public Dictionary<string, string?> RouteWith(int? page = null, string? source = null, string? school = null, bool clear = false) => new()
    {
        ["m"] = MonthStart.ToString("yyyy-MM"),
        ["source"] = clear ? null : source ?? Source,
        ["school"] = clear ? null : school ?? School,
        ["p"] = page?.ToString()
    };

    private bool TryParseMonth(out DateOnly monthStart) =>
        DateOnly.TryParseExact((MonthKey ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out monthStart);

    private List<MonthReceipt> ApplyFilters(IEnumerable<MonthReceipt> rows) => rows
        .Where(r => string.IsNullOrEmpty(Source) || r.FundSource == Source)
        .Where(r => string.IsNullOrEmpty(School) || r.InstitutionCode == School)
        .ToList();
}
