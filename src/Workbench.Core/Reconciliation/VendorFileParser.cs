using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Workbench.Core.Reconciliation;

public sealed record VendorPaymentRow(
    int LineNumber,
    string? VendorTxnId,
    string? StudentId,
    DateOnly? PaymentDate,
    decimal? Amount,
    string? PaymentMethod,
    string? PlanId,
    string? ParseError);

/// <summary>
/// Reads a payment plan vendor settlement file. Bad rows don't stop the import; they come back with
/// ParseError set so they show up as exceptions instead of disappearing.
/// </summary>
public static class VendorFileParser
{
    public static readonly string[] RequiredColumns =
        ["vendor_txn_id", "student_id", "payment_date", "amount", "payment_method", "plan_id"];

    public const int MaxRows = 5000;

    public static IReadOnlyList<VendorPaymentRow> Parse(Stream stream)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader())
            throw new InvalidDataException("The file is empty.");

        var header = csv.HeaderRecord!.Select(h => h.Trim().ToLowerInvariant()).ToHashSet();
        var missing = RequiredColumns.Where(c => !header.Contains(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"Missing column(s): {string.Join(", ", missing)}.");

        var rows = new List<VendorPaymentRow>();
        while (csv.Read())
        {
            if (rows.Count >= MaxRows)
                throw new InvalidDataException($"File has more than {MaxRows} rows. Split it and upload again.");

            var line = csv.Parser.Row;
            var txnId = Clean(csv.GetField("vendor_txn_id"));
            var studentId = Clean(csv.GetField("student_id"));
            var dateText = Clean(csv.GetField("payment_date"));
            var amountText = Clean(csv.GetField("amount"));

            var errors = new List<string>();
            if (txnId is null) errors.Add("vendor_txn_id is blank");
            if (studentId is null) errors.Add("student_id is blank");

            DateOnly? paymentDate = null;
            if (DateOnly.TryParseExact(dateText, ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                paymentDate = d;
            else
                errors.Add($"payment_date '{dateText}' is not a valid date");

            decimal? amount = null;
            if (decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) && a > 0)
                amount = a;
            else
                errors.Add(amountText is null ? "amount is blank" : $"amount '{amountText}' is not a valid positive number");

            rows.Add(new VendorPaymentRow(line, txnId, studentId?.ToUpperInvariant(), paymentDate, amount,
                Clean(csv.GetField("payment_method")), Clean(csv.GetField("plan_id")),
                errors.Count > 0 ? string.Join("; ", errors) : null));
        }

        return rows;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
