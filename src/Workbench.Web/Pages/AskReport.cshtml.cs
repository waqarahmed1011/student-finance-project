using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Workbench.Core.Reporting;
using Workbench.Web.Data;
using Workbench.Web.Services;

namespace Workbench.Web.Pages;

[EnableRateLimiting("ai")]
public class AskReportModel : PageModel
{
    public static readonly string[] Examples =
    [
        "Which programs have the most past-due dollars over 90 days?",
        "List veterans with a pending Chapter 33 payment and a past-due balance",
        "How much Title IV aid did each institution disburse for Fall 2026?",
        "Payment plan students whose plan was cancelled but still owe money",
        "Average past-due balance by award level for Fall 2026"
    ];

    private readonly IReportSqlGenerator _generator;
    private readonly ReportQueryRunner _runner;
    private readonly AuditRepository _audit;
    private readonly ILogger<AskReportModel> _logger;

    public AskReportModel(IReportSqlGenerator generator, ReportQueryRunner runner, AuditRepository audit, ILogger<AskReportModel> logger)
    {
        _generator = generator;
        _runner = runner;
        _audit = audit;
        _logger = logger;
    }

    [BindProperty, StringLength(500)]
    public string? Question { get; set; }

    [BindProperty, StringLength(8000)]
    public string? Sql { get; set; }

    public string? Explanation { get; private set; }
    public SqlGuardResult? Check { get; private set; }
    public QueryResult? Result { get; private set; }
    public string? Error { get; private set; }
    public bool AiConfigured => _generator.IsConfigured;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken ct)
    {
        if (!AiConfigured)
        {
            Error = "AI isn't configured on this deployment. You can still write a query against the rpt views below.";
            return Page();
        }
        if (string.IsNullOrWhiteSpace(Question))
        {
            Error = "Type a question first.";
            return Page();
        }

        try
        {
            var generated = await _generator.GenerateAsync(Question.Trim(), ct);
            Sql = generated.Sql;
            Explanation = generated.Explanation;
            Check = string.IsNullOrWhiteSpace(Sql) ? null : ReportSqlGuard.Validate(Sql);
            ModelState.Remove(nameof(Sql)); // show the generated SQL, not the posted (empty) value

            await _audit.WriteAsync("demo-user", "AI_SQL_GENERATED", "Report", null,
                $"Q: {Question.Trim()}\nSQL: {Sql}");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Report SQL generation failed");
            Error = "Couldn't generate a query right now. Try rephrasing, or write the SQL yourself.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken ct)
    {
        Check = ReportSqlGuard.Validate(Sql);
        if (!Check.IsAllowed)
            return Page();

        try
        {
            Result = await _runner.RunAsync(Sql!, ct);
            await _audit.WriteAsync("demo-user", "REPORT_QUERY_RUN", "Report", null,
                $"Rows: {Result.Rows.Count}{(Result.Truncated ? "+" : "")}\nQ: {Question}\nSQL: {Sql}");
        }
        catch (SqlException ex)
        {
            // the read-only login turns anything the parser missed into a permission error here
            Error = $"SQL Server rejected the query: {ex.Message}";
        }

        return Page();
    }
}
