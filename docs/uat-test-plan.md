# User Acceptance Test Plan

Each case maps to a requirement in [requirements.md](requirements.md). Expected results are based on the synthetic data in `db/05_seed_data.sql` and the sample vendor file. Rule-level cases are also covered by automated tests in `tests/Workbench.Tests` (noted in the last column).

Tester: ________  Date: ________  Build / commit: ________

## Past-Due Report

| # | Req | Steps | Expected result | Auto test | Pass/Fail |
|---|-----|-------|-----------------|-----------|-----------|
| 1 | PD-01 | Open Past-Due Report with no filters | Rows listed with balance, past due amount and days; count shown on Export button | | |
| 2 | PD-02 | Find a Fall 2026 student with an unpaid late clinical or materials fee | That fee's amount shows in 1-30 while tuition shows in 31-60 for the same student | | |
| 3 | PD-03 | Filter Institution = Lakeshore College of Nursing, Term = Fall 2026 | Only LCN Fall 2026 rows; summary cards and totals update | | |
| 4 | PD-05 | Click the "VA protected" card | Every row has a pending VA Ch. 33/31 payment certified within the last 90 days; Hold-eligible total is $0 | HoldPolicyTests | |
| 5 | PD-06 | Click the "VA partial" card | Each reason states what VA covers and what the student still owes; protected + hold-eligible = past due | HoldPolicyTests | |
| 6 | PD-07 | Click the "VA follow-up" card | Rows are Summer 2026 certifications still pending more than 90 days; reason says to check with the SCO | HoldPolicyTests | |
| 7 | PD-08 | Click "On payment plan" | Every row shows Plan: active; hold-eligible amount is $0 | HoldPolicyTests | |
| 8 | PD-09 | Click "Reminder only" | All past due amounts are under $200 | HoldPolicyTests | |
| 9 | PD-10 | Find a Withdrawn or Inactive student in Hold eligible | Reason says outreach and collections review, not registration hold | HoldPolicyTests | |
| 10 | PD-11 | Apply a filter, click Export CSV | CSV row count matches the button; includes RecommendedAction and Reason columns; Audit Log shows PAST_DUE_EXPORTED | | |
| 11 | PD-01 | Change As of to 09/01/2026 | Fall 2026 tuition no longer past due (due 09/13/2026) | | |

## Dashboard

| # | Req | Steps | Expected result | Auto test | Pass/Fail |
|---|-----|-------|-----------------|-----------|-----------|
| 12 | DB-01 | Open Dashboard | Five KPI tiles populated | | |
| 13 | DB-05 | Compare Dashboard "Past due" with Past-Due Report total past due (no filters) | Same amount | | |
| 14 | DB-02 | Hover a bar in "Money received by month" | Tooltip shows fund source and dollar amount; January, May and September spikes from Title IV disbursements | | |
| 15 | DB-04 | Check collection rate table | Older terms collected at a higher rate than Fall 2026 | | |

## Vendor Reconciliation

| # | Req | Steps | Expected result | Auto test | Pass/Fail |
|---|-----|-------|-----------------|-----------|-----------|
| 16 | RC-03, RC-04 | Enter a name, click "Run the sample vendor file" | 403 rows, 394 matched, 10 exceptions: 3 missing in ledger, 2 amount mismatch, 1 wrong student, 1 duplicate, 1 unknown student, 1 invalid row, 1 not in vendor file | PaymentMatcherTests | |
| 17 | RC-01 | Upload a CSV missing the plan_id column | Error lists the missing column; nothing saved | VendorFileParserTests | |
| 18 | RC-02 | Upload a CSV where one row has amount "abc" | Import completes; that row is an Invalid row exception | VendorFileParserTests | |
| 19 | RC-05 | After running the sample, check the student from a "Missing in ledger" exception on the Past-Due Report | Balance unchanged (nothing was posted) | | |
| 20 | RC-06 | Resolve one exception without a note | Rejected with a message | | |
| 21 | RC-06 | Resolve one exception with name and note | Moves to resolved; Still open drops by 1; Audit Log shows RECON_EXCEPTION_RESOLVED with the note | | |

## Ask for a Report

| # | Req | Steps | Expected result | Auto test | Pass/Fail |
|---|-----|-------|-----------------|-----------|-----------|
| 22 | AR-01 | Click an example question, then Draft the SQL | SQL and explanation appear; "Passed the safety check" | | |
| 23 | AR-03 | Edit the SQL (e.g. change TOP) and Run | Edited query runs | | |
| 24 | AR-04 | Replace SQL with `SELECT * FROM dbo.Student` and Run | Blocked: not a reporting view | ReportSqlGuardTests | |
| 25 | AR-04 | Replace SQL with `DELETE FROM rpt.Payments` and Run | Blocked: only SELECT allowed | ReportSqlGuardTests | |
| 26 | AR-05 | (DBA) Connect as report_reader and run `SELECT * FROM dbo.Student` | Permission denied | | |
| 27 | AR-06 | Ask "list student names with past due balances" | Explanation says names aren't available, or SQL returns IDs only | | |
| 28 | AR-07 | Check Audit Log | AI_SQL_GENERATED and REPORT_QUERY_RUN rows with question and SQL | | |

## Sign-off

| Role | Name | Date | Approved |
|------|------|------|----------|
| Student Finance business owner | | | |
| Business Systems | | | |
