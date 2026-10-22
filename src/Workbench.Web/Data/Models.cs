namespace Workbench.Web.Data;

// Row shapes returned by the stored procedures. Property names match the column names so Dapper can map them.

public sealed class PastDueRow
{
    public string StudentId { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string InstitutionCode { get; init; } = "";
    public string InstitutionName { get; init; } = "";
    public string ProgramName { get; init; } = "";
    public string TermCode { get; init; } = "";
    public string TermDescription { get; init; } = "";
    public DateTime PaymentDueDate { get; init; }
    public decimal Charges { get; init; }
    public decimal Payments { get; init; }
    public decimal Aid { get; init; }
    public decimal Adjustments { get; init; }
    public decimal Balance { get; init; }
    public decimal NotYetDue { get; init; }
    public decimal Due1To30 { get; init; }
    public decimal Due31To60 { get; init; }
    public decimal Due61To90 { get; init; }
    public decimal Due90Plus { get; init; }
    public decimal PastDueAmount { get; init; }
    public int DaysPastDue { get; init; }
    public string AgingBucket { get; init; } = "";
    public string EnrollmentStatus { get; init; } = "";
    public string? MilitaryAffiliation { get; init; }
    public string? VaChapter { get; init; }
    public string? VaStatus { get; init; }
    public DateTime? VaCertifiedDate { get; init; }
    public decimal? VaExpectedAmount { get; init; }
    public string? PaymentPlanStatus { get; init; }
}

public sealed record LookupItem(string Value, string Text);

public sealed class DashboardKpis
{
    public decimal TotalReceivable { get; init; }
    public decimal PastDueAmount { get; init; }
    public decimal PastDue90Plus { get; init; }
    public int StudentsPastDue { get; init; }
    public decimal PastDueWithVaPending { get; init; }
    public decimal CollectedLast30Days { get; init; }
}

public sealed class PaymentTrendPoint
{
    public DateTime MonthStart { get; init; }
    public string FundSource { get; init; } = "";
    public decimal Amount { get; init; }
}

public sealed class TermCollection
{
    public string TermCode { get; init; } = "";
    public string TermDescription { get; init; } = "";
    public decimal NetCharges { get; init; }
    public decimal Collected { get; init; }
    public decimal CollectionRate { get; init; }
}

public sealed class InstitutionAging
{
    public string InstitutionCode { get; init; } = "";
    public string AgingBucket { get; init; } = "";
    public decimal Amount { get; init; }
}

public sealed class ReconBatchSummary
{
    public int BatchId { get; init; }
    public string VendorName { get; init; } = "";
    public string FileName { get; init; } = "";
    public string UploadedBy { get; init; } = "";
    public DateTime UploadedAt { get; init; }
    public int TotalRows { get; init; }
    public decimal TotalAmount { get; init; }
    public int MatchedRows { get; init; }
    public int ExceptionRows { get; init; }
    public int OpenExceptions { get; init; }
}

public sealed class ReconExceptionRow
{
    public int ExceptionId { get; init; }
    public int BatchId { get; init; }
    public int? LineNumber { get; init; }
    public string? VendorTxnId { get; init; }
    public string? StudentId { get; init; }
    public DateTime? PaymentDate { get; init; }
    public decimal? FileAmount { get; init; }
    public decimal? LedgerAmount { get; init; }
    public string ExceptionType { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Status { get; init; } = "";
    public string? ResolvedBy { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string? ResolutionNote { get; init; }
}

public sealed class AuditEntry
{
    public long AuditId { get; init; }
    public DateTime EventTime { get; init; }
    public string Actor { get; init; } = "";
    public string Action { get; init; } = "";
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? Details { get; init; }
}
