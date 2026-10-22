# Student Account Workbench

A student finance reporting and reconciliation tool built on SQL Server and ASP.NET Core. It covers three requests a Student Finance team makes all the time:

1. **"Send me the students with past-due balances."** Aged past-due report where every row is checked against the hold rules: VA pending-payment protection under 38 U.S.C. 3679(e), active payment plans, and a minimum balance threshold.
2. **"Build me a dashboard of tuition payment trends."** Receivables, money received by fund source, past due by institution and age, and collection rate by term.
3. **"The payment plan vendor's file doesn't match what we posted."** Upload the vendor settlement file, compare it to the ledger, and work an exception queue with an audit trail.

Plus **Ask for a Report**: type a question in plain English, AI drafts the SQL, a person reviews it, and it runs under a read-only login that can only see de-identified reporting views.

All data is synthetic: about 2,500 made-up students across five fictional institutions.

![Dashboard](docs/images/dashboard.png)

## What's worth looking at

| | Where |
|---|---|
| FIFO AR aging in one inline table-valued function (window functions) that every report reads from | [`db/02_views.sql`](db/02_views.sql) |
| VA 3679(e) protection, payment plan and threshold rules, with tests for the 90-day boundary and partial coverage | [`HoldPolicy.cs`](src/Workbench.Core/PastDue/HoldPolicy.cs), [tests](tests/Workbench.Tests/HoldPolicyTests.cs) |
| Vendor file reconciliation: 7 exception types, never posts anything | [`PaymentMatcher.cs`](src/Workbench.Core/Reconciliation/PaymentMatcher.cs) |
| AI SQL guard using Microsoft's T-SQL parser (ScriptDom), not keyword matching | [`ReportSqlGuard.cs`](src/Workbench.Core/Reporting/ReportSqlGuard.cs) |
| Least-privilege logins: AI SQL runs as a user with SELECT on `rpt` only | [`db/04_security.sql`](db/04_security.sql) |
| Synthetic data with Title IV disbursements, credit balance refunds, R2T4, VA, TA, payment plans, late payers | [`tools/generate_seed_data.py`](tools/generate_seed_data.py) |
| Requirements, UAT test plan, data dictionary | [`docs/`](docs) |

## Screens

| Past-Due Report (VA partially protected) | Vendor reconciliation exceptions |
|---|---|
| ![Past-due](docs/images/past-due.png) | ![Reconciliation](docs/images/reconciliation.png) |

## Stack

- **SQL Server / Azure SQL**: schema, inline TVF for balances and aging, stored procedures for each report, two database logins
- **C# / ASP.NET Core 8 Razor Pages**, Dapper, Bootstrap, Chart.js
- **Workbench.Core** class library for business rules (no database dependency, reusable from a WinForms tool or batch job)
- **xUnit**: 52 tests
- **Google Gemini** for drafting SQL from plain English (optional)
- **Python** for the synthetic data generator

```
src/Workbench.Core/     business rules: hold policy, vendor file parser, payment matcher, SQL guard
src/Workbench.Web/      Razor Pages UI, Dapper repositories, Gemini client
tests/Workbench.Tests/  xUnit tests for Core
db/                     schema, views/function, procedures, security, generated seed data
tools/                  data generator, database setup script
samples/                vendor settlement file with planted exceptions
docs/                   requirements, UAT plan, data dictionary
```

## Run it locally

Needs Docker and the .NET 8 SDK. On an Apple Silicon Mac, turn on "Use Rosetta for x86/amd64 emulation" in Docker Desktop settings for the SQL Server image.

```bash
# 1. SQL Server in Docker
docker run -d --name sfw-sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Dev!Passw0rd123' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest

# 2. Load schema and data (brew install sqlcmd)
SQL_SERVER=localhost SQL_ADMIN_USER=sa SQL_ADMIN_PASSWORD='Dev!Passw0rd123' \
APP_PASSWORD='App!Passw0rd123' READER_PASSWORD='Read!Passw0rd123' ./tools/setup-database.sh

# 3. Connection strings
cp src/Workbench.Web/appsettings.Development.example.json src/Workbench.Web/appsettings.Development.json
# edit the two passwords to match step 2, optionally add a Gemini API key

# 4. Run
dotnet run --project src/Workbench.Web
dotnet test
```

To regenerate the data: `python3 tools/generate_seed_data.py` (deterministic, fixed seed).

Deploying to Azure: see [DEPLOY.md](DEPLOY.md).

## Simplifications

- VA protection assumes the student already submitted their certificate of eligibility.
- No login. It's a public demo with fake data; a real version would use SSO and roles.
- Reconciliation reports differences only. Posting corrections would go through the student system.
- One payment plan vendor and one file format.
