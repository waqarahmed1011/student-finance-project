using System.Data;
using Dapper;

namespace Workbench.Web.Data;

/// <summary>Fixed, reviewed reports: past-due aging and the dashboard.</summary>
public sealed class ReportRepository
{
    private readonly SqlConnectionFactory _db;

    public ReportRepository(SqlConnectionFactory db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PastDueRow>> GetPastDueAsync(DateOnly asOf, int? institutionId, string? termCode, string? agingBucket)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<PastDueRow>("dbo.usp_GetPastDueAging",
            new { AsOfDate = asOf.ToDateTime(TimeOnly.MinValue), InstitutionId = institutionId, TermCode = termCode, AgingBucket = agingBucket },
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<LookupItem>> GetInstitutionsAsync()
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<LookupItem>(
            "SELECT CAST(InstitutionId AS VARCHAR(10)) AS Value, Name AS Text FROM dbo.Institution ORDER BY Name");
        return rows.AsList();
    }

    public async Task<IReadOnlyList<LookupItem>> GetTermsAsync()
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<LookupItem>(
            "SELECT TermCode AS Value, Description AS Text FROM dbo.Term ORDER BY TermCode DESC");
        return rows.AsList();
    }

    public async Task<DashboardKpis> GetKpisAsync(DateOnly asOf)
    {
        await using var conn = _db.CreateWorkbench();
        return await conn.QuerySingleAsync<DashboardKpis>("dbo.usp_DashboardKpis",
            new { AsOfDate = asOf.ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure);
    }

    public async Task<IReadOnlyList<PaymentTrendPoint>> GetPaymentTrendAsync(DateOnly asOf, int months)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<PaymentTrendPoint>("dbo.usp_PaymentTrendByMonth",
            new { AsOfDate = asOf.ToDateTime(TimeOnly.MinValue), Months = months }, commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TermCollection>> GetCollectionRatesAsync(DateOnly asOf)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<TermCollection>("dbo.usp_CollectionRateByTerm",
            new { AsOfDate = asOf.ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<InstitutionAging>> GetPastDueByInstitutionAsync(DateOnly asOf)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<InstitutionAging>("dbo.usp_PastDueByInstitution",
            new { AsOfDate = asOf.ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    public async Task<(IReadOnlyList<FinancialPeriod> Periods, FinancialBalances Balances)> GetFinancialSummaryAsync(DateOnly asOf)
    {
        await using var conn = _db.CreateWorkbench();
        using var multi = await conn.QueryMultipleAsync("dbo.usp_FinancialSummary",
            new { AsOfDate = asOf.ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure);
        var periods = (await multi.ReadAsync<FinancialPeriod>()).AsList();
        var balances = await multi.ReadSingleAsync<FinancialBalances>();
        return (periods, balances);
    }

    public async Task<IReadOnlyList<InstitutionFinancials>> GetFinancialsByInstitutionAsync(DateOnly asOf)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<InstitutionFinancials>("dbo.usp_FinancialsByInstitution",
            new { AsOfDate = asOf.ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<MonthReceipt>> GetMonthReceiptsAsync(DateOnly monthStart)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<MonthReceipt>("dbo.usp_MonthReceipts",
            new { MonthStart = monthStart.ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<MonthComparison>> GetMonthComparisonsAsync(DateOnly monthStart)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<MonthComparison>("dbo.usp_MonthReceiptComparisons",
            new { MonthStart = monthStart.ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }
}
