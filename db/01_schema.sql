/*
    Student Account Workbench - core schema
    All data in this project is synthetic. Institutions, students and vendors are fictional.

    Sign convention on dbo.AccountTransaction.Amount:
        charges are positive, payments / aid / credits are negative,
        so SUM(Amount) is always the balance the student owes.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF SCHEMA_ID('rpt') IS NULL EXEC('CREATE SCHEMA rpt');
GO

CREATE TABLE dbo.Institution (
    InstitutionId   INT           NOT NULL PRIMARY KEY,
    Code            VARCHAR(10)   NOT NULL UNIQUE,
    Name            NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.Program (
    ProgramId       INT           NOT NULL PRIMARY KEY,
    InstitutionId   INT           NOT NULL REFERENCES dbo.Institution (InstitutionId),
    Code            VARCHAR(10)   NOT NULL,
    Name            NVARCHAR(100) NOT NULL,
    AwardLevel      VARCHAR(20)   NOT NULL  -- Undergraduate, Graduate, Doctoral
);

CREATE TABLE dbo.Term (
    TermCode        CHAR(6)       NOT NULL PRIMARY KEY,  -- yyyy + 10/20/30, Banner style
    Description     NVARCHAR(50)  NOT NULL,
    StartDate       DATE          NOT NULL,
    EndDate         DATE          NOT NULL,
    PaymentDueDate  DATE          NOT NULL
);

CREATE TABLE dbo.Student (
    StudentId           CHAR(9)       NOT NULL PRIMARY KEY,
    FirstName           NVARCHAR(50)  NOT NULL,
    LastName            NVARCHAR(50)  NOT NULL,
    Email               NVARCHAR(120) NOT NULL,
    InstitutionId       INT           NOT NULL REFERENCES dbo.Institution (InstitutionId),
    ProgramId           INT           NOT NULL REFERENCES dbo.Program (ProgramId),
    EnrollmentStatus    VARCHAR(12)   NOT NULL,  -- Active, Withdrawn, Graduated, LOA
    MilitaryAffiliation VARCHAR(20)   NULL       -- Veteran, ActiveDuty, Dependent, NULL = none
);

CREATE TABLE dbo.DetailCode (
    DetailCode      CHAR(4)       NOT NULL PRIMARY KEY,
    Description     NVARCHAR(60)  NOT NULL,
    Category        CHAR(3)       NOT NULL,  -- CHG charge, PAY payment, AID financial aid, ADJ adjustment
    FundSource      VARCHAR(12)   NOT NULL   -- Institution, SelfPay, TitleIV, VA, MilitaryTA, Employer, PaymentPlan, Scholarship
);

CREATE TABLE dbo.AccountTransaction (
    TransactionId   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    StudentId       CHAR(9)       NOT NULL REFERENCES dbo.Student (StudentId),
    TermCode        CHAR(6)       NOT NULL REFERENCES dbo.Term (TermCode),
    DetailCode      CHAR(4)       NOT NULL REFERENCES dbo.DetailCode (DetailCode),
    Amount          DECIMAL(12,2) NOT NULL,
    EffectiveDate   DATE          NOT NULL,
    DueDate         DATE          NULL,      -- set on charges; payments and aid leave it NULL
    ExternalRef     VARCHAR(40)   NULL,      -- vendor transaction id for feed-posted payments
    PostedBy        VARCHAR(40)   NOT NULL DEFAULT 'SYSTEM',
    PostedAt        DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX IX_AccountTransaction_Student_Term ON dbo.AccountTransaction (StudentId, TermCode) INCLUDE (Amount, DetailCode);
CREATE INDEX IX_AccountTransaction_EffectiveDate ON dbo.AccountTransaction (EffectiveDate) INCLUDE (Amount, DetailCode);
CREATE INDEX IX_AccountTransaction_ExternalRef ON dbo.AccountTransaction (ExternalRef) WHERE ExternalRef IS NOT NULL;

/*
    VA enrollment certifications (Chapter 33 Post-9/11 GI Bill, Chapter 31 VR&E).
    A PENDING row means the school has certified tuition and fees to VA and is waiting on payment.
*/
CREATE TABLE dbo.VaCertification (
    CertificationId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    StudentId       CHAR(9)       NOT NULL REFERENCES dbo.Student (StudentId),
    TermCode        CHAR(6)       NOT NULL REFERENCES dbo.Term (TermCode),
    Chapter         VARCHAR(3)    NOT NULL,  -- 33 or 31
    CertifiedDate   DATE          NOT NULL,
    ExpectedAmount  DECIMAL(12,2) NOT NULL,
    Status          VARCHAR(10)   NOT NULL,  -- PENDING, PAID
    PaidDate        DATE          NULL,
    CONSTRAINT UQ_VaCertification UNIQUE (StudentId, TermCode)
);

/* Payment plans administered by the third-party plan vendor (fictional "ClearPath Tuition Plans") */
CREATE TABLE dbo.PaymentPlan (
    PlanId          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    StudentId       CHAR(9)       NOT NULL REFERENCES dbo.Student (StudentId),
    TermCode        CHAR(6)       NOT NULL REFERENCES dbo.Term (TermCode),
    VendorPlanId    VARCHAR(20)   NOT NULL UNIQUE,
    PlanAmount      DECIMAL(12,2) NOT NULL,
    Installments    TINYINT       NOT NULL,
    EnrolledDate    DATE          NOT NULL,
    Status          VARCHAR(10)   NOT NULL,  -- ACTIVE, COMPLETED, CANCELLED (vendor cancels after missed installments)
    CONSTRAINT UQ_PaymentPlan UNIQUE (StudentId, TermCode)
);

/* Third-party vendor settlement file reconciliation */
CREATE TABLE dbo.ReconBatch (
    BatchId         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    VendorName      NVARCHAR(60)  NOT NULL,
    FileName        NVARCHAR(200) NOT NULL,
    UploadedBy      NVARCHAR(60)  NOT NULL,
    UploadedAt      DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    TotalRows       INT           NOT NULL,
    TotalAmount     DECIMAL(14,2) NOT NULL,
    MatchedRows     INT           NOT NULL,
    ExceptionRows   INT           NOT NULL
);

CREATE TABLE dbo.ReconException (
    ExceptionId     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    BatchId         INT           NOT NULL REFERENCES dbo.ReconBatch (BatchId),
    LineNumber      INT           NULL,      -- NULL for ledger-side findings (NotInVendorFile)
    VendorTxnId     VARCHAR(40)   NULL,
    StudentId       VARCHAR(20)   NULL,
    PaymentDate     DATE          NULL,
    FileAmount      DECIMAL(12,2) NULL,
    LedgerAmount    DECIMAL(12,2) NULL,
    ExceptionType   VARCHAR(30)   NOT NULL,  -- see ReconExceptionType in Workbench.Core
    Detail          NVARCHAR(400) NOT NULL,
    Status          VARCHAR(10)   NOT NULL DEFAULT 'OPEN',  -- OPEN, RESOLVED
    ResolvedBy      NVARCHAR(60)  NULL,
    ResolvedAt      DATETIME2(0)  NULL,
    ResolutionNote  NVARCHAR(400) NULL
);

CREATE INDEX IX_ReconException_Status ON dbo.ReconException (Status, BatchId);

/* Append-only audit trail for anything a user does in the app */
CREATE TABLE dbo.AuditLog (
    AuditId         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    EventTime       DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    Actor           NVARCHAR(60)  NOT NULL,
    Action          VARCHAR(40)   NOT NULL,
    EntityType      VARCHAR(40)   NULL,
    EntityId        VARCHAR(40)   NULL,
    Details         NVARCHAR(MAX) NULL
);
GO
