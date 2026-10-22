using Microsoft.Data.SqlClient;
using Workbench.Core.Reporting;
using Workbench.Web.Data;

namespace Workbench.Web.Services;

public sealed record QueryResult(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows, bool Truncated);

/// <summary>Runs ad hoc report SQL under the read-only reporting login, after the parser check.</summary>
public sealed class ReportQueryRunner
{
    public const int MaxRows = 500;
    private const int TimeoutSeconds = 15;

    private readonly SqlConnectionFactory _db;

    public ReportQueryRunner(SqlConnectionFactory db)
    {
        _db = db;
    }

    public async Task<QueryResult> RunAsync(string sql, CancellationToken ct)
    {
        var check = ReportSqlGuard.Validate(sql);
        if (!check.IsAllowed)
            throw new InvalidOperationException(check.Reason);

        await using var conn = _db.CreateReportReader();
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = TimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => string.IsNullOrEmpty(reader.GetName(i)) ? $"Column{i + 1}" : reader.GetName(i))
            .ToList();

        var rows = new List<object?[]>();
        var truncated = false;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count == MaxRows)
            {
                truncated = true;
                break;
            }
            var values = new object?[reader.FieldCount];
            reader.GetValues(values!);
            for (var i = 0; i < values.Length; i++)
                if (values[i] is DBNull) values[i] = null;
            rows.Add(values);
        }

        return new QueryResult(columns, rows, truncated);
    }
}
