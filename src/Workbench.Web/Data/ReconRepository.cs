using System.Data;
using System.Text.Json;
using Dapper;
using Workbench.Core.Reconciliation;

namespace Workbench.Web.Data;

public sealed class ReconRepository
{
    public const string VendorName = "ClearPath Tuition Plans";

    private readonly SqlConnectionFactory _db;

    public ReconRepository(SqlConnectionFactory db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LedgerPayment>> GetLedgerVendorPaymentsAsync(DateOnly from, DateOnly to, IEnumerable<string> vendorTxnIds)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<(int TransactionId, string StudentId, decimal Amount, DateTime EffectiveDate, string? ExternalRef)>(
            "dbo.usp_GetLedgerVendorPayments",
            new
            {
                FromDate = from.ToDateTime(TimeOnly.MinValue),
                ToDate = to.ToDateTime(TimeOnly.MinValue),
                VendorTxnIds = JsonSerializer.Serialize(vendorTxnIds)
            },
            commandType: CommandType.StoredProcedure);

        return rows.Select(r => new LedgerPayment(r.TransactionId, r.StudentId, r.Amount, DateOnly.FromDateTime(r.EffectiveDate), r.ExternalRef))
            .ToList();
    }

    public async Task<IReadOnlySet<string>> GetKnownStudentIdsAsync(IEnumerable<string> studentIds)
    {
        await using var conn = _db.CreateWorkbench();
        var found = await conn.QueryAsync<string>(
            "SELECT s.StudentId FROM dbo.Student s WHERE s.StudentId IN (SELECT [value] FROM OPENJSON(@ids))",
            new { ids = JsonSerializer.Serialize(studentIds.Distinct()) });
        return found.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Saves the batch, its exceptions and the audit row in one transaction.</summary>
    public async Task<int> SaveBatchAsync(string fileName, string uploadedBy, ReconResult result)
    {
        await using var conn = _db.CreateWorkbench();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var batchId = await conn.ExecuteScalarAsync<int>("""
            INSERT INTO dbo.ReconBatch (VendorName, FileName, UploadedBy, TotalRows, TotalAmount, MatchedRows, ExceptionRows)
            OUTPUT INSERTED.BatchId
            VALUES (@VendorName, @FileName, @UploadedBy, @TotalRows, @TotalAmount, @MatchedRows, @ExceptionRows)
            """,
            new
            {
                VendorName,
                FileName = fileName,
                UploadedBy = uploadedBy,
                result.TotalRows,
                TotalAmount = result.TotalFileAmount,
                result.MatchedRows,
                ExceptionRows = result.Exceptions.Count
            }, tx);

        if (result.Exceptions.Count > 0)
        {
            await conn.ExecuteAsync("""
                INSERT INTO dbo.ReconException
                    (BatchId, LineNumber, VendorTxnId, StudentId, PaymentDate, FileAmount, LedgerAmount, ExceptionType, Detail)
                VALUES
                    (@BatchId, @LineNumber, @VendorTxnId, @StudentId, @PaymentDate, @FileAmount, @LedgerAmount, @ExceptionType, @Detail)
                """,
                result.Exceptions.Select(e => new
                {
                    BatchId = batchId,
                    e.LineNumber,
                    VendorTxnId = Truncate(e.VendorTxnId, 40),
                    StudentId = Truncate(e.StudentId, 20),
                    PaymentDate = e.PaymentDate?.ToDateTime(TimeOnly.MinValue),
                    e.FileAmount,
                    e.LedgerAmount,
                    ExceptionType = e.Type.ToString(),
                    Detail = Truncate(e.Detail, 400)
                }), tx);
        }

        await AuditRepository.WriteAsync(conn, tx, uploadedBy, "RECON_FILE_UPLOADED", "ReconBatch", batchId.ToString(),
            $"{fileName}: {result.TotalRows} rows, {result.MatchedRows} matched, {result.Exceptions.Count} exceptions");

        await tx.CommitAsync();
        return batchId;
    }

    public async Task<IReadOnlyList<ReconBatchSummary>> GetBatchesAsync(int count)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<ReconBatchSummary>("""
            SELECT TOP (@count) b.BatchId, b.VendorName, b.FileName, b.UploadedBy, b.UploadedAt, b.TotalRows, b.TotalAmount,
                   b.MatchedRows, b.ExceptionRows,
                   (SELECT COUNT(*) FROM dbo.ReconException e WHERE e.BatchId = b.BatchId AND e.Status = 'OPEN') AS OpenExceptions
            FROM dbo.ReconBatch b
            ORDER BY b.BatchId DESC
            """, new { count });
        return rows.AsList();
    }

    public async Task<ReconBatchSummary?> GetBatchAsync(int batchId)
    {
        await using var conn = _db.CreateWorkbench();
        return await conn.QuerySingleOrDefaultAsync<ReconBatchSummary>("""
            SELECT b.BatchId, b.VendorName, b.FileName, b.UploadedBy, b.UploadedAt, b.TotalRows, b.TotalAmount,
                   b.MatchedRows, b.ExceptionRows,
                   (SELECT COUNT(*) FROM dbo.ReconException e WHERE e.BatchId = b.BatchId AND e.Status = 'OPEN') AS OpenExceptions
            FROM dbo.ReconBatch b
            WHERE b.BatchId = @batchId
            """, new { batchId });
    }

    public async Task<IReadOnlyList<ReconExceptionRow>> GetExceptionsAsync(int? batchId, string? status)
    {
        await using var conn = _db.CreateWorkbench();
        var rows = await conn.QueryAsync<ReconExceptionRow>("""
            SELECT ExceptionId, BatchId, LineNumber, VendorTxnId, StudentId, PaymentDate, FileAmount, LedgerAmount,
                   ExceptionType, Detail, Status, ResolvedBy, ResolvedAt, ResolutionNote
            FROM dbo.ReconException
            WHERE (@batchId IS NULL OR BatchId = @batchId)
              AND (@status IS NULL OR Status = @status)
            ORDER BY BatchId DESC, ISNULL(LineNumber, 2147483647), ExceptionId
            """, new { batchId, status });
        return rows.AsList();
    }

    /// <returns>false if the exception doesn't exist or was already resolved</returns>
    public async Task<bool> ResolveExceptionAsync(int exceptionId, string resolvedBy, string note)
    {
        await using var conn = _db.CreateWorkbench();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var updated = await conn.ExecuteAsync("""
            UPDATE dbo.ReconException
            SET Status = 'RESOLVED', ResolvedBy = @resolvedBy, ResolvedAt = SYSUTCDATETIME(), ResolutionNote = @note
            WHERE ExceptionId = @exceptionId AND Status = 'OPEN'
            """, new { exceptionId, resolvedBy, note }, tx);

        if (updated == 0)
        {
            await tx.RollbackAsync();
            return false;
        }

        await AuditRepository.WriteAsync(conn, tx, resolvedBy, "RECON_EXCEPTION_RESOLVED", "ReconException",
            exceptionId.ToString(), note);
        await tx.CommitAsync();
        return true;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
