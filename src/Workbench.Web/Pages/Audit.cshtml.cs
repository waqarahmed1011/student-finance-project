using Microsoft.AspNetCore.Mvc.RazorPages;
using Workbench.Web.Data;

namespace Workbench.Web.Pages;

public class AuditModel : PageModel
{
    private readonly AuditRepository _audit;

    public AuditModel(AuditRepository audit)
    {
        _audit = audit;
    }

    public IReadOnlyList<AuditEntry> Entries { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Entries = await _audit.GetRecentAsync(200);
    }
}
