/*
    Balance logic lives in one place: dbo.fn_StudentTermBalance.
    The past-due proc, the dashboard procs and the rpt views all read from it,
    so a report and a dashboard can never disagree about what a student owes.
*/

CREATE OR ALTER FUNCTION dbo.fn_StudentTermBalance (@AsOfDate DATE)
RETURNS TABLE
AS
RETURN
    /*
        Aging uses FIFO payment application: payments and aid pay off the oldest charges first,
        so whatever is still owed sits on the newest charges. Each unpaid slice is then aged by
        that charge's own due date. Same idea as a standard AR aging report.

        Students on an active or completed payment plan are aged on the plan's installment schedule
        instead, so someone paying on time isn't reported as past due on the original tuition date.
    */
    WITH Txn AS (
        SELECT at.TransactionId, at.StudentId, at.TermCode, at.Amount,
               COALESCE(at.DueDate, at.EffectiveDate) AS DueDate, dc.Category
        FROM dbo.AccountTransaction at
        JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
        WHERE at.EffectiveDate <= @AsOfDate
    ),
    TermTotals AS (
        SELECT
            StudentId,
            TermCode,
            SUM(CASE WHEN Category = 'CHG' THEN Amount ELSE 0 END)  AS Charges,
            -SUM(CASE WHEN Category = 'PAY' THEN Amount ELSE 0 END) AS Payments,
            -SUM(CASE WHEN Category = 'AID' THEN Amount ELSE 0 END) AS Aid,
            SUM(CASE WHEN Category = 'ADJ' THEN Amount ELSE 0 END)  AS Adjustments,
            SUM(Amount)                                             AS Balance
        FROM Txn
        GROUP BY StudentId, TermCode
    ),
    PlanTerm AS (
        SELECT pp.StudentId, pp.TermCode, pp.VendorPlanId, pp.PlanAmount
        FROM dbo.PaymentPlan pp
        WHERE pp.EnrolledDate <= @AsOfDate
          AND pp.Status <> 'CANCELLED'   -- a cancelled plan falls back to the original due dates
    ),
    DebitItems AS (
        -- no plan: every charge (and positive adjustment) with its own due date
        SELECT t.StudentId, t.TermCode, t.Amount, t.DueDate, t.TransactionId AS SortKey
        FROM Txn t
        WHERE t.Amount > 0
          AND NOT EXISTS (SELECT 1 FROM PlanTerm p WHERE p.StudentId = t.StudentId AND p.TermCode = t.TermCode)

        UNION ALL
        -- on a plan: the plan amount is due on the installment schedule
        SELECT p.StudentId, p.TermCode, i.Amount, i.DueDate, i.InstallmentNo
        FROM PlanTerm p
        JOIN dbo.PaymentPlanInstallment i ON i.VendorPlanId = p.VendorPlanId

        UNION ALL
        -- on a plan: whatever the plan doesn't cover (the part aid or VA pays) is due on the term due date
        SELECT p.StudentId, p.TermCode, d.Total - p.PlanAmount, tm.PaymentDueDate, 0
        FROM PlanTerm p
        JOIN dbo.Term tm ON tm.TermCode = p.TermCode
        CROSS APPLY (SELECT SUM(t.Amount) AS Total FROM Txn t
                     WHERE t.StudentId = p.StudentId AND t.TermCode = p.TermCode AND t.Amount > 0) d
        WHERE d.Total > p.PlanAmount
    ),
    Debits AS (
        -- running total of debits from newest to oldest
        SELECT d.StudentId, d.TermCode, d.Amount, d.DueDate,
               SUM(d.Amount) OVER (PARTITION BY d.StudentId, d.TermCode
                                   ORDER BY d.DueDate DESC, d.SortKey DESC
                                   ROWS UNBOUNDED PRECEDING) AS NewerAndThis
        FROM DebitItems d
    ),
    Unpaid AS (
        SELECT d.StudentId, d.TermCode, d.DueDate,
               CASE WHEN tt.Balance <= 0 THEN 0
                    WHEN d.NewerAndThis <= tt.Balance THEN d.Amount
                    WHEN d.NewerAndThis - d.Amount < tt.Balance THEN tt.Balance - (d.NewerAndThis - d.Amount)
                    ELSE 0 END AS UnpaidAmount
        FROM Debits d
        JOIN TermTotals tt ON tt.StudentId = d.StudentId AND tt.TermCode = d.TermCode
    ),
    Aging AS (
        SELECT StudentId, TermCode,
               SUM(CASE WHEN DueDate >= @AsOfDate THEN UnpaidAmount ELSE 0 END) AS NotYetDue,
               SUM(CASE WHEN DueDate <  @AsOfDate AND DATEDIFF(DAY, DueDate, @AsOfDate) <= 30 THEN UnpaidAmount ELSE 0 END) AS Due1To30,
               SUM(CASE WHEN DATEDIFF(DAY, DueDate, @AsOfDate) BETWEEN 31 AND 60 THEN UnpaidAmount ELSE 0 END) AS Due31To60,
               SUM(CASE WHEN DATEDIFF(DAY, DueDate, @AsOfDate) BETWEEN 61 AND 90 THEN UnpaidAmount ELSE 0 END) AS Due61To90,
               SUM(CASE WHEN DATEDIFF(DAY, DueDate, @AsOfDate) > 90 THEN UnpaidAmount ELSE 0 END) AS Due90Plus,
               MAX(CASE WHEN UnpaidAmount > 0 AND DueDate < @AsOfDate
                        THEN DATEDIFF(DAY, DueDate, @AsOfDate) ELSE 0 END) AS DaysPastDue
        FROM Unpaid
        GROUP BY StudentId, TermCode
    )
    SELECT
        s.StudentId,
        s.FirstName,
        s.LastName,
        s.Email,
        s.EnrollmentStatus,
        s.MilitaryAffiliation,
        i.InstitutionId,
        i.Code              AS InstitutionCode,
        i.Name              AS InstitutionName,
        i.Segment,
        p.Name              AS ProgramName,
        t.TermCode,
        t.Description       AS TermDescription,
        t.PaymentDueDate,
        tt.Charges,
        tt.Payments,
        tt.Aid,
        tt.Adjustments,
        tt.Balance,
        ISNULL(ag.NotYetDue, 0) AS NotYetDue,
        ISNULL(ag.Due1To30, 0)  AS Due1To30,
        ISNULL(ag.Due31To60, 0) AS Due31To60,
        ISNULL(ag.Due61To90, 0) AS Due61To90,
        ISNULL(ag.Due90Plus, 0) AS Due90Plus,
        ISNULL(ag.Due1To30 + ag.Due31To60 + ag.Due61To90 + ag.Due90Plus, 0) AS PastDueAmount,
        ISNULL(ag.DaysPastDue, 0) AS DaysPastDue,
        CASE
            WHEN ISNULL(ag.DaysPastDue, 0) = 0 THEN 'Current'
            WHEN ag.DaysPastDue <= 30          THEN '1-30'
            WHEN ag.DaysPastDue <= 60          THEN '31-60'
            WHEN ag.DaysPastDue <= 90          THEN '61-90'
            ELSE '90+'
        END                 AS AgingBucket,
        va.Chapter          AS VaChapter,
        CASE WHEN va.CertificationId IS NULL THEN NULL
             WHEN va.Status = 'PAID' AND va.PaidDate <= @AsOfDate THEN 'PAID'
             ELSE 'PENDING' END AS VaStatus,
        va.CertifiedDate    AS VaCertifiedDate,
        va.ExpectedAmount   AS VaExpectedAmount,
        pp.Status           AS PaymentPlanStatus
    FROM TermTotals tt
    JOIN dbo.Student s      ON s.StudentId = tt.StudentId
    JOIN dbo.Institution i  ON i.InstitutionId = s.InstitutionId
    JOIN dbo.Program p      ON p.ProgramId = s.ProgramId
    JOIN dbo.Term t         ON t.TermCode = tt.TermCode
    LEFT JOIN Aging ag      ON ag.StudentId = tt.StudentId AND ag.TermCode = tt.TermCode
    LEFT JOIN dbo.VaCertification va
           ON va.StudentId = tt.StudentId
          AND va.TermCode = tt.TermCode
          AND va.CertifiedDate <= @AsOfDate
    LEFT JOIN dbo.PaymentPlan pp
           ON pp.StudentId = tt.StudentId
          AND pp.TermCode = tt.TermCode
          AND pp.EnrolledDate <= @AsOfDate;
GO

/*
    Revenue recognised between @FromDate and @ToDate, using what had been billed by @AsOfDate.
    Tuition and fees are earned evenly over the days of the term (straight-line), the usual
    treatment for term-based tuition. Institutional scholarships reduce revenue (they're a
    discount, not cash). Withdrawal tuition credits reduce it too.
    The deferred revenue balance on a date is just this function from the next day onward.
*/
CREATE OR ALTER FUNCTION dbo.fn_RevenueRecognized (@FromDate DATE, @ToDate DATE, @AsOfDate DATE)
RETURNS TABLE
AS
RETURN
    WITH Billed AS (
        SELECT s.InstitutionId, at.TermCode,
               SUM(CASE WHEN dc.Category = 'CHG' OR at.DetailCode = 'WDTA' THEN at.Amount ELSE 0 END) AS GrossTuitionFees,
               -SUM(CASE WHEN at.DetailCode = 'SCHL' THEN at.Amount ELSE 0 END)                     AS Scholarships
        FROM dbo.AccountTransaction at
        JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
        JOIN dbo.Student s     ON s.StudentId = at.StudentId
        WHERE at.EffectiveDate <= @AsOfDate
        GROUP BY s.InstitutionId, at.TermCode
    ),
    Share AS (
        SELECT b.InstitutionId, b.TermCode, b.GrossTuitionFees, b.Scholarships,
               CAST(DATEDIFF(DAY, GREATEST(@FromDate, t.StartDate), LEAST(@ToDate, t.EndDate)) + 1 AS DECIMAL(9,4))
                 / (DATEDIFF(DAY, t.StartDate, t.EndDate) + 1) AS EarnedShare
        FROM Billed b
        JOIN dbo.Term t ON t.TermCode = b.TermCode
        WHERE t.StartDate <= @ToDate AND t.EndDate >= @FromDate
    )
    SELECT InstitutionId, TermCode,
           CAST(GrossTuitionFees * EarnedShare AS DECIMAL(14,2))                  AS GrossRevenue,
           CAST(Scholarships * EarnedShare AS DECIMAL(14,2))                      AS Scholarships,
           CAST((GrossTuitionFees - Scholarships) * EarnedShare AS DECIMAL(14,2)) AS NetRevenue
    FROM Share;
GO

/*
    rpt schema = the only objects the "Ask for a report" feature can query.
    No names or emails here on purpose (FERPA data minimization). A user who needs
    contact details uses the Past-Due report, which is a reviewed, fixed query.
*/

CREATE OR ALTER VIEW rpt.Students
AS
SELECT
    s.StudentId,
    i.Code          AS InstitutionCode,
    i.Name          AS InstitutionName,
    i.Segment,
    p.Name          AS ProgramName,
    p.AwardLevel,
    s.EnrollmentStatus,
    s.MilitaryAffiliation
FROM dbo.Student s
JOIN dbo.Institution i ON i.InstitutionId = s.InstitutionId
JOIN dbo.Program p     ON p.ProgramId = s.ProgramId;
GO

CREATE OR ALTER VIEW rpt.StudentBalances
AS
SELECT
    StudentId,
    InstitutionCode,
    InstitutionName,
    ProgramName,
    TermCode,
    TermDescription,
    PaymentDueDate,
    Charges,
    Payments,
    Aid,
    Adjustments,
    Balance,
    PastDueAmount,
    DaysPastDue,
    AgingBucket,
    VaChapter,
    VaStatus,
    PaymentPlanStatus
FROM dbo.fn_StudentTermBalance(CAST(GETDATE() AS DATE));
GO

CREATE OR ALTER VIEW rpt.Payments
AS
SELECT
    at.StudentId,
    i.Code              AS InstitutionCode,
    at.TermCode,
    at.EffectiveDate    AS PaymentDate,
    -at.Amount          AS Amount,
    dc.Category,
    dc.FundSource,
    dc.Description      AS DetailDescription
FROM dbo.AccountTransaction at
JOIN dbo.DetailCode dc ON dc.DetailCode = at.DetailCode
JOIN dbo.Student s     ON s.StudentId = at.StudentId
JOIN dbo.Institution i ON i.InstitutionId = s.InstitutionId
WHERE dc.Category IN ('PAY', 'AID');
GO

CREATE OR ALTER VIEW rpt.VaCertifications
AS
SELECT StudentId, TermCode, Chapter, CertifiedDate, ExpectedAmount, Status, PaidDate
FROM dbo.VaCertification;
GO

CREATE OR ALTER VIEW rpt.PaymentPlans
AS
SELECT StudentId, TermCode, PlanAmount, Installments, EnrolledDate, Status
FROM dbo.PaymentPlan;
GO
