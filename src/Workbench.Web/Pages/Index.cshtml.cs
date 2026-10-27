using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Workbench.Core.Finance;
using Workbench.Web.Data;

namespace Workbench.Web.Pages;

public class IndexModel : PageModel
{
    private static readonly string[] Buckets = ["1-30", "31-60", "61-90", "90+"];
    private readonly ReportRepository _reports;
    private readonly AllowanceRates _allowanceRates;

    public IndexModel(ReportRepository reports, IOptions<AllowanceRates> allowanceRates)
    {
        _reports = reports;
        _allowanceRates = allowanceRates.Value;
    }

    public DateOnly AsOf { get; private set; }
    public DashboardKpis Kpis { get; private set; } = new();
    public IReadOnlyList<TermCollection> CollectionRates { get; private set; } = [];

    public FinancialPeriod PriorFiscalYear { get; private set; } = new();
    public FinancialPeriod PriorYearToDate { get; private set; } = new();
    public FinancialPeriod YearToDate { get; private set; } = new();
    public FinancialBalances Balances { get; private set; } = new();
    public decimal Allowance { get; private set; }
    public decimal NetReceivable => Kpis.TotalReceivable - Allowance;
    public decimal? DaysSalesOutstanding { get; private set; }
    public IReadOnlyList<InstitutionFinancials> Institutions { get; private set; } = [];

    public string FiscalYearLabel => $"FY{(AsOf.Month >= 7 ? AsOf.Year + 1 : AsOf.Year)}";
    public string PriorFiscalYearLabel => $"FY{(AsOf.Month >= 7 ? AsOf.Year : AsOf.Year - 1)}";

    // chart data, serialized straight into the page
    public object PaymentTrendChart { get; private set; } = new();
    public object AgingChart { get; private set; } = new();

    public async Task OnGetAsync()
    {
        AsOf = DateOnly.FromDateTime(DateTime.Today);

        var kpisTask = _reports.GetKpisAsync(AsOf);
        var trendTask = _reports.GetPaymentTrendAsync(AsOf, 12);
        var ratesTask = _reports.GetCollectionRatesAsync(AsOf);
        var financialTask = _reports.GetFinancialSummaryAsync(AsOf);
        var institutionsTask = _reports.GetFinancialsByInstitutionAsync(AsOf);
        await Task.WhenAll(kpisTask, trendTask, ratesTask, financialTask, institutionsTask);

        Kpis = kpisTask.Result;
        CollectionRates = ratesTask.Result;
        Institutions = institutionsTask.Result;

        var (periods, balances) = financialTask.Result;
        PriorFiscalYear = periods.Single(p => p.Period == "PriorFY");
        PriorYearToDate = periods.Single(p => p.Period == "PriorYTD");
        YearToDate = periods.Single(p => p.Period == "YTD");
        Balances = balances;

        Allowance = Institutions.Sum(AllowanceFor);
        DaysSalesOutstanding = AllowancePolicy.DaysSalesOutstanding(NetReceivable, Balances.NetRevenueTrailing12);

        var trend = trendTask.Result;
        var months = trend.Select(t => t.MonthStart).Distinct().OrderBy(m => m).ToList();
        PaymentTrendChart = new
        {
            labels = months.Select(m => m.ToString("MMM yyyy")),
            keys = months.Select(m => m.ToString("yyyy-MM")),
            datasets = trend.Select(t => t.FundSource).Distinct().OrderBy(s => s).Select(source => new
            {
                label = FundSourceLabel(source),
                data = months.Select(m => trend.Where(t => t.MonthStart == m && t.FundSource == source).Sum(t => t.Amount))
            })
        };

        // the by-institution financials already carry the aging buckets, so the chart reuses them
        AgingChart = new
        {
            labels = Institutions.Select(i => i.InstitutionCode),
            datasets = Buckets.Select(bucket => new
            {
                label = bucket + " days",
                data = Institutions.Select(i => bucket switch
                {
                    "1-30" => i.Due1To30,
                    "31-60" => i.Due31To60,
                    "61-90" => i.Due61To90,
                    _ => i.Due90Plus
                })
            })
        };
    }

    public decimal AllowanceFor(InstitutionFinancials i) =>
        AllowancePolicy.Calculate(new AgingBuckets(i.NotYetDue, i.Due1To30, i.Due31To60, i.Due61To90, i.Due90Plus), _allowanceRates);

    public static string ChangeText(decimal current, decimal prior) =>
        prior == 0 ? "" : $"{(current - prior) / prior:+0.0%;-0.0%} vs same period last year";

    public static string FundSourceLabel(string source) => source switch
    {
        "TitleIV" => "Title IV aid",
        "VA" => "VA benefits",
        "MilitaryTA" => "Military TA",
        "SelfPay" => "Student self-pay",
        "PaymentPlan" => "Payment plan (vendor)",
        _ => source
    };
}
