using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Workbench.Web.Data;

namespace Workbench.Web.Pages.Reconciliation;

public class BatchModel : PageModel
{
    private readonly ReconRepository _repo;

    public BatchModel(ReconRepository repo)
    {
        _repo = repo;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowResolved { get; set; }

    public ReconBatchSummary? Batch { get; private set; }
    public IReadOnlyList<ReconExceptionRow> Exceptions { get; private set; } = [];
    public string? Message { get; private set; }

    public sealed class ResolveInput
    {
        public int ExceptionId { get; set; }

        [Required, StringLength(60)]
        public string ResolvedBy { get; set; } = "";

        [Required, StringLength(400, MinimumLength = 5)]
        public string Note { get; set; } = "";
    }

    [BindProperty] public ResolveInput Resolve { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync() ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostResolveAsync()
    {
        if (!ModelState.IsValid)
        {
            Message = "Enter your name and a resolution note (at least 5 characters).";
            return await LoadAsync() ? Page() : NotFound();
        }

        var ok = await _repo.ResolveExceptionAsync(Resolve.ExceptionId, Resolve.ResolvedBy.Trim(), Resolve.Note.Trim());
        TempData["Message"] = ok ? $"Exception #{Resolve.ExceptionId} resolved and logged." : "That exception was already resolved.";
        return RedirectToPage(new { id = Id, showResolved = ShowResolved });
    }

    private async Task<bool> LoadAsync()
    {
        Batch = await _repo.GetBatchAsync(Id);
        if (Batch is null)
            return false;

        Exceptions = await _repo.GetExceptionsAsync(Id, ShowResolved ? null : "OPEN");
        Message ??= TempData["Message"] as string;
        return true;
    }
}
