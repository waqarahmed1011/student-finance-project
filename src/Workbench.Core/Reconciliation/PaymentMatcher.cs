namespace Workbench.Core.Reconciliation;

public enum ReconExceptionType
{
    InvalidRow,
    DuplicateInFile,
    UnknownStudent,
    MissingInLedger,
    AmountMismatch,
    StudentMismatch,
    NotInVendorFile
}

public sealed record LedgerPayment(int TransactionId, string StudentId, decimal Amount, DateOnly EffectiveDate, string? ExternalRef);

public sealed record ReconFinding(
    int? LineNumber,
    string? VendorTxnId,
    string? StudentId,
    DateOnly? PaymentDate,
    decimal? FileAmount,
    decimal? LedgerAmount,
    ReconExceptionType Type,
    string Detail);

public sealed record ReconResult(
    int TotalRows,
    decimal TotalFileAmount,
    int MatchedRows,
    IReadOnlyList<ReconFinding> Exceptions)
{
    public DateOnly? FileStartDate { get; init; }
    public DateOnly? FileEndDate { get; init; }
}

/// <summary>
/// Compares a vendor settlement file to what was actually posted to student accounts.
/// Nothing gets posted or changed here. It only reports differences for a person to review.
/// </summary>
public static class PaymentMatcher
{
    /// <summary>How far apart the vendor date and ledger date can be for a match on student + amount when there's no reference.</summary>
    public const int DateToleranceDays = 2;

    public static ReconResult Match(
        IReadOnlyList<VendorPaymentRow> fileRows,
        IReadOnlyCollection<LedgerPayment> ledger,
        IReadOnlySet<string> knownStudentIds)
    {
        var findings = new List<ReconFinding>();
        var matched = 0;
        var seenTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedLedgerIds = new HashSet<int>();
        var ledgerByRef = ledger
            .Where(l => l.ExternalRef is not null)
            .GroupBy(l => l.ExternalRef!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in fileRows)
        {
            if (row.ParseError is not null)
            {
                findings.Add(Finding(row, null, ReconExceptionType.InvalidRow, row.ParseError));
                continue;
            }

            if (!seenTxnIds.Add(row.VendorTxnId!))
            {
                findings.Add(Finding(row, null, ReconExceptionType.DuplicateInFile,
                    $"Vendor transaction {row.VendorTxnId} appears more than once in the file."));
                continue;
            }

            if (!knownStudentIds.Contains(row.StudentId!))
            {
                findings.Add(Finding(row, null, ReconExceptionType.UnknownStudent,
                    $"Student ID {row.StudentId} doesn't exist in the student system. Possible typo at the vendor."));
                continue;
            }

            if (ledgerByRef.TryGetValue(row.VendorTxnId!, out var posted))
            {
                usedLedgerIds.Add(posted.TransactionId);

                if (!string.Equals(posted.StudentId, row.StudentId, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(Finding(row, posted.Amount, ReconExceptionType.StudentMismatch,
                        $"Posted to {posted.StudentId} but the vendor says it belongs to {row.StudentId}."));
                }
                else if (posted.Amount != row.Amount)
                {
                    findings.Add(Finding(row, posted.Amount, ReconExceptionType.AmountMismatch,
                        $"Vendor settled {row.Amount:0.00}, ledger shows {posted.Amount:0.00} (difference {row.Amount - posted.Amount:0.00})."));
                }
                else
                {
                    matched++;
                }
                continue;
            }

            // no reference match: look for an unreferenced posting with the same student, amount and a close date
            var candidate = ledger.FirstOrDefault(l =>
                l.ExternalRef is null
                && !usedLedgerIds.Contains(l.TransactionId)
                && string.Equals(l.StudentId, row.StudentId, StringComparison.OrdinalIgnoreCase)
                && l.Amount == row.Amount
                && Math.Abs(l.EffectiveDate.DayNumber - row.PaymentDate!.Value.DayNumber) <= DateToleranceDays);

            if (candidate is not null)
            {
                usedLedgerIds.Add(candidate.TransactionId);
                matched++;
                continue;
            }

            findings.Add(Finding(row, null, ReconExceptionType.MissingInLedger,
                "Vendor collected this payment but it was never posted to the student's account."));
        }

        var validDates = fileRows.Where(r => r.ParseError is null).Select(r => r.PaymentDate!.Value).ToList();
        DateOnly? start = validDates.Count > 0 ? validDates.Min() : null;
        DateOnly? end = validDates.Count > 0 ? validDates.Max() : null;

        if (start is not null)
        {
            var notInFile = ledger.Where(l =>
                    l.ExternalRef is not null
                    && !usedLedgerIds.Contains(l.TransactionId)
                    && l.EffectiveDate >= start && l.EffectiveDate <= end)
                .OrderBy(l => l.EffectiveDate);

            foreach (var l in notInFile)
            {
                findings.Add(new ReconFinding(null, l.ExternalRef, l.StudentId, l.EffectiveDate, null, l.Amount,
                    ReconExceptionType.NotInVendorFile,
                    "Posted to the student's account as a vendor payment, but the vendor didn't settle it. Possible reversal or chargeback."));
            }
        }

        var totalAmount = fileRows.Where(r => r.Amount is not null).Sum(r => r.Amount!.Value);
        return new ReconResult(fileRows.Count, totalAmount, matched, findings)
        {
            FileStartDate = start,
            FileEndDate = end
        };
    }

    private static ReconFinding Finding(VendorPaymentRow row, decimal? ledgerAmount, ReconExceptionType type, string detail) =>
        new(row.LineNumber, row.VendorTxnId, row.StudentId, row.PaymentDate, row.Amount, ledgerAmount, type, detail);
}
