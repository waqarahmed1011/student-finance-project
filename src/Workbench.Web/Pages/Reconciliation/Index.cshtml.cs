using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Workbench.Web.Data;
using Workbench.Web.Services;

namespace Workbench.Web.Pages.Reconciliation;

public class IndexModel : PageModel
{
    public const string SampleFileName = "clearpath_settlement_2026-10-01_2026-10-09.csv";

    private readonly ReconciliationService _recon;
    private readonly ReconRepository _repo;
    private readonly IWebHostEnvironment _env;

    public IndexModel(ReconciliationService recon, ReconRepository repo, IWebHostEnvironment env)
    {
        _recon = recon;
        _repo = repo;
        _env = env;
    }

    [BindProperty, Required, StringLength(60)]
    [Display(Name = "Your name")]
    public string UploadedBy { get; set; } = "";

    [BindProperty]
    public IFormFile? Upload { get; set; }

    public IReadOnlyList<ReconBatchSummary> Batches { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        Batches = await _repo.GetBatchesAsync(20);
    }

    public async Task<IActionResult> OnPostUploadAsync()
    {
        if (Upload is null || Upload.Length == 0)
            ModelState.AddModelError(nameof(Upload), "Choose a CSV file.");
        else if (!Upload.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(Upload), "Only .csv files are accepted.");

        if (!ModelState.IsValid)
            return await RedisplayAsync();

        await using var stream = Upload!.OpenReadStream();
        return await RunAsync(stream, Path.GetFileName(Upload.FileName));
    }

    public async Task<IActionResult> OnPostSampleAsync()
    {
        if (!ModelState.IsValid)
            return await RedisplayAsync();

        var path = Path.Combine(_env.WebRootPath, "samples", SampleFileName);
        await using var stream = System.IO.File.OpenRead(path);
        return await RunAsync(stream, SampleFileName);
    }

    private async Task<IActionResult> RunAsync(Stream stream, string fileName)
    {
        try
        {
            var batchId = await _recon.ReconcileAsync(stream, fileName, UploadedBy.Trim());
            return RedirectToPage("Batch", new { id = batchId });
        }
        catch (InvalidDataException ex)
        {
            Error = ex.Message;
            return await RedisplayAsync();
        }
    }

    private async Task<IActionResult> RedisplayAsync()
    {
        Batches = await _repo.GetBatchesAsync(20);
        return Page();
    }
}
