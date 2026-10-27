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

    -- evaluate the balance function once instead of once per KPI
    SELECT StudentId, Balance, PastDueAmount, Due90Plus, VaStatus
    INTO #bal
    FROM dbo.fn_StudentTermBalance(@AsOfDate);

    SELECT
        ISNULL(SUM(CASE WHEN Balance > 0 THEN Balance END), 0)                  AS TotalReceivable,
        ISNULL(SUM(PastDueAmount), 0)                                           AS PastDueAmount,
        ISNULL(SUM(Due90Plus), 0)                                               AS PastDue90Plus,
        COUNT(DISTINCT CASE WHEN PastDueAmount > 0 THEN StudentId END)          AS StudentsPastDue,
        ISNULL(SUM(CASE WHEN VaStatus = 'PENDING' THEN PastDueAmount END), 0)   AS PastDueWithVaPending,
        (SELECT ISNULL(-SUM(at.Amount), 0)
           FROM dbo.AccountTransaction at
           JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
          WHERE dc.Category IN ('PAY', 'AID')
            AND at.DetailCode <> 'SCHL'   -- scholarships are a discount, not cash
            AND at.EffectiveDate >  DATEADD(DAY, -30, @AsOfDate)
            AND at.EffectiveDate <= @AsOfDate)                                  AS CollectedLast30Days
    FROM #bal;
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
      AND at.DetailCode <> 'SCHL'
      AND at.EffectiveDate >= @FromDate
      AND at.EffectiveDate <= @AsOfDate
    GROUP BY DATEFROMPARTS(YEAR(at.EffectiveDate), MONTH(at.EffectiveDate), 1), dc.FundSource
    ORDER BY MonthStart, dc.FundSource;
END
GO

/* Dashboard: how much of each term's charges has been collected (doesn't need aging, so it reads the ledger directly) */
CREATE OR ALTER PROCEDURE dbo.usp_CollectionRateByTerm
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        t.TermCode,
        t.Description AS TermDescription,
        SUM(CASE WHEN dc.Category = 'CHG' OR at.DetailCode = 'WDTA' THEN at.Amount ELSE 0 END)     AS NetCharges,
        -- collected = what came in less what went back out (credit balance refunds, Title IV returns)
        -SUM(CASE WHEN dc.Category IN ('PAY', 'AID') OR at.DetailCode IN ('RFND', 'RT4R') THEN at.Amount ELSE 0 END) AS Collected,
        CAST(-SUM(CASE WHEN dc.Category IN ('PAY', 'AID') OR at.DetailCode IN ('RFND', 'RT4R') THEN at.Amount ELSE 0 END)
             / NULLIF(SUM(CASE WHEN dc.Category = 'CHG' OR at.DetailCode = 'WDTA' THEN at.Amount ELSE 0 END), 0) AS DECIMAL(6,4)) AS CollectionRate
    FROM dbo.AccountTransaction at
    JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
    JOIN dbo.Term t        ON t.TermCode = at.TermCode
    WHERE at.EffectiveDate <= @AsOfDate
    GROUP BY t.TermCode, t.Description
    ORDER BY t.TermCode;
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

/*
    Fiscal-year financial summary (Covista's fiscal year runs July 1 to June 30).
    Returns one row per period: prior fiscal year, prior-year-to-date and current year-to-date.
    Bad debt allowance is calculated in the app from the aging buckets (Workbench.Core AllowancePolicy).
*/
CREATE OR ALTER PROCEDURE dbo.usp_FinancialSummary
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FyStart DATE = DATEFROMPARTS(YEAR(@AsOfDate) - CASE WHEN MONTH(@AsOfDate) < 7 THEN 1 ELSE 0 END, 7, 1);
    DECLARE @PriorFyStart DATE = DATEADD(YEAR, -1, @FyStart);
    DECLARE @PriorFyEnd DATE = DATEADD(DAY, -1, @FyStart);
    DECLARE @PriorYtdEnd DATE = DATEADD(YEAR, -1, @AsOfDate);

    WITH Periods AS (
        SELECT * FROM (VALUES
            ('PriorFY',  @PriorFyStart, @PriorFyEnd),
            ('PriorYTD', @PriorFyStart, @PriorYtdEnd),
            ('YTD',      @FyStart,      @AsOfDate)) v (Period, FromDate, ToDate)
    )
    SELECT
        p.Period, p.FromDate, p.ToDate,
        ISNULL(r.GrossRevenue, 0) AS GrossRevenue,
        ISNULL(r.Scholarships, 0) AS Scholarships,
        ISNULL(r.NetRevenue, 0)   AS NetRevenue,
        ISNULL(c.CashCollected, 0) AS CashCollected,
        ISNULL(c.Refunds, 0)       AS Refunds
    FROM Periods p
    OUTER APPLY (
        SELECT SUM(GrossRevenue) AS GrossRevenue, SUM(Scholarships) AS Scholarships, SUM(NetRevenue) AS NetRevenue
        FROM dbo.fn_RevenueRecognized(p.FromDate, p.ToDate, CASE WHEN p.ToDate < @AsOfDate THEN p.ToDate ELSE @AsOfDate END)
    ) r
    OUTER APPLY (
        SELECT -SUM(CASE WHEN dc.Category IN ('PAY', 'AID') AND at.DetailCode <> 'SCHL' THEN at.Amount ELSE 0 END) AS CashCollected,
               SUM(CASE WHEN at.DetailCode = 'RFND' THEN at.Amount ELSE 0 END) AS Refunds
        FROM dbo.AccountTransaction at
        JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
        WHERE at.EffectiveDate BETWEEN p.FromDate AND p.ToDate
    ) c;

    -- balance-sheet items as of the date
    SELECT
        (SELECT ISNULL(SUM(NetRevenue), 0) FROM dbo.fn_RevenueRecognized(DATEADD(DAY, 1, @AsOfDate), '9999-12-31', @AsOfDate)) AS DeferredRevenue,
        (SELECT ISNULL(SUM(NetRevenue), 0) FROM dbo.fn_RevenueRecognized(DATEADD(DAY, -364, @AsOfDate), @AsOfDate, @AsOfDate)) AS NetRevenueTrailing12;
END
GO

/* Revenue, cash and receivables by institution for the dashboard table */
CREATE OR ALTER PROCEDURE dbo.usp_FinancialsByInstitution
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FyStart DATE = DATEFROMPARTS(YEAR(@AsOfDate) - CASE WHEN MONTH(@AsOfDate) < 7 THEN 1 ELSE 0 END, 7, 1);
    DECLARE @PriorFyStart DATE = DATEADD(YEAR, -1, @FyStart);
    DECLARE @PriorFyEnd DATE = DATEADD(DAY, -1, @FyStart);

    SELECT InstitutionId, Balance, NotYetDue, Due1To30, Due31To60, Due61To90, Due90Plus, PastDueAmount
    INTO #bal
    FROM dbo.fn_StudentTermBalance(@AsOfDate);

    SELECT
        i.InstitutionId, i.Code AS InstitutionCode, i.Name AS InstitutionName, i.Segment,
        ISNULL(py.NetRevenue, 0) AS NetRevenuePriorFY,
        ISNULL(ytd.NetRevenue, 0) AS NetRevenueYTD,
        ISNULL(cash.CashCollected, 0) AS CashCollectedYTD,
        ISNULL(ar.Receivable, 0) AS Receivable,
        ISNULL(ar.NotYetDue, 0) AS NotYetDue,
        ISNULL(ar.Due1To30, 0) AS Due1To30,
        ISNULL(ar.Due31To60, 0) AS Due31To60,
        ISNULL(ar.Due61To90, 0) AS Due61To90,
        ISNULL(ar.Due90Plus, 0) AS Due90Plus,
        ISNULL(ar.PastDue, 0) AS PastDue,
        ISNULL(st.ActiveStudents, 0) AS ActiveStudents
    FROM dbo.Institution i
    OUTER APPLY (SELECT SUM(NetRevenue) AS NetRevenue FROM dbo.fn_RevenueRecognized(@PriorFyStart, @PriorFyEnd, @PriorFyEnd) r
                 WHERE r.InstitutionId = i.InstitutionId) py
    OUTER APPLY (SELECT SUM(NetRevenue) AS NetRevenue FROM dbo.fn_RevenueRecognized(@FyStart, @AsOfDate, @AsOfDate) r
                 WHERE r.InstitutionId = i.InstitutionId) ytd
    OUTER APPLY (SELECT -SUM(at.Amount) AS CashCollected
                 FROM dbo.AccountTransaction at
                 JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
                 JOIN dbo.Student s ON s.StudentId = at.StudentId
                 WHERE s.InstitutionId = i.InstitutionId AND dc.Category IN ('PAY', 'AID') AND at.DetailCode <> 'SCHL'
                   AND at.EffectiveDate BETWEEN @FyStart AND @AsOfDate) cash
    OUTER APPLY (SELECT SUM(CASE WHEN b.Balance > 0 THEN b.Balance ELSE 0 END) AS Receivable,
                        SUM(b.NotYetDue) AS NotYetDue, SUM(b.Due1To30) AS Due1To30, SUM(b.Due31To60) AS Due31To60,
                        SUM(b.Due61To90) AS Due61To90, SUM(b.Due90Plus) AS Due90Plus, SUM(b.PastDueAmount) AS PastDue
                 FROM #bal b WHERE b.InstitutionId = i.InstitutionId) ar
    OUTER APPLY (SELECT COUNT(*) AS ActiveStudents FROM dbo.Student s
                 WHERE s.InstitutionId = i.InstitutionId AND s.EnrollmentStatus = 'Active') st
    ORDER BY i.InstitutionId;
END
GO

/* Every payment and aid receipt in one calendar month (the dashboard's month drill-down) */
CREATE OR ALTER PROCEDURE dbo.usp_MonthReceipts
    @MonthStart DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT at.TransactionId, at.EffectiveDate, at.StudentId,
           s.LastName + ', ' + s.FirstName AS StudentName,
           i.Code AS InstitutionCode, at.TermCode, at.DetailCode, dc.Description, dc.FundSource,
           -at.Amount AS Amount, at.ExternalRef
    FROM dbo.AccountTransaction at
    JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
    JOIN dbo.Student s     ON s.StudentId = at.StudentId
    JOIN dbo.Institution i ON i.InstitutionId = s.InstitutionId
    WHERE dc.Category IN ('PAY', 'AID')
      AND at.DetailCode <> 'SCHL'
      AND at.EffectiveDate >= @MonthStart
      AND at.EffectiveDate < DATEADD(MONTH, 1, @MonthStart)
    ORDER BY at.EffectiveDate, at.TransactionId;
END
GO

/* Totals for the month, the month before, and the same month a year earlier */
CREATE OR ALTER PROCEDURE dbo.usp_MonthReceiptComparisons
    @MonthStart DATE
AS
BEGIN
    SET NOCOUNT ON;

    WITH Months AS (
        SELECT * FROM (VALUES
            ('This', @MonthStart),
            ('PriorMonth', DATEADD(MONTH, -1, @MonthStart)),
            ('PriorYear', DATEADD(YEAR, -1, @MonthStart))) v (Label, MonthStart)
    )
    SELECT m.Label, m.MonthStart,
           (SELECT -SUM(at.Amount)
              FROM dbo.AccountTransaction at
              JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
             WHERE dc.Category IN ('PAY', 'AID') AND at.DetailCode <> 'SCHL'
               AND at.EffectiveDate >= m.MonthStart
               AND at.EffectiveDate < DATEADD(MONTH, 1, m.MonthStart)) AS Amount,
           CAST(CASE WHEN m.MonthStart >= f.FirstMonth THEN 1 ELSE 0 END AS BIT) AS HasData
    FROM Months m
    CROSS JOIN (SELECT DATEFROMPARTS(YEAR(MIN(EffectiveDate)), MONTH(MIN(EffectiveDate)), 1) AS FirstMonth
                FROM dbo.AccountTransaction) f;
END
GO
