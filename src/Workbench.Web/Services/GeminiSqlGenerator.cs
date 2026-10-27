using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Workbench.Web.Services;

public sealed class GeminiOptions
{
    public string? ApiKey { get; set; }

    /// <summary>Tried in order. Google retires and rate-limits models often, so the next one is the fallback.</summary>
    public string[] Models { get; set; } = [];
}

public sealed record GeneratedSql(string Sql, string Explanation);

public interface IReportSqlGenerator
{
    bool IsConfigured { get; }
    Task<GeneratedSql> GenerateAsync(string question, CancellationToken ct);
}

/// <summary>
/// Turns a plain-English report request into a T-SQL query. The model only ever sees the
/// rpt view definitions below, never any student data, and the SQL it writes is shown to the
/// user before anything runs.
/// </summary>
public sealed class GeminiSqlGenerator : IReportSqlGenerator
{
    private const string SchemaDescription = """
        rpt.Students            one row per student
            StudentId, InstitutionCode, InstitutionName, Segment ('Nursing','Online','Medical and Veterinary'),
            ProgramName, AwardLevel ('Undergraduate','Graduate','Doctoral'),
            EnrollmentStatus ('Active','Withdrawn','Inactive'),
            MilitaryAffiliation ('Veteran','ActiveDuty','Dependent' or NULL)

        rpt.StudentBalances     one row per student per term, as of today
            StudentId, InstitutionCode, InstitutionName, ProgramName, TermCode, TermDescription, PaymentDueDate,
            Charges, Payments, Aid, Adjustments,
            Balance        (what the student owes for the term, negative = credit balance),
            PastDueAmount  (part of Balance that is past its due date),
            DaysPastDue    (age of the oldest unpaid past-due charge),
            AgingBucket    ('Current','1-30','31-60','61-90','90+'),
            VaChapter      ('33','31' or NULL), VaStatus ('PENDING','PAID' or NULL),
            PaymentPlanStatus ('ACTIVE','COMPLETED','CANCELLED' or NULL)

        rpt.Payments            one row per payment or aid disbursement (positive amounts)
            StudentId, InstitutionCode, TermCode, PaymentDate, Amount,
            Category ('PAY' = payment, 'AID' = financial aid / benefits),
            FundSource ('SelfPay','PaymentPlan','Employer','TitleIV','VA','MilitaryTA','Scholarship'),
            DetailDescription (e.g. 'Federal Pell Grant', 'Direct Unsubsidized Loan', 'VA Post-9/11 GI Bill (Ch. 33)')

        rpt.VaCertifications    VA enrollment certifications
            StudentId, TermCode, Chapter ('33','31'), CertifiedDate, ExpectedAmount, Status ('PENDING','PAID'), PaidDate

        rpt.PaymentPlans        third-party payment plan enrollments
            StudentId, TermCode, PlanAmount, Installments, EnrolledDate, Status ('ACTIVE','COMPLETED','CANCELLED')

        Term codes: 202520 = Summer 2025, 202530 = Fall 2025, 202610 = Spring 2026, 202620 = Summer 2026, 202630 = Fall 2026.
        Fiscal year runs July 1 to June 30 (FY2027 = July 2026 to June 2027).
        Institution codes: LCN Lakeshore College of Nursing (Nursing), MHU Meridian Health University (Online),
        CSM Coral Sound School of Medicine, WSM Windward University School of Medicine,
        CVM Coral Sound School of Veterinary Medicine (the last three are the Medical and Veterinary segment).
        """;

    private static readonly string[] DefaultModels = ["gemini-3.5-flash", "gemini-3.8-flash", "gemini-flash-latest", "gemini-3.5-flash-lite", "gemini-3.1-flash-lite"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiSqlGenerator(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<GeneratedSql> GenerateAsync(string question, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini API key is not configured.");

        var systemPrompt = $$"""
            You write Microsoft SQL Server (T-SQL) queries for a student finance reporting team.
            Today's date is {{DateTime.UtcNow:yyyy-MM-dd}}.

            You can ONLY use these views. Always schema-qualify them (rpt.ViewName). No other tables exist for you.
            {{SchemaDescription}}

            Rules:
            - One SELECT statement. No INSERT/UPDATE/DELETE/EXEC/DDL, no temp tables, no SELECT INTO, no table-valued functions.
            - Use readable column aliases. Round money to 2 decimals.
            - If the request returns individual rows (not an aggregate), add TOP (500) and a sensible ORDER BY.
            - "Past due" means PastDueAmount > 0. "Outstanding" or "owes" means Balance > 0.
            - If the request can't be answered from these views, return an empty sql and say why in explanation.

            Respond with JSON only: {"sql": "...", "explanation": "one or two plain-English sentences about what the query returns"}
            """;

        var request = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = question } } } },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json",
                thinkingConfig = new { thinkingLevel = "low" } // fast answers; this is a simple task
            }
        };

        var models = _options.Models is { Length: > 0 } ? _options.Models : DefaultModels;
        var failures = new List<string>();

        // two passes: Google's 503 "high demand" errors usually clear within a couple of seconds
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var model in models)
            {
                if (pass > 0 && model == models[0])
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);

                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptTimeout.CancelAfter(TimeSpan.FromSeconds(20));

                using var message = new HttpRequestMessage(HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
                {
                    Content = JsonContent.Create(request)
                };
                message.Headers.Add("x-goog-api-key", _options.ApiKey);

                try
                {
                    using var response = await _http.SendAsync(message, attemptTimeout.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        // 404 retired model, 429 quota, 503 overloaded: try the next model
                        failures.Add($"{model}: {(int)response.StatusCode}");
                        continue;
                    }

                    var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(JsonOptions, attemptTimeout.Token);
                    var text = string.Concat(payload?.Candidates?.FirstOrDefault()?.Content?.Parts?
                        .Where(p => p.Thought != true)
                        .Select(p => p.Text) ?? []);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        failures.Add($"{model}: empty response");
                        continue;
                    }

                    var answer = JsonSerializer.Deserialize<ModelAnswer>(text, JsonOptions)
                        ?? throw new InvalidOperationException("Couldn't read Gemini's answer.");

                    return new GeneratedSql(answer.Sql?.Trim() ?? "", answer.Explanation?.Trim() ?? "");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    failures.Add($"{model}: timed out");
                }
            }
        }

        throw new HttpRequestException($"No Gemini model answered ({string.Join(", ", failures)}).");
    }

    private sealed record ModelAnswer(string? Sql, string? Explanation);

    private sealed record GeminiResponse(List<Candidate>? Candidates);
    private sealed record Candidate(Content? Content);
    private sealed record Content(List<Part>? Parts);
    private sealed record Part(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("thought")] bool? Thought);
}
