using GuidanceAdminServer.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GuidanceAdminServer.Pages;

public sealed class IndexModel : PageModel
{
    private readonly JobStore _jobStore;

    public IndexModel(JobStore jobStore) => _jobStore = jobStore;

    public IReadOnlyList<JobRecord> Jobs { get; private set; } = [];

    public void OnGet()
    {
        Jobs = _jobStore.All();
    }

    public IActionResult OnPostNotify(string jobId)
    {
        _jobStore.SetActiveJob(jobId);
        TempData["Message"] = $"Job '{jobId}' activated — Unity devices will receive Step 1.";
        return RedirectToPage();
    }
}
