using System.Data;
using Dapper;

namespace Workbench.Web.Data;

public sealed class AuditRepository
{
    private const string InsertSql = """
        INSERT INTO dbo.AuditLog (Actor, Action, EntityType, EntityId, Details)
        VALUES (@Actor, @Action, @EntityType, @EntityId, @Details)
        """;

    private readonly SqlConnectionFactory _db;

    public AuditRepository(SqlConnectionFactory db)
    {
        _db = db;
    }

    public async Task WriteAsync(string actor, string action, string? entityType, string? entityId, string? details)
    {
        await using var conn = _db.CreateWorkbench();
        await WriteAsync(conn, null, actor, action, entityType, entityId, details);
    }

    /// <summary>Use this overload to write the audit row inside the same transaction as the change it describes.</summary>
    public static Task WriteAsync(IDbConnection conn, IDbTransaction? tx, string actor, string action,
        string? entityType, string? entityId, string? details) =>
        conn.ExecuteAsync(InsertSql,
            new { Actor = Truncate(actor, 60), Action = action, EntityType = entityType, EntityId = entityId, Details = details },
            tx);

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int count)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<AuditEntry>(
            "SELECT TOP (@count) AuditId, EventTime, Actor, Action, EntityType, EntityId, Details FROM dbo.AuditLog ORDER BY AuditId DESC",
            new { count });
        return rows.AsList();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
