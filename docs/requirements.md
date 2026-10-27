# Business Requirements

Written the way I'd write them for a Student Finance request: what the business asked for, what the system has to do, and how we'll know it's done. Requirement IDs are referenced in the [UAT test plan](uat-test-plan.md).

## Background

Student Finance gets the same three requests over and over:

1. "Send me a list of students with past-due balances."
2. "Can we get a dashboard of tuition payment trends?"
3. "The payment plan vendor's file doesn't tie out to what we posted. What's off?"

Today these are answered with one-off queries and spreadsheets, which means the numbers don't always agree with each other, and a past-due list can include students the school isn't allowed to put a hold on.

## Scope

In scope: past-due report with hold rules, payment trends dashboard, vendor settlement reconciliation, ad hoc report requests, audit trail.

Out of scope for this version: posting payments, placing holds in the student system, sending student communications, R2T4 calculations, authentication (demo is public with synthetic data).

---

## 1. Past-Due Report

| ID | Requirement |
|----|-------------|
| PD-01 | Show every student-term with a past-due balance as of a chosen date (default today). |
| PD-02 | Age balances using FIFO payment application: payments and aid apply to the oldest charges first, and each unpaid piece is aged from its own due date into Current, 1-30, 31-60, 61-90 and 90+ day buckets. Students on an active payment plan are aged on the plan's installment schedule. |
| PD-03 | Filter by institution, term and oldest aging bucket. |
| PD-04 | For each row, recommend one action: Hold eligible, VA protected, VA partially protected, VA follow-up, On payment plan, or Reminder only. |
| PD-05 | **VA protection (38 U.S.C. 3679(e))**: if a Chapter 33 or 31 payment is pending, no hold or late fee for the amount VA is expected to pay, until VA pays or 90 days after certification, whichever is first. |
| PD-06 | If only part of the balance is covered by the pending VA payment, the student remains responsible for the rest, and the report shows both amounts. |
| PD-07 | If a VA certification is still pending after the 90-day window, flag it for School Certifying Official follow-up instead of putting it on the hold list. |
| PD-08 | Students on an active third-party payment plan are not treated as delinquent. If the vendor cancels the plan, normal rules apply. |
| PD-09 | Past-due amounts under the hold threshold (configurable, default $200) get a reminder only. |
| PD-10 | Students no longer enrolled are routed to outreach / collections review rather than a registration hold. |
| PD-11 | Export the filtered list to CSV with the recommended action and reason on every row. Exports are logged. |

## 2. Payment Trends Dashboard

| ID | Requirement |
|----|-------------|
| DB-01 | Headline numbers: total receivable, total past due (and number of students), past due over 90 days, past due waiting on VA, collected in the last 30 days. |
| DB-02 | Monthly money received for the last 12 months, split by fund source (Title IV, VA, military TA, self-pay, payment plan, employer, scholarship). |
| DB-03 | Past-due dollars by institution and aging bucket. |
| DB-04 | Collection rate by term (payments + aid divided by net charges). |
| DB-05 | Dashboard and Past-Due Report must use the same balance logic so totals always match. |
| DB-06 | Fiscal-year financials (July 1 to June 30): net tuition revenue recognised evenly over each term, scholarships/discounts, cash collected and refunds, deferred revenue, allowance for credit losses (aging method, rates in config), net receivables and days sales outstanding. Year-to-date compared with the same period last year. |
| DB-07 | By-institution table: segment, active students, prior-year and year-to-date revenue, cash, receivables, past due, allowance. |
| DB-08 | Clicking a month on the money-received chart opens that month: totals vs prior month and same month last year, split by fund source and school, a day-by-day chart, and a filterable transaction list with CSV export. |

## 3. Vendor Reconciliation

| ID | Requirement |
|----|-------------|
| RC-01 | Accept the vendor settlement file as CSV with columns vendor_txn_id, student_id, payment_date, amount, payment_method, plan_id. Reject files missing a required column with a clear message. |
| RC-02 | A bad row never stops the import. It becomes an "Invalid row" exception. |
| RC-03 | Match each file row to a posted payment by vendor transaction ID. If there is no reference, match on student + amount + date within 2 days. |
| RC-04 | Report these exceptions: missing in ledger, amount mismatch, posted to wrong student, duplicate in file, unknown student ID, invalid row, and posted in our ledger but not in the vendor file. |
| RC-05 | Never post, reverse or change a payment automatically. |
| RC-06 | Each exception is resolved by a named person with a note. Resolution and upload are written to the audit log in the same transaction. |
| RC-07 | Show match rate and open exception count per file. |

## 4. Ad Hoc Report Requests ("Ask for a Report")

| ID | Requirement |
|----|-------------|
| AR-01 | User types a request in plain English; AI drafts the SQL and a one-line explanation. |
| AR-02 | The AI only receives view definitions, never student data. |
| AR-03 | The user sees and can edit the SQL before anything runs. |
| AR-04 | SQL is checked with a real T-SQL parser: one SELECT, only `rpt` schema views, no SELECT INTO, OPENROWSET, OPENQUERY, OPENDATASOURCE or table-valued functions. |
| AR-05 | Queries run under a separate database login with SELECT on the `rpt` schema only. |
| AR-06 | Reporting views exclude names and email addresses. |
| AR-07 | 500-row cap and 15-second timeout. The question and SQL are logged. |
| AR-08 | Rate limit per visitor so the public demo can't run up the AI bill. |

## 5. Audit and Controls

| ID | Requirement |
|----|-------------|
| AU-01 | Log reconciliation uploads, exception resolutions, report exports, AI SQL generation and ad hoc query runs with who, when and what. |
| AU-02 | The application login can insert audit rows but cannot update or delete them. |

## Assumptions

- The student has already submitted their VA certificate of eligibility (3679(e) requires it).
- Hold threshold and VA protection days are business-owned settings, not code.
- The vendor file's transaction IDs are unique per payment.

## Success measures (if this were real)

- Time to produce the past-due list goes from hours of manual cleanup to minutes.
- Zero VA-protected students on a hold or late-fee list.
- Vendor file differences found the day the file arrives, not at month-end close.
- Ad hoc report turnaround measured in minutes for questions the reporting views can answer.
