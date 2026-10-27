/*
    Drops every object this project creates so the other scripts can run again on an existing
    database (used for Azure, where the database itself isn't dropped). Logins/users are kept.
*/
DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';'
FROM sys.procedures WHERE SCHEMA_NAME(schema_id) IN ('dbo', 'rpt');

SELECT @sql += N'DROP VIEW ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';'
FROM sys.views WHERE SCHEMA_NAME(schema_id) IN ('dbo', 'rpt');

SELECT @sql += N'DROP FUNCTION ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';'
FROM sys.objects WHERE type IN ('IF', 'TF', 'FN') AND SCHEMA_NAME(schema_id) IN ('dbo', 'rpt');

EXEC sp_executesql @sql;
GO

DROP TABLE IF EXISTS dbo.AuditLog, dbo.ReconException, dbo.ReconBatch, dbo.PaymentPlanInstallment, dbo.PaymentPlan,
    dbo.VaCertification, dbo.AccountTransaction, dbo.DetailCode, dbo.Student, dbo.Program, dbo.Term, dbo.Institution;
GO
