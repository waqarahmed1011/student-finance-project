using System.Text;
using Workbench.Core.Reconciliation;

namespace Workbench.Tests;

public class VendorFileParserTests
{
    private static IReadOnlyList<VendorPaymentRow> ParseText(string csv) =>
        VendorFileParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

    private const string Header = "vendor_txn_id,student_id,payment_date,amount,payment_method,plan_id\n";

    [Fact]
    public void Parses_a_good_row()
    {
        var row = Assert.Single(ParseText(Header + "CP-1,s20000001,2026-10-05,1250.50,ACH,CPP-1\n"));

        Assert.Null(row.ParseError);
        Assert.Equal("S20000001", row.StudentId);
        Assert.Equal(new DateOnly(2026, 10, 5), row.PaymentDate);
        Assert.Equal(1250.50m, row.Amount);
        Assert.Equal(2, row.LineNumber);
    }

    [Fact]
    public void Accepts_us_date_format()
    {
        var row = Assert.Single(ParseText(Header + "CP-1,S20000001,10/5/2026,10.00,ACH,CPP-1\n"));
        Assert.Equal(new DateOnly(2026, 10, 5), row.PaymentDate);
    }

    [Theory]
    [InlineData("CP-1,S20000001,2026-10-05,,ACH,CPP-1", "amount is blank")]
    [InlineData("CP-1,S20000001,2026-10-05,abc,ACH,CPP-1", "not a valid positive number")]
    [InlineData("CP-1,S20000001,2026-10-05,-5,ACH,CPP-1", "not a valid positive number")]
    [InlineData("CP-1,S20000001,13/45/2026,5,ACH,CPP-1", "not a valid date")]
    [InlineData(",S20000001,2026-10-05,5,ACH,CPP-1", "vendor_txn_id is blank")]
    public void Bad_rows_come_back_with_an_error_instead_of_throwing(string line, string expectedError)
    {
        var row = Assert.Single(ParseText(Header + line + "\n"));
        Assert.Contains(expectedError, row.ParseError);
    }

    [Fact]
    public void Missing_column_is_rejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ParseText("vendor_txn_id,student_id,payment_date,amount\nCP-1,S1,2026-10-05,5\n"));
        Assert.Contains("payment_method", ex.Message);
    }

    [Fact]
    public void Header_matching_ignores_case_and_spaces()
    {
        var rows = ParseText(" Vendor_Txn_Id , STUDENT_ID,Payment_Date,Amount,Payment_Method,Plan_Id\nCP-1,S20000001,2026-10-05,5,ACH,CPP-1\n");
        Assert.Null(Assert.Single(rows).ParseError);
    }

    [Fact]
    public void Sample_vendor_file_in_repo_parses_with_one_bad_row()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "clearpath_settlement_2026-10-01_2026-10-09.csv");
        using var stream = File.OpenRead(path);

        var rows = VendorFileParser.Parse(stream);

        Assert.True(rows.Count > 100);
        Assert.Single(rows, r => r.ParseError is not null);
    }
}
