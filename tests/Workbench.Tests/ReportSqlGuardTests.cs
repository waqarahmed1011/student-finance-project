using Workbench.Core.Reporting;

namespace Workbench.Tests;

public class ReportSqlGuardTests
{
    [Theory]
    [InlineData("SELECT TOP 10 StudentId, Balance FROM rpt.StudentBalances ORDER BY Balance DESC")]
    [InlineData("select InstitutionCode, sum(Amount) from RPT.Payments group by InstitutionCode")]
    [InlineData("""
        WITH pastdue AS (SELECT StudentId, PastDueAmount FROM rpt.StudentBalances WHERE PastDueAmount > 0)
        SELECT s.ProgramName, COUNT(*) AS Students, SUM(p.PastDueAmount) AS Amount
        FROM pastdue p JOIN rpt.Students s ON s.StudentId = p.StudentId
        GROUP BY s.ProgramName
        """)]
    [InlineData("SELECT * FROM rpt.StudentBalances WHERE StudentId IN (SELECT StudentId FROM rpt.VaCertifications WHERE Status = 'PENDING');")]
    public void Allows_selects_against_reporting_views(string sql)
    {
        var result = ReportSqlGuard.Validate(sql);
        Assert.True(result.IsAllowed, result.Reason);
    }

    [Theory]
    [InlineData("DELETE FROM rpt.Payments", "Only SELECT")]
    [InlineData("UPDATE dbo.AccountTransaction SET Amount = 0", "Only SELECT")]
    [InlineData("DROP TABLE dbo.Student", "Only SELECT")]
    [InlineData("EXEC xp_cmdshell 'dir'", "Only SELECT")]
    [InlineData("SELECT * FROM rpt.Payments; DELETE FROM dbo.Student", "single SELECT")]
    [InlineData("SELECT * FROM dbo.Student", "isn't a reporting view")]
    [InlineData("SELECT * FROM Student", "isn't a reporting view")]
    [InlineData("SELECT * FROM otherdb.rpt.Payments", "Cross-database")]
    [InlineData("SELECT * INTO rpt.Copy FROM rpt.Payments", "SELECT INTO")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;', 'SELECT 1') AS x", "OPENROWSET")]
    [InlineData("SELECT * FROM dbo.fn_StudentTermBalance('2026-10-01')", "Table-valued functions")]
    [InlineData("SELECT * FROM rpt.Payments p JOIN dbo.Student s ON s.StudentId = p.StudentId", "isn't a reporting view")]
    [InlineData("SELECT * FROM rpt.Payments WHERE StudentId IN (SELECT StudentId FROM dbo.Student)", "isn't a reporting view")]
    [InlineData("SELEC * FROM rpt.Payments", "doesn't parse")]
    [InlineData("", "No SQL")]
    public void Rejects_anything_else(string sql, string expectedReason)
    {
        var result = ReportSqlGuard.Validate(sql);
        Assert.False(result.IsAllowed);
        Assert.Contains(expectedReason, result.Reason);
    }

    [Fact]
    public void Comment_tricks_do_not_hide_a_second_statement()
    {
        var result = ReportSqlGuard.Validate("SELECT 1 FROM rpt.Payments /* harmless */ ; /**/ TRUNCATE TABLE dbo.AuditLog --");
        Assert.False(result.IsAllowed);
    }
}
