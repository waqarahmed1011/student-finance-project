using Workbench.Core.Reconciliation;
using Workbench.Web.Data;

namespace Workbench.Web.Services;

/// <summary>Parse the vendor file, pull the matching ledger rows, compare, save the results.</summary>
public sealed class ReconciliationService
{
    private readonly ReconRepository _repo;

    public ReconciliationService(ReconRepository repo)
    {
        _repo = repo;
    }

    public async Task<int> ReconcileAsync(Stream file, string fileName, string uploadedBy)
    {
        var rows = VendorFileParser.Parse(file);
        var valid = rows.Where(r => r.ParseError is null).ToList();
        if (valid.Count == 0)
            throw new InvalidDataException("No valid rows in the file.");

        var from = valid.Min(r => r.PaymentDate!.Value);
        var to = valid.Max(r => r.PaymentDate!.Value);

        var ledger = await _repo.GetLedgerVendorPaymentsAsync(from, to, valid.Select(r => r.VendorTxnId!));
        var knownStudents = await _repo.GetKnownStudentIdsAsync(valid.Select(r => r.StudentId!));

        var result = PaymentMatcher.Match(rows, ledger, knownStudents);
        return await _repo.SaveBatchAsync(fileName, uploadedBy, result);
    }

    public static string NextStep(string exceptionType) => exceptionType switch
    {
        nameof(ReconExceptionType.MissingInLedger) => "Post the payment to the student account with the vendor transaction ID as the reference.",
        nameof(ReconExceptionType.AmountMismatch) => "Check the vendor remittance and correct the posted amount.",
        nameof(ReconExceptionType.StudentMismatch) => "Reverse it from the wrong student and repost to the right one. Check both balances.",
        nameof(ReconExceptionType.DuplicateInFile) => "Don't post twice. Ask the vendor to confirm it's a file duplicate.",
        nameof(ReconExceptionType.UnknownStudent) => "Ask the vendor to correct the student ID before posting.",
        nameof(ReconExceptionType.InvalidRow) => "Ask the vendor for a corrected row.",
        nameof(ReconExceptionType.NotInVendorFile) => "Confirm with the vendor whether it was reversed. If so, reverse the posting.",
        _ => ""
    };

    public static string Label(string exceptionType) => exceptionType switch
    {
        nameof(ReconExceptionType.MissingInLedger) => "Missing in ledger",
        nameof(ReconExceptionType.AmountMismatch) => "Amount mismatch",
        nameof(ReconExceptionType.StudentMismatch) => "Wrong student",
        nameof(ReconExceptionType.DuplicateInFile) => "Duplicate in file",
        nameof(ReconExceptionType.UnknownStudent) => "Unknown student ID",
        nameof(ReconExceptionType.InvalidRow) => "Invalid row",
        nameof(ReconExceptionType.NotInVendorFile) => "Not in vendor file",
        _ => exceptionType
    };
}
