using Microsoft.Data.SqlClient;

namespace Workbench.Web.Data;

/// <summary>
/// Two connection strings on purpose:
///   Workbench    - app login, runs the fixed reports and writes reconciliation results and audit rows
///   ReportReader - read-only login with SELECT on the rpt schema only, used for ad hoc / AI-generated SQL
/// </summary>
public sealed class SqlConnectionFactory
{
    private readonly string _workbench;
    private readonly string _reportReader;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _workbench = configuration.GetConnectionString("Workbench")
            ?? throw new InvalidOperationException("ConnectionStrings:Workbench is not configured.");
        _reportReader = configuration.GetConnectionString("ReportReader")
            ?? throw new InvalidOperationException("ConnectionStrings:ReportReader is not configured.");
    }

    public SqlConnection CreateWorkbench() => new(_workbench);

    public SqlConnection CreateReportReader() => new(_reportReader);
}
