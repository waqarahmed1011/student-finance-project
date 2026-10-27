using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Workbench.Core.PastDue;

namespace Workbench.Web.Pages;

public class AboutModel : PageModel
{
    public AboutModel(IConfiguration configuration, IOptions<HoldPolicyOptions> holdPolicy)
    {
        SourceCodeUrl = configuration["Links:SourceCode"];
        HoldPolicy = holdPolicy.Value;
    }

    public string? SourceCodeUrl { get; }
    public HoldPolicyOptions HoldPolicy { get; }

    public void OnGet()
    {
    }
}
