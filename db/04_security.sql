/*
    Two database principals:
      workbench_app    - what the web app uses for fixed reports, reconciliation and audit writes
      report_reader    - what the AI "Ask for a report" feature runs under. SELECT on rpt only.

    The read-only login is the real safety net for AI-generated SQL. The parser check in
    the app is a second layer, not the only one.

    Run with sqlcmd variables, e.g.
      sqlcmd ... -v AppPassword="..." ReaderPassword="..." -i 04_security.sql
*/

IF CAST(SERVERPROPERTY('EngineEdition') AS INT) = 5
BEGIN
    -- Azure SQL Database: contained database users
    IF DATABASE_PRINCIPAL_ID('workbench_app') IS NULL
        EXEC('CREATE USER workbench_app WITH PASSWORD = ''$(AppPassword)''');
    IF DATABASE_PRINCIPAL_ID('report_reader') IS NULL
        EXEC('CREATE USER report_reader WITH PASSWORD = ''$(ReaderPassword)''');
END
ELSE
BEGIN
    -- Regular SQL Server (local Docker): server logins mapped to database users
    IF SUSER_ID('workbench_app') IS NULL
        EXEC('CREATE LOGIN workbench_app WITH PASSWORD = ''$(AppPassword)'', CHECK_POLICY = OFF');
    IF SUSER_ID('report_reader') IS NULL
        EXEC('CREATE LOGIN report_reader WITH PASSWORD = ''$(ReaderPassword)'', CHECK_POLICY = OFF');
    IF DATABASE_PRINCIPAL_ID('workbench_app') IS NULL
        EXEC('CREATE USER workbench_app FOR LOGIN workbench_app');
    IF DATABASE_PRINCIPAL_ID('report_reader') IS NULL
        EXEC('CREATE USER report_reader FOR LOGIN report_reader');
END
GO

ALTER ROLE db_datareader ADD MEMBER workbench_app;
GRANT INSERT, UPDATE ON dbo.ReconBatch TO workbench_app;
GRANT INSERT, UPDATE ON dbo.ReconException TO workbench_app;
GRANT INSERT ON dbo.AuditLog TO workbench_app;
GRANT EXECUTE ON SCHEMA::dbo TO workbench_app;

-- rpt views are owned by dbo, so ownership chaining lets them read dbo tables
-- without report_reader having any rights on dbo itself.
GRANT SELECT ON SCHEMA::rpt TO report_reader;
GO
