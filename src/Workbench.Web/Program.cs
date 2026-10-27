using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Workbench.Core.Finance;
using Workbench.Core.PastDue;
using Workbench.Web.Data;
using Workbench.Web.Services;

var usCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = usCulture;
CultureInfo.DefaultThreadCurrentUICulture = usCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.Configure<HoldPolicyOptions>(builder.Configuration.GetSection("HoldPolicy"));
builder.Services.Configure<AllowanceRates>(builder.Configuration.GetSection("Allowance"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 2 * 1024 * 1024);

builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddSingleton(sp => new HoldPolicy(sp.GetRequiredService<IOptions<HoldPolicyOptions>>().Value));
builder.Services.AddScoped<ReportRepository>();
builder.Services.AddScoped<ReconRepository>();
builder.Services.AddScoped<AuditRepository>();
builder.Services.AddScoped<PastDueService>();
builder.Services.AddScoped<ReportQueryRunner>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddHttpClient<IReportSqlGenerator, GeminiSqlGenerator>(c => c.Timeout = TimeSpan.FromSeconds(70));

// Azure App Service sits behind a proxy; this makes RemoteIpAddress the visitor's IP for the rate limiter
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// the site is public, so cap how often one visitor can hit the AI page (protects the API key's quota)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.MapRazorPages();

app.Run();
