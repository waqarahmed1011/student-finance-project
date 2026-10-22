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
    Debits AS (
        -- running total of debits from newest to oldest
        SELECT t.StudentId, t.TermCode, t.Amount, t.DueDate,
               SUM(t.Amount) OVER (PARTITION BY t.StudentId, t.TermCode
                                   ORDER BY t.DueDate DESC, t.TransactionId DESC
                                   ROWS UNBOUNDED PRECEDING) AS NewerAndThis
        FROM Txn t
        WHERE t.Amount > 0
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
