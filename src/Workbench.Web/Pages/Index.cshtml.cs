using Microsoft.AspNetCore.Mvc.RazorPages;
using Workbench.Web.Data;

namespace Workbench.Web.Pages;

public class IndexModel : PageModel
{
    private static readonly string[] Buckets = ["1-30", "31-60", "61-90", "90+"];
    private readonly ReportRepository _reports;

    public IndexModel(ReportRepository reports)
    {
        _reports = reports;
    }

    public DateOnly AsOf { get; private set; }
    public DashboardKpis Kpis { get; private set; } = new();
    public IReadOnlyList<TermCollection> CollectionRates { get; private set; } = [];

    // chart data, serialized straight into the page
    public object PaymentTrendChart { get; private set; } = new();
    public object AgingChart { get; private set; } = new();

    public async Task OnGetAsync()
    {
        AsOf = DateOnly.FromDateTime(DateTime.Today);

        var kpisTask = _reports.GetKpisAsync(AsOf);
        var trendTask = _reports.GetPaymentTrendAsync(AsOf, 12);
        var ratesTask = _reports.GetCollectionRatesAsync(AsOf);
        var agingTask = _reports.GetPastDueByInstitutionAsync(AsOf);
        await Task.WhenAll(kpisTask, trendTask, ratesTask, agingTask);

        Kpis = kpisTask.Result;
        CollectionRates = ratesTask.Result;

        var trend = trendTask.Result;
        var months = trend.Select(t => t.MonthStart).Distinct().OrderBy(m => m).ToList();
        PaymentTrendChart = new
        {
            labels = months.Select(m => m.ToString("MMM yyyy")),
            datasets = trend.Select(t => t.FundSource).Distinct().OrderBy(s => s).Select(source => new
            {
                label = FundSourceLabel(source),
                data = months.Select(m => trend.Where(t => t.MonthStart == m && t.FundSource == source).Sum(t => t.Amount))
            })
        };

        var aging = agingTask.Result;
        var institutions = aging.Select(a => a.InstitutionCode).Distinct().OrderBy(c => c).ToList();
        AgingChart = new
        {
            labels = institutions,
            datasets = Buckets.Select(bucket => new
            {
                label = bucket + " days",
                data = institutions.Select(i => aging.Where(a => a.InstitutionCode == i && a.AgingBucket == bucket).Sum(a => a.Amount))
            })
        };
    }

    private static string FundSourceLabel(string source) => source switch
    {
        "TitleIV" => "Title IV aid",
        "VA" => "VA benefits",
        "MilitaryTA" => "Military TA",
        "SelfPay" => "Student self-pay",
        "PaymentPlan" => "Payment plan (vendor)",
        _ => source
    };
}
