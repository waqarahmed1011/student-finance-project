/* Past-due report (feeds the Past-Due page and its CSV export) */
CREATE OR ALTER PROCEDURE dbo.usp_GetPastDueAging
    @AsOfDate       DATE,
    @InstitutionId  INT     = NULL,
    @TermCode       CHAR(6) = NULL,
    @AgingBucket    VARCHAR(10) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        b.StudentId, b.FirstName, b.LastName, b.Email,
        b.InstitutionCode, b.InstitutionName, b.ProgramName,
        b.TermCode, b.TermDescription, b.PaymentDueDate,
        b.Charges, b.Payments, b.Aid, b.Adjustments, b.Balance,
        b.NotYetDue, b.Due1To30, b.Due31To60, b.Due61To90, b.Due90Plus, b.PastDueAmount,
        b.DaysPastDue, b.AgingBucket,
        b.EnrollmentStatus, b.MilitaryAffiliation,
        b.VaChapter, b.VaStatus, b.VaCertifiedDate, b.VaExpectedAmount,
        b.PaymentPlanStatus
    FROM dbo.fn_StudentTermBalance(@AsOfDate) b
    WHERE b.PastDueAmount > 0
      AND (@InstitutionId IS NULL OR b.InstitutionId = @InstitutionId)
      AND (@TermCode IS NULL OR b.TermCode = @TermCode)
      AND (@AgingBucket IS NULL OR b.AgingBucket = @AgingBucket)
    ORDER BY b.DaysPastDue DESC, b.Balance DESC;
END
GO

/* Dashboard: headline numbers */
CREATE OR ALTER PROCEDURE dbo.usp_DashboardKpis
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    WITH bal AS (
        SELECT * FROM dbo.fn_StudentTermBalance(@AsOfDate)
    )
    SELECT
        (SELECT ISNULL(SUM(Balance), 0) FROM bal WHERE Balance > 0)                                AS TotalReceivable,
        (SELECT ISNULL(SUM(PastDueAmount), 0) FROM bal)                                            AS PastDueAmount,
        (SELECT ISNULL(SUM(Due90Plus), 0) FROM bal)                                                AS PastDue90Plus,
        (SELECT COUNT(DISTINCT StudentId) FROM bal WHERE PastDueAmount > 0)                        AS StudentsPastDue,
        (SELECT ISNULL(SUM(PastDueAmount), 0) FROM bal WHERE VaStatus = 'PENDING')                 AS PastDueWithVaPending,
        (SELECT ISNULL(-SUM(at.Amount), 0)
           FROM dbo.AccountTransaction at
           JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
          WHERE dc.Category IN ('PAY', 'AID')
            AND at.EffectiveDate >  DATEADD(DAY, -30, @AsOfDate)
            AND at.EffectiveDate <= @AsOfDate)                                                     AS CollectedLast30Days;
END
GO

/* Dashboard: money received per month by fund source */
CREATE OR ALTER PROCEDURE dbo.usp_PaymentTrendByMonth
    @AsOfDate DATE,
    @Months   INT = 12
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FromDate DATE = DATEADD(MONTH, -(@Months - 1), DATEFROMPARTS(YEAR(@AsOfDate), MONTH(@AsOfDate), 1));

    SELECT
        DATEFROMPARTS(YEAR(at.EffectiveDate), MONTH(at.EffectiveDate), 1) AS MonthStart,
        dc.FundSource,
        -SUM(at.Amount) AS Amount
    FROM dbo.AccountTransaction at
    JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
    WHERE dc.Category IN ('PAY', 'AID')
      AND at.EffectiveDate >= @FromDate
      AND at.EffectiveDate <= @AsOfDate
    GROUP BY DATEFROMPARTS(YEAR(at.EffectiveDate), MONTH(at.EffectiveDate), 1), dc.FundSource
    ORDER BY MonthStart, dc.FundSource;
END
GO

/* Dashboard: how much of each term's charges has been collected */
CREATE OR ALTER PROCEDURE dbo.usp_CollectionRateByTerm
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        TermCode,
        TermDescription,
        SUM(Charges + Adjustments)       AS NetCharges,
        SUM(Payments + Aid)              AS Collected,
        CAST(SUM(Payments + Aid) / NULLIF(SUM(Charges + Adjustments), 0) AS DECIMAL(6,4)) AS CollectionRate
    FROM dbo.fn_StudentTermBalance(@AsOfDate)
    GROUP BY TermCode, TermDescription
    ORDER BY TermCode;
END
GO

/* Dashboard: past-due dollars by institution and aging bucket */
CREATE OR ALTER PROCEDURE dbo.usp_PastDueByInstitution
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT b.InstitutionCode, x.AgingBucket, SUM(x.Amount) AS Amount
    FROM dbo.fn_StudentTermBalance(@AsOfDate) b
    CROSS APPLY (VALUES ('1-30',  b.Due1To30),
                        ('31-60', b.Due31To60),
                        ('61-90', b.Due61To90),
                        ('90+',   b.Due90Plus)) x (AgingBucket, Amount)
    WHERE x.Amount > 0
    GROUP BY b.InstitutionCode, x.AgingBucket
    ORDER BY b.InstitutionCode, x.AgingBucket;
END
GO

/*
    Reconciliation: ledger payments that came in through the payment plan vendor feed,
    either inside the file's date window or matching one of the file's vendor txn ids.
    @VendorTxnIds is a JSON array of strings, e.g. ["CP-100231","CP-100232"].
*/
CREATE OR ALTER PROCEDURE dbo.usp_GetLedgerVendorPayments
    @FromDate       DATE,
    @ToDate         DATE,
    @VendorTxnIds   NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT at.TransactionId, at.StudentId, -at.Amount AS Amount, at.EffectiveDate, at.ExternalRef
    FROM dbo.AccountTransaction at
    WHERE at.DetailCode = 'PPLN'
      AND (   at.EffectiveDate BETWEEN @FromDate AND @ToDate
           OR at.ExternalRef IN (SELECT [value] FROM OPENJSON(@VendorTxnIds)));
END
GO
