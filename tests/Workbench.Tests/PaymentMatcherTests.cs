using Workbench.Core.Reconciliation;

namespace Workbench.Tests;

public class PaymentMatcherTests
{
    private static readonly HashSet<string> Students = ["S20000001", "S20000002", "S20000003"];

    private static VendorPaymentRow Row(int line, string txn, string student, string date, decimal amount) =>
        new(line, txn, student, DateOnly.Parse(date), amount, "ACH", "CPP-1", null);

    private static LedgerPayment Posted(int id, string student, decimal amount, string date, string? txn) =>
        new(id, student, amount, DateOnly.Parse(date), txn);

    [Fact]
    public void Exact_reference_match_counts_as_matched()
    {
        var file = new[] { Row(2, "CP-1", "S20000001", "2026-10-05", 250m) };
        var ledger = new[] { Posted(1, "S20000001", 250m, "2026-10-05", "CP-1") };

        var result = PaymentMatcher.Match(file, ledger, Students);

        Assert.Equal(1, result.MatchedRows);
        Assert.Empty(result.Exceptions);
    }

    [Fact]
    public void Payment_not_posted_is_missing_in_ledger()
    {
        var file = new[] { Row(2, "CP-1", "S20000001", "2026-10-05", 250m) };

        var result = PaymentMatcher.Match(file, [], Students);

        Assert.Equal(ReconExceptionType.MissingInLedger, Assert.Single(result.Exceptions).Type);
    }

    [Fact]
    public void Different_amount_is_amount_mismatch_with_both_amounts()
    {
        var file = new[] { Row(2, "CP-1", "S20000001", "2026-10-05", 412.50m) };
        var ledger = new[] { Posted(1, "S20000001", 41.25m, "2026-10-05", "CP-1") };

        var finding = Assert.Single(PaymentMatcher.Match(file, ledger, Students).Exceptions);

        Assert.Equal(ReconExceptionType.AmountMismatch, finding.Type);
        Assert.Equal(412.50m, finding.FileAmount);
        Assert.Equal(41.25m, finding.LedgerAmount);
    }

    [Fact]
    public void Posted_to_wrong_student_is_student_mismatch()
    {
        var file = new[] { Row(2, "CP-1", "S20000001", "2026-10-05", 250m) };
        var ledger = new[] { Posted(1, "S20000002", 250m, "2026-10-05", "CP-1") };

        var finding = Assert.Single(PaymentMatcher.Match(file, ledger, Students).Exceptions);

        Assert.Equal(ReconExceptionType.StudentMismatch, finding.Type);
        Assert.Contains("S20000002", finding.Detail);
    }

    [Fact]
    public void Second_copy_of_a_row_is_flagged_and_first_still_matches()
    {
        var file = new[]
        {
            Row(2, "CP-1", "S20000001", "2026-10-05", 250m),
            Row(3, "CP-1", "S20000001", "2026-10-05", 250m)
        };
        var ledger = new[] { Posted(1, "S20000001", 250m, "2026-10-05", "CP-1") };

        var result = PaymentMatcher.Match(file, ledger, Students);

        Assert.Equal(1, result.MatchedRows);
        var finding = Assert.Single(result.Exceptions);
        Assert.Equal(ReconExceptionType.DuplicateInFile, finding.Type);
        Assert.Equal(3, finding.LineNumber);
    }

    [Fact]
    public void Unknown_student_id_is_flagged()
    {
        var file = new[] { Row(2, "CP-1", "S2O114857", "2026-10-05", 250m) };

        var finding = Assert.Single(PaymentMatcher.Match(file, [], Students).Exceptions);

        Assert.Equal(ReconExceptionType.UnknownStudent, finding.Type);
    }

    [Fact]
    public void Unparseable_row_becomes_invalid_row()
    {
        var file = new[] { new VendorPaymentRow(4, "CP-9", "S20000001", new DateOnly(2026, 10, 5), null, "ACH", "CPP-1", "amount is blank") };

        var finding = Assert.Single(PaymentMatcher.Match(file, [], Students).Exceptions);

        Assert.Equal(ReconExceptionType.InvalidRow, finding.Type);
    }

    [Fact]
    public void Ledger_vendor_payment_in_file_window_but_not_in_file_is_flagged()
    {
        var file = new[]
        {
            Row(2, "CP-1", "S20000001", "2026-10-01", 250m),
            Row(3, "CP-3", "S20000003", "2026-10-09", 100m)
        };
        var ledger = new[]
        {
            Posted(1, "S20000001", 250m, "2026-10-01", "CP-1"),
            Posted(2, "S20000002", 300m, "2026-10-04", "CP-2"),
            Posted(3, "S20000003", 100m, "2026-10-09", "CP-3"),
            Posted(4, "S20000002", 300m, "2026-10-15", "CP-4") // outside the file's dates, not this file's business
        };

        var result = PaymentMatcher.Match(file, ledger, Students);

        var finding = Assert.Single(result.Exceptions);
        Assert.Equal(ReconExceptionType.NotInVendorFile, finding.Type);
        Assert.Equal("CP-2", finding.VendorTxnId);
    }

    [Fact]
    public void Unreferenced_posting_matches_on_student_amount_and_close_date()
    {
        var file = new[] { Row(2, "CP-1", "S20000001", "2026-10-05", 250m) };
        var ledger = new[] { Posted(1, "S20000001", 250m, "2026-10-07", null) };

        var result = PaymentMatcher.Match(file, ledger, Students);

        Assert.Equal(1, result.MatchedRows);
        Assert.Empty(result.Exceptions);
    }

    [Fact]
    public void Unreferenced_posting_too_far_apart_does_not_match()
    {
        var file = new[] { Row(2, "CP-1", "S20000001", "2026-10-05", 250m) };
        var ledger = new[] { Posted(1, "S20000001", 250m, "2026-10-09", null) };

        var result = PaymentMatcher.Match(file, ledger, Students);

        Assert.Equal(0, result.MatchedRows);
        Assert.Equal(ReconExceptionType.MissingInLedger, Assert.Single(result.Exceptions).Type);
    }
}
