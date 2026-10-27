# Data Dictionary

All data is synthetic. Table and column names are generic on purpose; the comments note the Banner concept each one stands in for.

## dbo tables

### Institution
| Column | Type | Notes |
|---|---|---|
| InstitutionId | int | PK |
| Code | varchar(10) | LCN, MHU, CSM, WSM, CVM |
| Name | nvarchar(100) | |
| Segment | varchar(30) | Nursing, Online, Medical and Veterinary (mirrors Covista's three reporting segments) |

### Program
| Column | Type | Notes |
|---|---|---|
| ProgramId | int | PK |
| InstitutionId | int | FK Institution |
| Code, Name | | e.g. BSN, MD |
| AwardLevel | varchar(20) | Undergraduate, Graduate, Doctoral |

### Term (Banner STVTERM)
| Column | Type | Notes |
|---|---|---|
| TermCode | char(6) | 202520 Summer 2025, 202530 Fall 2025, 202610 Spring 2026, 202620 Summer 2026, 202630 Fall 2026 |
| StartDate, EndDate | date | |
| PaymentDueDate | date | Default due date for tuition |

### Student (Banner SPRIDEN / SGBSTDN)
| Column | Type | Notes |
|---|---|---|
| StudentId | char(9) | PK, S + 8 digits |
| FirstName, LastName, Email | | PII. Not exposed in rpt views |
| InstitutionId, ProgramId | int | |
| EnrollmentStatus | varchar(12) | Active, Withdrawn, Inactive |
| MilitaryAffiliation | varchar(20) | Veteran, ActiveDuty, Dependent, NULL |

### DetailCode (Banner TBBDETC)
| Column | Type | Notes |
|---|---|---|
| DetailCode | char(4) | PK |
| Category | char(3) | CHG charge, PAY payment, AID aid/benefit, ADJ adjustment |
| FundSource | varchar(12) | Institution, SelfPay, TitleIV, VA, MilitaryTA, Employer, PaymentPlan, Scholarship |

Codes: TUIT, FEES, CLIN, MATL (charges); PELL, DLSU, DLUN, GPLS, VA33, VA31, MLTA, SCHL (aid); CARD, PPLN, EMPL (payments); RFND credit balance refund, WDTA withdrawal tuition adjustment, RT4R Return of Title IV funds, WOFF bad debt write-off (adjustments).

### AccountTransaction (Banner TBRACCD)
| Column | Type | Notes |
|---|---|---|
| TransactionId | int identity | PK |
| StudentId, TermCode, DetailCode | | FKs |
| Amount | decimal(12,2) | **Charges positive, credits negative.** SUM(Amount) = balance owed |
| EffectiveDate | date | When it hit the account |
| DueDate | date | Set on charges. Drives aging |
| ExternalRef | varchar(40) | Vendor transaction ID for feed-posted payments |
| PostedBy | varchar(40) | SYSTEM or CLEARPATH_FEED |

### VaCertification
| Column | Type | Notes |
|---|---|---|
| StudentId, TermCode | | Unique together |
| Chapter | varchar(3) | 33 Post-9/11 GI Bill, 31 VR&E |
| CertifiedDate | date | Starts the 3679(e) protection window |
| ExpectedAmount | decimal | What VA should pay (after eligibility %) |
| Status | varchar(10) | PENDING, PAID |
| PaidDate | date | |

### PaymentPlan
| Column | Type | Notes |
|---|---|---|
| VendorPlanId | varchar(20) | Vendor's plan ID (CPP-xxxxxx) |
| PlanAmount, Installments | | |
| Status | varchar(10) | ACTIVE, COMPLETED, CANCELLED |

### PaymentPlanInstallment
| Column | Type | Notes |
|---|---|---|
| VendorPlanId, InstallmentNo | | PK |
| DueDate, Amount | | The plan's schedule. Aging uses these due dates for students on an active or completed plan |

### ReconBatch / ReconException
One batch row per uploaded file; one exception row per finding. `ReconException.LineNumber` is NULL for ledger-side findings (NotInVendorFile). Status OPEN or RESOLVED with ResolvedBy, ResolvedAt, ResolutionNote.

### AuditLog
Append-only. Actor, Action, EntityType, EntityId, Details, EventTime (UTC). App login has INSERT but not UPDATE or DELETE.

## Logic

### dbo.fn_StudentTermBalance(@AsOfDate)
Inline table-valued function, one row per student-term with activity on or before @AsOfDate. Returns charges, payments, aid, adjustments, balance, FIFO aging buckets (NotYetDue, Due1To30, Due31To60, Due61To90, Due90Plus), PastDueAmount, DaysPastDue (oldest unpaid past-due charge), AgingBucket, VA and payment plan status. Every report and the dashboard read from this.

### dbo.fn_RevenueRecognized(@FromDate, @ToDate, @AsOfDate)
Revenue earned in a date range from what had been billed by @AsOfDate, per institution and term. Tuition and fees are recognised evenly over the days of the term; institutional scholarships and withdrawal credits reduce revenue. Deferred revenue on a date = this function from the next day onward.

### Stored procedures
| Proc | Used by |
|---|---|
| usp_GetPastDueAging | Past-Due Report and CSV export |
| usp_DashboardKpis | Dashboard tiles |
| usp_PaymentTrendByMonth | Dashboard trend chart |
| usp_CollectionRateByTerm | Dashboard table |
| usp_FinancialSummary | Dashboard revenue/collections row (prior FY, prior-year-to-date, year-to-date, deferred revenue, trailing 12-month revenue) |
| usp_FinancialsByInstitution | Dashboard by-institution table and aging chart |
| usp_MonthReceipts, usp_MonthReceiptComparisons | Month drill-down page |
| usp_GetLedgerVendorPayments | Reconciliation |

## rpt views (Ask for a Report)

| View | Grain | Notes |
|---|---|---|
| rpt.Students | student | No names or emails |
| rpt.StudentBalances | student-term | Balance and aging as of today |
| rpt.Payments | transaction | Payments and aid only, amounts positive |
| rpt.VaCertifications | certification | |
| rpt.PaymentPlans | plan | |

## Database logins

| Login | Rights |
|---|---|
| workbench_app | db_datareader, EXECUTE on dbo, INSERT/UPDATE on ReconBatch and ReconException, INSERT on AuditLog |
| report_reader | SELECT on schema rpt only |
